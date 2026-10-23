using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.World;
using ImGui = ImGuiNET.ImGui;

namespace Herculan.Engine.Editor;

/// <summary>
/// What the editor draws over the placed objects and the groups they belong to: the selection boxes,
/// the outlines that stand in for a waiting group's members, the selected group's own point and
/// heading, and a line from it to whatever each of its orders names. Groups, their members and their orders' subjects are
/// the layout's (<see cref="MissionIndex"/>), by GUID. A group kept out of the scene (<see cref="PlannedGroup.OffScene"/>)
/// has none of it, nor is it a line's subject.
/// </summary>
internal sealed class GroupOverlay {
	private static readonly Vector3 WaitingColor = new(0.55f, 0.6f, 0.72f);
	private static readonly Vector3 SubjectColor = new(1f, 0.5f, 0.8f);

	/// <summary>How long the selected group's heading arrow is, in world units (about 50 m).</summary>
	private const int ArrowLength = 8000;

	/// <summary>How long each stroke of the arrow's head is, in world units.</summary>
	private const int ArrowHeadLength = 2500;

	private readonly MissionIndex _index;
	private readonly DrapedLines _drape;
	private readonly ScenePicker _picker;

	public GroupOverlay(MissionIndex index, DrapedLines drape, ScenePicker picker) {
		_index = index;
		_drape = drape;
		_picker = picker;
	}

	/// <summary>
	/// The boxes: a highlight around each selected object, and a dim outline around every member of a
	/// waiting group when the settings outline rather than draw them.
	/// </summary>
	public void DrawObjects(WireframeRenderer wireframe, Camera camera, float aspect, EditorSettings settings,
			SelectionState selection) {
		var highlighted = HighlightedObjects(selection);

		foreach (var pickable in _picker.Pickables) {
			if (highlighted.Contains(pickable.SceneObject)) {
				wireframe.DrawBox(camera, pickable.CenterRender, pickable.RadiusRender, MissionOverlay.SelectedColor, aspect);
			} else if (settings.WaitingGroups == WaitingGroupDisplay.Outlined && _index.IsWaiting(pickable.SceneObject)) {
				wireframe.DrawBox(camera, pickable.CenterRender, pickable.RadiusRender, WaitingColor, aspect);
			}
		}
	}

	/// <summary>The objects the selection boxes: the selected object, a group's members, or an objective's subject.</summary>
	private HashSet<Scene.SceneObject> HighlightedObjects(SelectionState selection) => selection.Current switch {
		ObjectSelection selected when _index.ObjectOf(selected) is { } placed => new HashSet<Scene.SceneObject> { placed },
		GroupSelection { Guid: var guid } => _index.MembersOf(guid).ToHashSet(),
		ObjectiveSelection selected when _index.Layout.Objectives.FirstOrDefault(objective => objective.Index == selected.Record)
				is { } objective => _index.SubjectObjects(objective.Fields).ToHashSet(),
		_ => new HashSet<Scene.SceneObject>()
	};

	// The selected group, or null when a group is not selected or it is out of the scene.
	private PlannedGroup? SelectedGroup(SelectionState selection) =>
		selection.Current is GroupSelection { Guid: var guid } && _index.Group(guid) is { OffScene: false } group ? group : null;

	/// <summary>
	/// The selected group's point as a post, its heading as an arrow on the ground, and a line from
	/// the point to each of its orders' subjects.
	/// </summary>
	public void DrawSelectedGroup(WireframeRenderer wireframe, Camera camera, float aspect, SelectionState selection) {
		if (SelectedGroup(selection) is not { } group) {
			return;
		}

		var setup = group.Setup;
		var marker = new List<Vector3>();
		_drape.AddPost(marker, setup.Point.X, setup.Point.Y, MissionOverlay.PostHeight);
		AddArrow(marker, setup.Point, setup.Heading);
		MissionOverlay.DrawLines(wireframe, camera, aspect, marker, MissionOverlay.SelectedColor);

		var subjects = new List<Vector3>();
		foreach (var (_, _, at) in Subjects(group)) {
			_drape.AddEdge(subjects, setup.Point, at);
		}

		MissionOverlay.DrawLines(wireframe, camera, aspect, subjects, SubjectColor);
	}

	/// <summary>
	/// The labels: each waiting group's gate and arrival at its point, unless the settings hide waiting
	/// groups, and each of the selected group's order subjects at the far end of its line. A group and an action are
	/// numbered by their place in the layout's row 16 and row 10.
	/// </summary>
	public void DrawLabels(Camera camera, EditorSettings settings, SelectionState selection) {
		var drawList = ImGui.GetBackgroundDrawList();
		var project = new ScreenProjection(camera);

		// Waiting groups routinely share one placeholder point, so labels at the same spot are
		// stacked rather than drawn over each other.
		var stacked = new Dictionary<(int X, int Y), int>();
		float lineHeight = ImGui.GetTextLineHeightWithSpacing();

		void StackedLabel(Vec3i point, string text, Vector3 color) {
			if (project.ToScreen(_drape.OnGround(point.X, point.Y)) is not { } at) {
				return;
			}

			var key = (point.X, point.Y);
			int row = stacked.GetValueOrDefault(key);
			stacked[key] = row + 1;
			MissionOverlay.Label(drawList, at + new Vector2(ScaledImGui.Scaled(8f), row * lineHeight), text, color);
		}

		if (settings.WaitingGroups != WaitingGroupDisplay.Hidden) {
			var actions = _index.Layout.Entries<MissionAction82>();
			var numbers = Enumerable.Range(0, actions.Count).ToDictionary(i => actions[i].Guid);
			foreach (var group in _index.Groups) {
				if (!group.Waiting || group.OffScene || _index.MembersOf(group.Guid).Count == 0) {
					continue;
				}

				StackedLabel(group.Setup.Point,
					$"Group {group.Number}: waits on trigger {numbers[group.DeploymentAction]}, {MissionText.ArrivalKind(group.ArrivalVerb)}",
					WaitingColor);
			}
		}

		if (SelectedGroup(selection) is { } selected) {
			foreach (var (slot, verb, at) in Subjects(selected)) {
				StackedLabel(at, $"{MissionText.Ordinal(slot + 1)} order: {MissionText.OrderVerb(verb)}", SubjectColor);
			}
		}
	}

	/// <summary>
	/// Each order slot of <paramref name="group"/> whose order the layout has and names a subject the layout places in the
	/// scene, with the order's verb and where that subject stands: a group's point, or the object of a herc, flyer or
	/// structure.
	/// </summary>
	private IEnumerable<(int Slot, short Verb, Vec3i At)> Subjects(PlannedGroup group) {
		for (int slot = 0; slot < group.Orders.Count; slot++) {
			if (group.Orders[slot] == -1 || _index.Layout.Entry<MissionOrder22>(group.Orders[slot])?.Fields is not { } order
					|| order.SubjectRef == -1) {
				continue;
			}

			Vec3i? at = (MissionOrderSubject)order.SubjectKind switch {
				MissionOrderSubject.Group => _index.Group(order.SubjectRef) is { OffScene: false } subject ? subject.Setup.Point : null,
				MissionOrderSubject.Mech => ObjectAt(new ObjectKey(12, order.SubjectRef)),
				MissionOrderSubject.Flyer => ObjectAt(new ObjectKey(13, order.SubjectRef)),
				MissionOrderSubject.Base => ObjectAt(new ObjectKey(14, order.SubjectRef)),
				_ => null
			};

			if (at is { } point) {
				yield return (slot, order.Verb, point);
			}
		}
	}

	// Where the object of <key> stands, or null when the scene has none for it or it is out of the scene.
	private Vec3i? ObjectAt(ObjectKey key) =>
		_index.ObjectOf(key) is { } placed && _index.InScene(placed) ? placed.Object.Position : null;

	/// <summary>
	/// An arrow on the ground from <paramref name="from"/> along <paramref name="heading"/>. A machine
	/// faces <c>(-sin h, cos h)</c> in world XY — see <see cref="Scene.MissionScene.TransformOf"/>.
	/// </summary>
	private void AddArrow(List<Vector3> into, Vec3i from, int heading) {
		Vec3i Along(Vec3i origin, int angle, int length) {
			float radians = BinaryAngle.ToRadians(angle);
			return new Vec3i(origin.X + (int)(-MathF.Sin(radians) * length),
				origin.Y + (int)(MathF.Cos(radians) * length), 0);
		}

		var tip = Along(from, heading, ArrowLength);
		_drape.AddEdge(into, from, tip);

		// Each stroke of the head points back from the tip, a little either side of straight back.
		const int HeadSpread = BinaryAngle.QuarterTurn / 3;
		int back = heading + BinaryAngle.HalfTurn;
		_drape.AddEdge(into, tip, Along(tip, back - HeadSpread, ArrowHeadLength));
		_drape.AddEdge(into, tip, Along(tip, back + HeadSpread, ArrowHeadLength));
	}
}
