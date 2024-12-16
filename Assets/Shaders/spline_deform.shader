HEADER
{
	Description = "Standard shader but it deforms splines";
}

MODES
{
	VrForward();
	Depth();
	ToolsVis( S_MODE_TOOLS_VIS );
}

FEATURES
{
    #include "common/features.hlsl"
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	// TODO can be packed more efficiently
	struct SplineSegment
	{
		// float3 P0;
		float4 P1;
		float4 P2;
		float4 P3;
		float4 StartEndRoll;
		float4 StartEndWidthHeightScale;
	};

	StructuredBuffer<SplineSegment> SplineSegments < Attribute("SplineSegments"); >;

	float MinInModelDir < Attribute("MinInModelDir"); >;
	float SizeInModelDir < Attribute("SizeInModelDir"); >;

	// calculate how far along we are in the bezier curve
	float CalculateBezierT(float3 vertLocalPos, float minForwad, float sizeForward)
	{
		float distanceAlongX = vertLocalPos.x - minForwad;
		float t = distanceAlongX / sizeForward;

		return t;
	}

	float3 CalculateBezierPosition(float t, float3 p0, float3 p1, float3 p2, float3 p3)
	{
		// Calculate value for cubic Bernstein polynominal
		// B(t) = (1 - t)^3 * P0 + 3 * (1 - t)^2 * t * P1 + 3 * (1 - t)^2 * t^2 * P2 + t^3 * P3
		float tSquare = t * t;
		float tCubic = tSquare * t;
		float oneMinusT = 1 - t;
		float oneMinusTSquare = oneMinusT * oneMinusT;
		float oneMinusTCubic = oneMinusTSquare * oneMinusT;

		float w0 = oneMinusTCubic; // -t^3 + 3t^2 - 3t + 1
		float w1 = 3 * oneMinusTSquare * t; // 3t^3 - 6t^2 + 3t
		float w2 = 3 * oneMinusT * tSquare; // -3t^3 + 3t^2
		float w3 = tCubic; // t^3

		float3 weightedP0 = w0 * p0;
		float3 weightedP1 = w1 * p1;
		float3 weightedP2 = w2 * p2;
		float3 weightedP3 = w3 * p3;

		return weightedP0 + weightedP1 + weightedP2 + weightedP3;
	}

	float3 CalculateBezierTangent(float t, float3 p0, float3 p1, float3 p2, float3 p3)
	{
		// Calculate the derivative of the cubic Bernstein polynominal
		// B'(t) = 3 * (1 - t) ^2 * (P1 - P0) + 6 * (1 - t) * t * (P2 - P1) + 3 * t^2 * (P3 - P2)
		float t2 = t * t;

		float w0 = -3 * t2 + 6 * t - 3;
		float w1 = 9 * t2 - 12 * t + 3;
		float w2 = -9 * t2 + 6 * t;
		float w3 = 3 * t2;

		float3 weightedP0 = w0 * p0;
		float3 weightedP1 = w1 * p1;
		float3 weightedP2 = w2 * p2;
		float3 weightedP3 = w3 * p3;

		return weightedP0 + weightedP1 + weightedP2 + weightedP3;
	}

	float CalculateRoll(float t, float rollStart, float rollEnd)
	{
		return lerp(rollStart, rollEnd, t);
	}

	float2 CalculateScale(float t, float2 startScaleWidthHeight, float2 endScaleWidthHeight)
	{
		return lerp(startScaleWidthHeight, endScaleWidthHeight, t);
	}

	float3 ScaleAndRotateVector(float t, float3 vertLocalPos, float2 scale, float3 right, float3 up)
	{
		float3 scaledRight = right * scale.x;
		float3 scaledUp = up * scale.y;

		// only modify the Y and Z components
		// X will only be modified by the spline location
		return vertLocalPos.y * scaledRight + vertLocalPos.z * scaledUp;
	}

	float3 RotateNormal(float3 normal, float3 forward, float3 right, float3 up)
	{
		return normal.x * forward + normal.y * right + normal.z * up;
	}

	PixelInput MainVs( VertexInput i, uint instanceID : SV_InstanceID )
	{
		float t = CalculateBezierT(i.vPositionOs, MinInModelDir, SizeInModelDir);

		float3 p0 = float3(0, 0, 0);
		float3 p1 = SplineSegments[instanceID].P1;
		float3 p2 = SplineSegments[instanceID].P2;
		float3 p3 = SplineSegments[instanceID].P3;

		float rollStart = SplineSegments[instanceID].StartEndRoll.x;
		float rollEnd = SplineSegments[instanceID].StartEndRoll.y;

		float roll = CalculateRoll(t, rollStart, rollEnd);

		// Maybbbeee we want to expose this in the future
		float2 startScaleWidthHeight = SplineSegments[instanceID].StartEndWidthHeightScale.xy;
		float2 endScaleWidthHeight = SplineSegments[instanceID].StartEndWidthHeightScale.zw;

		float2 scale = CalculateScale(t, startScaleWidthHeight, endScaleWidthHeight);

		float3 up = float3(0, 0, 1); // TODO make up axis configurable
		float3 forward = CalculateBezierTangent(t, p0, p1, p2, p3);
		float3 right = normalize(cross(up, forward));
		up = normalize( cross(forward, right) );

		float sine;
		float cosine;

		sincos(roll, sine, cosine);
		float3 rightRotated = cosine * right - sine * up;
		float3 upRotated = sine * right + cosine * up;

		float3 curvePosition = CalculateBezierPosition(t, p0, p1, p2, p3);
		float3 deformedPosition = ScaleAndRotateVector(t, i.vPositionOs, scale, rightRotated, upRotated);

		// Deform position
		i.vPositionOs = curvePosition + deformedPosition;

		PixelInput o = ProcessVertex( i );

		float3 vNormalOs;
		float4 vTangentUOs_flTangentVSign;
		float3x4 matObjectToWorld = CalculateInstancingObjectToWorldMatrix( i );

		VS_DecodeObjectSpaceNormalAndTangent( i, vNormalOs, vTangentUOs_flTangentVSign );

		// Deform normal
		vNormalOs = RotateNormal(vNormalOs, forward, rightRotated, upRotated);

		#if ( S_MODE_TOOLS_VIS )
		{
			float3x3 matInvTranspose = ComputeInverseTranspose( matObjectToWorld );
			o.vNormalWs.xyz = normalize( mul( matInvTranspose, vNormalOs.xyz ) );
		}
		#else
		{
			o.vNormalWs.xyz = normalize( mul( matObjectToWorld, float4( vNormalOs.xyz, 0.0 ) ) );
		}
		#endif

		#ifdef PS_INPUT_HAS_TANGENT_BASIS 
		{
			float3 vTangentUWs;
			#if ( S_MODE_TOOLS_VIS )
			{
				float3x3 matInvTranspose = ComputeInverseTranspose( matObjectToWorld );
				vTangentUWs = mul( matInvTranspose, vTangentUOs_flTangentVSign.xyz );
			}
			#else
			{
				vTangentUWs = mul( matObjectToWorld, float4( vTangentUOs_flTangentVSign.xyz, 0.0 ) );
			}
			#endif

			//
			// Force tangentU perpendicular to normal and normalize
			//
			vTangentUWs.xyz = normalize( vTangentUWs.xyz - ( o.vNormalWs.xyz * dot( vTangentUWs.xyz, o.vNormalWs.xyz ) ) );

			o.vTangentUWs.xyz = vTangentUWs.xyz;
			o.vTangentVWs.xyz = cross( o.vNormalWs.xyz, vTangentUWs.xyz ) * vTangentUOs_flTangentVSign.w;
		}
		#endif

		return FinalizeVertex( o );
	}
}

//=========================================================================================================================

PS
{
    #include "common/pixel.hlsl"
	

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		//return float4(1, 0, 0, 1);
		return ShadingModelStandard::Shade( i, m );
	}
}
