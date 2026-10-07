using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.View;

/// <summary>
/// How a <see cref="ViewCamera"/> places itself — the <c>CAM</c> object's <c>+0x36</c>.
/// </summary>
public enum ViewCameraMode : short {
	/// <summary>Flies free, steered by the controls. Reached only through the developer keys.</summary>
	Free = 0,

	/// <summary>Circles its object's orbit centre — the outside view.</summary>
	Orbit = 1,

	/// <summary>Rides its object's eye — the cockpit.</summary>
	Attached = 2,

	/// <summary>Follows the player's own path, a fixed time behind — the chase view.</summary>
	Chase = 3,
}

/// <summary>
/// DBSIM's view camera, class <c>CAM</c> (<c>0x4e</c> bytes, vtable <c>0049f900</c>, a
/// <c>TSCamera</c>): the object <c>ViewObjectPtr</c> points at, placed once a frame by
/// <c>Cam_Update</c> (<c>004011a0</c>) in whichever of four <see cref="ViewCameraMode"/>s it is in. Field names
/// follow their offsets in the original; the rules are docs/retail/simulation/external-views.md's
/// "The camera object".
///
/// <para>Every angle and rate is a 16-bit binary angle and wraps as one, and nothing here is scaled
/// by the tick length: retail steps each rate once a frame, so the orbit turns at a rate per tick.</para>
/// </summary>
public sealed class ViewCamera {
	/// <summary>Least and most orbit distance, <c>0x9c4</c> and <c>30000</c> world units.</summary>
	public const short MinDistance = 0x9c4;

	/// <inheritdoc cref="MinDistance"/>
	public const short MaxDistance = 30000;

	/// <summary>How far the orbit and the free camera may pitch each way.</summary>
	public const short PitchLimit = 0x3000;

	/// <summary>Least height above the ground the orbit and the free camera keep.</summary>
	public const int GroundClearance = 0x226;

	/// <summary><c>+0x04</c>-<c>+0x0c</c>: the eye, in world units.</summary>
	public Vec3i Position { get; private set; }

	/// <summary><c>+0x10</c>-<c>+0x14</c>: the view's euler triple, X pitch, Y roll, Z heading.</summary>
	public (short X, short Y, short Z) Rotation { get; private set; }

	/// <summary><c>+0x22</c>: the orbit distance.</summary>
	public short Distance { get; private set; }

	/// <summary><c>+0x24</c>: the orbit's zoom rate, or the free camera's speed.</summary>
	public short ZoomRate { get; private set; }

	/// <summary><c>+0x26</c>, <c>+0x28</c>, <c>+0x2a</c>: the orbit's pitch, roll and heading, the
	/// last relative to the object's own.</summary>
	public short OrbitPitch { get; private set; }

	/// <inheritdoc cref="OrbitPitch"/>
	public short OrbitRoll { get; private set; }

	/// <inheritdoc cref="OrbitPitch"/>
	public short OrbitYaw { get; private set; }

	/// <summary><c>+0x2c</c>, <c>+0x2e</c>, <c>+0x30</c>: the rates those three change at each frame.</summary>
	public short PitchRate { get; private set; }

	/// <inheritdoc cref="PitchRate"/>
	public short RollRate { get; private set; }

	/// <inheritdoc cref="PitchRate"/>
	public short YawRate { get; private set; }

	/// <summary><c>+0x32</c>: the object this camera views.</summary>
	public SimObject? Target { get; private set; }

	/// <summary><c>+0x36</c>.</summary>
	public ViewCameraMode Mode { get; private set; }

	/// <summary>
	/// The object this camera rides, or null in any mode but <see cref="ViewCameraMode.Attached"/> — the
	/// one object <c>Cam_IsAttachedTo</c> (<c>00401078</c>) answers true for.
	/// </summary>
	public SimObject? AttachedTo => Mode == ViewCameraMode.Attached ? Target : null;

	/// <summary><c>+0x38</c>: the orbit centre in the target's frame.</summary>
	public Vec3i OrbitCentre { get; private set; }

	/// <summary><c>+0x3e</c>: the attached eye in the target's frame.</summary>
	public Vec3i Eye { get; private set; }

	/// <summary>
	/// <c>+0x4a</c>: while set, nothing may change the camera's mode, target or rates. The
	/// player-death camera sets it (<see cref="ExternalViewChain.DeathCameraRunning"/>).
	/// </summary>
	public bool Locked { get; set; }

	/// <summary><c>Cam_SetMode</c> (<c>00401148</c>) and the inline copies of it: sets the mode unless locked.</summary>
	public void SetMode(ViewCameraMode mode) {
		if (!Locked) {
			Mode = mode;
		}
	}

	/// <summary>
	/// The player-death camera's opening pose, written straight into <c>+0x22</c> and
	/// <c>+0x26</c>-<c>+0x2a</c> unless locked: the orbit's pitch and heading, its roll levelled, and its
	/// distance. The rates are left as they were.
	/// </summary>
	public void PoseOrbit(short pitch, short heading, short distance) {
		if (Locked) {
			return;
		}

		OrbitPitch = pitch;
		OrbitRoll = 0;
		OrbitYaw = heading;
		Distance = distance;
	}

	/// <summary>
	/// The field-by-field copy <c>ViewChain_ViewObject</c> (<c>0045df18</c>) makes before re-attaching a camera to a new
	/// object: everything but the vtable.
	/// </summary>
	public void CopyFrom(ViewCamera other) {
		Position = other.Position;
		Rotation = other.Rotation;
		Distance = other.Distance;
		ZoomRate = other.ZoomRate;
		OrbitPitch = other.OrbitPitch;
		OrbitRoll = other.OrbitRoll;
		OrbitYaw = other.OrbitYaw;
		PitchRate = other.PitchRate;
		RollRate = other.RollRate;
		YawRate = other.YawRate;
		Target = other.Target;
		Mode = other.Mode;
		OrbitCentre = other.OrbitCentre;
		Eye = other.Eye;
		Locked = other.Locked;
	}

	/// <summary>
	/// <c>Cam_AttachTo</c> (<c>0045ed94</c>): asks <paramref name="target"/> for its <see cref="SimObject.ViewMounts"/>
	/// and attaches to it twice over — <c>Cam_AttachEye</c> (<c>0040111c</c>) takes the eye and sets
	/// <see cref="ViewCameraMode.Attached"/>, then <c>Cam_AttachOrbit</c> (<c>0040109c</c>) takes the orbit centre, starts
	/// the orbit over from directly behind at no distance, and sets <see cref="ViewCameraMode.Orbit"/>.
	/// </summary>
	public void AttachTo(SimObject target) {
		if (Locked) {
			return;
		}

		var (eye, orbitCentre) = target.ViewMounts;
		Eye = eye;
		OrbitCentre = orbitCentre;
		Target = target;
		OrbitPitch = 0;
		OrbitRoll = 0;
		OrbitYaw = 0;
		Distance = 0;
		PitchRate = 0;
		RollRate = 0;
		YawRate = 0;
		ZoomRate = 0;
		Mode = ViewCameraMode.Orbit;
	}

	/// <summary>
	/// <c>Cam_Steer</c> (<c>00401c74</c>), once a frame from <c>Sim_PollPlayerInput</c>: steers the free camera or the
	/// orbit off the steering and throttle axes, zero unless the controls drive the camera. With the
	/// trigger held the throttle axis zooms (or, free, flies) instead of pitching. The other two modes
	/// ignore it, so their rates hold until the camera is next in one of these.
	/// </summary>
	public void Steer(short steer, short throttle, bool trigger) {
		if (Locked) {
			return;
		}

		if (Mode == ViewCameraMode.Free) {
			short zoom = 0;
			YawRate = Approach(YawRate, 0x800, (short)-steer, 0xc0);
			if (trigger) {
				zoom = (short)-throttle;
				throttle = 0;
			}

			ZoomRate = Approach(ZoomRate, 0x400, zoom, 0x50);
			PitchRate = Approach(PitchRate, 0x800, throttle, 0xc0);
		} else if (Mode == ViewCameraMode.Orbit) {
			short zoom = 0;
			YawRate = Approach(YawRate, 0x800, steer, 0xc0);
			if (trigger) {
				zoom = throttle;
				throttle = 0;
			}

			ZoomRate = Approach(ZoomRate, 0x400, zoom, 0x50);
			PitchRate = Approach(PitchRate, 0x800, throttle, 0xc0);
		}

		// Cam_ApproachRate (00401c20) and Cam_ApproachZoom (00401c4c): the axis scaled by a Q8 gain, reached at a fixed step.
		static short Approach(short current, int gain, short axis, short step) {
			SimMath.RateLimitedMoveToward(ref current, (short)SimMath.Q8Multiply(gain, axis), step);
			return current;
		}
	}

	/// <summary>
	/// <c>Cam_Update</c> (<c>004011a0</c>)'s placement, after it has recorded the player into
	/// <paramref name="trail"/>. <paramref name="terrain"/> may be null, which drops the ground
	/// clearance; retail always has a zone.
	/// </summary>
	public void Update(HeightGrid? terrain, PlayerTrail trail) {
		switch (Mode) {
			case ViewCameraMode.Free:
				UpdateFree(terrain);
				break;
			case ViewCameraMode.Orbit:
				UpdateOrbit(terrain);
				break;
			case ViewCameraMode.Attached:
				UpdateAttached();
				break;
			case ViewCameraMode.Chase:
				if (trail.Chase() is { } chase) {
					Position = chase.Position;
					Rotation = chase.Rotation;
				}
				break;
		}
	}

	/// <summary>
	/// Hands the placement to a render <see cref="Camera"/>. Camera yaw runs opposite to a simulation
	/// heading (see <c>MissionScene.TransformOf</c>).
	/// </summary>
	public void ApplyTo(Camera camera) {
		camera.Position = Position;
		camera.Pitch = Rotation.X & 0xffff;
		camera.Roll = Rotation.Y & 0xffff;
		camera.Yaw = -Rotation.Z & 0xffff;
	}

	private void UpdateFree(HeightGrid? terrain) {
		short yaw = (short)(Rotation.Z + YawRate);
		short pitch = (short)(Rotation.X + PitchRate);
		if (pitch > PitchLimit || pitch < -PitchLimit) {
			pitch = pitch < 0 ? (short)-PitchLimit : PitchLimit;
			PitchRate = 0;
		}

		Rotation = (pitch, (short)(Rotation.Y + RollRate), yaw);

		var frame = Transform3.FromEuler(Rotation.X, Rotation.Y, Rotation.Z);
		frame.X = Position.X;
		frame.Y = Position.Y;
		frame.Z = Position.Z;
		var moved = frame.TransformPoint(0, ZoomRate, 0);

		// The floor is taken under where the camera was, not where it is going, and rises with the
		// slope there: the face normal Terrain_FaceNormalAt (0046e394) returns, its ground-plane length through Q10 x 500.
		if (terrain != null) {
			short slope = terrain.SurfaceNormalAt(Position.X, Position.Y) is { } normal
				? (short)SimMath.FastMagnitude2D(normal.X, normal.Y)
				: (short)0;
			int floor = terrain.HeightAtWorld(Position.X, Position.Y) + SimMath.Q10Multiply(500, slope)
				+ GroundClearance;
			if (moved.Z < floor) {
				moved = new Vec3i(moved.X, moved.Y, floor);
			}
		}

		Position = moved;
	}

	private void UpdateOrbit(HeightGrid? terrain) {
		if (Target is not { } target) {
			return;
		}

		short previousPitch = Rotation.X;

		short distance = (short)(Distance + ZoomRate);
		Distance = distance >= MaxDistance ? MaxDistance : distance <= MinDistance ? MinDistance : distance;

		OrbitYaw += YawRate;
		OrbitPitch += PitchRate;
		OrbitRoll += RollRate;
		if (OrbitPitch > PitchLimit || OrbitPitch < -PitchLimit) {
			OrbitPitch = OrbitPitch < 0 ? (short)-PitchLimit : PitchLimit;
			PitchRate = 0;
		}

		short heading = (short)(OrbitYaw + (short)target.Heading);
		Rotation = (OrbitPitch, OrbitRoll, heading);
		Position = OrbitEye(target);

		if (terrain == null) {
			return;
		}

		int floor = terrain.HeightAtWorld(Position.X, Position.Y) + GroundClearance;
		if (Position.Z >= floor) {
			return;
		}

		Position = new Vec3i(Position.X, Position.Y, floor);

		// Pitching up swings the eye down into the ground, so a pitch that has just done that is taken
		// back, and the view with it — to last frame's view pitch rather than the orbit's own.
		if (PitchRate > 0) {
			OrbitPitch -= PitchRate;
			PitchRate = 0;
			Rotation = (previousPitch, OrbitRoll, heading);
			Position = OrbitEye(target);

			floor = terrain.HeightAtWorld(Position.X, Position.Y) + GroundClearance;
			if (Position.Z < floor) {
				Position = new Vec3i(Position.X, Position.Y, floor);
			}
		}
	}

	// The orbit centre put through the target's frame, and the eye Distance back from it along the
	// view's own forward axis.
	private Vec3i OrbitEye(SimObject target) {
		var back = Transform3.FromEuler(Rotation.X, Rotation.Y, Rotation.Z).RotateVector(0, -Distance, 0);
		var centre = target.WorldFrame.TransformPoint(OrbitCentre.X, OrbitCentre.Y, OrbitCentre.Z);
		return centre + back;
	}

	private void UpdateAttached() {
		if (Target is not { } target) {
			return;
		}

		if (target.ViewNodeFrame is { } node) {
			Position = node.TransformPoint(Eye.X, Eye.Y, Eye.Z);
			Rotation = node.ToEuler();
		} else {
			var frame = target.WorldFrame;
			Position = frame.TransformPoint(Eye.X, Eye.Y, Eye.Z);
			Rotation = (target.Pitch, target.Roll, (short)target.Heading);
		}
	}
}
