using Sandbox.Citizen;

//
// This all exists to test the PhysicsCharacterController 
// It needs a clean up !
//

public class PhysicalPlayerController : Component
{
	[RequireComponent] public PhysicalCharacterController Controller { get; set; }

	public Vector3 WishVelocity { get; private set; }

	[Property] public GameObject Body { get; set; }
	[Property] public GameObject Eye { get; set; }
	[Property] public CitizenAnimationHelper AnimationHelper { get; set; }
	[Property] public bool FirstPerson { get; set; }

	[Sync] public Angles EyeAngles { get; set; }
	[Sync] public bool IsRunning { get; set; }
	[Sync] public bool IsDucked { get; set; }

	protected override void OnEnabled()
	{
		base.OnEnabled();

		if ( IsProxy )
			return;

		var cam = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		if ( cam.IsValid() )
		{
			var ee = cam.WorldRotation.Angles();
			ee.roll = 0;
			EyeAngles = ee;
		}
	}

	protected override void OnUpdate()
	{
		// Eye input
		if ( !IsProxy )
		{
			var ee = EyeAngles;
			ee += Input.AnalogLook * 0.5f;
			ee.roll = 0;
			EyeAngles = ee;

			var cam = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();

			var lookDir = EyeAngles.ToRotation();

			if ( FirstPerson )
			{
				cam.WorldPosition = Eye.WorldPosition;
				cam.WorldRotation = lookDir;

				foreach ( var c in Body.GetComponentsInChildren<ModelRenderer>() )
				{
					c.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
				}

			}
			else
			{
				cam.WorldPosition = WorldPosition + lookDir.Backward * 300 + Vector3.Up * 75.0f;
				cam.WorldRotation = lookDir;

				foreach ( var c in Body.GetComponentsInChildren<ModelRenderer>() )
				{
					c.RenderType = ModelRenderer.ShadowRenderType.On;
				}

			}

			IsRunning = Input.Down( "Run" );
		}

		if ( !Controller.IsValid() ) return;

		var eee = EyeAngles;
		eee.yaw += Controller.GroundYaw;
		EyeAngles = eee;

		float moveRotationSpeed = 0;

		// rotate body to look angles
		if ( Body.IsValid() )
		{
			var targetAngle = new Angles( 0, EyeAngles.yaw, 0 ).ToRotation();

			var v = Controller.Velocity.WithZ( 0 );

			if ( v.Length > 10.0f )
			{
				targetAngle = Rotation.LookAt( v, Vector3.Up );
			}

			float rotateDifference = Body.WorldRotation.Distance( targetAngle );

			if ( rotateDifference > 50.0f || Controller.Velocity.Length > 10.0f )
			{
				var newRotation = Rotation.Lerp( Body.WorldRotation, targetAngle, Time.Delta * 2.0f );

				// We won't end up actually moving to the targetAngle, so calculate how much we're actually moving
				var angleDiff = Body.WorldRotation.Angles() - newRotation.Angles(); // Rotation.Distance is unsigned
				moveRotationSpeed = angleDiff.yaw / Time.Delta;

				Body.WorldRotation = newRotation;
			}
		}


		if ( AnimationHelper.IsValid() )
		{
			AnimationHelper.WithVelocity( Controller.Velocity );
			AnimationHelper.WithWishVelocity( WishVelocity );
			AnimationHelper.IsGrounded = Controller.IsOnGround;
			AnimationHelper.MoveRotationSpeed = moveRotationSpeed;
			AnimationHelper.WithLook( EyeAngles.Forward, 1, 1, 1.0f );
			AnimationHelper.MoveStyle = IsRunning ? CitizenAnimationHelper.MoveStyles.Run : CitizenAnimationHelper.MoveStyles.Walk;
			AnimationHelper.DuckLevel = IsDucked ? 1 : 0;
		}
	}

	[Broadcast]
	public void OnJump()
	{
		AnimationHelper?.TriggerJump();
	}

	TimeSince timeSinceJump;

	protected override void OnFixedUpdate()
	{
		if ( IsProxy )
			return;

		BuildWishVelocity();

		if ( Controller.TimeSinceGrounded < 0.3f && Input.Down( "Jump" ) && timeSinceJump > 0.5f )
		{
			timeSinceJump = 0;

			var jumpDir = WishVelocity + Vector3.Up * 1200;

			Controller.Punch( jumpDir.Normal * 130 );
			OnJump();
		}

		if ( Input.Pressed( "score" ) ) FirstPerson = !FirstPerson;

		IsDucked = Input.Down( "duck" );

		if ( IsDucked )
		{
			Controller.BodyHeight = 40;
		}
		else
		{
			Controller.BodyHeight = 64;
		}

		Controller.WishVelocity = WishVelocity.WithZ( 0 );

	}

	public void BuildWishVelocity()
	{
		var rot = EyeAngles.ToRotation();

		WishVelocity = rot * Input.AnalogMove;
		WishVelocity = WishVelocity.WithZ( 0 );

		if ( !WishVelocity.IsNearZeroLength ) WishVelocity = WishVelocity.Normal;

		if ( Input.Down( "Run" ) ) WishVelocity *= 320.0f;
		else WishVelocity *= 110.0f;
	}
}