namespace HercWorks.Core.Data.File.Msn;

/// <summary>Row 4's three line lists: the objective lines the player is shown, the briefing and the intelligence report.</summary>
public enum MissionTextList {
	Objectives,
	Briefing,
	Intelligence,
}

/// <summary>
/// A record field that references <c>.ENG</c> text: <paramref name="Row"/> 4, 5, 10 or 17, the record's index in that
/// row, <paramref name="List"/> for row 4, and the slot of the field's array (0 for row 17's one field).
/// </summary>
public readonly record struct TextOwner(int Row, int Record, MissionTextList? List, int Slot) {
	/// <summary>Row-17 objective <paramref name="record"/>'s failure text.</summary>
	public static TextOwner Objective(int record) => new(17, record, null, 0);

	/// <summary>Slot <paramref name="slot"/> of <paramref name="list"/> in row-4 record <paramref name="record"/>.</summary>
	public static TextOwner Package(int record, MissionTextList list, int slot) => new(4, record, list, slot);
}

/// <summary>
/// The mission's text as record edits of a <see cref="MissionDocument"/>'s <c>.ENG</c> and the rows that reference it: a
/// line's text, row 4's line lists, and the ids and line count new text needs. What the <c>.ENG</c> holds and how the load
/// turns it into <c>data\mission.str</c> is docs/retail/formats/msn-mission-file.md#the-eng-string-table.
///
/// <list type="bullet">
/// <item>An edit of a line writes the <c>.ENG</c> record whose text the line shows (<see cref="LayoutLine.Supplier"/>), the
/// last record of its id carrying the first one's condition, as every edit writes the record that supplies the value; an
/// edit of its replacement under a condition (<see cref="SetLineUnder"/>) writes the last record of the id carrying that
/// condition, as the debrief's outcomes do (<see cref="DebriefText"/>).</item>
/// <item>Rows 4, 5, 10 and 17 reference text by id, the only rows that do (<c>Msn_ResolveTextRef</c> (<c>00415a7c</c>) is
/// called for them alone). An objective's failure text is three consecutive <c>mission.str</c> lines from the one it
/// references (docs/retail/formats/script-dat.md#block-12-in-memory--76-bytes-0x4c), taken here as its id and the next two
/// ids in the <c>.ENG</c>'s order (<see cref="FailureIds"/>). When an id a field shows is referenced by another field too,
/// an edit first gives the edited field its own copy: every <c>.ENG</c> record of the id copied under a new id, conditioned
/// ones included, and for a failure text all three ids as one block, so the other fields keep their text.</item>
/// <item>A new id is one past the largest any <c>.ENG</c> record has or any field references (<see cref="NewIds"/>). Ids
/// are matched by equality alone: <c>Msn_LoadEngText</c> (<c>0041768c</c>) keeps the records in an array sized by their
/// count and searches it, <c>MissionStr_Write</c> (<c>004179f0</c>) writes it in order, and every field is renumbered to a
/// line index; no table is indexed by id, and <c>-1</c> is the one id with a meaning (no text).</item>
/// <item>DBSIM reads at most <see cref="LineCap"/> lines of <c>mission.str</c> (<c>DBSim_LoadScriptDat</c>
/// (<c>00424308</c>)), so an edit that would take the text past that in any version of the mission is refused, every
/// <c>.ENG</c> id counted (<see cref="Lines"/>).</item>
/// <item>A row-4 list stays filled from its first slot, as block 13's count assumes
/// (docs/retail/formats/script-dat.md#the-13-block-structure): an add fills the slot after the last filled one, and a
/// remove moves every later line up a slot.</item>
/// </list>
/// </summary>
public static class MissionTextEditing {
	/// <summary>The <c>mission.str</c> lines DBSIM reads: <c>StringTable_LoadGroup</c>'s count of <c>0x32</c>.</summary>
	public const int LineCap = 50;

	/// <summary>Lines an objective's failure text spans.</summary>
	public const int FailureLines = 3;

	/// <summary>How many slots each row-4 list has.</summary>
	public static int Capacity(MissionTextList list) => list == MissionTextList.Objectives ? 10 : 30;

	/// <summary>
	/// How many lines the mission's text takes in every version of the mission at once: one per <c>.ENG</c> id
	/// (<see cref="MissionLayout.Lines"/>), each of which places a <c>mission.str</c> line in a version whose records of it survive.
	/// </summary>
	public static int Lines(MissionDocument document) => document.Layout.Lines.Count;

	/// <summary>Why <paramref name="adding"/> more lines cannot be added to the mission's text, or null when they can.</summary>
	public static string? WhyNoLines(MissionDocument document, int adding) {
		int lines = Lines(document) + adding;
		return lines > LineCap ? $"The mission's text would run to {lines} lines; the simulator reads only the first {LineCap}." : null;
	}

	/// <summary>
	/// The ids an objective's failure text referencing <paramref name="first"/> spans: it and the next
	/// <see cref="FailureLines"/> - 1 distinct ids in the <c>.ENG</c>'s order, as far as the file has them. Empty for no text.
	/// </summary>
	public static IReadOnlyList<short> FailureIds(MissionStringFile? text, short first) {
		if (first == -1) {
			return [];
		}

		var order = DistinctIds(text);
		int at = order.IndexOf(first);
		return at < 0 ? [first] : order.Skip(at).Take(FailureLines).ToList();
	}

	/// <summary>The ids <paramref name="owner"/> references now: a failure text's (<see cref="FailureIds"/>), or the one id in the field.</summary>
	public static IReadOnlyList<short> IdsOf(MissionDocument document, TextOwner owner) =>
		References(document).Where(reference => reference.Owner == owner).Select(reference => reference.Id).ToList();

	/// <summary>
	/// Every field of every record of rows 4, 5, 10 and 17 that references text, whatever the record's condition, with each id
	/// it references: one per field, and a failure text's three.
	/// </summary>
	public static IEnumerable<(TextOwner Owner, short Id)> References(MissionDocument document) {
		var mission = document.Mission;
		var texts = mission.Texts ?? [];
		for (int record = 0; record < texts.Length; record++) {
			foreach (var list in Enum.GetValues<MissionTextList>()) {
				var lines = LinesOf(texts[record], list);
				for (int slot = 0; slot < lines.Length; slot++) {
					if (lines[slot] != -1) {
						yield return (TextOwner.Package(record, list, slot), lines[slot]);
					}
				}
			}
		}

		var debrief = mission.DebriefBytes ?? [];
		for (int record = 0; record < debrief.Length / DebriefText.RecordLength; record++) {
			for (int slot = 0; slot < DebriefText.Slots; slot++) {
				short id = BitConverter.ToInt16(debrief, DebriefText.SlotOffset(record, slot));
				if (id != -1) {
					yield return (new TextOwner(5, record, null, slot), id);
				}
			}
		}

		var actions = mission.Actions ?? [];
		for (int record = 0; record < actions.Length; record++) {
			for (int slot = 0; slot < actions[record].TextRefs.Length; slot++) {
				if (actions[record].TextRefs[slot] != -1) {
					yield return (new TextOwner(10, record, null, slot), actions[record].TextRefs[slot]);
				}
			}
		}

		var objectives = mission.Objectives ?? [];
		for (int record = 0; record < objectives.Length; record++) {
			if (objectives[record] is { } objective) {
				foreach (short id in FailureIds(document.Text, objective.TextRef)) {
					yield return (TextOwner.Objective(record), id);
				}
			}
		}
	}

	/// <summary>Whether a field other than <paramref name="owner"/> references one of the ids <paramref name="owner"/> does.</summary>
	public static bool Shared(MissionDocument document, TextOwner owner) {
		var ids = IdsOf(document, owner);
		return References(document).Any(reference => reference.Owner != owner && ids.Contains(reference.Id));
	}

	/// <summary>
	/// The first of <paramref name="count"/> consecutive new ids: one past the largest id any <c>.ENG</c> record has or any
	/// field references, a failure text's last line counted as its id plus two, so a new id makes no field that referenced
	/// nothing reference it.
	/// </summary>
	public static short NewIds(MissionDocument document, int count) {
		int largest = -1;
		foreach (var entry in document.Text?.Strings ?? []) {
			largest = Math.Max(largest, entry.Guid);
		}

		foreach (var (owner, id) in References(document)) {
			largest = Math.Max(largest, id);
		}

		foreach (var objective in document.Mission.Objectives ?? []) {
			if (objective is { TextRef: not -1 }) {
				largest = Math.Max(largest, objective.TextRef + FailureLines - 1);
			}
		}

		if (largest + count > short.MaxValue) {
			throw new InvalidOperationException($"The mission's text ids already reach {largest}.");
		}

		return (short)(largest + 1);
	}

	/// <summary>An unconditioned <c>.ENG</c> record of <paramref name="id"/> holding <paramref name="text"/>, NUL-terminated as the files store it.</summary>
	public static MissionStringFile.StringEntry Entry(short id, string text) => new() {
		Guid = id, ConditionRef = -1, ParentRef = -1, Len = (short)(text.Length + 1), Val = text + "\0",
	};

	/// <summary>
	/// The index of the <c>.ENG</c> record whose text the line of <paramref name="id"/> shows (<see cref="LayoutLine.Supplier"/>),
	/// or null when no record carries the id.
	/// </summary>
	public static int? SupplierOf(MissionDocument document, short id) => document.Layout.Line(id)?.Supplier;

	/// <summary>
	/// The text <paramref name="id"/>'s line shows, without its NUL and the one trailing newline the simulator's load strips
	/// (docs/retail/formats/msn-mission-file.md#the-eng-string-table); null when no record carries the id.
	/// </summary>
	public static string? LineText(MissionDocument document, short id) =>
		SupplierOf(document, id) is { } record ? Shown(document.Text!.Strings![record].Val) : null;

	/// <summary>
	/// Sets the text of part <paramref name="part"/> of what <paramref name="owner"/> references — a failure text's line 0 to
	/// 2, or a row-4 or row-5 slot's one line, part 0 — to <paramref name="text"/>, in the record its line holds, keeping that record's
	/// trailing newline. A field shared with another (<see cref="Shared"/>) is first given its own copy. Throws
	/// <see cref="InvalidOperationException"/> for a part with no line, and for a copy that would take the text past
	/// <see cref="LineCap"/> lines.
	/// </summary>
	public static MsnEdit SetLine(MissionDocument document, TextOwner owner, int part, string text) {
		var ids = IdsOf(document, owner);
		if (part < 0 || part >= ids.Count) {
			throw new InvalidOperationException("There is no such line to edit.");
		}

		if (SupplierOf(document, ids[part]) is not { } supplier) {
			throw new InvalidOperationException("That line is not in the mission's text.");
		}

		return SetRecord(document, owner, ids, part, supplier, text);
	}

	/// <summary>
	/// The index of the <c>.ENG</c> record of <paramref name="id"/> carrying <paramref name="condition"/>, <c>-1</c> for none:
	/// the last such, as a later record of the id replaces an earlier one's text
	/// (docs/retail/formats/msn-mission-file.md#the-eng-string-table); null when no record of the id carries it.
	/// </summary>
	public static int? RecordUnder(MissionDocument document, short id, short condition) {
		var strings = document.Text?.Strings ?? [];
		for (int i = strings.Length - 1; i >= 0; i--) {
			if (strings[i].Guid == id && strings[i].ConditionRef == condition) {
				return i;
			}
		}

		return null;
	}

	/// <summary>The text <paramref name="id"/>'s record under <paramref name="condition"/> holds (<see cref="RecordUnder"/>), as <see cref="LineText"/> shows it; null for none.</summary>
	public static string? TextUnder(MissionDocument document, short id, short condition) =>
		RecordUnder(document, id, condition) is { } record ? Shown(document.Text!.Strings![record].Val) : null;

	/// <summary>
	/// <see cref="SetLine"/> for the record of the line carrying <paramref name="condition"/> (<see cref="RecordUnder"/>) in
	/// place of its base, so a replacement under one condition is edited and the line's other records keep their text. Throws
	/// <see cref="InvalidOperationException"/> when no record of the line carries the condition.
	/// </summary>
	public static MsnEdit SetLineUnder(MissionDocument document, TextOwner owner, int part, short condition, string text) {
		var ids = IdsOf(document, owner);
		if (part < 0 || part >= ids.Count) {
			throw new InvalidOperationException("There is no such line to edit.");
		}

		if (RecordUnder(document, ids[part], condition) is not { } record) {
			throw new InvalidOperationException("That line has no text under that condition.");
		}

		return SetRecord(document, owner, ids, part, record, text);
	}

	// Sets .ENG record <supplier>, of line <part> of <ids>, which <owner> references, to <text>, giving <owner> its own copy
	// first when it shares the ids.
	private static MsnEdit SetRecord(MissionDocument document, TextOwner owner, IReadOnlyList<short> ids, int part, int supplier, string text) {
		int lines = Lines(document);
		return document.Change(() => {
			int record = supplier;
			if (Shared(document, owner)) {
				record = OwnCopy(document, owner, ids, ids[part], supplier);
			}

			string old = document.Text!.Strings![record].Val ?? string.Empty;
			document.SetText(record, Shown(old) == old.TrimEnd('\0') ? text : text + "\n");
			CheckLines(document, lines);
		});
	}

	// ---- Row 4's line lists -----------------------------------------------------------------------

	/// <summary>
	/// The mission's text package, the row-4 record its line edits write, by its index in the row: the first, which block 13
	/// is written from whenever its condition holds (every retail one is unconditioned); null for a mission with none, as
	/// <c>TRAIN1</c> and <c>TRAIN3</c> have none.
	/// </summary>
	public static int? PackageRecord(MissionDocument document) => document.Mission.Texts is { Length: > 0 } ? 0 : null;

	/// <summary><paramref name="list"/>'s slots in <paramref name="package"/>, its own array.</summary>
	public static short[] LinesOf(MissionText144 package, MissionTextList list) => list switch {
		MissionTextList.Objectives => package.ObjectiveLines,
		MissionTextList.Briefing => package.BriefingLines,
		_ => package.IntelligenceLines,
	};

	/// <summary>The ids in <paramref name="list"/>'s filled slots of the mission's text package, in slot order, with each slot.</summary>
	public static IReadOnlyList<(int Slot, short Id)> Filled(MissionDocument document, MissionTextList list) {
		if (PackageRecord(document) is not { } record || document.Mission.Texts?[record] is not { } package) {
			return [];
		}

		return LinesOf(package, list).Select((id, slot) => (slot, id)).Where(entry => entry.id != -1).ToList();
	}

	/// <summary>Why no line can be added to <paramref name="list"/>, or null when one can.</summary>
	public static string? WhyNoAdd(MissionDocument document, MissionTextList list) {
		var filled = Filled(document, list);
		int next = filled.Count == 0 ? 0 : filled[^1].Slot + 1;
		return next >= Capacity(list) ? $"All {Capacity(list)} slots are filled." : WhyNoLines(document, 1);
	}

	/// <summary>
	/// Adds an empty line to <paramref name="list"/> after its last filled slot, as a new unconditioned <c>.ENG</c> record with
	/// a new id, appending the mission's text package first when it has none: unconditioned, every slot empty and no briefing
	/// movie, the shape and values of <c>TRAIN5.MSN</c>'s, as File &gt; New mission writes one (<see cref="NewMission"/>). Its
	/// condition must be <c>-1</c>, as all 60 retail records' is (<c>HercWorks.Query values --row 4 --field ConditionRef</c>):
	/// a record whose condition fails still writes block 13 when it is the first read, its ids not renumbered
	/// (<c>MsnGen_ParseMsnFile</c> (<c>00417b67</c>)). A movie ref of <c>-1</c> is left <c>-1</c> by the load, as 8 of the 60 carry it.
	/// Throws <see cref="InvalidOperationException"/> when <see cref="WhyNoAdd"/> refuses it.
	/// </summary>
	public static MsnEdit AddLine(MissionDocument document, MissionTextList list, out short id) {
		if (WhyNoAdd(document, list) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		var filled = Filled(document, list);
		int slot = filled.Count == 0 ? 0 : filled[^1].Slot + 1;
		short added = NewIds(document, 1);
		int lines = Lines(document);
		var change = document.Change(() => {
			if (PackageRecord(document) is not { } record) {
				document.AppendUnkeyed(4, new MissionText144 {
					ConditionRef = -1, ObjectiveLines = Unset(10), BriefingLines = Unset(30), IntelligenceLines = Unset(30), MovieRef = -1,
				});
				record = PackageRecord(document)!.Value;
			}

			document.AppendText([Entry(added, string.Empty)]);
			document.Rewrite<MissionText144>(4, record, package => LinesOf(package, list)[slot] = added);
			CheckLines(document, lines);
		});
		id = added;
		return change;
	}

	/// <summary>
	/// Removes the line in <paramref name="list"/>'s slot <paramref name="slot"/>, moving every later slot up one, and the
	/// <c>.ENG</c> records of its id when no field references the id any more.
	/// </summary>
	public static MsnEdit RemoveLine(MissionDocument document, MissionTextList list, int slot) {
		int record = PackageRecord(document) ?? throw new InvalidOperationException("The mission has no text package.");
		var lines = LinesOf(document.Mission.Texts![record], list);
		if (slot < 0 || slot >= lines.Length || lines[slot] == -1) {
			throw new InvalidOperationException($"There is no line in slot {slot}.");
		}

		short id = lines[slot];
		return document.Change(() => {
			document.Rewrite<MissionText144>(4, record, package => {
				var array = LinesOf(package, list);
				Array.Copy(array, slot + 1, array, slot, array.Length - slot - 1);
				array[^1] = -1;
			});
			RemoveUnreferenced(document, [id]);
		});
	}

	/// <summary>Swaps the lines in <paramref name="list"/>'s slots <paramref name="slot"/> and <paramref name="to"/>, both filled.</summary>
	public static MsnEdit MoveLine(MissionDocument document, MissionTextList list, int slot, int to) {
		int record = PackageRecord(document) ?? throw new InvalidOperationException("The mission has no text package.");
		var lines = LinesOf(document.Mission.Texts![record], list);
		if (slot < 0 || to < 0 || slot >= lines.Length || to >= lines.Length || lines[slot] == -1 || lines[to] == -1) {
			throw new InvalidOperationException("There is no line there to trade places with.");
		}

		return document.Rewrite<MissionText144>(4, record, package => {
			var array = LinesOf(package, list);
			(array[slot], array[to]) = (array[to], array[slot]);
		});
	}

	// ---- Shared ------------------------------------------------------------------------------------

	/// <summary>Removes every <c>.ENG</c> record of each of <paramref name="ids"/> that no field references any more.</summary>
	internal static void RemoveUnreferenced(MissionDocument document, IReadOnlyCollection<short> ids) {
		var referenced = References(document).Select(reference => reference.Id).ToHashSet();
		var gone = ids.Where(id => !referenced.Contains(id)).ToList();
		if (gone.Count > 0 && (document.Text?.Strings ?? []).Any(entry => gone.Contains(entry.Guid))) {
			document.RemoveText(gone);
		}
	}

	/// <summary>Throws when the text has grown past <see cref="LineCap"/> lines (<see cref="Lines"/>) from <paramref name="before"/>.</summary>
	internal static void CheckLines(MissionDocument document, int before) {
		int lines = Lines(document);
		if (lines > before && lines > LineCap) {
			throw new InvalidOperationException($"The mission's text would run to {lines} lines; the simulator reads only the first {LineCap}.");
		}
	}

	internal static short[] Unset(int count) => Enumerable.Repeat((short)-1, count).ToArray();

	// The text a record's line shows: without its NUL and one trailing newline.
	internal static string Shown(string? value) {
		string text = (value ?? string.Empty).TrimEnd('\0');
		return text.EndsWith('\n') ? text[..^1] : text;
	}

	// The .ENG's ids in the order of their first records, -1 left out.
	private static List<short> DistinctIds(MissionStringFile? text) {
		var order = new List<short>();
		foreach (var entry in text?.Strings ?? []) {
			if (entry.Guid != -1 && !order.Contains(entry.Guid)) {
				order.Add(entry.Guid);
			}
		}

		return order;
	}

	// Gives <owner> its own copy of <ids>, every record of each copied under consecutive new ids in order, and points the
	// field at the copy; answers the copy of record <supplier>, of id <id>. Made inside the caller's change.
	private static int OwnCopy(MissionDocument document, TextOwner owner, IReadOnlyList<short> ids, short id, int supplier) {
		var strings = document.Text!.Strings!;
		int ordinal = strings.Take(supplier).Count(entry => entry.Guid == id);
		short first = NewIds(document, ids.Count);
		var copies = new List<MissionStringFile.StringEntry>();
		for (int i = 0; i < ids.Count; i++) {
			foreach (var entry in strings.Where(entry => entry.Guid == ids[i])) {
				copies.Add(new MissionStringFile.StringEntry {
					Guid = (short)(first + i), ConditionRef = entry.ConditionRef, ParentRef = entry.ParentRef, Len = entry.Len, Val = entry.Val,
				});
			}
		}

		int start = strings.Length;
		document.AppendText(copies);
		if (owner.Row == 17) {
			document.Rewrite<MissionObjective58>(17, owner.Record, objective => objective.TextRef = first);
		} else if (owner is { Row: 4, List: { } list }) {
			document.Rewrite<MissionText144>(4, owner.Record, package => LinesOf(package, list)[owner.Slot] = first);
		} else if (owner.Row == 5) {
			document.SetDebriefLine(owner.Record, owner.Slot, first);
		} else {
			throw new InvalidOperationException("Only an objective's failure text, the text package's lines and the debrief's are edited.");
		}

		short copied = (short)(first + ids.ToList().IndexOf(id));
		var after = document.Text.Strings!;
		int seen = 0;
		for (int i = start; i < after.Length; i++) {
			if (after[i].Guid == copied && seen++ == ordinal) {
				return i;
			}
		}

		throw new InvalidOperationException("The copied line was not found.");
	}
}
