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
/// </summary>
public sealed class EditorCamera {
	/// <summary>Cruise speed, in metres per second.</summary>
	public float CruiseSpeedMetersPerSecond { get; set; } = 30f;

	/// <summary>Multiplies <see cref="CruiseSpeedMetersPerSecond"/> while boosting (shift held).</summary>
	public float BoostMultiplier { get; set; } = 3f;

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

	/// <summary>Starts the camera at a sim position/heading — typically the scene's default camera start.</summary>
	public void ResetTo(Vec3i position, int headingBam) {
		WorldPosition = new Vector3(position.X, position.Y, position.Z);
		Yaw = BinaryAngle.ToRadians(headingBam);
		Pitch = 0f;
	}

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
			Pitch -= lookDelta.Y * LookSensitivity;
			Pitch = Math.Clamp(Pitch, -PitchLimit, PitchLimit);
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
		// stays in the horizontal plane.
		float horizontal = forwardAxis * cosPitch;
		var move = new Vector3(
			horizontal * sinYaw + strafeAxis * cosYaw,
			horizontal * cosYaw - strafeAxis * sinYaw,
			forwardAxis * sinPitch + verticalAxis);

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
