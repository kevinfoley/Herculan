using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>The <c>.MSN</c> records that name a row-8 route, by kind, whatever their condition.</summary>
/// <param name="Orders">Row-15 orders whose route it is (<see cref="MissionOrder22.RouteRef"/>), templates included.</param>
/// <param name="Objectives">Row-17 objectives whose route it is (<see cref="MissionObjective58.RouteRef"/>).</param>
/// <param name="Groups">Row-16 groups whose own route it is (<see cref="MissionGroup164.RouteRef"/>).</param>
public sealed record RouteNamers(IReadOnlyList<MsnRecordId> Orders, IReadOnlyList<MsnRecordId> Objectives,
		IReadOnlyList<MsnRecordId> Groups) {
	/// <summary>Whether any record names it.</summary>
	public bool Any => Orders.Count > 0 || Objectives.Count > 0 || Groups.Count > 0;
}

/// <summary>
/// Creates, edits and deletes routes in a <see cref="MissionDocument"/>, as record edits: a route is a row-8 record whose
/// list names row-6 points in route order (docs/retail/formats/msn-mission-file.md#row-8-field-decode--the-waypoint-group-dat_00470656-10-fixed-bytes--nested-count2-bytesrecord).
/// A closed route is one whose last entry names the same point as its first, which DBSIM's route cursor wraps on
/// (docs/retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once); a waypoint is counted once however many entries
/// name it, so a closed list of <c>n</c> entries has <c>n - 1</c> waypoints.
///
/// <para>A new route is one row-6 point per waypoint at Z 0, as all but one retail waypoint has it, and one row-8 record
/// listing them, each unconditioned, picking from no random table and under a new GUID (<see cref="MissionDocument.Append"/>), with the
/// dead word <c>0x06</c> at <c>-1</c> as in every retail record. A list edit replaces the whole list in the record whose list
/// the route holds (<see cref="MissionDocument.SetWaypoints"/>): for a list a template supplies, that template, which every
/// route picking it shares.</para>
///
/// <para>Removing a waypoint removes its list entry, and deleting a route removes every row-8 record of its GUID. Either can
/// leave a row-6 point the route named that no field names any more (<see cref="MissionDocument.CountPointRefs"/>, every
/// record whatever its condition); its records are removed in the same change, since block 1's extent frames the
/// heads-down map and the mission box (docs/herculan/editor-design.md#positions).
/// A point anything else still names stays. A route is deleted only while no order, objective or group names it: a ref to
/// a GUID with no record resolves to <c>-1</c>, so the order would lose its route, the objective its order, and a group
/// whose slot-0 order walked it its starting point.</para>
/// </summary>
public static class RouteEditing {
	/// <summary>
	/// The fewest waypoints a route keeps: retail groups walk routes of one
	/// (docs/retail/formats/msn-mission-file.md#row-8-field-decode--the-waypoint-group-dat_00470656-10-fixed-bytes--nested-count2-bytesrecord).
	/// </summary>
	public const int MinWaypoints = 1;

	/// <summary>
	/// The fewest waypoints a loop made here has: no route of fewer is closed, and a loop a removal leaves with fewer is
	/// left open. Retail has a loop of one waypoint
	/// (docs/retail/formats/msn-mission-file.md#row-8-field-decode--the-waypoint-group-dat_00470656-10-fixed-bytes--nested-count2-bytesrecord),
	/// which is shown and opened as it is; the editor makes none, at the user's request. A restriction of this editor, not
	/// of the file.
	/// </summary>
	public const int MinLoopWaypoints = 2;

	private const int PointRow = 6;
	private const int RouteRow = 8;
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int ObjectiveRow = 17;

	/// <summary>Whether <paramref name="list"/> closes on itself: more than one entry, the last naming the first's point.</summary>
	public static bool IsClosed(IReadOnlyList<short> list) => list.Count > 1 && list[^1] == list[0];

	/// <summary>How many waypoints <paramref name="list"/> holds: a closed list's last entry is its first waypoint again.</summary>
	public static int WaypointCount(IReadOnlyList<short> list) => IsClosed(list) ? list.Count - 1 : list.Count;

	/// <summary>
	/// Appends a route through <paramref name="waypoints"/>, closed back on the first when <paramref name="closed"/>, as one
	/// <see cref="MissionDocument.Change"/>, and answers its GUID in <paramref name="route"/>. Throws
	/// <see cref="InvalidOperationException"/> for fewer than <see cref="MinWaypoints"/> waypoints, or a loop of fewer than
	/// <see cref="MinLoopWaypoints"/>.
	/// </summary>
	public static MsnEdit Create(MissionDocument document, IReadOnlyList<(int X, int Y)> waypoints, bool closed, out short route) {
		if (waypoints.Count < MinWaypoints) {
			throw new InvalidOperationException($"A route needs at least {MinWaypoints} waypoint.");
		}

		if (closed && waypoints.Count < MinLoopWaypoints) {
			throw new InvalidOperationException(LoopTooShort);
		}

		short created = -1;
		var change = document.Change(() => created = Append(document, waypoints, closed));
		route = created;
		return change;
	}

	/// <summary>
	/// The record edits of <see cref="Create"/>, made inside the caller's <see cref="MissionDocument.Change"/>, which
	/// checks the waypoint count: a point per waypoint and the route listing them. Answers the route's GUID.
	/// </summary>
	internal static short Append(MissionDocument document, IReadOnlyList<(int X, int Y)> waypoints, bool closed) {
		var list = waypoints.Select(at => AppendPoint(document, at)).ToList();
		if (closed) {
			list.Add(list[0]);
		}

		document.Append(RouteRow, new WaypointGroup {
			ConditionRef = -1, RandomTableKey = -1, Unk06 = -1, Waypoints = list.ToArray(),
		}, out short route);
		return route;
	}

	/// <summary>
	/// Adds a waypoint at each of <paramref name="waypoints"/> to route GUID <paramref name="route"/>, after its last: before
	/// the closing entry of a closed route, which stays closed. <paramref name="close"/> closes an open route after them.
	/// One <see cref="MissionDocument.Change"/>; throws <see cref="InvalidOperationException"/> when it would change nothing,
	/// or close a loop of fewer than <see cref="MinLoopWaypoints"/>.
	/// </summary>
	public static MsnEdit Extend(MissionDocument document, short route, IReadOnlyList<(int X, int Y)> waypoints, bool close) {
		var list = document.Waypoints(route).ToList();
		bool closed = IsClosed(list);
		if (waypoints.Count == 0 && (!close || closed || list.Count == 0)) {
			throw new InvalidOperationException("No waypoint was added.");
		}

		if (close && !closed && list.Count + waypoints.Count < MinLoopWaypoints) {
			throw new InvalidOperationException(LoopTooShort);
		}

		return document.Change(() => {
			var added = waypoints.Select(at => AppendPoint(document, at)).ToList();
			if (closed) {
				list.InsertRange(list.Count - 1, added);
			} else {
				list.AddRange(added);
				if (close) {
					list.Add(list[0]);
				}
			}

			document.SetWaypoints(route, list.ToArray());
		});
	}

	/// <summary>
	/// Why no waypoint can be inserted after list entry <paramref name="entry"/> of route GUID <paramref name="route"/>, or
	/// null when one can: the leg from it to the next entry must exist, and a record must carry each of its points.
	/// </summary>
	public static string? WhyNoInsert(MissionDocument document, short route, int entry) {
		var list = document.Waypoints(route);
		if (entry < 0 || entry + 1 >= list.Length) {
			return "There is no leg after the last waypoint: Add waypoints adds there.";
		}

		return Coordinates(document, list[entry]) == null || Coordinates(document, list[entry + 1]) == null
			? "A waypoint of that leg references a point not in the mission."
			: null;
	}

	/// <summary>
	/// Inserts a waypoint halfway along the leg from list entry <paramref name="entry"/> of route GUID
	/// <paramref name="route"/> to the next, on a new point at Z 0, as one <see cref="MissionDocument.Change"/>. Throws
	/// <see cref="InvalidOperationException"/> when <see cref="WhyNoInsert"/> refuses it.
	/// </summary>
	public static MsnEdit InsertAfter(MissionDocument document, short route, int entry) {
		if (WhyNoInsert(document, route, entry) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var list = document.Waypoints(route).ToList();
		var from = Coordinates(document, list[entry])!.Value;
		var to = Coordinates(document, list[entry + 1])!.Value;
		var middle = ((int)(((long)from.X + to.X) / 2), (int)(((long)from.Y + to.Y) / 2));
		return document.Change(() => {
			list.Insert(entry + 1, AppendPoint(document, middle));
			document.SetWaypoints(route, list.ToArray());
		});
	}

	/// <summary>
	/// Why waypoint <paramref name="entry"/> of route GUID <paramref name="route"/> cannot be removed, or null when it can:
	/// it must be a waypoint rather than a closed list's closing entry, and the route must keep <see cref="MinWaypoints"/>.
	/// </summary>
	public static string? WhyNoRemove(MissionDocument document, short route, int entry) {
		var list = document.Waypoints(route);
		int count = WaypointCount(list);
		if (entry < 0 || entry >= count) {
			return $"The route has no waypoint {entry}.";
		}

		return count - 1 < MinWaypoints ? "A route keeps at least one waypoint; Delete removes the route." : null;
	}

	/// <summary>
	/// Removes waypoint <paramref name="entry"/> from route GUID <paramref name="route"/>'s list, and the records of a point
	/// that leaves named by nothing, as one <see cref="MissionDocument.Change"/>. A closed route's first waypoint is also its
	/// last entry: both go, and the route closes on its new first waypoint. A loop left with fewer than
	/// <see cref="MinLoopWaypoints"/> is left open. Throws <see cref="InvalidOperationException"/> when
	/// <see cref="WhyNoRemove"/> refuses it.
	/// </summary>
	public static MsnEdit RemoveAt(MissionDocument document, short route, int entry) {
		if (WhyNoRemove(document, route, entry) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		short[] before = document.Waypoints(route);
		var list = before.ToList();
		bool closed = IsClosed(list);
		if (closed) {
			list.RemoveAt(list.Count - 1);
		}

		list.RemoveAt(entry);
		if (closed && list.Count >= MinLoopWaypoints) {
			list.Add(list[0]);
		}

		return document.Change(() => {
			document.SetWaypoints(route, list.ToArray());
			RemoveLeftBehind(document, before.Except(list));
		});
	}

	/// <summary>
	/// Why route GUID <paramref name="route"/> cannot be closed (<paramref name="closed"/>) or opened, or null when it can:
	/// it must not be so already, and closing needs <see cref="MinLoopWaypoints"/>.
	/// </summary>
	public static string? WhyNoClose(MissionDocument document, short route, bool closed) {
		var list = document.Waypoints(route);
		if (IsClosed(list) == closed) {
			return closed ? "It is already a loop." : "It is already open.";
		}

		return closed && list.Length < MinLoopWaypoints ? LoopTooShort : null;
	}

	/// <summary>
	/// Closes route GUID <paramref name="route"/> by appending its first waypoint's point as its last entry, or opens it by
	/// removing that entry. Throws <see cref="InvalidOperationException"/> when <see cref="WhyNoClose"/> refuses it.
	/// </summary>
	public static MsnEdit SetClosed(MissionDocument document, short route, bool closed) {
		if (WhyNoClose(document, route, closed) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var list = document.Waypoints(route);
		return document.SetWaypoints(route, closed ? [.. list, list[0]] : list[..^1]);
	}

	/// <summary>The orders, objectives and groups naming route GUID <paramref name="route"/>, every record whatever its condition.</summary>
	public static RouteNamers Namers(MissionDocument document, short route) {
		var mission = document.Mission;
		if (route == -1) {
			return new RouteNamers([], [], []);
		}

		var orders = new List<MsnRecordId>();
		var orderRow = mission.Orders ?? [];
		for (int i = 0; i < orderRow.Length; i++) {
			if (orderRow[i].RouteRef == route) {
				orders.Add(new MsnRecordId(OrderRow, i));
			}
		}

		var objectives = new List<MsnRecordId>();
		var objectiveRow = mission.Objectives ?? [];
		for (int i = 0; i < objectiveRow.Length; i++) {
			if (objectiveRow[i] is { } objective && objective.RouteRef == route) {
				objectives.Add(new MsnRecordId(ObjectiveRow, i));
			}
		}

		var groups = new List<MsnRecordId>();
		var groupRow = mission.Groups ?? [];
		for (int i = 0; i < groupRow.Length; i++) {
			if (groupRow[i].RouteRef == route) {
				groups.Add(new MsnRecordId(GroupRow, i));
			}
		}

		return new RouteNamers(orders, objectives, groups);
	}

	/// <summary>
	/// Deletes route GUID <paramref name="route"/>: every row-8 record of the GUID, whatever its condition, and the records of
	/// each point its lists named that is left named by nothing, as one <see cref="MissionDocument.Change"/>. The
	/// templates a deleted record picked its list from are GUID <c>-1</c> records shared by every record picking them, and
	/// stay; deleting a record that picks from a random table moves every later draw. Throws <see cref="InvalidOperationException"/>
	/// while <see cref="Namers"/> finds anything.
	/// </summary>
	public static MsnEdit Delete(MissionDocument document, short route) {
		if (Namers(document, route).Any) {
			throw new InvalidOperationException($"Route {route} is referenced by an order, an objective or a group.");
		}

		var named = (document.Mission.WaypointGroups ?? []).Where(record => record.GUID == route)
			.SelectMany(record => record.Waypoints).ToList();
		return document.Change(() => {
			document.Remove(RouteRow, route);
			RemoveLeftBehind(document, named);
		});
	}

	private const string LoopTooShort = "A loop needs at least two waypoints.";

	// Removes the records of each of <points> that no field names any more. A sum-flag point names two others by the low
	// words of its X and Y (unused in retail, MapPoint22.SumFlag), and keeps them too.
	internal static void RemoveLeftBehind(MissionDocument document, IEnumerable<short> points) {
		foreach (short point in points.Where(point => point != -1).Distinct()) {
			var records = document.Mission.Points ?? [];
			bool summed = records.Any(record => record.SumFlag != 0 && ((short)record.X == point || (short)record.Y == point));
			if (document.CountPointRefs(point) == 0 && !summed && records.Any(record => record.GUID == point)) {
				document.Remove(PointRow, point);
			}
		}
	}

	// Appends a row-6 point at (at.X, at.Y, 0), unconditioned, picking from no random table and under a new GUID, and answers its GUID.
	internal static short AppendPoint(MissionDocument document, (int X, int Y) at) {
		document.Append(PointRow, new MapPoint22 { ConditionRef = -1, RandomTableKey = -1, Unk06 = -1, X = at.X, Y = at.Y, Z = 0 },
			out short point);
		return point;
	}

	/// <summary>Where row-6 GUID <paramref name="point"/>'s base stands, or null when no record carries it.</summary>
	internal static (int X, int Y)? Coordinates(MissionDocument document, short point) =>
		document.Layout.Entry<MapPoint22>(point)?.Fields is { } fields ? (fields.X, fields.Y) : null;
}
