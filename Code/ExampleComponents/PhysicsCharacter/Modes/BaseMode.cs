﻿
namespace Sandbox.PhysicsCharacterMode;

/// <summary>
/// A move mode for this character
/// </summary>
public partial class BaseMode : Component
{
	public virtual bool AllowGrounding => false;

	[RequireComponent]
	public PhysicsCharacter Controller { get; set; }

	public virtual int Score( PhysicsCharacter controller ) => 0;

	/// <summary>
	/// A nice place for you to do something
	/// </summary>
	public virtual void PostPhysicsStep()
	{

	}

	public virtual void UpdateRigidBody( Rigidbody body )
	{
		body.Gravity = true;
		body.LinearDamping = 0.1f;
		body.AngularDamping = 1f;
	}

	public virtual void AddVelocity()
	{
		var body = Controller.Body;
		var wish = Controller.WishVelocity;
		if ( wish.IsNearZeroLength ) return;

		var groundFriction = 0.25f + Controller.GroundFriction * 10;
		var groundVelocity = Controller.GroundVelocity;

		var z = body.Velocity.z;

		var velocity = (body.Velocity - Controller.GroundVelocity);
		var speed = velocity.Length;

		var maxSpeed = MathF.Max( wish.Length, speed );

		if ( Controller.IsOnGround )
		{
			var amount = 1 * groundFriction;
			velocity = velocity.AddClamped( wish * amount, wish.Length * amount );
		}
		else
		{
			var amount = 0.05f;
			velocity = velocity.AddClamped( wish * amount, wish.Length );
		}

		if ( velocity.Length > maxSpeed )
			velocity = velocity.Normal * maxSpeed;

		velocity += groundVelocity;

		if ( Controller.IsOnGround )
		{
			velocity.z = z;
		}

		body.Velocity = velocity;
	}

	/// <summary>
	/// This mode has just started
	/// </summary>
	public virtual void OnModeBegin()
	{

	}

	/// <summary>
	/// This mode has stopped. We're swapping to another move mode.
	/// </summary>
	public virtual void OnModeEnd( BaseMode next )
	{

	}
}