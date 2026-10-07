using System.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using ImGuiNET;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// A small orientation gizmo pinned to a corner of the viewport: the world's horizontal plane and
/// its four cardinal axes, drawn through the camera's own basis so the ring tilts with pitch and
/// spins with yaw, with north called out.
///
/// <para>Drawn as an ImGui overlay rather than as <see cref="WireframeRenderer"/> geometry in a
/// corner viewport: the gizmo needs text labels and a fixed pixel size, both of which the 2D draw
/// list gives for free, and staying out of the GL pipeline means it cannot disturb the scene pass's
/// depth or viewport state. The projection below is still the camera's — only the rasterization is
/// 2D.</para>
///
/// <para>World axes here are in render space, where <see cref="Camera"/> puts the simulation's +Y
/// (heading 0, the world's north) along -Z and its +Z (up) along +Y.</para>
/// </summary>
public static class CompassGizmo {
	/// <summary>Render-space directions of the four compass points, and what to label each.</summary>
	private static readonly (Vector3 Direction, string Label)[] CardinalAxes = {
		(new Vector3(0f, 0f, -1f), "N"),
		(new Vector3(1f, 0f, 0f), "E"),
		(new Vector3(0f, 0f, 1f), "S"),
		(new Vector3(-1f, 0f, 0f), "W")
	};

	private const int RingSegments = 48;

	private static readonly uint RingColor = Color(210, 214, 224, 90);
	private static readonly uint SpokeColor = Color(210, 214, 224, 150);
	private static readonly uint NorthColor = Color(232, 72, 60, 255);
	private static readonly uint MinorPointColor = Color(120, 128, 142, 235);
	private static readonly uint LabelColor = Color(240, 242, 248, 255);
	private static readonly uint BackLabelColor = Color(240, 242, 248, 110);
	private static readonly uint UpColor = Color(120, 190, 255, 200);

	/// <summary>
	/// Draws the gizmo into its own borderless, input-transparent overlay window whose top-left
	/// corner is <paramref name="topLeft"/> and whose extent is <paramref name="size"/> pixels
	/// square, its lines and discs scaled to the display (<see cref="ScaledImGui.Scaled(float)"/>). Input-transparent matters: the editor picks objects with a viewport click, and a
	/// window that swallowed the mouse would put a dead square over the scene.
	/// </summary>
	public static void Draw(Camera camera, Vector2 topLeft, float size) {
		ImGui.SetNextWindowPos(topLeft);
		ImGui.SetNextWindowSize(new Vector2(size, size));
		ImGui.Begin("##compass", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs
			| ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings
			| ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);

		var drawList = ImGui.GetWindowDrawList();
		var center = topLeft + new Vector2(size * 0.5f, size * 0.5f);

		// Leave room for the labels, which are drawn centred on the ring itself.
		float radius = size * 0.5f - ImGui.GetFontSize() * 0.75f;

		// The camera's view basis. Right and up are rebuilt from forward the same way
		// Camera.ViewportPointToRay does, so a rolled camera rolls the gizmo with it.
		Vector3 forward = camera.Forward;
		Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
		Vector3 up = Vector3.Cross(right, forward);

		Vector2 Project(Vector3 direction) => center + new Vector2(
			Vector3.Dot(direction, right) * radius,
			-Vector3.Dot(direction, up) * radius);

		DrawRing(drawList, Project);

		// Straight up, so the gizmo still reads as an orientation when looking along the horizon and
		// the ring collapses to a line.
		drawList.AddLine(center, Project(new Vector3(0f, 1f, 0f)), UpColor, ScaledImGui.Scaled(1.5f));

		// Back to front, so a point behind the camera passes under the ones in front of it rather
		// than over them — the whole reason this is projected rather than drawn as a flat rose.
		foreach (var (direction, label) in CardinalAxes.OrderBy(axis => Vector3.Dot(axis.Direction, forward))) {
			DrawPoint(drawList, center, Project(direction), label, Vector3.Dot(direction, forward) >= 0f);
		}

		ImGui.End();
	}

	/// <summary>The world's horizontal plane, sampled around and projected — an ellipse under pitch.</summary>
	private static void DrawRing(ImDrawListPtr drawList, Func<Vector3, Vector2> project) {
		for (int i = 0; i < RingSegments; i++) {
			float from = i / (float)RingSegments * MathF.Tau;
			float to = (i + 1) / (float)RingSegments * MathF.Tau;
			drawList.AddLine(
				project(new Vector3(MathF.Sin(from), 0f, -MathF.Cos(from))),
				project(new Vector3(MathF.Sin(to), 0f, -MathF.Cos(to))),
				RingColor, ScaledImGui.Scaled(1f));
		}
	}

	private static void DrawPoint(ImDrawListPtr drawList, Vector2 center, Vector2 tip, string label,
			bool facingCamera) {
		bool north = label == "N";

		drawList.AddLine(center, tip, north ? NorthColor : SpokeColor, ScaledImGui.Scaled(north ? 2f : 1f));

		// North gets a solid disc at any angle; the other three only when they face the camera, so
		// the far side of the compass stays quiet.
		if (north || facingCamera) {
			drawList.AddCircleFilled(tip, ScaledImGui.Scaled(north ? 9f : 7f), north ? NorthColor : MinorPointColor);
		} else {
			drawList.AddCircle(tip, ScaledImGui.Scaled(7f), MinorPointColor, 0, ScaledImGui.Scaled(1f));
		}

		var labelSize = ImGui.CalcTextSize(label);
		drawList.AddText(tip - labelSize * 0.5f, facingCamera || north ? LabelColor : BackLabelColor, label);
	}

	/// <summary>Packs an RGBA colour the way ImGui stores one (little-endian ABGR).</summary>
	private static uint Color(byte r, byte g, byte b, byte a) =>
		((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
}
