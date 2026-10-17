using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// A mission held as its <c>.MSN</c> and <c>.ENG</c>, with the <c>script.dat</c> and <c>mission.str</c> generated from
/// them (<see cref="Regenerate"/>) and the provenance that maps each generated record back to the <c>.MSN</c> records
/// that supplied it. An edit writes the <c>.MSN</c> record that supplied the value and generates again; the design is
/// docs/herculan/handoff-editor-mission-editing.md#holding-the-msn-as-the-document.
///
/// <para>Every edit returns an <see cref="MsnEdit"/> holding the rows it changed, which <see cref="Undo"/> and
/// <see cref="Redo"/> put back. Several edits made inside one <see cref="Change"/> come back as one, and are all put back
/// when one of them fails.</para>
///
/// <para>Which record an edit writes, per the handoff's table "An edit writes the record that supplied the value":
/// a field of a row whose repeated GUID replaces (rows 6 and 8) is written in the last surviving record of the GUID, and
/// a field of a row whose repeated GUID overlays (rows 12 to 16) in the last surviving record that sets it, else the
/// first; both are what <see cref="SlotProvenance.Word"/> names. A roster field copied from a variant is written in a
/// record of the same GUID appended to the row, carrying that field alone, under the condition of the record that drew
/// the variant, so it overlays whatever is drawn in every state that record survives in. A field set to its unset value
/// is unset in every record of the GUID, since an overlay cannot carry an unset value.</para>
/// </summary>
public sealed class MissionDocument {
	private const int PointRow = 6;
	private const int HeadingRow = 7;
	private const int RouteRow = 8;
	private const int GroupRow = 16;

	/// <summary>Row 8's word 4, its waypoint count and the list after it, as <see cref="SlotProvenance.Word"/> takes it.</summary>
	private const int RouteListWord = 4;

	/// <summary>The exported slot each GUID of rows 6 to 16 reached in the last generation.</summary>
	private readonly Dictionary<(int Row, short Guid), SlotProvenance> _slots = new();

	/// <summary>The edits made so far inside <see cref="Change"/>, or null outside one.</summary>
	private List<MsnEdit>? _change;

	public MissionDocument(MissionFile mission, MissionStringFile? text, MissionGeneration generation) {
		Mission = mission;
		Text = text;
		Generation = generation;
		Regenerate();
	}

	/// <summary>A document over the <c>.MSN</c> <paramref name="msn"/> and the <c>.ENG</c> <paramref name="text"/>, or none.</summary>
	public static MissionDocument Read(byte[] msn, byte[]? text, MissionGeneration generation) =>
		new(new MissionFileTransformer().Parse(msn) ?? throw new InvalidDataException("Not a mission file."),
			text == null ? null : new MissionStringFileTransformer().Parse(text), generation);

	public MissionFile Mission { get; }

	/// <summary>The <c>.ENG</c>, or null when the mission has none.</summary>
	public MissionStringFile? Text { get; }

	public MissionGeneration Generation { get; }

	/// <summary>
	/// The generated <c>script.dat</c>: one object for the document's life, whose header and blocks each generation
	/// replaces, so whatever holds it reads the latest.
	/// </summary>
	public ScriptDat Script { get; } = new();

	/// <summary>The generated <c>script.dat</c>'s bytes.</summary>
	public byte[] ScriptBytes { get; private set; } = [];

	/// <summary>The generated <c>mission.str</c>'s bytes.</summary>
	public byte[] MissionText { get; private set; } = [];

	/// <summary>Which records supplied the generated files, from the last generation.</summary>
	public MissionProvenance Provenance { get; private set; } = new();

	/// <summary>
	/// The last generation's run, which answers what the shell reads back from a loaded mission besides the files: its
	/// squad positions and group 0's machines (<see cref="MissionGenerator.SquadPositions"/>, <see cref="MissionGenerator.Herc"/>).
	/// </summary>
	public MissionGenerator Generator { get; private set; } = null!;

	/// <summary>Writes the files, generates from them and replaces <see cref="Script"/>'s blocks with what was generated.</summary>
	public void Regenerate() {
		byte[] msn = new MissionFileTransformer().Write(Mission) ?? throw new InvalidOperationException("The mission file does not write.");
		byte[]? text = Text == null ? null : new MissionStringFileTransformer().Write(Text);
		var generator = Generation.Run(msn, text);
		var script = generator.WriteScriptDat();
		var parsed = new ScriptDatTransformer().Parse(script)
			?? throw new InvalidOperationException("The generated script.dat does not parse.");

		ScriptBytes = script;
		MissionText = generator.WriteMissionText();
		Provenance = generator.Provenance;
		Generator = generator;
		CopyBlocks(parsed, Script);

		_slots.Clear();
		for (int row = PointRow; row <= GroupRow; row++) {
			foreach (var slot in Provenance.Slots(row - 5)) {
				if (RecordAt(slot.Created) is { } record) {
					_slots[(row, record.GUID)] = slot;
				}
			}
		}
	}

	// ---- Lookups ----------------------------------------------------------------------------------

	/// <summary>
	/// The GUID of the <c>.MSN</c> record that created record <paramref name="index"/> of <c>script.dat</c> block
	/// <paramref name="block"/> (1 to 11), or <c>-1</c> when there is none.
	/// </summary>
	public short GuidOf(int block, int index) =>
		block is >= 1 and <= 11 && Provenance.Slot(block, index) is { } slot && RecordAt(slot.Created) is { } record
			? record.GUID
			: (short)-1;

	/// <summary>The <c>script.dat</c> index row <paramref name="row"/>'s GUID <paramref name="guid"/> was exported at, or null when it was not.</summary>
	public int? ExportedIndex(int row, short guid) => _slots.TryGetValue((row, guid), out var slot) ? slot.Index : null;

	/// <summary>
	/// Whether a variant supplies <paramref name="field"/> of GUID <paramref name="guid"/>, so that <see cref="SetField"/> writes
	/// it in an overlay record, which cannot carry the field's unset value. Throws as <see cref="SetField"/> does for a GUID no
	/// record exports.
	/// </summary>
	public bool SuppliedByVariant(MsnField field, short guid) => SlotOf(field.Row, guid).Word(field.Word).CopiedFrom != null;

	/// <summary>The GUID-keyed record <paramref name="id"/> names, or null for one of rows 4 and 17, which have no GUID.</summary>
	public MapObject? RecordAt(MsnRecordId id) => MsnRows.Get(Mission, id.Row).GetValue(id.Index) as MapObject;

	/// <summary>
	/// How many fields name row-6 GUID <paramref name="point"/>: route waypoints, a trigger area's first corner or center
	/// and a box's second corner, the three rosters' points, an order's point, a group's point and an objective's point.
	/// Every record counts whatever its condition, since one that fails in the generation's state names the point in
	/// another.
	/// </summary>
	public int CountPointRefs(short point) {
		if (point == -1) {
			return 0;
		}

		int Is(short reference) => reference == point ? 1 : 0;
		int count = 0;
		foreach (var route in Mission.WaypointGroups ?? []) {
			count += route.Waypoints.Count(reference => reference == point);
		}

		foreach (var area in Mission.TriggerAreas ?? []) {
			count += Is(area.PointRef) + (area.Shape == 0 ? Is(area.SecondPointOrRadius) : 0);
		}

		count += (Mission.Mechs ?? []).Sum(record => Is(record.PositionRef));
		count += (Mission.Flyers ?? []).Sum(record => Is(record.PositionRef));
		count += (Mission.Bases ?? []).Sum(record => Is(record.PositionRef));
		count += (Mission.Orders ?? []).Sum(record => Is(record.PointRef));
		count += (Mission.Groups ?? []).Sum(record => Is(record.PositionRef));
		count += (Mission.Objectives ?? []).Sum(record => record == null ? 0 : Is(record.PointRef));
		return count;
	}

	/// <summary>
	/// The first member slot of group GUID <paramref name="group"/> that no record of the GUID fills, whatever its
	/// condition, so writing it fills it in every state; or <c>-1</c> when every slot is filled somewhere.
	/// </summary>
	public int FreeMemberSlot(short group) {
		var records = (Mission.Groups ?? []).Where(record => record.GUID == group).ToList();
		for (int slot = 0; slot < 20; slot++) {
			if (records.All(record => record.MemberRefs[slot] == -1)) {
				return slot;
			}
		}

		return -1;
	}

	/// <summary>
	/// The GUID of a row-7 heading that reads <paramref name="degrees"/> and can be shared: the GUID's one record,
	/// unconditioned and drawing no variant. <c>-1</c> when there is none.
	/// </summary>
	public short ShareableHeading(short degrees) {
		var headings = Mission.Headings ?? [];
		foreach (var heading in headings) {
			if (heading.GUID != -1 && heading.Degrees == degrees && heading.ConditionRef == -1 && heading.VariantKey == -1
					&& headings.Count(other => other.GUID == heading.GUID) == 1) {
				return heading.GUID;
			}
		}

		return -1;
	}

	// ---- Edits ------------------------------------------------------------------------------------

	/// <summary>
	/// Makes the edits <paramref name="edits"/> calls for as one: answers them combined, or, when one throws, puts back
	/// every row they changed, generates again and rethrows.
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
				made[i].WriteTo(Mission, after: false);
			}

			Regenerate();
			throw;
		}

		_change = null;
		return MsnEdit.Combine(made);
	}

	/// <summary>Puts back the rows <paramref name="edit"/> changed as it found them, and generates again.</summary>
	public void Undo(MsnEdit edit) {
		edit.WriteTo(Mission, after: false);
		Regenerate();
	}

	/// <summary>Puts back the rows <paramref name="edit"/> changed as it left them, and generates again.</summary>
	public void Redo(MsnEdit edit) {
		edit.WriteTo(Mission, after: true);
		Regenerate();
	}

	/// <summary>
	/// Stands row-6 point <paramref name="point"/> at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>),
	/// writing the record that supplies its coordinates, so everything naming the point moves with it. Throws
	/// <see cref="InvalidOperationException"/> for a point no record exports, or whose coordinates are drawn from a variant
	/// or summed from two other points, which no retail point is
	/// (docs/retail/formats/msn-mission-file.md#row-6-field-decode--the-point-record-dat_0047064e-22-bytesrecord).
	/// </summary>
	public MsnEdit SetPoint(short point, int x, int y, int z) {
		var source = SlotOf(PointRow, point).Word(MapPoint22.XWord);
		var record = (MapPoint22)RecordAt(source.Record)!;
		if (source.CopiedFrom != null) {
			throw new InvalidOperationException($"Point {point}'s coordinates are drawn from a variant.");
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
				&& Array.Find(points, record => record.GUID == point) is { VariantKey: -1, SumFlag: 0 } own) {
			var before = Begin(PointRow);
			own.X = x;
			own.Y = y;
			own.Z = z;
			placed = point;
			return End(before);
		}

		return Append(PointRow, new MapPoint22 { ConditionRef = -1, VariantKey = -1, Unk06 = -1, X = x, Y = y, Z = z }, out placed);
	}

	/// <summary>
	/// Sets <paramref name="field"/> of the record of GUID <paramref name="guid"/> that supplies it to
	/// <paramref name="value"/>, by the rules in the class summary. Throws <see cref="InvalidOperationException"/> for a
	/// GUID no record exports, and for a field a variant supplies being unset or outside rows 12 to 14.
	/// </summary>
	public MsnEdit SetField(MsnField field, short guid, short value) {
		var source = SlotOf(field.Row, guid).Word(field.Word);
		if (value == field.Unset) {
			if (source.CopiedFrom != null) {
				throw new InvalidOperationException($"A variant supplies that field of row {field.Row}'s GUID {guid}, and an overlay cannot unset it.");
			}

			var unsetting = Begin(field.Row);
			foreach (var record in Records(field.Row).Where(record => record.GUID == guid)) {
				field.Set(record, field.Unset);
			}

			return End(unsetting);
		}

		if (source.CopiedFrom != null) {
			var overlay = OverlayRecord(RecordAt(source.Record)!);
			field.Set(overlay, value);
			var overlaying = Begin(field.Row);
			Add(field.Row, overlay);
			return End(overlaying);
		}

		var before = Begin(field.Row);
		field.Set(RecordAt(source.Record)!, value);
		return End(before);
	}

	/// <summary>
	/// Points entry <paramref name="entry"/> of route GUID <paramref name="route"/>'s waypoint list at row-6 GUID
	/// <paramref name="point"/>, in the record whose list the route holds: its last surviving record, or, for a list a
	/// variant supplies, that alternative, which every route drawing it shares.
	/// </summary>
	public MsnEdit SetWaypoint(short route, int entry, short point) {
		var source = SlotOf(RouteRow, route).Word(RouteListWord);
		var record = (WaypointGroup)RecordAt(source.CopiedFrom ?? source.Record)!;
		if (entry < 0 || entry >= record.Waypoints.Length) {
			throw new InvalidOperationException($"Route {route}'s list has no entry {entry}.");
		}

		var before = Begin(RouteRow);
		record.Waypoints[entry] = point;
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
	/// Removes every record of row <paramref name="row"/>'s GUID <paramref name="guid"/>, whatever its condition. Nothing
	/// is renumbered: <c>.MSN</c> refs name GUIDs, the generation resolves every index again, and a ref to the removed GUID
	/// resolves to <c>-1</c> (<c>Msn_ResolveRow6Ref</c> (<c>00415b44</c>) and its siblings). Removing a record that draws a
	/// variant moves every later draw.
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

	// ---- Internals --------------------------------------------------------------------------------

	private SlotProvenance SlotOf(int row, short guid) =>
		_slots.TryGetValue((row, guid), out var slot)
			? slot
			: throw new InvalidOperationException($"No record of row {row} with GUID {guid} reaches script.dat.");

	private IEnumerable<MapObject> Records(int row) => MsnRows.Get(Mission, row).Cast<MapObject>();

	private void Add(int row, object record) {
		var records = MsnRows.Get(Mission, row);
		var grown = Array.CreateInstance(records.GetType().GetElementType()!, records.Length + 1);
		Array.Copy(records, grown, records.Length);
		grown.SetValue(record, records.Length);
		MsnRows.Set(Mission, row, grown);
	}

	// The rows an edit is about to change, copied.
	private Dictionary<int, Array> Begin(params int[] rows) =>
		rows.Distinct().ToDictionary(row => row, row => MsnRows.Copy(MsnRows.Get(Mission, row)));

	// The edit that changed the rows Begin copied, once it has; generates again, and puts the rows back if that fails.
	private MsnEdit End(Dictionary<int, Array> before) {
		var rows = new SortedDictionary<int, (Array Before, Array After)>();
		foreach (var (row, records) in before) {
			rows[row] = (records, MsnRows.Copy(MsnRows.Get(Mission, row)));
		}

		var edit = new MsnEdit(rows);
		try {
			Regenerate();
		} catch {
			edit.WriteTo(Mission, after: false);
			Regenerate();
			throw;
		}

		_change?.Add(edit);
		return edit;
	}

	/// <summary>
	/// A roster record of <paramref name="asking"/>'s GUID and condition that sets no field: each field the generator's
	/// merge overlays at its unset value (<c>Msn_MergeRow12</c> (<c>00416864</c>), <c>Msn_MergeRow13</c> (<c>00416c17</c>),
	/// <c>Msn_MergeRow14</c> (<c>00416e7a</c>)), and no variant key, so its refs are resolved as it is read. The words no
	/// merge reads are <paramref name="asking"/>'s.
	/// </summary>
	private static MapObject OverlayRecord(MapObject asking) => asking switch {
		MechRosterEntry144 mech => new MechRosterEntry144 {
			GUID = mech.GUID, ConditionRef = mech.ConditionRef, VariantKey = -1,
			CompoundConditionPartner = mech.CompoundConditionPartner, AiRadarActive = 0, AiCruiseSpeed = 0,
			DeadZone = new short[18], TypeIndex = -1, WeaponRefs = Unset(10), PositionRef = -1, HeadingRef = -1,
			PairCount = mech.PairCount, OutOfActionReport = Unset(20), WeaponSecondary = Enumerable.Repeat((short)5, 10).ToArray(),
			Constant2 = 2, EngagementActionRef = -1, DefeatActionRef = -1, StartingCondition = 100,
		},
		FlyerRosterEntry102 flyer => new FlyerRosterEntry102 {
			GUID = flyer.GUID, ConditionRef = flyer.ConditionRef, VariantKey = -1, Unk06 = flyer.Unk06,
			FlagSpan = new short[20], PositionRef = -1, HeadingRef = -1, TypeIndex = -1, PairCount = flyer.PairCount,
			OutOfActionReport = Unset(20), EngagementActionRef = -1, DefeatActionRef = -1, UnkVal_100 = 100,
		},
		BaseRosterEntry62 structure => new BaseRosterEntry62 {
			GUID = structure.GUID, ConditionRef = structure.ConditionRef, VariantKey = -1, Unk06 = structure.Unk06,
			TypeIndex = -1, PositionRef = -1, HeadingRef = -1, PairCount = structure.PairCount, OutOfActionReport = Unset(20),
			EngagementActionRef = -1, DefeatActionRef = -1, StartingCondition = 100,
		},
		_ => throw new InvalidOperationException($"A variant supplies a field of a {asking.GetType().Name}, which only rows 12 to 14 carry in an overlay record."),
	};

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
