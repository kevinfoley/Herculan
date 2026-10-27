using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Render;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Editor;

/// <summary>Why a squad HERC that picks from a random choice has no effect in play (docs/herculan/handoff-editor-roster.md#the-players-squad).</summary>
internal enum ChoiceUnused {
	/// <summary>A campaign mission's squad pilots the career's HERCs.</summary>
	CareerSquad,

	/// <summary>On practice rows 4 to 7 the player pilots the practice screen's Herc Type.</summary>
	HercType,

	/// <summary>A training load leaves the last squadmate listed behind (a retail bug).</summary>
	LeftBehind,

	/// <summary>A squadmate past a gap in group 0's member slots, which no squad build reads.</summary>
	AfterGap,
}

/// <summary>
/// One record a random choice's Used by lists: its identity, a link when it has a view (<see cref="Target"/>); the option the
/// scene shows it with, an index into the table's options, null when it shows none of them; whether it shows an option of
/// another table under the same key instead; and why it has no effect in play, when it has none.
/// </summary>
internal sealed record ChoiceUser(string Label, EditorSelection? Target, int? Shown, bool OtherChoice, ChoiceUnused? Unused);

/// <summary>
/// The open mission's random choices, its variant tables, as the Random choice view, the Mission panel's Random choices list and
/// the object and route views show them (docs/herculan/handoff-editor-conditions.md#variants), all from the <c>.MSN</c> and its
/// layout: each table read once per layout (<see cref="VariantEditing.Table"/>), titled by what it picks (<see cref="Title"/>),
/// each option with what tells it apart (<see cref="Option"/>), and the records picking from it (<see cref="Users"/>), named in
/// game terms, with the option the layout shows each with (<see cref="LayoutDraw.Shown"/>).
/// </summary>
internal sealed class RandomChoices {
	private const int RouteRow = 8;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;

	/// <summary>The rows whose records carry a variant key, of which the layout holds each GUID's draws.</summary>
	private static readonly int[] DrawingRows = [6, 7, 8, 12, 13, 14, 15];

	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly MissionNames _names;
	private readonly MissionSquad _squad;

	// What has been read of the layout _readFor, cleared once an edit builds another.
	private MissionLayout? _readFor;
	private IReadOnlyList<short> _all = [];
	private readonly Dictionary<short, VariantTable?> _tables = new();
	private readonly Dictionary<short, (IReadOnlyList<ChoiceUser> Users, int Others)> _users = new();

	/// <param name="names">What the records picking from a choice are named by.</param>
	/// <param name="squad">Which Hercs the mission's squad pilots, which says when a squad HERC's pick has no effect.</param>
	public RandomChoices(MissionDocument source, MissionIndex index, MissionNames names, MissionSquad squad) {
		_source = source;
		_index = index;
		_names = names;
		_squad = squad;
	}

	/// <summary>The mission as its <c>.MSN</c>.</summary>
	public MissionDocument Source => _source;

	/// <summary>The scene's objects and groups as the layout places them.</summary>
	public MissionIndex Index => _index;

	/// <summary>Every variant table of the mission, by GUID in row-1 order.</summary>
	public IReadOnlyList<short> All {
		get {
			Refresh();
			return _all;
		}
	}

	/// <summary>Table <paramref name="guid"/>, or null when it is not one.</summary>
	public VariantTable? Read(short guid) {
		Refresh();
		if (!_tables.TryGetValue(guid, out var table)) {
			_tables[guid] = table = VariantEditing.Table(_source, guid);
		}

		return table;
	}

	/// <summary>
	/// What <paramref name="table"/> picks, built from its options: "Random herc: SCARAB or DIABLO", each type named once, three
	/// at most and then how many more; a route choice by its number of options.
	/// </summary>
	public string Title(VariantTable table) {
		if (table.Alternatives.Count == 0) {
			return "Random choice with no options";
		}

		if (table.Row == RouteRow) {
			return $"Random route: {table.Alternatives.Count} options";
		}

		var names = table.Alternatives.Select(alternative => ShortName(Record(alternative))).Distinct().ToList();
		string list = names.Count switch {
			1 => names[0],
			<= 3 => $"{string.Join(", ", names.Take(names.Count - 1))} or {names[^1]}",
			_ => $"{string.Join(", ", names.Take(3))} or {names.Count - 3} more",
		};

		return $"Random {Noun(table)}: {list}";
	}

	/// <summary>
	/// What option <paramref name="at"/> of <paramref name="table"/> is, with what tells it from the others: a herc's chassis,
	/// and its weapons when another option has that chassis; a flyer's type; a structure's or ground vehicle's kind and the
	/// name the MFD prints, as its view titles it (<see cref="RosterTypeNames.StructureTitle"/>); a route's waypoint count and length, and its start when another option matches both.
	/// </summary>
	public string Option(VariantTable table, int at) {
		var record = Record(table.Alternatives[at]);
		var others = table.Alternatives.Where((_, i) => i != at).Select(Record).ToList();
		switch (record) {
			case MechRosterEntry144 herc:
				return others.OfType<MechRosterEntry144>().Any(other => other.TypeIndex == herc.TypeIndex)
					? $"{ShortName(herc)}: {Weapons(herc)}"
					: ShortName(herc);
			case BaseRosterEntry62 structure:
				return _names.Types.StructureTitle(structure);
			case WaypointGroup route:
				var (count, meters, start) = Shape(route);
				string text = $"{count} waypoint{(count == 1 ? "" : "s")}, {meters:F0} m";
				return others.OfType<WaypointGroup>().Select(Shape).Any(other => other.Count == count && $"{other.Meters:F0}" == $"{meters:F0}")
					&& start is { } first
						? $"{text}, from {first.X}, {first.Y}"
						: text;
			default:
				return record != null ? ShortName(record) : "(missing)";
		}
	}

	/// <summary>What <paramref name="table"/>'s options are, in the singular: herc, flyer, structure, ground vehicle or route.</summary>
	public string Noun(VariantTable table) => table.Row switch {
		MechRow => "HERC",
		FlyerRow => "flyer",
		BaseRow => table.Alternatives.Count > 0 && Record(table.Alternatives[0]) is BaseRosterEntry62 first && IsVehicle(first)
			? "ground vehicle"
			: "structure",
		RouteRow => "route",
		_ => "option",
	};

	/// <summary>
	/// Every record that picks from <paramref name="table"/>, whatever its condition: each layout entry of a row carrying a
	/// variant key, one of whose draws is by the table's key, in row and layout order.
	/// </summary>
	public IReadOnlyList<ChoiceUser> Users(VariantTable table) => UsersOf(table).Users;

	/// <summary>How many variant alternatives, records of no entry, pick from <paramref name="table"/>'s key themselves.</summary>
	public int Others(VariantTable table) => UsersOf(table).Others;

	/// <summary>
	/// How many entries pick from a table one of whose options is variant alternative <paramref name="option"/>: the records an
	/// edit of that option changes wherever it is picked.
	/// </summary>
	public int Picking(MsnRecordId option) =>
		DrawingRows.Sum(row => _source.Layout.Entries(row).Count(entry => DrawsOf(entry)
			.Any(draw => draw.Options.Any(seen => seen.Record == option))));

	/// <summary>What references <paramref name="table"/> other than its own options, one short identity per line (<see cref="VariantEditing.Referencers"/>).</summary>
	public IReadOnlyList<(string Label, EditorSelection? Target)> Referencing(VariantTable table) {
		var namers = VariantEditing.Referencers(_source, table);
		var found = new List<(string Label, EditorSelection? Target)>();
		foreach (var id in namers.Records) {
			found.Add(id.Row switch {
				1 => ($"Condition {ConditionEditing.Records(_source)[id.Index].GUID}",
					new ConditionSelection(ConditionEditing.Records(_source)[id.Index].GUID)),
				2 => ("The mission's start settings", null),
				3 => ("A briefing movie", null),
				4 => ("The mission text", null),
				5 => ("The debrief", null),
				17 => ObjectiveName(id.Index),
				_ when _source.RecordAt(id) is { GUID: -1 } => ("An option of another random choice", null),
				_ when _source.RecordAt(id) is { } record => Named(id.Row, record.GUID),
				_ => ("Another record", null),
			});
		}

		if (namers.Text.Count > 0) {
			found.Add(("A text line", null));
		}

		return found.DistinctBy(item => item.Label).ToList();
	}

	/// <summary>What a Used by line says for <paramref name="unused"/>: why the record has no effect in play.</summary>
	public static string UnusedNote(ChoiceUnused unused) => unused switch {
		ChoiceUnused.CareerSquad => "Not used: campaign missions use the career's HERCs.",
		ChoiceUnused.HercType => "Not used: the player pilots the Herc Type chosen on the practice screen.",
		ChoiceUnused.LeftBehind => "Not used: the last squadmate listed stays behind (a retail bug).",
		_ => "Not used: it comes after a gap in the squad's list.",
	};

	/// <summary>
	/// The warning a random choice gets when it has no effect in play: every record picking from it is a squad HERC that is not
	/// used (<see cref="ChoiceUnused"/>); null when one has an effect, or nothing picks from it.
	/// </summary>
	public string? NoEffect(VariantTable table) {
		var users = Users(table);
		if (users.Count == 0 || Others(table) > 0 || users.Any(user => user.Unused == null)) {
			return null;
		}

		return users.All(user => user.Unused == ChoiceUnused.CareerSquad)
				? "No effect in play: campaign missions use the career's HERCs."
			: users.All(user => user.Unused == ChoiceUnused.HercType)
				? "No effect in play: the player pilots the Herc Type chosen on the practice screen."
			: "No effect in play: none of the HERCs that pick from it is used.";
	}

	// ---- Internals ---------------------------------------------------------------------------------

	private void Refresh() {
		if (ReferenceEquals(_readFor, _source.Layout)) {
			return;
		}

		_readFor = _source.Layout;
		_tables.Clear();
		_users.Clear();
		_all = VariantEditing.Tables(_source);
	}

	private (IReadOnlyList<ChoiceUser> Users, int Others) UsersOf(VariantTable table) {
		Refresh();
		if (_users.TryGetValue(table.Guid, out var cached)) {
			return cached;
		}

		var users = new List<ChoiceUser>();
		foreach (int row in DrawingRows) {
			foreach (var entry in _source.Layout.Entries(row)) {
				if (DrawsOf(entry).FirstOrDefault(draw => draw.Key == table.Key) is { } draw) {
					users.Add(User(table, entry, draw));
				}
			}
		}

		int others = VariantEditing.Drawers(_source, table.Key).Count(id => _source.RecordAt(id) is { GUID: -1 });
		_users[table.Guid] = (users, others);
		return (users, others);
	}

	// An entry's draws: its base's, then its variations'.
	private static IEnumerable<LayoutDraw> DrawsOf(LayoutEntry entry) =>
		entry.Presence.Draws.Concat(entry.Variations.Select(variation => variation.Draw).OfType<LayoutDraw>());

	private ChoiceUser User(VariantTable table, LayoutEntry entry, LayoutDraw draw) {
		bool other = draw.Table != table.Guid;
		int? shown = null;
		if (!other && draw.Shown is { Record: not null } option) {
			int at = table.Alternatives.ToList().FindIndex(alternative => alternative.Range == option.Range);
			shown = at >= 0 ? at : null;
		}

		var (label, target, unused) = Identity(entry);
		return new ChoiceUser(label, target, shown, other, unused);
	}

	// A record that picks, by what the designer sees (MissionNames): an object by its name and its group, a squad herc as the
	// player's or a squadmate's HERC, a route by its shape and who walks it.
	private (string Label, EditorSelection? Target, ChoiceUnused? Unused) Identity(LayoutEntry entry) {
		var (label, target) = Named(entry.Row, entry.Guid);
		var key = new ObjectKey(entry.Row, entry.Guid);
		return entry.Row is >= MechRow and <= BaseRow && _index.Plan.Object(key) is { Squad: true } planned
			? (label, target, SquadUnused(planned))
			: (label, target, null);
	}

	private (string Label, EditorSelection? Target) Named(int row, short guid) {
		var layout = _source.Layout;
		switch (row) {
			case 6:
				return ("A position", null);
			case 7:
				return ("A heading", null);
			case RouteRow:
				return (_names.Route(guid).Full, new RouteSelection(guid));
			case 9:
				return (_names.Area(guid).Full, new AreaSelection(guid));
			case 10:
				return (_names.Trigger(guid).Full, new ActionSelection(guid));
			case 11:
				return (_names.Countdown(guid).Full, new TimerSelection(guid));
			case 15:
				return ("An order", null);
			case 16:
				return (_names.GroupBrief(guid).Full, new GroupSelection(guid));
		}

		var key = new ObjectKey(row, guid);
		if (_index.Plan.Object(key) is not { } planned) {
			return (layout.Entry(row, guid) != null ? _names.Object(key).Full : "A record", null);
		}

		EditorSelection? target = _index.ObjectOf(key) != null ? new ObjectSelection(row, guid) : null;
		if (planned.Squad) {
			return (planned.MemberIndex == 0 ? "The player's HERC" : $"Squadmate {planned.MemberIndex}'s HERC", target);
		}

		return (_names.Object(key).Full, target);
	}

	// Why a squad herc's pick has no effect, by the launch's rule (MissionSquad) and the squad build's list: group 0's members
	// in the unbroken run from its second slot, the last of which a training load leaves behind.
	private ChoiceUnused? SquadUnused(PlannedObject planned) {
		if (_squad.PlayerMachine == PlayerMachineSource.CareerSquad) {
			return ChoiceUnused.CareerSquad;
		}

		if (planned.MemberIndex == 0) {
			return _squad.PlayerMachine == PlayerMachineSource.HercType ? ChoiceUnused.HercType : null;
		}

		var members = _index.Plan.Objects.Where(placed => placed.Squad).Select(placed => placed.MemberIndex).ToHashSet();
		int run = 0;
		while (members.Contains(run + 1)) {
			run++;
		}

		return planned.MemberIndex > run ? ChoiceUnused.AfterGap : planned.MemberIndex == run ? ChoiceUnused.LeftBehind : null;
	}

	private (string Label, EditorSelection? Target) ObjectiveName(int record) =>
		(_names.Objective(record).Full, new ObjectiveSelection(record));

	private MapObject? Record(VariantAlternative alternative) => _source.RecordAt(alternative.Record);

	// A record's type by name (RosterTypeNames); a route option is a route.
	private string ShortName(MapObject? record) => record is WaypointGroup ? "Route" : _names.Types.Name(record);

	private bool IsVehicle(BaseRosterEntry62 structure) => _names.Types.IsVehicle(structure);

	// A herc's weapons by name, its empty mounts and the slots past its capacity left out.
	private static string Weapons(MechRosterEntry144 herc) {
		var names = herc.WeaponRefs.Where(weapon => weapon > 0).Select(weapon => WeaponLUT.GetById(weapon)?.Name ?? $"weapon {weapon}").ToList();
		return names.Count > 0 ? string.Join(", ", names) : "no weapons";
	}

	// A route option's waypoint count (a loop's closing waypoint not counted twice), its length in metres over the ground, and
	// its first waypoint's point; points the layout has none for are skipped.
	private (int Count, double Meters, MapPoint22? Start) Shape(WaypointGroup route) {
		var points = route.Waypoints.Select(guid => _source.Layout.Entry<MapPoint22>(guid)?.Fields).OfType<MapPoint22>().ToList();
		double units = 0;
		for (int i = 1; i < points.Count; i++) {
			double dx = points[i].X - points[i - 1].X, dy = points[i].Y - points[i - 1].Y;
			units += Math.Sqrt(dx * dx + dy * dy);
		}

		bool closed = points.Count > 1 && points[^1].X == points[0].X && points[^1].Y == points[0].Y;
		return (closed ? points.Count - 1 : points.Count, units / WorldScale.WorldUnitsPerMeter, points.Count > 0 ? points[0] : null);
	}
}
