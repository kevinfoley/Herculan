using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Silk.NET.Input;

namespace Herculan.Engine.Editor;

/// <summary>
/// A Unity-Editor-style free-fly observer camera: hold the right mouse button and move the mouse
/// to look, WASD/arrow keys to move, shift to move faster. Unlike <see cref="Sim.FlyCameraObject"/>
/// this is not a <see cref="Sim.SimObject"/> — it is tool-only input handling with no place in the
/// simulation, so it lives in the editor host and drives a <see cref="Camera"/> directly from
/// float deltas each frame rather than through the fixed-point sim tick.
///
/// <para>It also has a top-down view (<see cref="EnterTopDown"/>), looking as nearly straight down as
/// <see cref="PitchLimit"/> lets it, north up; leaving it returns to the pose it was entered from.</para>
/// </summary>
public sealed class EditorCamera {
	/// <summary>Cruise speed, in meters per second.</summary>
	public float CruiseSpeedMetersPerSecond { get; set; } = 90f;

	/// <summary>Multiplies <see cref="CruiseSpeedMetersPerSecond"/> while boosting (shift held).</summary>
	public float BoostMultiplier { get; set; } = 4f;

	/// <summary>Radians of yaw/pitch per pixel of mouse movement while looking.</summary>
	public float LookSensitivity { get; set; } = 0.0025f;

	/// <summary>Pitch clamp, just short of straight up/down (matches <see cref="Sim.FlyCameraObject.PitchLimit"/>'s intent).</summary>
	public float PitchLimit { get; set; } = MathF.PI / 2f * 0.98f;

	/// <summary>Eye position in world units (float, not the sim's fixed <see cref="Vec3i"/>).</summary>
	public Vector3 WorldPosition { get; set; }

	/// <summary>Yaw in radians. 0 faces world +Y, increasing turns toward +X — same convention as <see cref="Camera.Yaw"/>.</summary>
	public float Yaw { get; set; }

	/// <summary>Pitch in radians. Positive looks up.</summary>
	public float Pitch { get; set; }

	/// <summary>Extra distance added when framing the camera to an object.</summary>
	public float ExtraFrameDistance { get; set; } = 18 * WorldScale.WorldUnitsPerMeter;

	// The pose the top-down view was entered from, which leaving it returns to; null outside it.
	private (Vector3 Position, float Yaw, float Pitch)? _beforeTopDown;

	/// <summary>
	/// Whether the camera is in its top-down view. Looking then turns it about the vertical alone, and moving keeps
	/// to the horizontal plane, forward being up the screen.
	/// </summary>
	public bool TopDown => _beforeTopDown != null;

	/// <summary>Starts the camera at a sim position/heading — typically the scene's default camera start.</summary>
	public void ResetTo(Vec3i position, int headingBam) {
		_beforeTopDown = null;
		WorldPosition = new Vector3(position.X, position.Y, position.Z);
		Yaw = BinaryAngle.ToRadians(headingBam);
		Pitch = 0f;
	}

	/// <summary>
	/// Switches to the top-down view, north up, centered on <paramref name="ground"/> (world units) from
	/// <paramref name="height"/> world units above it. The pose it leaves is kept for <see cref="LeaveTopDown"/>.
	/// </summary>
	public void EnterTopDown(Vector3 ground, float height) {
		if (TopDown) {
			return;
		}

		_beforeTopDown = (WorldPosition, Yaw, Pitch);
		Yaw = 0f;
		Pitch = -PitchLimit;
		WorldPosition = ground - Forward * (height / MathF.Sin(PitchLimit));
	}

	/// <summary>Leaves the top-down view for the pose it was entered from.</summary>
	public void LeaveTopDown() {
		if (_beforeTopDown is not { } before) {
			return;
		}

		(WorldPosition, Yaw, Pitch) = before;
		_beforeTopDown = null;
	}

	/// <summary>The least angle below level <see cref="Frame"/> looks down at what it frames, in radians (20 degrees).</summary>
	public float FramePitch { get; set; } = 20f * MathF.PI / 180f;

	/// <summary>The nearest <see cref="Frame"/> stands to what it frames, in meters.</summary>
	public float MinFrameDistanceMeters { get; set; } = 25f;

	/// <summary>
	/// Frame the camera around a sphere with the given center and radius. Never farther than 
	/// <paramref name="maxDistance"/>, so the sphere's center stays short of the far plane.
	/// </summary>
	public void Frame(Vector3 center, float radius, float verticalFieldOfView, float maxDistance) {
		Pitch = MathF.Min(Pitch, -FramePitch);

		float distance = MathF.Min(MathF.Max(radius / MathF.Sin(verticalFieldOfView / 2f),
			MinFrameDistanceMeters * WorldScale.WorldUnitsPerMeter), maxDistance);

		// Add some extra distance so the camera doesn't crop too tightly on small objects.
		// This is fixed rather than proportional so that the camera doesn't pull too far
		// back on very large objects like trigger areas.
		distance += ExtraFrameDistance;

		WorldPosition = center - Forward * distance;
	}

	/// <summary>The direction the eye looks, in world axes.</summary>
	private Vector3 Forward =>
		new(MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Cos(Pitch) * MathF.Cos(Yaw), MathF.Sin(Pitch));

	/// <summary>
	/// Advances the camera by one frame. <paramref name="lookDelta"/> is the mouse's screen-space
	/// movement (+X right, +Y down) since last frame, applied only while <paramref name="lookActive"/>
	/// is true. Movement keys are read only when <paramref name="acceptKeyboardInput"/> is true, so
	/// the caller can withhold them while ImGui wants the keyboard.
	/// </summary>
	public void Update(double deltaSeconds, IKeyboard? keyboard, Vector2 lookDelta, bool lookActive,
			bool acceptKeyboardInput) {
		if (lookActive) {
			Yaw += lookDelta.X * LookSensitivity;
			if (!TopDown) {
				Pitch -= lookDelta.Y * LookSensitivity;
				Pitch = Math.Clamp(Pitch, -PitchLimit, PitchLimit);
			}
		}

		if (keyboard == null || !acceptKeyboardInput) {
			return;
		}

		int forwardAxis = Math.Clamp(Axis(keyboard, Key.W, Key.S) + Axis(keyboard, Key.Up, Key.Down), -1, 1);
		int strafeAxis = Math.Clamp(Axis(keyboard, Key.D, Key.A) + Axis(keyboard, Key.Right, Key.Left), -1, 1);
		int verticalAxis = Axis(keyboard, Key.E, Key.Q);

		if (forwardAxis == 0 && strafeAxis == 0 && verticalAxis == 0) {
			return;
		}

		bool boost = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
		float speed = CruiseSpeedMetersPerSecond * WorldScale.WorldUnitsPerMeter * (boost ? BoostMultiplier : 1f);

		float sinYaw = MathF.Sin(Yaw), cosYaw = MathF.Cos(Yaw);
		float sinPitch = MathF.Sin(Pitch), cosPitch = MathF.Cos(Pitch);

		// Same convention as FlyCameraObject.Tick: forward carries the pitch component, strafe
		// stays in the horizontal plane. Looking down from above, forward is up the screen instead.
		float horizontal = TopDown ? forwardAxis : forwardAxis * cosPitch;
		var move = new Vector3(
			horizontal * sinYaw + strafeAxis * cosYaw,
			horizontal * cosYaw - strafeAxis * sinYaw,
			(TopDown ? 0f : forwardAxis * sinPitch) + verticalAxis);

		WorldPosition += Vector3.Normalize(move) * speed * (float)deltaSeconds;
	}

	/// <summary>Copies this frame's pose onto a render camera.</summary>
	public void ApplyTo(Camera camera) {
		camera.Position = new Vec3i(
			(int)MathF.Round(WorldPosition.X), (int)MathF.Round(WorldPosition.Y), (int)MathF.Round(WorldPosition.Z));
		camera.Yaw = BinaryAngle.FromRadians(Yaw);
		camera.Pitch = BinaryAngle.FromRadians(Pitch);
	}

	private static int Axis(IKeyboard keyboard, Key positive, Key negative) =>
		(keyboard.IsKeyPressed(positive) ? 1 : 0) - (keyboard.IsKeyPressed(negative) ? 1 : 0);
}
