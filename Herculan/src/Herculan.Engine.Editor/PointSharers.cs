using HercWorks.Core.Data.File.Msn;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// What else stands on a point a drag moves (docs/herculan/handoff-editor-placement.md#how-positions-are-held), listed for
/// the group, route and trigger area views: "Also on this spot, moving with it:", then each sharer by its identity in the
/// layout as a link, one per line, and how to move the dragged thing alone. The fields come from <see cref="PointMover"/>'s
/// <c>SharersOf</c>, every record whatever its condition; a field of an option of a random choice (GUID <c>-1</c>) and an
/// order no group runs are counted, as nothing selects them.
/// </summary>
internal sealed class PointSharers {
	private const int RouteRow = 8;
	private const int AreaRow = 9;
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int ObjectiveRow = 17;

	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly PanelLinks _links;

	public PointSharers(MissionDocument source, MissionIndex index, PanelLinks links) {
		_source = source;
		_index = index;
		_links = links;
	}

	private MissionNames Names => _links.Names;

	/// <summary>
	/// Draws <paramref name="sharers"/>, each once by its identity, then that Alt+drag moves the dragged thing alone; nothing
	/// when there are none.
	/// </summary>
	public void Draw(IReadOnlyList<PointRef> sharers) {
		if (sharers.Count == 0) {
			return;
		}

		ImGui.PushID("sharers");
		ImGui.TextWrapped("Also on this spot, moving with it:");
		var drawn = new HashSet<(int Row, short Guid, int Part)>();
		int options = 0, unrun = 0;
		foreach (var sharer in sharers) {
			// Row 17 has no GUID: an objective is its index.
			if (sharer.Record.Row == ObjectiveRow) {
				if (drawn.Add((ObjectiveRow, 0, sharer.Record.Index))) {
					Line(() => _links.Objective(sharer.Record.Index));
				}

				continue;
			}

			short guid = _source.RecordAt(sharer.Record)?.GUID ?? -1;
			if (guid == -1) {
				options++;
				continue;
			}

			switch (sharer.Record.Row) {
				case RouteRow:
					int waypoint = Waypoint(guid, sharer.Field);
					if (drawn.Add((RouteRow, guid, waypoint))) {
						Line(() => WaypointLink(guid, waypoint));
					}

					break;
				case AreaRow:
					if (drawn.Add((AreaRow, guid, sharer.Field))) {
						Line(() => CornerLink(guid, sharer.Field));
					}

					break;
				case 12 or 13 or 14:
					if (drawn.Add((sharer.Record.Row, guid, 0))) {
						Line(() => _links.Object(new ObjectKey(sharer.Record.Row, guid)));
					}

					break;
				case OrderRow:
					var slots = _index.OrderSlots(guid);
					if (slots.Count == 0 && drawn.Add((OrderRow, guid, -1))) {
						unrun++;
					}

					foreach (var slot in slots.Where(slot => drawn.Add((OrderRow, slot.Group, slot.Slot)))) {
						Line(() => OrderLink(slot));
					}

					break;
				case GroupRow:
					if (drawn.Add((GroupRow, guid, 0))) {
						Line(() => _links.ShortLayoutGroup(guid));
					}

					break;
			}
		}

		if (options > 0) {
			ImGui.TextWrapped(options == 1 ? "1 option of a random choice" : $"{options} options of random choices");
		}

		if (unrun > 0) {
			ImGui.TextWrapped(unrun == 1 ? "1 order no group runs" : $"{unrun} orders no group runs");
		}

		EditorText.Note("Alt+drag to move it alone.");
		ImGui.PopID();

		void Line(Action draw) {
			ImGui.PushID(drawn.Count);
			draw();
			ImGui.PopID();
		}
	}

	// Where entry <entry> of row-8 GUID <route>'s list stands among the waypoints the route view lists: a closed route's
	// closing entry is its first waypoint; -1 for an entry naming no point the layout has.
	private int Waypoint(short route, int entry) {
		if (_index.Route(route) is not { } placed) {
			return -1;
		}

		int at = -1;
		for (int i = 0; i < placed.Entries.Count; i++) {
			if (placed.Entries[i] == entry) {
				at = i;
				break;
			}
		}

		return placed.Closed && at == placed.Points.Count - 1 ? 0 : at;
	}

	// A route's waypoint, by the route's name and the waypoint's number as the route view lists it.
	private void WaypointLink(short route, int waypoint) {
		var name = Names.Route(route);
		string part = waypoint >= 0 ? $"waypoint {waypoint}" : "a waypoint";
		_links.Link(name with { Text = $"{name.Text}: {part}" }, new RouteSelection(route, waypoint));
	}

	// A trigger area's corner, or a circle's center, by the area's name and the corner as the area view labels it.
	private void CornerLink(short area, int corner) {
		var name = Names.Area(area);
		bool circle = _index.Area(area)?.Area.Shape == Herculan.Engine.World.MissionTriggerShape.Circle;
		string part = circle ? "center" : $"corner {corner + 1}";
		_links.Link(name with { Text = $"{name.Text}: {part}" }, new AreaSelection(area));
	}

	// An order by the group slot that runs it: "1st order of Cybrid hercs (group 4)", whose map point it is.
	private void OrderLink(EndsOrder slot) {
		var group = Names.GroupBrief(slot.Group);
		_links.Link(group with { Text = $"{MissionText.Ordinal(slot.Slot + 1)} order of {group.Text}" }, new GroupSelection(slot.Group));
	}
}
