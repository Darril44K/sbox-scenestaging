namespace Sandbox;

public sealed class SplineModelRenderer : ModelRenderer
{
	[Property, Category( "Spline" )] public Spline Spline { get; set; }

	[Property, Category( "Spline" )]
	public Rotation ModelRotation
	{
		get => _modelRotation;
		set
		{
			_modelRotation = value;
			UpdateObject();
		}
	}

	private Rotation _modelRotation = Rotation.Identity;

	[Property, Category( "Spline" )]
	public Vector3 ModelScale
	{
		get => _modelScale;
		set
		{
			_modelScale = value;
			UpdateObject();
		}
	}

	private Vector3 _modelScale = Vector3.One;

	[Property, Category( "Spline" )]
	public Vector3 ModelOffset
	{
		get => _modelOffset;
		set
		{
			_modelOffset = value;
			UpdateObject();
		}
	}

	private Vector3 _modelOffset = Vector3.Zero;

	[Property, Category( "Spline" )]
	public bool UseRotationMinimizingFrames
	{
		get => _useRotationMinimizingFrames;
		set
		{
			_useRotationMinimizingFrames = value;
			UpdateObject();
		}
	}

	private bool _useRotationMinimizingFrames = true;

	private Mesh customMesh = new();
	private Model customModel = Model.Error;

	private Vertex[] modelVertices = null;
	private uint[] modelIndices = null;

	private Vertex[] deformedVertices;
	private int[] deformedIndices;

	[Property, Category( "Spline" ), MinMax(0, float.PositiveInfinity)]
	public float Spacing
	{
		get => _spacing;
		set
		{
			_spacing = value;
			UpdateObject();
		}
	}

	private float _spacing = 0f;

	protected override void OnEnabled()
	{
		base.OnEnabled();

		if ( Spline.IsValid() )
		{
			Spline.SplineChanged += UpdateObject;
		}
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		customMesh = null;

		if ( Spline.IsValid() )
		{
			Spline.SplineChanged -= UpdateObject;
		}
	}

	protected override void DrawGizmos()
	{
		//var rotatedModelBounds = Model.Bounds.Rotate( ModelRotation );
		//var sizeInModelDir = rotatedModelBounds.Size.Dot( Vector3.Forward );

		//var minInModelDir = rotatedModelBounds.Center.Dot( Vector3.Forward ) - sizeInModelDir / 2;

		//var splineLength = Spline.GetLength();
		//var meshesRequired = (int)Math.Ceiling( splineLength / sizeInModelDir );
		//var distancePerMesh = splineLength / meshesRequired;

		//var frames = CalculateTangentFramesUsingUpDir( meshesRequired, 16, distancePerMesh );

		//float arrowLength = distancePerMesh / 4;

		//foreach ( var frame in frames )
		//{
		//	var position = frame.Position;
		//	var tangent = frame.Forward;
		//	var finalUp = frame.Up;
		//	var right = frame.Right;

		//	// Draw tangent vector (forward)
		//	Gizmo.Draw.Color = Color.Red;
		//	Gizmo.Draw.Arrow( position, position + tangent * arrowLength, arrowLength / 10f, arrowLength / 15f );

		//	// Draw up vector (normal)
		//	Gizmo.Draw.Color = Color.Green;
		//	Gizmo.Draw.Arrow( position, position + finalUp * arrowLength, arrowLength / 10f, arrowLength / 15f );

		//	// Draw right vector (binormal)
		//	Gizmo.Draw.Color = Color.Blue;
		//	Gizmo.Draw.Arrow( position, position + right * arrowLength, arrowLength / 10f, arrowLength / 15f );
		//}
	}

	protected override void UpdateObject()
	{
		base.UpdateObject();

		if ( customMesh == null)
		{
			customMesh = new();
		}

		if ( !SceneObject.IsValid() || !Spline.IsValid() )
			return;

		// Set by base call
		var model = SceneObject.Model;

		customMesh.Material = MaterialOverride ?? model.Materials.FirstOrDefault();

		modelIndices = model.GetIndices();
		modelVertices = model.GetVertices();

		var transformedBounds = model.Bounds;
		transformedBounds.Mins = transformedBounds.Mins * ModelScale;
		transformedBounds.Maxs = transformedBounds.Maxs * ModelScale;
		transformedBounds = transformedBounds.Rotate( ModelRotation );

		var sizeInModelDir = transformedBounds.Size.Dot( Vector3.Forward );
		var minInModelDir = transformedBounds.Center.Dot( Vector3.Forward ) - sizeInModelDir / 2;

		var splineLength = Spline.GetLength();
		var sizeWithSpacing = sizeInModelDir + Spacing;

		var meshesRequired = (int)Math.Ceiling( splineLength / sizeInModelDir );
		var meshesRequiredWithSpacing = (int)Math.Ceiling( splineLength / sizeWithSpacing );

		if ( meshesRequiredWithSpacing == 0 )
		{
			return;
		}

		// Adjust total vertices and indices
		var totalVertices = modelVertices.Length * meshesRequiredWithSpacing;
		var totalIndices = modelIndices.Length * meshesRequiredWithSpacing;

		if ( deformedVertices == null || deformedVertices.Length < totalVertices )
		{
			deformedVertices = new Vertex[totalVertices];
		}
		if ( deformedIndices == null || deformedIndices.Length < totalIndices )
		{
			deformedIndices = new int[totalIndices];
		}

		int framesPerMesh = 12;
		var frames = UseRotationMinimizingFrames ? CalculateRotationMinimizingTangentFrames( Spline, meshesRequired * framesPerMesh + 1 ) : CalculateTangentFramesUsingUpDir( Spline, meshesRequired * framesPerMesh + 1 );

		Utility.Parallel.For(
			0,
			meshesRequiredWithSpacing,
			meshIndex =>
			{
				float startDistance = meshIndex * sizeWithSpacing;
				float endDistance = startDistance + sizeInModelDir;

				// Deform vertices for this segment
				for ( int i = 0; i < modelVertices.Length; i++ )
				{
					var vertex = modelVertices[i];

					var deformedVertex = vertex;

					// Deform the vertex using tangent frames
					Deform( Spline, ModelRotation, ModelOffset, ModelScale, vertex.Position, vertex.Normal, vertex.Tangent, frames, startDistance, endDistance, minInModelDir, sizeInModelDir, out deformedVertex.Position, out deformedVertex.Normal, out deformedVertex.Tangent );

					deformedVertices[modelVertices.Length * meshIndex + i] = deformedVertex;
				}

				for ( int i = 0; i < modelIndices.Length; i++ )
				{
					deformedIndices[modelIndices.Length * meshIndex + i] = (int)(modelIndices[i] + modelVertices.Length * meshIndex);
				}

			}
		);


		if ( customMesh.HasVertexBuffer )
		{
			if ( customMesh.IndexCount < totalIndices )
			{
				customMesh.SetIndexBufferSize( deformedIndices.Length );
			}
			customMesh.SetIndexBufferData( deformedIndices.AsSpan( 0, totalIndices ) );
			customMesh.SetIndexRange( 0, totalIndices );

			if ( customMesh.VertexCount < totalVertices )
			{
				customMesh.SetVertexBufferSize( deformedVertices.Length );
			}
			customMesh.SetVertexRange( 0, totalVertices );
			customMesh.SetVertexBufferData( deformedVertices.AsSpan( 0, totalVertices ) );
		}
		else
		{
			customMesh.CreateVertexBuffer( deformedVertices.Length, Vertex.Layout, deformedVertices.AsSpan( 0, totalVertices ) );
			customMesh.CreateIndexBuffer( deformedIndices.Length, deformedIndices.AsSpan( 0, totalIndices ) );
		}

		customModel = Model.Builder.AddMesh( customMesh ).Create();
		SceneObject.Model = customModel;
	}

	public static Transform[] CalculateTangentFramesUsingUpDir( Spline spline, int frameCount )
	{
		Transform[] frames = new Transform[frameCount];

		float totalSplineLength = spline.GetLength();

		Vector3 initialTangent = spline.GetTangetAtDistance( 0f );
		Vector3 up = spline.GetUpVectorAtDistance( 0f );

		// Choose an initial up vector if tangent is parallel to Up
		if ( MathF.Abs( Vector3.Dot( initialTangent, up ) ) > 0.999f )
		{
			up = Vector3.Right;
		}

		for ( int i = 0; i < frameCount; i++ )
		{
			float t = (float)i / (frameCount - 1);
			float distance = t * totalSplineLength;

			Vector3 position = spline.GetPositionAtDistance( distance );
			Vector3 tangent = spline.GetTangetAtDistance( distance );
			up = spline.GetUpVectorAtDistance( distance );

			// Apply roll
			float roll = spline.GetRollAtDistance( distance );
			var newUp = Rotation.FromAxis( tangent, roll ) * up;

			Rotation rotation = Rotation.LookAt( tangent, newUp );

			Vector2 scale2D = spline.GetScaleAtDistance( distance );

			// Create scale vector with 1 for x (since we're only scaling along y and z)
			Vector3 scale = new Vector3( 1f, scale2D.x, scale2D.y );

			frames[i] = new Transform( position, rotation, scale );
		}

		return frames;
	}

	public static Transform[] CalculateRotationMinimizingTangentFrames( Spline spline, int frameCount )
	{
		Transform[] frames = new Transform[frameCount];

		float totalSplineLength = spline.GetLength();

		// Initialize the up vector
		Vector3 previousTangent = spline.GetTangetAtDistance( 0f );
		Vector3 up = Vector3.Up;

		// Choose an initial up vector if tangent is parallel to Up
		if ( MathF.Abs( Vector3.Dot( previousTangent, up ) ) > 0.999f )
		{
			up = Vector3.Right;
		}

		float previousRoll = spline.GetRollAtDistance( 0f );
		up = Rotation.FromAxis( previousTangent, previousRoll ) * up;

		Vector2 scale2D = spline.GetScaleAtDistance( 0f );

		// Create scale vector with 1 for x (since we're only scaling along y and z)
		Vector3 scale = new Vector3( 1f, scale2D.x, scale2D.y );

		Vector3 previousPosition = spline.GetPositionAtDistance( 0f );
		frames[0] = new Transform( previousPosition, Rotation.LookAt( previousTangent, up ), scale );

		for ( int i = 1; i < frameCount; i++ )
		{
			float t = (float)i / (frameCount - 1);
			float distance = t * totalSplineLength;

			Vector3 position = spline.GetPositionAtDistance( distance );
			Vector3 tangent = spline.GetTangetAtDistance( distance );

			// Parallel transport the up vector
			up = GetRotationMinimizingNormal( previousPosition, previousTangent, up, position, tangent );

			// Apply roll
			float roll = spline.GetRollAtDistance( distance );
			float deltaRoll = roll - previousRoll;
			up = Rotation.FromAxis( tangent, deltaRoll ) * up;

			Rotation rotation = Rotation.LookAt( tangent, up );

			scale2D = spline.GetScaleAtDistance( distance );

			// Create scale vector with 1 for x (since we're only scaling along y and z)
			scale = new Vector3( 1f, scale2D.x, scale2D.y );

			frames[i] = new Transform( position, rotation, scale );

			previousTangent = tangent;
			previousPosition = position;
			previousRoll = roll;
		}

		// Correct up vectors for looped splines
		if ( spline.IsLoop && frames.Length > 1 )
		{
			Vector3 startUp = frames[0].Rotation.Up;
			Vector3 endUp = frames[^1].Rotation.Up;

			float theta = MathF.Acos( Vector3.Dot( startUp, endUp ) ) / (frames.Length - 1);
			if ( Vector3.Dot( frames[0].Rotation.Forward, Vector3.Cross( startUp, endUp ) ) > 0 )
			{
				theta = -theta;
			}

			for ( int i = 0; i < frames.Length; i++ )
			{
				Rotation R = Rotation.FromAxis( frames[i].Rotation.Forward, (theta * i).RadianToDegree() );
				Vector3 correctedUp = R * frames[i].Rotation.Up;
				frames[i] = new Transform( frames[i].Position, Rotation.LookAt( frames[i].Rotation.Forward, correctedUp ), frames[i].Scale );
			}
		}

		return frames;
	}

	private static Vector3 GetRotationMinimizingNormal( Vector3 posA, Vector3 tangentA, Vector3 normalA, Vector3 posB, Vector3 tangentB )
	{
		// Source: https://www.microsoft.com/en-us/research/wp-content/uploads/2016/12/Computation-of-rotation-minimizing-frames.pdf
		Vector3 v1 = posB - posA;
		float v1DotV1Half = Vector3.Dot( v1, v1 ) / 2f;
		float r1 = Vector3.Dot( v1, normalA ) / v1DotV1Half;
		float r2 = Vector3.Dot( v1, tangentA ) / v1DotV1Half;
		Vector3 nL = normalA - r1 * v1;
		Vector3 tL = tangentA - r2 * v1;
		Vector3 v2 = tangentB - tL;
		float r3 = Vector3.Dot( v2, nL ) / Vector3.Dot( v2, v2 );
		return (nL - 2f * r3 * v2).Normal;
	}

	// TODO Has there ever been a function with more args?
	public static void Deform( Spline spline, Rotation modelRoation, Vector3 modelOffset, Vector3 modelScale, Vector3 localPosition, Vector3 localNormal, Vector4 localTangent, Span<Transform> frames, float startDistance, float endDistance, float minInModelDir, float sizeInModelDir, out Vector3 deformedPosition, out Vector3 deformedNormal, out Vector4 deformedTangent )
	{
		// rotate localPosition by model rotation
		localPosition = modelRoation * (localPosition * modelScale);

		// Map localPosition.x to t along the spline segment
		float t = (localPosition.x - minInModelDir) / sizeInModelDir;
		t = Math.Clamp( t, 0f, 1f );

		float distanceAlongSpline = MathX.Lerp( startDistance, endDistance, t );

		// Calculate the frame index and interpolation factor
		float frameFloatIndex = (distanceAlongSpline / spline.GetLength()) * (frames.Length - 1);
		int frameIndex = Math.Clamp( (int)Math.Floor( frameFloatIndex ), 0, frames.Length - 2 );
		float frameT = Math.Clamp( frameFloatIndex - frameIndex, 0f, 1f );

		Transform frame0 = frames[frameIndex];
		Transform frame1 = frames[frameIndex + 1];

		Vector3 position = Vector3.Lerp( frame0.Position, frame1.Position, frameT );
		Rotation rotation = Rotation.Slerp( frame0.Rotation, frame1.Rotation, frameT );

		// Interpolate scale from frames
		Vector3 scale0 = frame0.Scale;
		Vector3 scale1 = frame1.Scale;
		Vector3 scale = Vector3.Lerp( scale0, scale1, frameT );

		// Scale localPosition along y and z axes
		Vector3 scaledLocalPosition = new Vector3( 0, localPosition.y * scale.y, localPosition.z * scale.z );

		// Apply model rotation and local offsets
		deformedPosition = position + rotation * scaledLocalPosition + modelOffset;

		deformedNormal = rotation * (modelRoation * localNormal);
		deformedTangent = new Vector4( rotation * (modelRoation * localTangent), localTangent.w );
	}

}
