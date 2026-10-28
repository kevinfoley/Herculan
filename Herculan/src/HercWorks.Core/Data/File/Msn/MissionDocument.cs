using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// A mission held as its <c>.MSN</c> and <c>.ENG</c>, with its <see cref="Layout"/>, every record present, and the
/// <c>script.dat</c> and <c>mission.str</c> generated from them with the provenance that maps each generated record back to
/// the <c>.MSN</c> records that supplied it. An edit writes the <c>.MSN</c> record the layout names as the field's supplier
/// and builds the layout again; the design is docs/herculan/handoff-editor-document.md#holding-the-msn-as-the-document.
///
/// <para>The generation is not run by an edit: an edit, undo or redo leaves it stale, and <see cref="Script"/>,
/// <see cref="ScriptBytes"/>, <see cref="MissionText"/>, <see cref="Provenance"/> and <see cref="Generator"/> generate again
/// when one of them is next read, so a reader of the layout alone never pays for it.</para>
///
/// <para>Every edit returns an <see cref="MsnEdit"/> holding the rows it changed, which <see cref="Undo"/> and
/// <see cref="Redo"/> put back. Several edits made inside one <see cref="Change"/> come back as one, and are all put back
/// when one of them fails; what can fail inside an edit is the edit itself, the layout's build and the <c>.MSN</c>'s write,
/// not the generation, which fails when it is next read.</para>
///
/// <para>Which record an edit writes is docs/herculan/handoff-editor-document.md#which-record-an-edit-writes: a field of a
/// GUID's base is written in its supplier (<see cref="LayoutEntry.Supplier"/>), the last base record that sets it, else the
/// first record, whether or not the generation keeps it. A roster field the supplier copies from a template is written in a
/// record of the same GUID that overlays it under the supplier's condition, so it overlays whatever is picked wherever the
/// base is present: the GUID's last record when that already is one (<see cref="LaterOverlay"/>), else one appended to the
/// row carrying that field alone. A field set to its unset value is unset in every record of the GUID,
/// since an overlay cannot carry an unset value.</para>
/// </summary>
public sealed class MissionDocument {
	private const int SettingsRow = 2;
	private const int PointRow = 6;
	private const int HeadingRow = 7;
	private const int RouteRow = 8;
	private const int AreaRow = 9;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int StructureRow = 14;
	private const int OrderRow = 15;
	private const int GroupRow = 16;
	private const int ObjectiveRow = 17;

	/// <summary>The edits made so far inside <see cref="Change"/>, or null outside one.</summary>
	private List<MsnEdit>? _change;

	private readonly ScriptDat _script = new();
	private byte[] _scriptBytes = [];
	private byte[] _missionText = [];
	private MissionProvenance _provenance = new();
	private MissionGenerator _generator = null!;

	/// <summary>The files the generation runs over, as the last <see cref="Rebuild"/> wrote them.</summary>
	private byte[] _msn = [];
	private byte[]? _eng;

	/// <summary>Whether the records have changed since the last generation.</summary>
	private bool _stale = true;

	public MissionDocument(MissionFile mission, MissionStringFile? text, MissionGeneration generation) {
		Mission = mission;
		Text = text;
		Generation = generation;
		Rebuild();
	}

	/// <summary>A document over the <c>.MSN</c> <paramref name="msn"/> and the <c>.ENG</c> <paramref name="text"/>, or none.</summary>
	public static MissionDocument Read(byte[] msn, byte[]? text, MissionGeneration generation) =>
		new(new MissionFileTransformer().Parse(msn) ?? throw new InvalidDataException("Not a mission file."),
			text == null ? null : new MissionStringFileTransformer().Parse(text), generation);

	public MissionFile Mission { get; }

	/// <summary>The <c>.ENG</c>, or null when the mission has none; the first text edit of such a mission gives it one.</summary>
	public MissionStringFile? Text { get; private set; }

	/// <summary>What every generation hands the generator besides the files: the campaign flags, the draws and the header words the launch writes over.</summary>
	public MissionGeneration Generation { get; }

	/// <summary>
	/// The generated <c>script.dat</c>: one object for the document's life, whose header and blocks each generation
	/// replaces. Generates first when the records have changed since the last, so whatever holds the object reads the
	/// latest once this or another of the generation's members has been read after the edit.
	/// </summary>
	public ScriptDat Script {
		get {
			Generate();
			return _script;
		}
	}

	/// <summary>The generated <c>script.dat</c>'s bytes, generated first as <see cref="Script"/> is.</summary>
	public byte[] ScriptBytes {
		get {
			Generate();
			return _scriptBytes;
		}
	}

	/// <summary>The generated <c>mission.str</c>'s bytes, generated first as <see cref="Script"/> is.</summary>
	public byte[] MissionText {
		get {
			Generate();
			return _missionText;
		}
	}

	/// <summary>Which records supplied the generated files, generated first as <see cref="Script"/> is.</summary>
	public MissionProvenance Provenance {
		get {
			Generate();
			return _provenance;
		}
	}

	/// <summary>The mission with every record present, built again with each edit; its suppliers are the records edits write.</summary>
	public MissionLayout Layout { get; private set; } = null!;

	/// <summary>
	/// The ten header words the launch plays (<c>script.dat</c> offset <c>word * 2</c>), built again with each edit: the
	/// layout's (<see cref="MissionLayout.HeaderWords"/>), with every word <see cref="Generation"/> writes over the loaded header
	/// written over them, as the launch writes them (<see cref="MissionGeneration.WriteHeader"/>). No condition is tested, so a
	/// row-2 variation is not applied.
	/// </summary>
	public IReadOnlyList<short> LaunchHeader { get; private set; } = [];

	/// <summary>
	/// The generation's run, which answers what the shell reads back from a loaded mission besides the files: its squad
	/// positions and group 0's machines (<see cref="MissionGenerator.SquadPositions"/>, <see cref="MissionGenerator.Herc"/>).
	/// Generated first as <see cref="Script"/> is.
	/// </summary>
	public MissionGenerator Generator {
		get {
			Generate();
			return _generator;
		}
	}

	/// <summary>
	/// Builds <see cref="Layout"/> and <see cref="LaunchHeader"/> from the records, writes the files the generation runs over,
	/// and leaves the generation stale.
	/// </summary>
	private void Rebuild() {
		Layout = MissionLayout.Build(Mission, Text);
		var header = Layout.HeaderWords.ToArray();
		MissionGeneration.WriteHeader(header, Generation.Header);
		LaunchHeader = header;
		_msn = new MissionFileTransformer().Write(Mission) ?? throw new InvalidOperationException("The mission file does not write.");
		_eng = Text == null ? null : new MissionStringFileTransformer().Write(Text);
		_stale = true;
	}

	/// <summary>
	/// When the records have changed since the last generation, generates from the files <see cref="Rebuild"/> wrote and
	/// replaces <see cref="Script"/>'s blocks with what was generated; a generation that throws leaves it stale.
	/// </summary>
	private void Generate() {
		if (!_stale) {
			return;
		}

		var generator = Generation.Run(_msn, _eng);
		var script = generator.WriteScriptDat();
		var parsed = new ScriptDatTransformer().Parse(script)
			?? throw new InvalidOperationException("The generated script.dat does not parse.");

		_scriptBytes = script;
		_missionText = generator.WriteMissionText();
		_provenance = generator.Provenance;
		_generator = generator;
		CopyBlocks(parsed, _script);
		_stale = false;
	}

	// ---- Lookups ----------------------------------------------------------------------------------

	/// <summary>
	/// Whether a template supplies <paramref name="field"/> of GUID <paramref name="guid"/>, so that <see cref="SetField"/> writes
	/// it in an overlay record, which cannot carry the field's unset value. Throws as <see cref="SetField"/> does for a GUID no
	/// record carries.
	/// </summary>
	public bool SuppliedByTemplate(MsnField field, short guid) => EntryOf(field.Row, guid).Supplier(field.Word).CopiedFrom != null;

	/// <summary>The GUID-keyed record <paramref name="id"/> names, or null for one of rows 4 and 17, which have no GUID.</summary>
	public MapObject? RecordAt(MsnRecordId id) => MsnRows.Get(Mission, id.Row).GetValue(id.Index) as MapObject;

	/// <summary>
	/// How many fields name row-6 GUID <paramref name="point"/>: route waypoints, a trigger area's first corner or center
	/// and a box's second corner, the three rosters' points, an order's point, a group's point and an objective's point.
	/// Every record counts whatever its condition, since one that fails in the generation's state names the point in
	/// another.
	/// </summary>
	public int CountPointRefs(short point) => PointRefs(point).Count;

	/// <summary>
	/// Every field naming row-6 GUID <paramref name="point"/>, in row and file order, the fields <see cref="CountPointRefs"/>
	/// counts: each by its record and, for a route, the entry of its list, for a trigger area 0 its first corner or center and
	/// 1 a box's second corner.
	/// </summary>
	public IReadOnlyList<PointRef> PointRefs(short point) {
		var refs = new List<PointRef>();
		if (point == -1) {
			return refs;
		}

		void Each<T>(int row, T?[]? records, Func<T, short> reference) where T : class {
			for (int i = 0; i < (records?.Length ?? 0); i++) {
				if (records![i] is { } record && reference(record) == point) {
					refs.Add(new PointRef(new MsnRecordId(row, i), 0));
				}
			}
		}

		var routes = Mission.WaypointGroups ?? [];
		for (int i = 0; i < routes.Length; i++) {
			for (int entry = 0; entry < routes[i].Waypoints.Length; entry++) {
				if (routes[i].Waypoints[entry] == point) {
					refs.Add(new PointRef(new MsnRecordId(RouteRow, i), entry));
				}
			}
		}

		var areas = Mission.TriggerAreas ?? [];
		for (int i = 0; i < areas.Length; i++) {
			if (areas[i].PointRef == point) {
				refs.Add(new PointRef(new MsnRecordId(AreaRow, i), 0));
			}

			if (areas[i].Shape == 0 && areas[i].SecondPointOrRadius == point) {
				refs.Add(new PointRef(new MsnRecordId(AreaRow, i), 1));
			}
		}

		Each(MechRow, Mission.Mechs, record => record.PositionRef);
		Each(FlyerRow, Mission.Flyers, record => record.PositionRef);
		Each(StructureRow, Mission.Bases, record => record.PositionRef);
		Each(OrderRow, Mission.Orders, record => record.PointRef);
		Each(GroupRow, Mission.Groups, record => record.PositionRef);
		Each(ObjectiveRow, Mission.Objectives, record => record.PointRef);
		return refs;
	}

	/// <summary>
	/// The first member slot of group GUID <paramref name="group"/> that no record of the GUID fills, whatever its
	/// condition, so writing it fills it in every state; or <c>-1</c> when every slot is filled somewhere.
	/// </summary>
	public int FreeMemberSlot(short group) {
		for (int slot = 0; slot < 20; slot++) {
			if (MemberSlotFree(group, slot)) {
				return slot;
			}
		}

		return -1;
	}

	/// <summary>
	/// Whether no record of group GUID <paramref name="group"/> fills member slot <paramref name="slot"/> (0 to 19), whatever
	/// its condition, so writing it fills it in every state.
	/// </summary>
	public bool MemberSlotFree(short group, int slot) =>
		(Mission.Groups ?? []).Where(record => record.GUID == group).All(record => record.MemberRefs[slot] == -1);

	/// <summary>
	/// The GUID of a row-7 heading that reads <paramref name="degrees"/> and can be shared: the GUID's one record,
	/// unconditioned and picking from no random table. <c>-1</c> when there is none.
	/// </summary>
	public short ShareableHeading(short degrees) {
		var headings = Mission.Headings ?? [];
		foreach (var heading in headings) {
			if (heading.GUID != -1 && heading.Degrees == degrees && heading.ConditionRef == -1 && heading.RandomTableKey == -1
					&& headings.Count(other => other.GUID == heading.GUID) == 1) {
				return heading.GUID;
			}
		}

		return -1;
	}

	// ---- Edits ------------------------------------------------------------------------------------

	/// <summary>
	/// Makes the edits <paramref name="edits"/> calls for as one: answers them combined, or, when one throws, puts back
	/// every row they changed, builds the layout again and rethrows.
	/// </summary>
	public MsnEdit Change(Action edits) {
		if (_change != null) {
			throw new InvalidOperationException("A change is already under way.");
		}

		var made = _change = new List<MsnEdit>();
		try {
			edits();
		} catch {
			_change = null;
			for (int i = made.Count - 1; i >= 0; i--) {
				WriteBack(made[i], after: false);
			}

			Rebuild();
			throw;
		}

		_change = null;
		return MsnEdit.Combine(made);
	}

	/// <summary>Puts back the rows and text <paramref name="edit"/> changed as it found them, and builds the layout again.</summary>
	public void Undo(MsnEdit edit) {
		WriteBack(edit, after: false);
		Rebuild();
	}

	/// <summary>Puts back the rows and text <paramref name="edit"/> changed as it left them, and builds the layout again.</summary>
	public void Redo(MsnEdit edit) {
		WriteBack(edit, after: true);
		Rebuild();
	}

	/// <summary>
	/// Stands row-6 point <paramref name="point"/> at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>),
	/// writing the record that supplies its coordinates, the GUID's last base record, so everything naming the point moves
	/// with it. Throws <see cref="InvalidOperationException"/> for a point no record carries, or whose coordinates are copied
	/// from a template or summed from two other points, which no retail point is
	/// (docs/retail/formats/msn-mission-file.md#row-6-field-decode--the-point-record-dat_0047064e-22-bytesrecord).
	/// </summary>
	public MsnEdit SetPoint(short point, int x, int y, int z) {
		var source = EntryOf(PointRow, point).Supplier(MapPoint22.XWord);
		var record = (MapPoint22)RecordAt(source.Record)!;
		if (source.CopiedFrom != null) {
			throw new InvalidOperationException($"Point {point}'s coordinates come from a random table.");
		}

		if (record.SumFlag != 0) {
			throw new InvalidOperationException($"Point {point} is the sum of two other points.");
		}

		var before = Begin(PointRow);
		record.X = x;
		record.Y = y;
		record.Z = z;
		return End(before);
	}

	/// <summary>
	/// The point <paramref name="moving"/> fields naming row-6 GUID <paramref name="point"/> should name to stand together
	/// at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>), as <paramref name="placed"/>: the same
	/// GUID, its record rewritten, when those fields are all that name it (<see cref="CountPointRefs"/>) and it has one
	/// record of its own; otherwise a point appended with a new GUID (<see cref="Append"/>), which nothing else names.
	/// </summary>
	public MsnEdit PlacePoint(short point, int moving, int x, int y, int z, out short placed) {
		var points = Mission.Points ?? [];
		if (point != -1 && points.Count(record => record.GUID == point) == 1 && CountPointRefs(point) == moving
				&& Array.Find(points, record => record.GUID == point) is { RandomTableKey: -1, SumFlag: 0 } own) {
			var before = Begin(PointRow);
			own.X = x;
			own.Y = y;
			own.Z = z;
			placed = point;
			return End(before);
		}

		return Append(PointRow, new MapPoint22 { ConditionRef = -1, RandomTableKey = -1, Unk06 = -1, X = x, Y = y, Z = z }, out placed);
	}

	/// <summary>
	/// Sets <paramref name="field"/> of the record of GUID <paramref name="guid"/> that supplies it to
	/// <paramref name="value"/>, by the rules in the class summary. Throws <see cref="InvalidOperationException"/> for a
	/// GUID no record carries, and for a field a template supplies being unset or outside rows 12 to 14.
	/// </summary>
	public MsnEdit SetField(MsnField field, short guid, short value) {
		var source = EntryOf(field.Row, guid).Supplier(field.Word);
		if (value == field.Unset) {
			if (source.CopiedFrom != null) {
				throw new InvalidOperationException($"A template supplies that field of row {field.Row}'s GUID {guid}, and an overlay cannot unset it.");
			}

			var unsetting = Begin(field.Row);
			foreach (var record in Records(field.Row).Where(record => record.GUID == guid)) {
				field.Set(record, field.Unset);
			}

			return End(unsetting);
		}

		if (source.CopiedFrom != null) {
			var overlaying = Begin(field.Row);
			if (LaterOverlay(field.Row, guid, source.Record) is not { } overlay) {
				overlay = OverlayRecord(RecordAt(source.Record)!);
				Add(field.Row, overlay);
			}

			field.Set(overlay, value);
			return End(overlaying);
		}

		var before = Begin(field.Row);
		field.Set(RecordAt(source.Record)!, value);
		return End(before);
	}

	/// <summary>
	/// Unsets <paramref name="field"/> in every base record of GUID <paramref name="guid"/>, those carrying its first record's
	/// condition, leaving the records under other conditions setting it over the base where they hold, where
	/// <see cref="SetField"/> unsets it in every record. Throws <see cref="InvalidOperationException"/> for a GUID no record
	/// carries, and where a template the base picks then supplies another value, which no overlay can unset.
	/// </summary>
	public MsnEdit UnsetBaseField(MsnField field, short guid) {
		var before = Begin(field.Row);
		foreach (var id in EntryOf(field.Row, guid).Base) {
			field.Set(RecordAt(id)!, field.Unset);
		}

		var edit = End(before);
		if (field.Get(EntryOf(field.Row, guid).Fields) != field.Unset) {
			WriteBack(edit, after: false);
			Rebuild();
			throw new InvalidOperationException($"A template supplies that field of row {field.Row}'s GUID {guid}, and an overlay cannot unset it.");
		}

		return edit;
	}

	/// <summary>
	/// Sets header word <paramref name="word"/> (<c>script.dat</c> offset <c>word * 2</c>) as row 2 patches it to
	/// <paramref name="value"/>. Row 2 patches one header, so the record written is, as for an overlay field, the header's
	/// supplier of the word (<see cref="LayoutHeader.Supplier"/>), the last base record that sets it, else the first
	/// record; with no row-2 record, a record appended unconditioned that sets nothing else. The word's unset value
	/// (<see cref="MissionSettingsPatch.Unset"/>) is written into every row-2 record that sets the word, whatever its
	/// condition, since a record cannot carry it over another's. A launch that writes over the word
	/// (<see cref="MissionGeneration"/>) still does.
	/// </summary>
	public MsnEdit SetHeaderWord(int word, short value) {
		if (word is < 0 or >= MissionGeneration.HeaderWords) {
			throw new ArgumentOutOfRangeException(nameof(word), word, $"The header has {MissionGeneration.HeaderWords} words.");
		}

		var patches = Mission.SettingsPatches ?? [];
		int at = MissionSettingsPatch.HeaderWordsStart + word;
		short unset = MissionSettingsPatch.Unset(word);
		var before = Begin(SettingsRow);
		if (value == unset) {
			foreach (var patch in patches) {
				patch.Data[at] = unset;
			}

			return End(before);
		}

		if (Layout.Header is { } patched) {
			patches[patched.Supplier(word).Index].Data[at] = value;
			return End(before);
		}

		var data = new short[MissionSettingsPatch.Words];
		data[0] = -1;
		for (int header = 0; header < MissionGeneration.HeaderWords; header++) {
			data[MissionSettingsPatch.HeaderWordsStart + header] = MissionSettingsPatch.Unset(header);
		}

		Array.Fill(data, (short)-1, MissionSettingsPatch.ClearListStart, MissionSettingsPatch.Words - MissionSettingsPatch.ClearListStart);
		data[at] = value;
		Add(SettingsRow, new MissionSettingsPatch { Data = data });
		return End(before);
	}

	/// <summary>
	/// Why the mission cannot be marked for HERCULAN alone, or back, or null when it can. A file that ends inside its
	/// objectives (retail's <c>DEMO2</c>) cannot carry the block: retail would read it as the rest of the last objective.
	/// </summary>
	public string? WhyNoHerculanOnly => Mission.TruncatedRow17Tail != null ? "The mission file ends inside its objectives." : null;

	/// <summary>
	/// Marks the mission as meant for HERCULAN alone, or not (<see cref="MissionExtensions.HerculanOnly"/>), keeping whatever
	/// else its block holds. Throws <see cref="InvalidOperationException"/> when <see cref="WhyNoHerculanOnly"/> says why not.
	/// </summary>
	public MsnEdit SetHerculanOnly(bool on) {
		if (WhyNoHerculanOnly is { } why) {
			throw new InvalidOperationException(why);
		}

		var before = BeginExtensions();
		Mission.Extensions = (Mission.Extensions ?? new MissionExtensions()) with { HerculanOnly = on };
		return End(before);
	}

	/// <summary>
	/// Points entry <paramref name="entry"/> of route GUID <paramref name="route"/>'s waypoint list at row-6 GUID
	/// <paramref name="point"/>, in the record whose list the route shows: its last base record, or, for a list a template
	/// supplies, that template, which every route picking it shares.
	/// </summary>
	public MsnEdit SetWaypoint(short route, int entry, short point) {
		var record = ListRecord(route);
		if (entry < 0 || entry >= record.Waypoints.Length) {
			throw new InvalidOperationException($"Route {route}'s list has no entry {entry}.");
		}

		var before = Begin(RouteRow);
		record.Waypoints[entry] = point;
		return End(before);
	}

	/// <summary>
	/// Replaces route GUID <paramref name="route"/>'s whole waypoint list with <paramref name="waypoints"/>, row-6 GUIDs in
	/// route order, in the record whose list the route holds, as <see cref="SetWaypoint"/> chooses it.
	/// </summary>
	public MsnEdit SetWaypoints(short route, short[] waypoints) {
		var record = ListRecord(route);
		var before = Begin(RouteRow);
		record.Waypoints = (short[])waypoints.Clone();
		return End(before);
	}

	/// <summary>
	/// A copy of the waypoint list route GUID <paramref name="route"/> shows: the row-6 GUIDs of the record
	/// <see cref="SetWaypoints"/> writes, entry for entry.
	/// </summary>
	public short[] Waypoints(short route) => (short[])ListRecord(route).Waypoints.Clone();

	/// <summary>
	/// Whether a template supplies route GUID <paramref name="route"/>'s list, so that the list is a GUID <c>-1</c>
	/// template every route picking it shares, and an edit of it changes them all.
	/// </summary>
	public bool WaypointsShared(short route) => EntryOf(RouteRow, route).Supplier(MergeRules.RouteListWord).CopiedFrom != null;

	/// <summary>
	/// The row-9 record that supplies trigger area GUID <paramref name="area"/>: its last base record, which row 9's replace
	/// of a repeated GUID leaves the area holding whole. Row 9 has no random table key. Throws
	/// <see cref="InvalidOperationException"/> for a GUID no record carries.
	/// </summary>
	public TriggerArea12 AreaRecord(short area) =>
		(TriggerArea12)RecordAt(EntryOf(AreaRow, area).Supplier(TriggerArea12.ShapeWord).Record)!;

	/// <summary>Rewrites, through <paramref name="write"/>, the row-9 record <see cref="AreaRecord"/> answers.</summary>
	public MsnEdit SetArea(short area, Action<TriggerArea12> write) {
		var record = AreaRecord(area);
		var before = Begin(AreaRow);
		write(record);
		return End(before);
	}

	/// <summary>
	/// Appends <paramref name="record"/> to row <paramref name="row"/> under a new GUID, <paramref name="guid"/>: the row's
	/// largest plus one, since GUIDs are per row. A record with a new GUID merges with none, so once it survives it is
	/// exported last in its block.
	/// </summary>
	public MsnEdit Append(int row, MapObject record, out short guid) {
		short largest = Records(row).Select(existing => existing.GUID).DefaultIfEmpty((short)-1).Max();
		if (largest == short.MaxValue) {
			throw new InvalidOperationException($"Row {row}'s GUIDs already reach {short.MaxValue}.");
		}

		guid = (short)(largest + 1);
		record.GUID = guid;
		var before = Begin(row);
		Add(row, record);
		return End(before);
	}

	/// <summary>
	/// Appends a copy of every record of row <paramref name="row"/>'s GUID <paramref name="guid"/>, in file order and each
	/// under its own condition, under one new GUID, <paramref name="copy"/>: the row's largest plus one. The copy survives
	/// and merges as the GUID does in every state. A copied record that picks from a random table picks again, which moves every later
	/// draw (docs/herculan/handoff-editor-document.md#conditions-and-random-tables);
	/// row 15's records pick from none in retail.
	/// </summary>
	public MsnEdit AppendCopy(int row, short guid, out short copy) {
		var records = Records(row).Where(record => record.GUID == guid).ToList();
		if (records.Count == 0) {
			throw new InvalidOperationException($"Row {row} has no record with GUID {guid}.");
		}

		short largest = Records(row).Max(existing => existing.GUID);
		if (largest == short.MaxValue) {
			throw new InvalidOperationException($"Row {row}'s GUIDs already reach {short.MaxValue}.");
		}

		copy = (short)(largest + 1);
		var before = Begin(row);
		foreach (var record in records) {
			var copied = (MapObject)MsnRows.CopyRecord(record);
			copied.GUID = copy;
			Add(row, copied);
		}

		return End(before);
	}

	/// <summary>
	/// Removes every record of row <paramref name="row"/>'s GUID <paramref name="guid"/>, whatever its condition. Nothing
	/// is renumbered: <c>.MSN</c> refs name GUIDs, the generation resolves every index again, and a ref to the removed GUID
	/// resolves to <c>-1</c> (<c>Msn_ResolveRow6Ref</c> (<c>00415b44</c>) and its siblings). Removing a record that picks from a
	/// random table moves every later draw.
	/// </summary>
	public MsnEdit Remove(int row, short guid) {
		var records = MsnRows.Get(Mission, row);
		var kept = records.Cast<MapObject>().Where(record => record.GUID != guid).ToList();
		if (kept.Count == records.Length) {
			throw new InvalidOperationException($"Row {row} has no record with GUID {guid}.");
		}

		var before = Begin(row);
		var shrunk = Array.CreateInstance(records.GetType().GetElementType()!, kept.Count);
		for (int i = 0; i < kept.Count; i++) {
			shrunk.SetValue(kept[i], i);
		}

		MsnRows.Set(Mission, row, shrunk);
		return End(before);
	}

	/// <summary>
	/// Rewrites, through <paramref name="write"/>, every record of row <paramref name="row"/>'s GUID <paramref name="guid"/>,
	/// whatever its condition: for a word the generation neither exports nor merges, such as a pair count, which each record
	/// keeps for its own pairs.
	/// </summary>
	public MsnEdit RewriteEvery<T>(int row, short guid, Action<T> write) where T : MapObject {
		var before = Begin(row);
		foreach (var record in Records(row).Where(record => record.GUID == guid).Cast<T>()) {
			write(record);
		}

		return End(before);
	}

	/// <summary>
	/// The template that supplies <paramref name="field"/> of GUID <paramref name="guid"/>'s base: the GUID
	/// <c>-1</c> record the supplier copied it from (<see cref="WordSource.CopiedFrom"/>), or null when the field is the
	/// record's own or an overlay's. Throws as <see cref="SetField"/> does for a GUID no record carries.
	/// </summary>
	public MsnRecordId? TemplateOf(MsnField field, short guid) => EntryOf(field.Row, guid).Supplier(field.Word).CopiedFrom;

	/// <summary>
	/// Puts <paramref name="record"/> at <paramref name="index"/> of row <paramref name="row"/>, the records from there on
	/// moving up one: for a template, a GUID <c>-1</c> record, which a record picks only when it is read before it
	/// (docs/retail/formats/msn-mission-file.md#random-tables), or a row-1 condition that a later one must be able to require
	/// (<see cref="PresenceRules"/>). A GUID <c>-1</c> record is never exported or merged, so no <c>script.dat</c> index
	/// moves; a condition's GUID is the caller's.
	/// </summary>
	public MsnEdit Insert(int row, int index, MapObject record) {
		var records = MsnRows.Get(Mission, row);
		if (index < 0 || index > records.Length) {
			throw new InvalidOperationException($"Row {row} has no place {index}.");
		}

		var before = Begin(row);
		var grown = Array.CreateInstance(records.GetType().GetElementType()!, records.Length + 1);
		Array.Copy(records, 0, grown, 0, index);
		grown.SetValue(record, index);
		Array.Copy(records, index, grown, index + 1, records.Length - index);
		MsnRows.Set(Mission, row, grown);
		return End(before);
	}

	/// <summary>
	/// Removes record <paramref name="index"/> of GUID-keyed row <paramref name="row"/> alone, for a template, which
	/// has no GUID of its own to remove it by (<see cref="Remove"/>).
	/// </summary>
	public MsnEdit RemoveAt(int row, int index) {
		var records = MsnRows.Get(Mission, row);
		if (index < 0 || index >= records.Length) {
			throw new InvalidOperationException($"Row {row} has no record {index}.");
		}

		var before = Begin(row);
		var shrunk = Array.CreateInstance(records.GetType().GetElementType()!, records.Length - 1);
		Array.Copy(records, 0, shrunk, 0, index);
		Array.Copy(records, index + 1, shrunk, index, records.Length - index - 1);
		MsnRows.Set(Mission, row, shrunk);
		return End(before);
	}

	/// <summary>
	/// Rewrites, through <paramref name="write"/>, record <paramref name="index"/> of GUID-keyed row <paramref name="row"/>
	/// alone: a template, or the one record of a GUID that carries its random table key.
	/// </summary>
	public MsnEdit RewriteAt<T>(int row, int index, Action<T> write) where T : MapObject {
		var records = MsnRows.Get(Mission, row);
		if (index < 0 || index >= records.Length || records.GetValue(index) is not T record) {
			throw new InvalidOperationException($"Row {row} has no record {index}.");
		}

		var before = Begin(row);
		write(record);
		return End(before);
	}

	/// <summary>
	/// Unsets every member slot naming roster GUID <paramref name="member"/> in every record of row-16 group GUID
	/// <paramref name="group"/>, whatever its condition, since an overlay cannot carry an unset value.
	/// </summary>
	public MsnEdit ClearMember(short group, short member) {
		var before = Begin(GroupRow);
		foreach (var record in Records(GroupRow).Cast<MissionGroup164>().Where(record => record.GUID == group)) {
			for (int slot = 0; slot < record.MemberRefs.Length; slot++) {
				if (record.MemberRefs[slot] == member) {
					record.MemberRefs[slot] = -1;
				}
			}
		}

		return End(before);
	}

	// ---- Rows 4 and 17, and the text --------------------------------------------------------------

	/// <summary>
	/// Rewrites record <paramref name="index"/> of row <paramref name="row"/>, 1, 4 or 17, through <paramref name="write"/>.
	/// Rows 4 and 17 have no GUID, nothing names one of their records and neither repeats or overlays, so the record is the
	/// one that supplies everything it exports, whatever its condition (docs/retail/formats/msn-mission-file.md#row-17-field-decode--the-objective-record-dat_0047064a-58-bytesrecord).
	/// A row-1 record is read on its own, a repeated GUID merging nothing (docs/retail/formats/msn-mission-file.md#the-conditions--row-1),
	/// so it too is the record that decides what it tests.
	/// </summary>
	public MsnEdit Rewrite<T>(int row, int index, Action<T> write) where T : class {
		if (row != 1) {
			CheckUnkeyed(row);
		}

		var records = MsnRows.Get(Mission, row);
		if (index < 0 || index >= records.Length || records.GetValue(index) is not T record) {
			throw new InvalidOperationException($"Row {row} has no record {index}.");
		}

		var before = Begin(row);
		write(record);
		return End(before);
	}

	/// <summary>Appends <paramref name="record"/> to row <paramref name="row"/>, 4 or 17, as its last record.</summary>
	public MsnEdit AppendUnkeyed(int row, object record) {
		CheckUnkeyed(row);
		var before = Begin(row);
		Add(row, record);
		return End(before);
	}

	/// <summary>
	/// Removes record <paramref name="index"/> of row <paramref name="row"/>, 4 or 17. Nothing names one of their records, so
	/// the records after it moving down one changes no ref.
	/// </summary>
	public MsnEdit RemoveUnkeyed(int row, int index) {
		CheckUnkeyed(row);
		var records = MsnRows.Get(Mission, row);
		if (index < 0 || index >= records.Length) {
			throw new InvalidOperationException($"Row {row} has no record {index}.");
		}

		var before = Begin(row);
		var shrunk = Array.CreateInstance(records.GetType().GetElementType()!, records.Length - 1);
		Array.Copy(records, 0, shrunk, 0, index);
		Array.Copy(records, index + 1, shrunk, index, records.Length - index - 1);
		MsnRows.Set(Mission, row, shrunk);
		return End(before);
	}

	/// <summary>
	/// Appends copies of <paramref name="entries"/> to the <c>.ENG</c>, in order, giving the mission an <c>.ENG</c> when it has
	/// none. A record whose id no earlier surviving record has places a new <c>mission.str</c> line, after every other
	/// (docs/retail/formats/msn-mission-file.md#the-eng-string-table).
	/// </summary>
	public MsnEdit AppendText(IEnumerable<MissionStringFile.StringEntry> entries) {
		var before = BeginText();
		Text ??= new MissionStringFile { Strings = [] };
		Text.Strings = [.. Text.Strings ?? [], .. MsnRows.CopyText([.. entries])!];
		return End(before);
	}

	/// <summary>
	/// Sets the text of <c>.ENG</c> record <paramref name="record"/>, by its index in the file, to <paramref name="text"/>,
	/// NUL-terminated as the files store it.
	/// </summary>
	public MsnEdit SetText(int record, string text) {
		var strings = Text?.Strings ?? [];
		if (record < 0 || record >= strings.Length) {
			throw new InvalidOperationException($"The mission's text has no record {record}.");
		}

		var before = BeginText();
		Text!.Strings![record].Val = text + "\0";
		Text.Strings[record].Len = (short)(text.Length + 1);
		return End(before);
	}

	/// <summary>Removes every <c>.ENG</c> record whose id is one of <paramref name="ids"/>, whatever its condition.</summary>
	public MsnEdit RemoveText(IReadOnlyCollection<short> ids) {
		var before = BeginText();
		if (Text?.Strings is { } strings) {
			Text.Strings = strings.Where(entry => !ids.Contains(entry.Guid)).ToArray();
		}

		return End(before);
	}

	/// <summary>
	/// Points text slot <paramref name="slot"/> of row-5 debrief record <paramref name="record"/> at <c>.ENG</c> id
	/// <paramref name="id"/>. Row 5 has no GUID and nothing names one of its records, so the record is the one that supplies
	/// what it holds (docs/retail/formats/msn-mission-file.md#row-5--the-debrief); its layout is <see cref="DebriefText"/>'s.
	/// </summary>
	public MsnEdit SetDebriefLine(int record, int slot, short id) {
		var bytes = Mission.DebriefBytes ?? [];
		if (record < 0 || (record + 1) * DebriefText.RecordLength > bytes.Length || slot < 0 || slot >= DebriefText.Slots) {
			throw new InvalidOperationException($"The debrief has no record {record} slot {slot}.");
		}

		var before = Begin(5);
		BitConverter.TryWriteBytes(Mission.DebriefBytes.AsSpan(DebriefText.SlotOffset(record, slot)), id);
		return End(before);
	}

	// ---- Internals --------------------------------------------------------------------------------

	private static void CheckUnkeyed(int row) {
		if (row is not (4 or 17)) {
			throw new ArgumentOutOfRangeException(nameof(row), row, "Rows 4 and 17 are the rows with no GUID.");
		}
	}

	// Writes each row and the text an edit changed as one side of it left them.
	private void WriteBack(MsnEdit edit, bool after) {
		edit.WriteTo(Mission, after);
		if (edit.TextSide(after, out var strings)) {
			if (strings == null) {
				Text = null;
			} else if (Text == null) {
				Text = new MissionStringFile { Strings = strings };
			} else {
				Text.Strings = strings;
			}
		}
	}

	private LayoutEntry EntryOf(int row, short guid) =>
		Layout.Entry(row, guid) ?? throw new InvalidOperationException($"Row {row} has no record with GUID {guid}.");

	// The row-8 record whose waypoint list route GUID <route> shows: its last base record, or the template it
	// copies the list from.
	private WaypointGroup ListRecord(short route) {
		var source = EntryOf(RouteRow, route).Supplier(MergeRules.RouteListWord);
		return (WaypointGroup)RecordAt(source.CopiedFrom ?? source.Record)!;
	}

	private IEnumerable<MapObject> Records(int row) => MsnRows.Get(Mission, row).Cast<MapObject>();

	private void Add(int row, object record) {
		var records = MsnRows.Get(Mission, row);
		var grown = Array.CreateInstance(records.GetType().GetElementType()!, records.Length + 1);
		Array.Copy(records, grown, records.Length);
		grown.SetValue(record, records.Length);
		MsnRows.Set(Mission, row, grown);
	}

	// What an edit is about to change, copied: the rows, the text when it is about to change that, and HERCULAN's settings when
	// it is about to change those, which are replaced rather than changed in place, so need no copy.
	private sealed record Snapshot(Dictionary<int, Array> Rows, bool Text, MissionStringFile.StringEntry[]? TextBefore,
		bool Extensions = false, MissionExtensions? ExtensionsBefore = null);

	private Snapshot Begin(params int[] rows) =>
		new(rows.Distinct().ToDictionary(row => row, row => MsnRows.Copy(MsnRows.Get(Mission, row))), false, null);

	private Snapshot BeginText() => new([], true, MsnRows.CopyText(Text?.Strings));

	private Snapshot BeginExtensions() => new([], false, null, true, Mission.Extensions);

	// The edit that changed what Begin copied, once it has; builds the layout again and writes the .MSN, and puts the edit
	// back if that fails.
	private MsnEdit End(Snapshot before) {
		var rows = new SortedDictionary<int, (Array Before, Array After)>();
		foreach (var (row, records) in before.Rows) {
			rows[row] = (records, MsnRows.Copy(MsnRows.Get(Mission, row)));
		}

		var edit = new MsnEdit(rows, before.Text ? new TextSides(before.TextBefore, MsnRows.CopyText(Text?.Strings)) : null,
			before.Extensions ? new ExtensionSides(before.ExtensionsBefore, Mission.Extensions) : null);
		try {
			Rebuild();
		} catch {
			WriteBack(edit, after: false);
			Rebuild();
			throw;
		}

		_change?.Add(edit);
		return edit;
	}

	/// <summary>
	/// A roster record of <paramref name="asking"/>'s GUID and condition that sets no field: each field the generator's
	/// merge overlays at its unset value (<c>Msn_MergeRow12</c> (<c>00416864</c>), <c>Msn_MergeRow13</c> (<c>00416c17</c>),
	/// <c>Msn_MergeRow14</c> (<c>00416e7a</c>)), and no random table key, so its refs are resolved as it is read. The words no
	/// merge reads are <paramref name="asking"/>'s.
	/// </summary>
	private static MapObject OverlayRecord(MapObject asking) => asking switch {
		MechRosterEntry144 mech => new MechRosterEntry144 {
			GUID = mech.GUID, ConditionRef = mech.ConditionRef, RandomTableKey = -1,
			CompoundConditionPartner = mech.CompoundConditionPartner, AiRadarActive = 0, AiCruiseSpeed = 0,
			DeadZone = new short[18], TypeIndex = -1, WeaponRefs = Unset(10), PositionRef = -1, HeadingRef = -1,
			PairCount = mech.PairCount, OutOfActionReport = Unset(20), WeaponSecondary = Enumerable.Repeat((short)5, 10).ToArray(),
			Constant2 = 2, EngagementActionRef = -1, DefeatActionRef = -1, StartingCondition = 100,
		},
		FlyerRosterEntry102 flyer => new FlyerRosterEntry102 {
			GUID = flyer.GUID, ConditionRef = flyer.ConditionRef, RandomTableKey = -1, Unk06 = flyer.Unk06,
			FlagSpan = new short[20], PositionRef = -1, HeadingRef = -1, TypeIndex = -1, PairCount = flyer.PairCount,
			OutOfActionReport = Unset(20), EngagementActionRef = -1, DefeatActionRef = -1, UnkVal_100 = 100,
		},
		BaseRosterEntry62 structure => new BaseRosterEntry62 {
			GUID = structure.GUID, ConditionRef = structure.ConditionRef, RandomTableKey = -1, Unk06 = structure.Unk06,
			TypeIndex = -1, PositionRef = -1, HeadingRef = -1, PairCount = structure.PairCount, OutOfActionReport = Unset(20),
			EngagementActionRef = -1, DefeatActionRef = -1, StartingCondition = 100,
		},
		_ => throw new InvalidOperationException($"A template supplies a field of a {asking.GetType().Name}, which only rows 12 to 14 carry in an overlay record."),
	};

	/// <summary>
	/// The record of row <paramref name="row"/>'s GUID <paramref name="guid"/> that already overlays what
	/// <paramref name="supplier"/> copies from a template, or null: the GUID's last record, when it comes after the supplier,
	/// carries the base's condition and picks from no random table, such as the one an earlier field's edit appended. Writing a field
	/// there merges as a record appended after it would, so the fields of one edit, a chassis and its mounts, share one record.
	/// </summary>
	private MapObject? LaterOverlay(int row, short guid, MsnRecordId supplier) {
		var entry = EntryOf(row, guid);
		var last = entry.Records[^1];
		var record = RecordAt(last);
		bool unkeyed = record is MechRosterEntry144 { RandomTableKey: -1 } or FlyerRosterEntry102 { RandomTableKey: -1 }
			or BaseRosterEntry62 { RandomTableKey: -1 };
		return last.Index > supplier.Index && entry.Base.Contains(last) && unkeyed ? record : null;
	}

	private static short[] Unset(int count) => Enumerable.Repeat((short)-1, count).ToArray();

	private static void CopyBlocks(ScriptDat from, ScriptDat to) {
		to.HeaderBytes = from.HeaderBytes;
		to.Coordinates = from.Coordinates;
		to.Headings = from.Headings;
		to.WaypointGroups = from.WaypointGroups;
		to.TriggerAreas = from.TriggerAreas;
		to.Actions = from.Actions;
		to.ActionTimers = from.ActionTimers;
		to.Mechs = from.Mechs;
		to.Flyers = from.Flyers;
		to.Bases = from.Bases;
		to.Orders = from.Orders;
		to.Groups = from.Groups;
		to.Objectives = from.Objectives;
		to.ObjectiveTextRefs = from.ObjectiveTextRefs;
	}
}

/// <summary>
/// A field naming a row-6 point (<see cref="MissionDocument.PointRefs"/>): its <paramref name="Record"/>, and
/// <paramref name="Field"/>, for a route the entry of its list, for a trigger area 0 its first corner or center and 1 a box's
/// second corner, and 0 for any other record.
/// </summary>
public readonly record struct PointRef(MsnRecordId Record, int Field);
