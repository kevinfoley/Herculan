using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.Sim;

namespace Herculan.Es1.View;

/// <summary>
/// The main camera <c>Sim_StartMission</c> builds and attaches to the player's machine
/// (docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc): <c>Cam_AttachTo</c> (<c>ES1DBSIM 0005923c</c>)
/// takes the machine's eye from <c>Mech_GetAimPoint</c>, its slot <c>+0x34</c>, and the camera is set to mode 2, which
/// <c>Cam_Update</c> (<c>000004b8</c>) places through the machine's camera node (docs/es1/simulation/external-views.md#cam_update).
/// </summary>
public sealed class Es1EyeCamera {
	/// <summary>The main camera's near plane, <c>0x100</c>, its focal shift 8 (docs/es1/rendering/view-transform-and-projection.md#differences-from-es2).</summary>
	public const int NearPlane = 0x100;

	/// <summary><c>2^shift</c>, the focal length in the 320 × 200 screen's pixels.</summary>
	public const int FocalPixels = 1 << 8;

	public Es1EyeCamera(Es1Mech machine) {
		Machine = machine;

		// Mech_GetAimPoint (00010ee0): the eye (0, +0x64, +0x66), file offsets 98 and 100.
		Eye = (0, machine.Type.Chassis.EyeOffsetY, machine.Type.Chassis.EyeOffsetZ);
	}

	/// <summary><c>+0x32</c>, the object the camera rides in.</summary>
	public Es1Mech Machine { get; }

	/// <summary><c>+0x3e</c>, the eye in the camera node's frame.</summary>
	public (short X, short Y, short Z) Eye { get; }

	/// <summary><c>+0x44</c>–<c>+0x48</c>, the rotation added to the attached view, zeroed when the camera is built.</summary>
	public (short X, short Y, short Z) AddedRotation { get; } = (0, 0, 0);

	/// <summary>
	/// <c>Cam_Update</c>'s mode 2: the eye through the node <c>Mech_GetAimNodeTransform</c> (<c>00013204</c>) gives, the
	/// chassis' camera part's, composed with the machine's frame, and the view from that matrix through
	/// <c>Transform_RotationToEuler</c> plus <see cref="AddedRotation"/>; with no such part, the machine's frame and its own
	/// angles.
	/// </summary>
	public (Vec3i Position, (short X, short Y, short Z) View) Update() {
		var frame = Machine.Frame;
		int transformId = Machine.Type.Animation?.TransformIdOfPart(Machine.Type.Chassis.CameraPartId) ?? -1;
		if (transformId < 0 || Machine.Shape == null) {
			return (frame.TransformPoint(Eye.X, Eye.Y, Eye.Z), Machine.Rotation);
		}

		var node = Transform3.Concat(Machine.Shape.NodeTransform(transformId), frame);
		var (x, y, z) = node.ToEuler();
		return (node.TransformPoint(Eye.X, Eye.Y, Eye.Z),
			((short)(x + AddedRotation.X), (short)(y + AddedRotation.Y), (short)(z + AddedRotation.Z)));
	}

	/// <summary>
	/// Puts <see cref="Update"/>'s eye on <paramref name="camera"/> with the projection the view window gives: the focal
	/// length <see cref="FocalPixels"/> in the screen's pixels over the window's rows, its axis on the window's projection
	/// centre.
	/// </summary>
	public void ApplyTo(Camera camera, Es1ViewWindow window) {
		var (position, view) = Update();
		camera.Position = position;
		camera.Pitch = view.X;
		camera.Roll = view.Y;
		camera.Yaw = -view.Z;
		ApplyProjection(camera, window);
	}

	/// <summary>The window's projection on <paramref name="camera"/>, for a viewport the window's own size in the screen's pixels.</summary>
	public static void ApplyProjection(Camera camera, Es1ViewWindow window) {
		var (width, height) = Size(window);
		camera.FieldOfView = 2f * MathF.Atan(height / 2f / FocalPixels);
		camera.PrincipalPoint = new System.Numerics.Vector2((window.CentreX - window.X0) / (float)width,
			(window.CentreY - window.Y0) / (float)height);
		camera.NearPlane = WorldScale.DistanceToRender(NearPlane);
	}

	/// <summary>The window's size in screen pixels, its right column and bottom row included and held to the screen.</summary>
	public static (int Width, int Height) Size(Es1ViewWindow window) => (
		Math.Max(Math.Min(window.X1, Es1CockpitViews.ScreenWidth - 1) - window.X0 + 1, 1),
		Math.Max(Math.Min(window.Y1, Es1CockpitViews.ScreenHeight - 1) - window.Y0 + 1, 1));
}
