using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Render;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Editor;

/// <summary>Why a squad HERC that picks from a random table has no effect in play (docs/herculan/handoff-editor-roster.md#the-players-squad).</summary>
internal enum RandomTableUnused {
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
/// One record a random table's Used by lists: its identity, a link when it has a view (<see cref="Target"/>); the option the
/// scene shows it with, an index into the table's options, null when it shows none of them; whether it shows an option of
/// another table under the same key instead; and why it has no effect in play, when it has none.
/// </summary>
internal sealed record RandomTableUser(string Label, EditorSelection? Target, int? Shown, bool OtherTable, RandomTableUnused? Unused);

/// <summary>
/// The open mission's random tables, as the Random table view, the Mission panel's Random tables list and the object and
/// route views show them (docs/herculan/handoff-editor-conditions.md#random-tables), all from the <c>.MSN</c> and its
/// layout: each table read once per layout (<see cref="RandomTableEditing.Table"/>), titled by what it picks (<see cref="Title"/>),
/// each option with what tells it apart (<see cref="Option"/>), and the records picking from it (<see cref="Users"/>), named in
/// game terms, with the option the layout shows each with (<see cref="LayoutPick.Shown"/>).
/// </summary>
internal sealed class RandomTables {
	private const int RouteRow = 8;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;

	/// <summary>The rows whose records carry a random table key, of which the layout holds each GUID's picks.</summary>
	private static readonly int[] KeyedRows = [6, 7, 8, 12, 13, 14, 15];

	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly MissionNames _names;
	private readonly MissionSquad _squad;

	// What has been read of the layout _readFor, cleared once an edit builds another.
	private MissionLayout? _readFor;
	private IReadOnlyList<short> _all = [];
	private readonly Dictionary<short, RandomTable?> _tables = new();
	private readonly Dictionary<short, (IReadOnlyList<RandomTableUser> Users, int Others)> _users = new();

	/// <param name="names">What the records picking from a table are named by.</param>
	/// <param name="squad">Which Hercs the mission's squad pilots, which says when a squad HERC's pick has no effect.</param>
	public RandomTables(MissionDocument source, MissionIndex index, MissionNames names, MissionSquad squad) {
		_source = source;
		_index = index;
		_names = names;
		_squad = squad;
	}

	/// <summary>The mission as its <c>.MSN</c>.</summary>
	public MissionDocument Source => _source;

	/// <summary>The scene's objects and groups as the layout places them.</summary>
	public MissionIndex Index => _index;

	/// <summary>Every random table of the mission, by GUID in row-1 order.</summary>
	public IReadOnlyList<short> All {
		get {
			Refresh();
			return _all;
		}
	}

	/// <summary>Table <paramref name="guid"/>, or null when it is not one.</summary>
	public RandomTable? Read(short guid) {
		Refresh();
		if (!_tables.TryGetValue(guid, out var table)) {
			_tables[guid] = table = RandomTableEditing.Table(_source, guid);
		}

		return table;
	}

	/// <summary>
	/// What <paramref name="table"/> picks, by its kind numbered from 1 among the tables of that kind in row-1 order, so two
	/// tables of one chassis read apart (the editor's choice), then its options: each type named once, three at most and then
	/// how many more; a table of routes by its number of options.
	/// </summary>
	public string Title(RandomTable table) {
		string noun = KindOf(table);
		int number = All.Select(Read).TakeWhile(other => other?.Guid != table.Guid).Count(other => other != null && KindOf(other) == noun) + 1;
		string kind = $"{number}: Random {noun}";
		if (table.Options.Count == 0) {
			return $"{kind}: no options";
		}

		if (table.Row == RouteRow) {
			return $"{kind}: {table.Options.Count} options";
		}

		var names = table.Options.Select(option => ShortName(Record(option))).Distinct().ToList();
		string list = names.Count switch {
			1 => names[0],
			<= 3 => $"{string.Join(", ", names.Take(names.Count - 1))} or {names[^1]}",
			_ => $"{string.Join(", ", names.Take(3))} or {names.Count - 3} more",
		};

		return $"{kind}: {list}";
	}

	// What a title calls <table>'s kind: its options' noun, or "table" for one with no options.
	private string KindOf(RandomTable table) => table.Options.Count == 0 ? "table" : Noun(table);

	/// <summary>
	/// What option <paramref name="at"/> of <paramref name="table"/> is, with what tells it from the others: a herc's chassis,
	/// and its weapons when another option has that chassis; a flyer's type; a structure's or ground vehicle's kind and the
	/// name the MFD prints, as its view titles it (<see cref="RosterTypeNames.StructureTitle"/>); a route's waypoint count and length, and its start when another option matches both.
	/// </summary>
	public string Option(RandomTable table, int at) {
		var record = Record(table.Options[at]);
		var others = table.Options.Where((_, i) => i != at).Select(Record).ToList();
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
	public string Noun(RandomTable table) => table.Row switch {
		MechRow => "HERC",
		FlyerRow => "flyer",
		BaseRow => table.Options.Count > 0 && Record(table.Options[0]) is BaseRosterEntry62 first && IsVehicle(first)
			? "ground vehicle"
			: "structure",
		RouteRow => "route",
		_ => "option",
	};

	/// <summary>
	/// Every record that picks from <paramref name="table"/>, whatever its condition: each layout entry of a row carrying a
	/// random table key, one of whose picks is by the table's key, in row and layout order.
	/// </summary>
	public IReadOnlyList<RandomTableUser> Users(RandomTable table) => UsersOf(table).Users;

	/// <summary>How many templates, records of no entry, pick by <paramref name="table"/>'s key themselves.</summary>
	public int Others(RandomTable table) => UsersOf(table).Others;

	/// <summary>What references <paramref name="table"/> other than its own options, one short identity per line (<see cref="RandomTableEditing.Referencers"/>).</summary>
	public IReadOnlyList<(string Label, EditorSelection? Target)> Referencing(RandomTable table) {
		var namers = RandomTableEditing.Referencers(_source, table);
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
				_ when _source.RecordAt(id) is { GUID: -1 } => ("A template of another random table", null),
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
	public static string UnusedNote(RandomTableUnused unused) => unused switch {
		RandomTableUnused.CareerSquad => "Not used: campaign missions use the career's HERCs.",
		RandomTableUnused.HercType => "Not used: the player pilots the Herc Type chosen on the practice screen.",
		RandomTableUnused.LeftBehind => "Not used: the last squadmate listed stays behind (a retail bug).",
		_ => "Not used: it comes after a gap in the squad's list.",
	};

	/// <summary>
	/// The warning a random table gets when it has no effect in play: every record picking from it is a squad HERC that is not
	/// used (<see cref="RandomTableUnused"/>); null when one has an effect, or nothing picks from it.
	/// </summary>
	public string? NoEffect(RandomTable table) {
		var users = Users(table);
		if (users.Count == 0 || Others(table) > 0 || users.Any(user => user.Unused == null)) {
			return null;
		}

		return users.All(user => user.Unused == RandomTableUnused.CareerSquad)
				? "No effect in play: campaign missions use the career's HERCs."
			: users.All(user => user.Unused == RandomTableUnused.HercType)
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
		_all = RandomTableEditing.Tables(_source);
	}

	private (IReadOnlyList<RandomTableUser> Users, int Others) UsersOf(RandomTable table) {
		Refresh();
		if (_users.TryGetValue(table.Guid, out var cached)) {
			return cached;
		}

		var users = new List<RandomTableUser>();
		foreach (int row in KeyedRows) {
			foreach (var entry in _source.Layout.Entries(row)) {
				if (PicksOf(entry).FirstOrDefault(pick => pick.Key == table.Key) is { } pick) {
					users.Add(User(table, entry, pick));
				}
			}
		}

		int others = RandomTableEditing.Users(_source, table.Key).Count(id => _source.RecordAt(id) is { GUID: -1 });
		_users[table.Guid] = (users, others);
		return (users, others);
	}

	// An entry's picks: its base's, then its variations'.
	private static IEnumerable<LayoutPick> PicksOf(LayoutEntry entry) =>
		entry.Presence.Picks.Concat(entry.Variations.Select(variation => variation.Pick).OfType<LayoutPick>());

	private RandomTableUser User(RandomTable table, LayoutEntry entry, LayoutPick pick) {
		bool other = pick.Table != table.Guid;
		int? shown = null;
		if (!other && pick.Shown is { Record: not null } option) {
			int at = table.Options.ToList().FindIndex(own => own.Range == option.Range);
			shown = at >= 0 ? at : null;
		}

		var (label, target, unused) = Identity(entry);
		return new RandomTableUser(label, target, shown, other, unused);
	}

	// A record that picks, by what the designer sees (MissionNames): an object by its name and its group, a squad herc as the
	// player's or a squadmate's HERC, a route by its shape and who walks it.
	private (string Label, EditorSelection? Target, RandomTableUnused? Unused) Identity(LayoutEntry entry) {
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
	private RandomTableUnused? SquadUnused(PlannedObject planned) {
		if (_squad.PlayerMachine == PlayerMachineSource.CareerSquad) {
			return RandomTableUnused.CareerSquad;
		}

		if (planned.MemberIndex == 0) {
			return _squad.PlayerMachine == PlayerMachineSource.HercType ? RandomTableUnused.HercType : null;
		}

		var members = _index.Plan.Objects.Where(placed => placed.Squad).Select(placed => placed.MemberIndex).ToHashSet();
		int run = 0;
		while (members.Contains(run + 1)) {
			run++;
		}

		return planned.MemberIndex > run ? RandomTableUnused.AfterGap : planned.MemberIndex == run ? RandomTableUnused.LeftBehind : null;
	}

	private (string Label, EditorSelection? Target) ObjectiveName(int record) =>
		(_names.Objective(record).Full, new ObjectiveSelection(record));

	private MapObject? Record(RandomTableOption option) => _source.RecordAt(option.Template);

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
