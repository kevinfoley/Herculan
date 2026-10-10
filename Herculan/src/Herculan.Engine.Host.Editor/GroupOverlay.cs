using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.World;
using ImGui = ImGuiNET.ImGui;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// What the editor draws over the placed objects and the groups they belong to: the selection boxes,
/// the outlines that stand in for a waiting group's members, the selected group's own point and
/// heading, and a line from it to whatever each of its orders names.
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

	private Mission Mission => _index.Mission;

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
		ObjectSelection selected => new HashSet<Scene.SceneObject> { selected.Object },
		GroupSelection group when group.Group >= 0 && group.Group < _index.GroupMembers.Count =>
			_index.GroupMembers[group.Group].ToHashSet(),
		ObjectiveSelection objective when objective.Objective < Mission.Objectives.Count =>
			_index.SubjectObjectsOf(Mission.Objectives[objective.Objective]).ToHashSet(),
		_ => new HashSet<Scene.SceneObject>()
	};

	/// <summary>
	/// The selected group's point as a post, its heading as an arrow on the ground, and a line from
	/// the point to each of its orders' subjects.
	/// </summary>
	public void DrawSelectedGroup(WireframeRenderer wireframe, Camera camera, float aspect, SelectionState selection) {
		if (selection.Current is not GroupSelection { Group: var group } || _index.SetupOf(group) is not { } setup) {
			return;
		}

		var marker = new List<Vector3>();
		_drape.AddPost(marker, setup.Point.X, setup.Point.Y, MissionOverlay.PostHeight);
		AddArrow(marker, setup.Point, setup.Heading);
		MissionOverlay.DrawLines(wireframe, camera, aspect, marker, MissionOverlay.SelectedColor);

		var subjects = new List<Vector3>();
		foreach (var (_, at) in Subjects(group)) {
			_drape.AddEdge(subjects, setup.Point, at);
		}

		MissionOverlay.DrawLines(wireframe, camera, aspect, subjects, SubjectColor);
	}

	/// <summary>
	/// The labels: each waiting group's gate and arrival at its point, unless the settings hide waiting
	/// groups, and each of the selected group's order subjects at the far end of its line.
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
			for (int group = 0; group < _index.GroupMembers.Count; group++) {
				int action = _index.DeploymentActionOf(group);
				if (action < 0 || _index.GroupMembers[group].Count == 0 || _index.SetupOf(group) is not { } setup) {
					continue;
				}

				StackedLabel(setup.Point,
					$"Group {group}: waits on action {action}, {MissionText.ArrivalKind(Mission.Actions[action].Verb)}",
					WaitingColor);
			}
		}

		if (selection.Current is GroupSelection { Group: var selected }) {
			foreach (var (slot, at) in Subjects(selected)) {
				var order = Mission.GroupOrders[selected][slot]!;
				StackedLabel(at, $"order {slot}: {MissionText.OrderVerb(order.Verb)}", SubjectColor);
			}
		}
	}

	/// <summary>Each order slot of <paramref name="group"/> that names a subject, with where that subject stands.</summary>
	private IEnumerable<(int Slot, Vec3i At)> Subjects(int group) {
		if (group < 0 || group >= Mission.GroupOrders.Count) {
			yield break;
		}

		var orders = Mission.GroupOrders[group];
		for (int slot = 0; slot < orders.Count; slot++) {
			if (orders[slot] is not { SubjectKind: not MissionOrderSubject.None, SubjectRef: >= 0 } order) {
				continue;
			}

			if (_index.PointOf(MissionIndex.UnitKindOf(order.SubjectKind), order.SubjectRef) is { } at) {
				yield return (slot, at);
			}
		}
	}

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
