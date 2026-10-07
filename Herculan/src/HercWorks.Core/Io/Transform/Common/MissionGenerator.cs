using System.Text;
using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// VSHELL's mission load (<c>msn_gen.cpp</c>): a <c>.MSN</c> and its <c>.ENG</c> text in, the
/// <c>data\script.dat</c> and <c>data\mission.str</c> the simulator reads out. <see cref="Load"/> is
/// <c>MsnGen_ParseMsnFile</c> (<c>00417b67</c>) with the <c>.ENG</c> load (<c>Msn_LoadEngText</c> (<c>0041768c</c>)) it makes
/// between rows 2 and 3, <see cref="WriteScriptDat"/> is <c>WriteScriptDatFile</c> (<c>0041ac54</c>) and
/// <see cref="WriteMissionText"/> is <c>MissionStr_Write</c> (<c>004179f0</c>). What the rows mean, and the condition,
/// variant and merge rules, are docs/retail/formats/msn-mission-file.md's.
///
/// <para>Loads all 62 retail missions. At the generator state docs/retail/shell/campaign-loop.md, "The
/// shell's generator", describes, it reproduces two retail training handoffs of TRAIN5 — each a
/// <c>script11.dat</c> and <c>missn11.str</c> that retail wrote to save slot 11 — byte for byte
/// through their content.</para>
///
/// <para><b>This is not <see cref="MissionFileTransformer"/>'s job.</b> That transformer round-trips
/// a file for editing; this is the game's own load, which filters, merges and renumbers as it reads —
/// a nested waypoint list is resolved against the points loaded so far before its own record's
/// condition is even tested — so it walks the bytes itself, as the original does. Records are kept as
/// the original keeps them, as arrays of <c>int16</c> words, and every offset below is the original's
/// byte offset halved. A field a row's model class names is read through the <c>…Word</c> constant
/// beside that property; the offsets no model property covers — row 2's, row 5's and row 8's count —
/// are literals.</para>
///
/// <para>Where the original would read outside an array — a variant with no record to copy, a ref to
/// a row-3 record through an index taken from another space — this leaves the field as it was, which
/// is this port's choice rather than anything the original does on purpose.</para>
/// </summary>
public sealed class MissionGenerator {
	/// <summary><c>MsnGen_ParseMsnFile</c>'s revision assert.</summary>
	public const short Revision = 5;

	/// <summary>How many flag indices a row-2 record can name for clearing — the 30 shorts at <c>DAT_0048545a</c>.</summary>
	public const int ClearListLength = 30;

	/// <summary><c>maybe_CampaignFlagArray</c>'s length in <c>int16</c>s.</summary>
	public const int CampaignFlagCount = 1000;

	/// <summary>The row-1 comparison opcodes, <c>0x119</c>-<c>0x11e</c>.</summary>
	private const short OpEqual = 0x119;
	private const short OpNotEqual = 0x11a;
	private const short OpLess = 0x11b;
	private const short OpLessOrEqual = 0x11c;
	private const short OpGreater = 0x11d;
	private const short OpGreaterOrEqual = 0x11e;

	/// <summary>What <c>Msn_ConditionParentValue</c> (<c>004159d0</c>) answers for a type-3 parent: a range test that always passes.</summary>
	private const short AnyVariant = -99;

	private readonly List<short[]> _conditions = new();
	private readonly List<short[]> _variants = new();
	private readonly List<short[]> _texts = new();
	private readonly List<short[]> _points = new();
	private readonly List<short[]> _headings = new();
	private readonly List<WaypointGroup> _waypointGroups = new();
	private readonly List<short[]> _areas = new();
	private readonly List<short[]> _actions = new();
	private readonly List<short[]> _timers = new();
	private readonly List<short[]> _hercs = new();
	private readonly List<short[]> _flyers = new();
	private readonly List<short[]> _bases = new();
	private readonly List<short[]> _orders = new();
	private readonly List<short[]> _groups = new();
	private readonly List<short[]> _objectives = new();
	private readonly List<(short Id, byte[] Text)> _strings = new();

	/// <summary>
	/// Row 4's slot 0 as the writer finds it: the first record that survived, or when none did the last
	/// one read into the slot — the original writes block 13 from the array's first slot whenever the
	/// file had any row-4 record at all.
	/// </summary>
	private short[]? _textSlot0;

	private readonly short[] _flags;
	private readonly Func<short, int> _roll;

	private MissionGenerator(short[] flags, Func<short, int> roll) {
		_flags = flags;
		_roll = roll;
	}

	/// <summary>
	/// The ten header words, <c>DAT_00485446</c> upward: zeroed with the first set to 1 once row 1 is
	/// read, then patched by row 2. <c>MsnGen_LoadMission</c> (<c>0041c73d</c>) overwrites some of them
	/// before the write; <see cref="WriteScriptDat"/> puts a literal 1 in place of the third.
	/// </summary>
	public short[] Header { get; } = new short[10];

	/// <summary>
	/// Loads a mission. <paramref name="flags"/> is the campaign flag array the row-1 comparisons read,
	/// and row 2 zeroes the entries it names in it. <paramref name="clearList"/> is <c>DAT_0048545a</c>:
	/// row 2 writes into it and the entries it holds are then cleared from the flags. The original never
	/// resets it, so what one load leaves there the next load clears again — the caller keeps it.
	/// <paramref name="roll"/> is VSHELL's <c>ShellRandom_Below</c> (<c>004659ec</c>), a draw in <c>[0, n)</c>.
	/// <paramref name="text"/> is the <c>.ENG</c> beside the mission, or null for none.
	/// </summary>
	public static MissionGenerator Load(byte[] msn, byte[]? text, short[] flags, short[] clearList, Func<short, int> roll) {
		if (flags.Length < CampaignFlagCount) {
			throw new ArgumentException($"The campaign flag array holds {CampaignFlagCount} entries.", nameof(flags));
		}

		if (clearList.Length < ClearListLength) {
			throw new ArgumentException($"The clear list holds {ClearListLength} entries.", nameof(clearList));
		}

		var generator = new MissionGenerator(flags, roll);
		var reader = new Cursor(msn);
		if (reader.Short() != Revision) {
			throw new InvalidDataException("Not a revision 5 mission file.");
		}

		generator.ReadConditions(reader);
		generator.ReadHeaderPatches(reader, clearList);
		generator.ReadText(text);
		generator.ReadRows(reader);
		return generator;
	}

	/// <summary>
	/// <c>Msn_LoadDebriefRows</c> (<c>0041ca4e</c>) and <c>Msn_LoadDebrief</c> (<c>0041d2c3</c>), the debrief's reload of the mission
	/// just flown (docs/retail/formats/msn-mission-file.md#row-5--the-debrief): row 1 against <paramref name="flags"/>,
	/// drawing from <paramref name="roll"/> as <see cref="Load"/> does; row 2 skipped, so no flag is cleared;
	/// the <c>.ENG</c> text; row 3; row 4 skipped; and row 5. Returns row 5's first slot — its thirty
	/// <c>mission.str</c> lines and its row-3 value, the debrief movie — with the <c>mission.str</c> it writes,
	/// or null for a mission with no row-5 record, where the original asserts.
	/// </summary>
	public static MissionDebriefText? LoadDebriefText(byte[] msn, byte[]? text, short[] flags, Func<short, int> roll) {
		if (flags.Length < CampaignFlagCount) {
			throw new ArgumentException($"The campaign flag array holds {CampaignFlagCount} entries.", nameof(flags));
		}

		var generator = new MissionGenerator(flags, roll);
		var reader = new Cursor(msn);
		if (reader.Short() != Revision) {
			throw new InvalidDataException("Not a revision 5 mission file.");
		}

		generator.ReadConditions(reader);
		reader.Skip(reader.Short() * 0x52);
		generator.ReadText(text);
		generator.ReadVariants(reader);
		reader.Skip(reader.Short() * 0x90);

		// Row 5, 64 bytes: a condition, thirty text refs and a row-3 ref, read into the slot the survivor
		// count names, so while nothing has survived each record lands in slot 0.
		short[]? slot0 = null;
		bool survived = false;
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(32);
			if (!survived) {
				slot0 = record;
			}

			if (!generator.Gate(record[0], generator._conditions.Count)) {
				continue;
			}

			for (int word = 1; word < 31; word++) {
				if (record[word] != -1) {
					record[word] = generator.TextLine(record[word]);
				}
			}

			if (record[31] != -1) {
				short variant = Find(generator._variants, record[31], generator._variants.Count, exported: true);
				if (variant >= 0 && variant < generator._variants.Count) {
					record[31] = generator._variants[variant][VariantValue8.ValueWord];
				}
			}

			survived = true;
		}

		return slot0 == null ? null : new MissionDebriefText(slot0[1..31], slot0[31], generator.WriteMissionText());
	}

	// ---- Row 1: the conditions ------------------------------------------------------------------

	/// <summary>
	/// Row 1, 14 bytes. A record survives by type: 0 compares a flag and survives when the comparison
	/// holds, 1 draws a number and survives, 2 survives when its parent's number falls in its range, 3
	/// survives. Types 0, 1 and 3 must first pass their own condition. Survivors are compacted, and are
	/// what every later condition ref tests against.
	/// </summary>
	private void ReadConditions(Cursor reader) {
		const int condition = MissionCondition14.ConditionRefWord;
		const int lower = MissionCondition14.FlagIndexOrRangeLowerWord;
		const int upper = MissionCondition14.OperatorOrRangeUpperOrResultWord;
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(7);
			switch (record[MissionCondition14.TypeWord]) {
				case 0:
					if (Gate(record[condition], _conditions.Count)) {
						record[upper] = Compare(record[upper], Flag(record[lower]), record[MissionCondition14.ComparisonOperandWord])
							? (short)1 : (short)0;
						if (record[upper] != 0) {
							_conditions.Add(record);
						}
					}

					break;
				case 1:
					if (Gate(record[condition], _conditions.Count)) {
						record[upper] = (short)_roll(record[lower]);
						_conditions.Add(record);
					}

					break;
				case 2:
					int value = ParentValue(record[condition], _conditions.Count);
					if (value == AnyVariant || ((ushort)record[lower] <= value && value <= (ushort)record[upper])) {
						_conditions.Add(record);
					}

					break;
				case 3:
					if (Gate(record[condition], _conditions.Count)) {
						_conditions.Add(record);
					}

					break;
			}
		}
	}

	private short Flag(short index) => index >= 0 && index < CampaignFlagCount ? _flags[index] : (short)0;

	/// <summary>The switch on <c>0x119</c>-<c>0x11e</c>; any other code fails.</summary>
	private static bool Compare(short op, short flag, short operand) => op switch {
		OpEqual => flag == operand,
		OpNotEqual => flag != operand,
		OpLess => flag < operand,
		OpLessOrEqual => flag <= operand,
		OpGreater => operand < flag,
		OpGreaterOrEqual => operand <= flag,
		_ => false,
	};

	/// <summary>
	/// <c>Msn_ConditionFilterGate</c> (<c>00417610</c>): no condition passes, and a condition passes when
	/// it names one of the first <paramref name="count"/> surviving row-1 records.
	/// </summary>
	private bool Gate(short condition, int count) {
		if (condition == -1) {
			return true;
		}

		for (int i = 0; i < count && i < _conditions.Count; i++) {
			if (_conditions[i][MapObject.GUIDWord] == condition) {
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>Msn_ConditionParentValue</c> (<c>004159d0</c>): the first earlier survivor with the GUID — a type-1 record's drawn number, or
	/// <see cref="AnyVariant"/> for a type-3 record — and <c>-1</c> when there is none.
	/// </summary>
	private int ParentValue(short condition, int count) {
		for (int i = 0; i < count; i++) {
			var record = _conditions[i];
			if (record[MapObject.GUIDWord] == condition && record[MissionCondition14.TypeWord] == 1) {
				return record[MissionCondition14.OperatorOrRangeUpperOrResultWord];
			}

			if (record[MapObject.GUIDWord] == condition && record[MissionCondition14.TypeWord] == 3) {
				return AnyVariant;
			}
		}

		return -1;
	}

	/// <summary>
	/// <c>Msn_PickVariant</c> (<c>00415fc3</c>), which a record's variant field (<c>0x04</c>) goes through: the type-3 record
	/// whose <c>0x06</c> matches is rolled against its <c>0x0a</c>, the roll stored at its <c>0x0c</c>, and
	/// the type-2 child whose range holds the roll is the answer — its row-1 index. It rolls again on
	/// every call, and returns <c>-1</c> when no type-3 record's child claims the roll.
	/// </summary>
	private int PickVariant(short variant) {
		const int type = MissionCondition14.TypeWord;
		const int lower = MissionCondition14.FlagIndexOrRangeLowerWord;
		const int upper = MissionCondition14.OperatorOrRangeUpperOrResultWord;
		const int draw = MissionCondition14.LatestDrawWord;
		for (int parent = 0; parent < _conditions.Count; parent++) {
			var record = _conditions[parent];
			if (record[type] != 3 || record[lower] != variant) {
				continue;
			}

			record[draw] = (short)_roll(record[MissionCondition14.ComparisonOperandWord]);
			for (int child = 0; child < _conditions.Count; child++) {
				var candidate = _conditions[child];
				if (candidate[type] == 2 && candidate[MissionCondition14.ConditionRefWord] == record[MapObject.GUIDWord]
					&& (ushort)candidate[lower] <= record[draw] && record[draw] <= (ushort)candidate[upper]) {
					return child;
				}
			}
		}

		return -1;
	}

	/// <summary>
	/// <c>Msn_VariantSourceRow6</c> (<c>00416120</c>) and its six siblings: the variant picked, then the first of the row's first
	/// <paramref name="count"/> records whose condition is that variant's GUID. That record is what a
	/// record with a variant field copies its fields from.
	/// </summary>
	private int VariantSource(IReadOnlyList<short[]> row, short variant, int count) {
		int picked = PickVariant(variant);
		if (picked < 0) {
			return -1;
		}

		short guid = _conditions[picked][MapObject.GUIDWord];
		for (int i = 0; i < count; i++) {
			// Word 1 is the condition ref of every row with a variant key.
			if (row[i][1] == guid) {
				return i;
			}
		}

		return -1;
	}

	// ---- Row 2: the header patches --------------------------------------------------------------

	/// <summary>
	/// Row 2, 82 bytes read into one reused buffer and never kept: a condition, ten header words and
	/// thirty flag indices. A record whose condition passes writes each header word that is not its
	/// sentinel — 1 for the first, 0 for the rest — and each flag index that is not <c>-1</c> into the
	/// clear list. The header is reset first, and the clear list is not. Then every flag the clear list
	/// names is zeroed (<c>Msn_ClearPatchedFlags</c> (<c>00417659</c>)).
	/// </summary>
	private void ReadHeaderPatches(Cursor reader, short[] clearList) {
		Array.Clear(Header);
		Header[0] = 1;

		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] patch = reader.Words(41);
			if (!Gate(patch[0], _conditions.Count)) {
				continue;
			}

			if (patch[1] != 1) {
				Header[0] = patch[1];
			}

			for (int word = 1; word < Header.Length; word++) {
				if (patch[1 + word] != 0) {
					Header[word] = patch[1 + word];
				}
			}

			for (int slot = 0; slot < ClearListLength; slot++) {
				if (patch[11 + slot] != -1) {
					clearList[slot] = patch[11 + slot];
				}
			}
		}

		for (int slot = 0; slot < ClearListLength; slot++) {
			if (clearList[slot] != -1 && clearList[slot] >= 0 && clearList[slot] < CampaignFlagCount) {
				_flags[clearList[slot]] = 0;
			}
		}
	}

	// ---- The .ENG text ----------------------------------------------------------------------------

	/// <summary>
	/// <c>Msn_LoadEngText</c> (<c>0041768c</c>): the <c>.ENG</c> beside the mission, records of id, condition, a word nothing
	/// reads, a length and the text. A record whose condition fails is skipped; one whose id is already
	/// loaded replaces that entry's text. The survivors, in order, are <c>data\mission.str</c>, and every
	/// text ref in the mission is renumbered into them.
	/// </summary>
	private void ReadText(byte[]? text) {
		if (text == null) {
			return;
		}

		var reader = new Cursor(text);
		int count = reader.Short();
		for (int i = 0; i < count && !reader.AtEnd; i++) {
			short id = reader.Short();
			short condition = reader.Short();
			reader.Short();
			int length = reader.Short();
			if (!Gate(condition, _conditions.Count)) {
				reader.Skip(length);
				continue;
			}

			byte[] body = reader.Bytes(length);
			int existing = -1;
			for (int j = 0; j < _strings.Count; j++) {
				if (id != -1 && _strings[j].Id == id) {
					existing = j;
					break;
				}
			}

			if (existing == -1) {
				_strings.Add((id, body));
			} else {
				_strings[existing] = (_strings[existing].Id, body);
			}
		}
	}

	/// <summary><c>Msn_ResolveTextRef(id, count, 1)</c> (<c>00415a7c</c>): a text id to its <c>mission.str</c> line, or <c>-1</c>.</summary>
	private short TextLine(short id) {
		if (id == -1) {
			return -1;
		}

		short line = 0;
		foreach (var (stringId, _) in _strings) {
			if (stringId == -1) {
				continue;
			}

			if (stringId == id) {
				return line;
			}

			line++;
		}

		return -1;
	}

	// ---- Rows 3-17 --------------------------------------------------------------------------------

	private void ReadRows(Cursor reader) {
		ReadVariants(reader);
		ReadTextPackages(reader);
		reader.Skip(reader.Short() * 0x40);
		ReadPoints(reader);
		ReadHeadings(reader);
		ReadWaypointGroups(reader);
		ReadAreas(reader);
		ReadActions(reader);
		ReadTimers(reader);
		ReadHercs(reader);
		ReadFlyers(reader);
		ReadBases(reader);
		ReadOrders(reader);
		ReadGroups(reader);
		ResolveDeferredRefs();
		ReadObjectives(reader);
	}

	/// <summary>
	/// The family <c>Msn_ResolveRow6Ref</c> (<c>00415b44</c>) and its siblings share, one per row: a GUID's position among the row's first <paramref name="count"/> records. Exported
	/// (<c>1</c>) skips the records whose GUID is <c>-1</c>, which is the index <c>script.dat</c> gives the
	/// record; otherwise (<c>0</c>) they count, which is the record's place in the array. <c>-1</c> for a
	/// <c>-1</c> GUID or none found.
	/// </summary>
	private static short Find(IReadOnlyList<short[]> row, short guid, int count, bool exported) {
		if (guid == -1) {
			return -1;
		}

		short index = 0;
		for (int i = 0; i < count && i < row.Count; i++) {
			if (row[i][MapObject.GUIDWord] == -1) {
				if (!exported) {
					index++;
				}

				continue;
			}

			if (row[i][MapObject.GUIDWord] == guid) {
				return index;
			}

			index++;
		}

		return -1;
	}

	private static short FindGroup(IReadOnlyList<WaypointGroup> row, short guid, int count, bool exported) {
		if (guid == -1) {
			return -1;
		}

		short index = 0;
		for (int i = 0; i < count && i < row.Count; i++) {
			if (row[i].Words[MapObject.GUIDWord] == -1) {
				if (!exported) {
					index++;
				}

				continue;
			}

			if (row[i].Words[MapObject.GUIDWord] == guid) {
				return index;
			}

			index++;
		}

		return -1;
	}

	/// <summary>A ref into another row, resolved to its exported index — every cross-row ref's form.</summary>
	private static short Ref(IReadOnlyList<short[]> row, short guid) => Find(row, guid, row.Count, exported: true);

	/// <summary>
	/// Adds a record, or when an earlier record has its GUID hands both to <paramref name="merge"/> —
	/// the compaction every GUID-keyed row shares. The slot the original read into is reused.
	/// </summary>
	private static void AddOrMerge(List<short[]> row, short[] record, Action<short[], short[]> merge) {
		int existing = Find(row, record[MapObject.GUIDWord], row.Count, exported: false);
		if (existing == -1) {
			row.Add(record);
		} else {
			merge(record, row[existing]);
		}
	}

	/// <summary>The whole-record replace rows 3, 6, 7 and 9 use for a repeated GUID.</summary>
	private static void Replace(short[] source, short[] target) => Array.Copy(source, target, target.Length);

	/// <summary>Copies each word of <paramref name="source"/> in the range that is not <paramref name="unset"/>.</summary>
	private static void Overlay(short[] source, short[] target, int first, int count, short unset) {
		for (int i = first; i < first + count; i++) {
			if (source[i] != unset) {
				target[i] = source[i];
			}
		}
	}

	private static void Copy(short[] source, short[] target, int first, int count) =>
		Array.Copy(source, first, target, first, count);

	/// <summary>Row 3, 8 bytes: GUID, condition, a word, the value row 4 fetches.</summary>
	private void ReadVariants(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(4);
			if (Gate(record[VariantValue8.ConditionRefWord], _conditions.Count)) {
				AddOrMerge(_variants, record, Replace);
			}
		}
	}

	/// <summary>
	/// Row 4, 144 bytes, keyed by nothing: a condition, three text-ref arrays renumbered into
	/// <c>mission.str</c>, and a row-3 ref replaced by that record's value. The row-3 ref is resolved to
	/// an exported index and then read as an array index, as the original does.
	/// </summary>
	private void ReadTextPackages(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(72);
			if (_texts.Count == 0) {
				_textSlot0 = record;
			}

			if (!Gate(record[MissionText144.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			for (int word = MissionText144.ObjectiveLinesWord; word < MissionText144.MovieRefWord; word++) {
				if (record[word] != -1) {
					record[word] = TextLine(record[word]);
				}
			}

			const int movie = MissionText144.MovieRefWord;
			if (record[movie] != -1) {
				short variant = Find(_variants, record[movie], _variants.Count, exported: true);
				if (variant >= 0 && variant < _variants.Count) {
					record[movie] = _variants[variant][VariantValue8.ValueWord];
				}
			}

			_texts.Add(record);
		}
	}

	/// <summary>
	/// Row 6, 22 bytes: GUID, condition, variant, a word, the sum flag, and an <c>int32</c> X, Y and Z. A
	/// variant copies the three coordinates. The sum flag makes the point the sum of two earlier points,
	/// named by the low words of its own X and Y.
	/// </summary>
	private void ReadPoints(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(11);
			if (!Gate(record[MapPoint22.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			short variantKey = record[MapPoint22.VariantKeyWord];
			if (variantKey != -1 && VariantSource(_points, variantKey, _points.Count) is >= 0 and var source) {
				Copy(_points[source], record, MapPoint22.XWord, 6);
			}

			if (record[MapPoint22.SumFlagWord] != 0) {
				short a = Find(_points, record[MapPoint22.XWord], _points.Count, exported: false);
				short b = Find(_points, record[MapPoint22.YWord], _points.Count, exported: false);
				if (a >= 0 && b >= 0) {
					for (int axis = 0; axis < 3; axis++) {
						int at = MapPoint22.XWord + axis * 2;
						SetInt(record, at, GetInt(_points[a], at) + GetInt(_points[b], at));
					}
				}
			}

			AddOrMerge(_points, record, Replace);
		}
	}

	private static int GetInt(short[] words, int at) => (ushort)words[at] | (words[at + 1] << 16);

	private static void SetInt(short[] words, int at, int value) {
		words[at] = (short)value;
		words[at + 1] = (short)(value >> 16);
	}

	/// <summary>Row 7, 10 bytes: GUID, condition, variant, a word, the heading. A variant copies the heading.</summary>
	private void ReadHeadings(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(5);
			if (!Gate(record[Heading10.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			short variantKey = record[Heading10.VariantKeyWord];
			if (variantKey != -1 && VariantSource(_headings, variantKey, _headings.Count) is >= 0 and var source) {
				record[Heading10.DegreesWord] = _headings[source][Heading10.DegreesWord];
			}

			AddOrMerge(_headings, record, Replace);
		}
	}

	/// <summary>
	/// Row 8: GUID, condition, variant, a word, a count, then that many point refs, each resolved as it
	/// is read — before the record's own condition is tested. A variant copies the list; a repeated GUID
	/// replaces the earlier record whole.
	/// </summary>
	private void ReadWaypointGroups(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			var group = new WaypointGroup(reader.Words(5));
			for (int point = 0; point < group.Words[4]; point++) {
				group.Points.Add(Ref(_points, reader.Short()));
			}

			if (!Gate(group.Words[Data.File.Msn.WaypointGroup.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			if (group.Words[Data.File.Msn.WaypointGroup.VariantKeyWord] != -1) {
				int picked = PickVariant(group.Words[Data.File.Msn.WaypointGroup.VariantKeyWord]);
				int source = -1;
				for (int j = 0; picked >= 0 && j < _waypointGroups.Count; j++) {
					if (_waypointGroups[j].Words[Data.File.Msn.WaypointGroup.ConditionRefWord] == _conditions[picked][MapObject.GUIDWord]) {
						source = j;
						break;
					}
				}

				if (source >= 0) {
					group.Words[4] = _waypointGroups[source].Words[4];
					group.Points.Clear();
					group.Points.AddRange(_waypointGroups[source].Points);
				}
			}

			int existing = FindGroup(_waypointGroups, group.Words[MapObject.GUIDWord], _waypointGroups.Count, exported: false);
			if (existing == -1) {
				_waypointGroups.Add(group);
			} else {
				_waypointGroups[existing] = group;
			}
		}
	}

	/// <summary>Row 9, 12 bytes: GUID, condition, a word, the type, a point, and a second point when the type is 0.</summary>
	private void ReadAreas(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(6);
			if (!Gate(record[TriggerArea12.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			record[TriggerArea12.PointRefWord] = Ref(_points, record[TriggerArea12.PointRefWord]);
			if (record[TriggerArea12.ShapeWord] == 0) {
				record[TriggerArea12.SecondPointOrRadiusWord] = Ref(_points, record[TriggerArea12.SecondPointOrRadiusWord]);
			}

			AddOrMerge(_areas, record, Replace);
		}
	}

	/// <summary>
	/// Row 10, 82 bytes: the action. Its eight trigger areas are resolved, its five text refs (<c>0x44</c>)
	/// renumbered into <c>mission.str</c>; its target waits for <see cref="ResolveDeferredRefs"/>. A
	/// repeated GUID overlays the fields that are set onto the earlier record (<c>Msn_MergeRow10</c> (<c>00416521</c>)).
	/// </summary>
	private void ReadActions(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(41);
			if (!Gate(record[MissionAction82.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			for (int slot = MissionAction82.AreaRefsWord; slot < MissionAction82.AreaRefsWord + 8; slot++) {
				if (record[slot] != -1) {
					record[slot] = Ref(_areas, record[slot]);
				}
			}

			for (int slot = MissionAction82.TextRefsWord; slot < MissionAction82.TextRefsWord + 5; slot++) {
				if (record[slot] != -1) {
					record[slot] = TextLine(record[slot]);
				}
			}

			AddOrMerge(_actions, record, (source, target) => {
				Overlay(source, target, MissionAction82.TypeWord, 10, -1);
				Overlay(source, target, MissionAction82.CounterPairsWord, 20, -1);
				Overlay(source, target, MissionAction82.TextRefsWord, 5, -1);
				Overlay(source, target, MissionAction82.MessageIdWord, 1, 0);
				Overlay(source, target, MissionAction82.TargetRefWord, 1, -1);
			});
		}
	}

	/// <summary>Row 11, 30 bytes: the timer, its arming action and its ten actions resolved. Merged by <c>Msn_MergeRow11</c> (<c>00416768</c>).</summary>
	private void ReadTimers(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(15);
			if (!Gate(record[ActionTimer30.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			for (int slot = ActionTimer30.PrimaryActionRefWord; slot < ActionTimer30.SequenceRefsWord + 10; slot++) {
				if (slot != ActionTimer30.DelayWord && record[slot] != -1) {
					record[slot] = Ref(_actions, record[slot]);
				}
			}

			AddOrMerge(_timers, record, (source, target) => {
				Overlay(source, target, ActionTimer30.PrimaryActionRefWord, 1, -1);
				Overlay(source, target, ActionTimer30.DelayWord, 1, 0);
				Overlay(source, target, ActionTimer30.SequenceRefsWord, 10, -1);
			});
		}
	}

	/// <summary>
	/// Row 12, 144 bytes: the HERC roster. With no variant its point, heading and two actions are
	/// resolved; with one, every field past the header but <c>0x06</c> and <c>0x4a</c> is copied from the
	/// variant's record, refs already resolved. Merged by <c>Msn_MergeRow12</c> (<c>00416864</c>).
	/// </summary>
	private void ReadHercs(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(72);
			if (!Gate(record[MechRosterEntry144.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			if (record[MechRosterEntry144.VariantKeyWord] == -1) {
				record[MechRosterEntry144.PositionRefWord] = Ref(_points, record[MechRosterEntry144.PositionRefWord]);
				record[MechRosterEntry144.HeadingRefWord] = Ref(_headings, record[MechRosterEntry144.HeadingRefWord]);
				record[MechRosterEntry144.EngagementActionRefWord] = Ref(_actions, record[MechRosterEntry144.EngagementActionRefWord]);
				record[MechRosterEntry144.DefeatActionRefWord] = Ref(_actions, record[MechRosterEntry144.DefeatActionRefWord]);
			} else if (VariantSource(_hercs, record[MechRosterEntry144.VariantKeyWord], _hercs.Count) is >= 0 and var source) {
				var from = _hercs[source];
				Copy(from, record, MechRosterEntry144.AiRadarActiveWord, 20);
				Copy(from, record, MechRosterEntry144.WeaponRefsWord, 10);
				Copy(from, record, MechRosterEntry144.PositionRefWord, 2);
				record[MechRosterEntry144.TypeIndexWord] = from[MechRosterEntry144.TypeIndexWord];
				Copy(from, record, MechRosterEntry144.OutOfActionReportWord, 20);
				Copy(from, record, MechRosterEntry144.WeaponSecondaryWord, 10);
				Copy(from, record, MechRosterEntry144.Constant2Word, 4);
			}

			AddOrMerge(_hercs, record, (source, target) => {
				Overlay(source, target, MechRosterEntry144.AiRadarActiveWord, 20, 0);
				Overlay(source, target, MechRosterEntry144.WeaponRefsWord, 10, -1);
				Overlay(source, target, MechRosterEntry144.WeaponSecondaryWord, 10, HercNoGuidance);
				Overlay(source, target, MechRosterEntry144.TypeIndexWord, 1, -1);
				Overlay(source, target, MechRosterEntry144.PositionRefWord, 2, -1);
				Overlay(source, target, MechRosterEntry144.OutOfActionReportWord, 20, -1);
				Overlay(source, target, MechRosterEntry144.Constant2Word, 1, 2);
				Overlay(source, target, MechRosterEntry144.EngagementActionRefWord, 2, -1);
				Overlay(source, target, MechRosterEntry144.StartingConditionWord, 1, 100);
			});
		}
	}

	/// <summary>The ammunition word a HERC mount leaves at when it carries nothing guided.</summary>
	private const short HercNoGuidance = 5;

	/// <summary>Row 13, 102 bytes: the flyer roster, the same arrangement one class down. Merged by <c>Msn_MergeRow13</c> (<c>00416c17</c>).</summary>
	private void ReadFlyers(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(51);
			if (!Gate(record[FlyerRosterEntry102.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			if (record[FlyerRosterEntry102.VariantKeyWord] == -1) {
				record[FlyerRosterEntry102.PositionRefWord] = Ref(_points, record[FlyerRosterEntry102.PositionRefWord]);
				record[FlyerRosterEntry102.HeadingRefWord] = Ref(_headings, record[FlyerRosterEntry102.HeadingRefWord]);
				record[FlyerRosterEntry102.EngagementActionRefWord] = Ref(_actions, record[FlyerRosterEntry102.EngagementActionRefWord]);
				record[FlyerRosterEntry102.DefeatActionRefWord] = Ref(_actions, record[FlyerRosterEntry102.DefeatActionRefWord]);
			} else if (VariantSource(_flyers, record[FlyerRosterEntry102.VariantKeyWord], _flyers.Count) is >= 0 and var source) {
				var from = _flyers[source];
				Copy(from, record, FlyerRosterEntry102.FlagSpanWord, 23);
				Copy(from, record, FlyerRosterEntry102.OutOfActionReportWord, 23);
			}

			AddOrMerge(_flyers, record, (source, target) => {
				Overlay(source, target, FlyerRosterEntry102.FlagSpanWord, 20, 0);
				Overlay(source, target, FlyerRosterEntry102.PositionRefWord, 3, -1);
				Overlay(source, target, FlyerRosterEntry102.OutOfActionReportWord, 22, -1);
				Overlay(source, target, FlyerRosterEntry102.UnkVal_100Word, 1, 100);
			});
		}
	}

	/// <summary>
	/// Row 14, 62 bytes: the base roster. A variant copies everything but <c>0x0e</c>. Merged by
	/// <c>Msn_MergeRow14</c> (<c>00416e7a</c>).
	/// </summary>
	private void ReadBases(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(31);
			if (!Gate(record[BaseRosterEntry62.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			if (record[BaseRosterEntry62.VariantKeyWord] == -1) {
				record[BaseRosterEntry62.PositionRefWord] = Ref(_points, record[BaseRosterEntry62.PositionRefWord]);
				record[BaseRosterEntry62.HeadingRefWord] = Ref(_headings, record[BaseRosterEntry62.HeadingRefWord]);
				record[BaseRosterEntry62.EngagementActionRefWord] = Ref(_actions, record[BaseRosterEntry62.EngagementActionRefWord]);
				record[BaseRosterEntry62.DefeatActionRefWord] = Ref(_actions, record[BaseRosterEntry62.DefeatActionRefWord]);
			} else if (VariantSource(_bases, record[BaseRosterEntry62.VariantKeyWord], _bases.Count) is >= 0 and var source) {
				var from = _bases[source];
				Copy(from, record, BaseRosterEntry62.TypeIndexWord, 3);
				Copy(from, record, BaseRosterEntry62.OutOfActionReportWord, 23);
			}

			AddOrMerge(_bases, record, (source, target) => {
				Overlay(source, target, BaseRosterEntry62.TypeIndexWord, 3, -1);
				Overlay(source, target, BaseRosterEntry62.OutOfActionReportWord, 22, -1);
				Overlay(source, target, BaseRosterEntry62.StartingConditionWord, 1, 100);
			});
		}
	}

	/// <summary>
	/// Row 15, 22 bytes: the group order. Its subject is resolved by the kind at <c>0x10</c> — 1 a HERC,
	/// 2 a flyer, 3 a base — except kind 0, a group, whose row is not loaded yet and waits for
	/// <see cref="ResolveDeferredRefs"/>. A variant copies the seven payload words. Merged by
	/// <c>Msn_MergeRow15</c> (<c>004170e8</c>).
	/// </summary>
	private void ReadOrders(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(11);
			if (!Gate(record[MissionOrder22.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			if (record[MissionOrder22.VariantKeyWord] == -1) {
				const int subject = MissionOrder22.SubjectRefWord;
				record[MissionOrder22.PointRefWord] = Ref(_points, record[MissionOrder22.PointRefWord]);
				record[MissionOrder22.RouteRefWord] = FindGroup(_waypointGroups, record[MissionOrder22.RouteRefWord], _waypointGroups.Count, exported: true);
				record[MissionOrder22.ActionRefWord] = Ref(_actions, record[MissionOrder22.ActionRefWord]);
				record[subject] = record[MissionOrder22.SubjectKindWord] switch {
					1 => Ref(_hercs, record[subject]),
					2 => Ref(_flyers, record[subject]),
					3 => Ref(_bases, record[subject]),
					_ => record[subject],
				};
			} else if (VariantSource(_orders, record[MissionOrder22.VariantKeyWord], _orders.Count) is >= 0 and var source) {
				Copy(_orders[source], record, MissionOrder22.VerbWord, 7);
			}

			AddOrMerge(_orders, record, (source, target) => Overlay(source, target, MissionOrder22.VerbWord, 7, -1));
		}
	}

	/// <summary>
	/// Row 16, 164 bytes: the group. It has no variant field. Its twenty members are resolved into the
	/// roster its discriminator (<c>0x2e</c>) names — 0 HERCs, 1 flyers, 2 bases — and its ten orders into
	/// row 15. Merged by <c>Msn_MergeRow16</c> (<c>00417286</c>).
	/// </summary>
	private void ReadGroups(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			short[] record = reader.Words(82);
			if (!Gate(record[MissionGroup164.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			record[MissionGroup164.PositionRefWord] = Ref(_points, record[MissionGroup164.PositionRefWord]);
			record[MissionGroup164.HeadingRefWord] = Ref(_headings, record[MissionGroup164.HeadingRefWord]);
			record[MissionGroup164.RouteRefWord] = FindGroup(_waypointGroups, record[MissionGroup164.RouteRefWord], _waypointGroups.Count, exported: true);
			record[MissionGroup164.DeploymentActionRefWord] = Ref(_actions, record[MissionGroup164.DeploymentActionRefWord]);
			for (int slot = MissionGroup164.MemberRefsWord; slot < MissionGroup164.MemberRefsWord + 20; slot++) {
				if (record[slot] == -1) {
					continue;
				}

				record[slot] = record[MissionGroup164.MemberKindWord] switch {
					0 => Ref(_hercs, record[slot]),
					1 => Ref(_flyers, record[slot]),
					2 => Ref(_bases, record[slot]),
					_ => record[slot],
				};
			}

			for (int slot = MissionGroup164.OrderRefsWord; slot < MissionGroup164.OrderRefsWord + 10; slot++) {
				record[slot] = Ref(_orders, record[slot]);
			}

			AddOrMerge(_groups, record, (source, target) => {
				Overlay(source, target, MissionGroup164.PaintsGroundWord, 20, 0);
				Overlay(source, target, MissionGroup164.MemberRefsWord, 30, -1);
				Overlay(source, target, MissionGroup164.MemberKindWord, 5, -1);
				Overlay(source, target, MissionGroup164.SideWord, 2, -1);
				Overlay(source, target, MissionGroup164.OutOfActionReportWord, 21, -1);
			});
		}
	}

	/// <summary>
	/// The two passes after row 16: each order whose subject is a group has it resolved now, and each
	/// action whose type is 7-10 has its target (<c>0x50</c>) resolved into HERCs, flyers, bases or groups.
	/// An action of any other type keeps its target as authored.
	/// </summary>
	private void ResolveDeferredRefs() {
		foreach (var order in _orders) {
			if (order[MissionOrder22.SubjectKindWord] == 0) {
				order[MissionOrder22.SubjectRefWord] = Ref(_groups, order[MissionOrder22.SubjectRefWord]);
			}
		}

		const int target = MissionAction82.TargetRefWord;
		foreach (var action in _actions) {
			action[target] = action[MissionAction82.TypeWord] switch {
				7 => Ref(_hercs, action[target]),
				8 => Ref(_flyers, action[target]),
				9 => Ref(_bases, action[target]),
				10 => Ref(_groups, action[target]),
				_ => action[target],
			};
		}
	}

	/// <summary>
	/// Row 17, 58 bytes and no GUID: the objective. Its condition is its first word. Its point, route
	/// and failure text are resolved, and its subject by the kind at <c>0x06</c> — 0 a group, 1 a HERC, 2
	/// a flyer, 3 a base. Every survivor is kept.
	/// </summary>
	private void ReadObjectives(Cursor reader) {
		int count = reader.Short();
		for (int i = 0; i < count; i++) {
			if (reader.Remaining < 58) {
				break;
			}

			short[] record = reader.Words(29);
			if (!Gate(record[MissionObjective58.ConditionRefWord], _conditions.Count)) {
				continue;
			}

			const int subject = MissionObjective58.SubjectRefWord;
			record[MissionObjective58.PointRefWord] = Ref(_points, record[MissionObjective58.PointRefWord]);
			record[MissionObjective58.RouteRefWord] = FindGroup(_waypointGroups, record[MissionObjective58.RouteRefWord], _waypointGroups.Count, exported: true);
			record[MissionObjective58.TextRefWord] = TextLine(record[MissionObjective58.TextRefWord]);
			record[subject] = record[MissionObjective58.SubjectKindWord] switch {
				0 => Ref(_groups, record[subject]),
				1 => Ref(_hercs, record[subject]),
				2 => Ref(_flyers, record[subject]),
				3 => Ref(_bases, record[subject]),
				_ => record[subject],
			};

			_objectives.Add(record);
		}
	}

	// ---- What the shell reads back ----------------------------------------------------------------

	/// <summary>
	/// <c>DAT_00470600</c> as <c>MsnGen_LoadMission</c> counts it: 1, plus each member of group 0 set in
	/// an unbroken run from its second slot. Group 0 is the player's squad, so this is how many squad
	/// positions the mission has, the player's among them. The original does not stop at the twentieth
	/// slot, and nor does this: it reads on into the orders as the original would.
	/// </summary>
	public int SquadPositions {
		get {
			if (_groups.Count == 0) {
				return 1;
			}

			var group = _groups[0];
			int positions = 1;
			while (MissionGroup164.MemberRefsWord + positions < group.Length && group[MissionGroup164.MemberRefsWord + positions] != -1) {
				positions++;
			}

			return positions;
		}
	}

	/// <summary>
	/// Row 4's slot 0, the 72 words <c>MsnGen_LoadMission</c> hands <c>Career_SetBriefing</c> (<c>00412ece</c>) by
	/// value in a campaign: the condition, the ten objective, thirty briefing and thirty intelligence
	/// <c>mission.str</c> lines, and the row-3 value, the briefing movie. The same slot block 13 is written
	/// from, so it is the unfiltered last record read when none survived; null when the file had none.
	/// </summary>
	public short[]? TextPackage => (_texts.Count > 0 ? _texts[0] : _textSlot0)?.ToArray();

	/// <summary>Group 0's member slot <paramref name="slot"/> — a HERC's <c>script.dat</c> index, or <c>-1</c>.</summary>
	public short SquadMember(int slot) =>
		_groups.Count > 0 && slot >= 0 && slot < 20 ? _groups[0][MissionGroup164.MemberRefsWord + slot] : (short)-1;

	/// <summary>
	/// The HERC a <c>script.dat</c> index names — <c>MsnGen_HercArrayIndex</c> (<c>0041c23e</c>), which counts past the records with
	/// no GUID. A negative index names the first exported record, as its loop does. Null when the roster
	/// runs out first.
	/// </summary>
	public MissionHerc? Herc(short exportedIndex) {
		int index = 0;
		int seen = 0;
		if (exportedIndex > 0) {
			do {
				if (index >= _hercs.Count) {
					return null;
				}

				if (_hercs[index][MapObject.GUIDWord] != -1) {
					seen++;
				}

				index++;
			} while (seen < exportedIndex);
		}

		while (index < _hercs.Count && _hercs[index][MapObject.GUIDWord] == -1) {
			index++;
		}

		if (index >= _hercs.Count) {
			return null;
		}

		var record = _hercs[index];
		return new MissionHerc(
			record[MechRosterEntry144.TypeIndexWord],
			record[MechRosterEntry144.WeaponRefsWord..(MechRosterEntry144.WeaponRefsWord + 10)],
			record[MechRosterEntry144.WeaponSecondaryWord..(MechRosterEntry144.WeaponSecondaryWord + 10)]);
	}

	// ---- The writers ------------------------------------------------------------------------------

	/// <summary>
	/// <c>WriteScriptDatFile</c> (<c>0041ac54</c>): the header, with a literal 1 as its third word, then
	/// each row's records whose GUID is set, cut to what the simulator reads — the layout is
	/// docs/retail/formats/script-dat.md's — then row 17 whole, then the objective lines of row 4's slot 0.
	/// </summary>
	public byte[] WriteScriptDat() {
		using var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);

		for (int word = 0; word < Header.Length; word++) {
			writer.Write(word == 2 ? (short)1 : Header[word]);
		}

		var points = Exported(_points);
		writer.Write((short)points.Count);
		foreach (var point in points) {
			writer.Write(GetInt(point, MapPoint22.XWord));
			writer.Write(GetInt(point, MapPoint22.YWord));
			writer.Write(GetInt(point, MapPoint22.ZWord));
		}

		var headings = Exported(_headings);
		writer.Write((short)headings.Count);
		foreach (var heading in headings) {
			writer.Write(heading[Heading10.DegreesWord]);
		}

		var groupsOfPoints = _waypointGroups.Where(group => group.Words[MapObject.GUIDWord] != -1).ToList();
		writer.Write((short)groupsOfPoints.Count);
		foreach (var group in groupsOfPoints) {
			writer.Write(group.Words[4]);
			for (int point = 0; point < group.Words[4]; point++) {
				writer.Write(point < group.Points.Count ? group.Points[point] : (short)0);
			}
		}

		WriteBlock(writer, _areas, record => Words(writer, record, TriggerArea12.ShapeWord, 3));
		WriteBlock(writer, _actions, record => {
			Words(writer, record, MissionAction82.TypeWord, 10);
			Interleaved(writer, record, MissionAction82.CounterPairsWord);
			Words(writer, record, MissionAction82.TextRefsWord, 7);
		});
		WriteBlock(writer, _timers, record => Words(writer, record, ActionTimer30.PrimaryActionRefWord, 12));
		WriteBlock(writer, _hercs, record => {
			Words(writer, record, MechRosterEntry144.AiRadarActiveWord, 33);
			Interleaved(writer, record, MechRosterEntry144.OutOfActionReportWord);
			Words(writer, record, MechRosterEntry144.WeaponSecondaryWord, 14);
		});
		WriteBlock(writer, _flyers, record => {
			Words(writer, record, FlyerRosterEntry102.FlagSpanWord, 23);
			Interleaved(writer, record, FlyerRosterEntry102.OutOfActionReportWord);
			Words(writer, record, FlyerRosterEntry102.EngagementActionRefWord, 3);
		});
		WriteBlock(writer, _bases, record => {
			Words(writer, record, BaseRosterEntry62.TypeIndexWord, 3);
			Interleaved(writer, record, BaseRosterEntry62.OutOfActionReportWord);
			Words(writer, record, BaseRosterEntry62.EngagementActionRefWord, 3);
		});
		WriteBlock(writer, _orders, record => Words(writer, record, MissionOrder22.VerbWord, 7));
		WriteBlock(writer, _groups, record => {
			Words(writer, record, MissionGroup164.PaintsGroundWord, 57);
			Interleaved(writer, record, MissionGroup164.OutOfActionReportWord);
			Words(writer, record, MissionGroup164.MapShownWord, 1);
		});

		writer.Write((short)_objectives.Count);
		foreach (var objective in _objectives) {
			Words(writer, objective, MissionObjective58.RequiredWord, 7);
			Interleaved(writer, objective, MissionObjective58.PairsWord);
		}

		var package = TextPackage;
		if (package == null) {
			writer.Write((short)0);
		} else {
			short lines = 0;
			for (int word = MissionText144.ObjectiveLinesWord; word < MissionText144.ObjectiveLinesWord + 10; word++) {
				if (package[word] != -1) {
					lines++;
				}
			}

			writer.Write(lines);
			Words(writer, package, MissionText144.ObjectiveLinesWord, lines);
		}

		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>
	/// <c>MissionStr_Write</c> (<c>004179f0</c>): <c>data\mission.str</c>, an ordinary <c>.STR</c> of one group — the length of
	/// the rest, the count, then each line's length with its NUL, the line, and an attribute count of 0.
	/// A line ends at its first NUL.
	/// </summary>
	public byte[] WriteMissionText() {
		using var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);
		writer.Write(0);
		writer.Write((short)_strings.Count);
		foreach (var (_, text) in _strings) {
			int length = Array.IndexOf(text, (byte)0);
			if (length < 0) {
				length = text.Length;
			}

			writer.Write((short)(length + 1));
			writer.Write(text, 0, length);
			writer.Write((byte)0);
			writer.Write((byte)0);
		}

		writer.Flush();
		byte[] bytes = stream.ToArray();
		BitConverter.GetBytes(bytes.Length - 4).CopyTo(bytes, 0);
		return bytes;
	}

	/// <summary>The lines <see cref="WriteMissionText"/> writes, for a caller that wants them as text.</summary>
	public IReadOnlyList<string> TextLines =>
		_strings.Select(entry => {
			int length = Array.IndexOf(entry.Text, (byte)0);
			return Encoding.ASCII.GetString(entry.Text, 0, length < 0 ? entry.Text.Length : length);
		}).ToList();

	private static List<short[]> Exported(IReadOnlyList<short[]> row) => row.Where(record => record[MapObject.GUIDWord] != -1).ToList();

	private static void WriteBlock(BinaryWriter writer, IReadOnlyList<short[]> row, Action<short[]> write) {
		var records = Exported(row);
		writer.Write((short)records.Count);
		foreach (var record in records) {
			write(record);
		}
	}

	private static void Words(BinaryWriter writer, short[] record, int first, int count) {
		for (int i = first; i < first + count; i++) {
			writer.Write(record[i]);
		}
	}

	/// <summary>
	/// The writer's split of a span of ten word pairs: the first word of every pair, then the second —
	/// the order <c>ScriptDat</c>'s <c>CounterRefs</c>/<c>CounterOps</c> keep.
	/// </summary>
	private static void Interleaved(BinaryWriter writer, short[] record, int first) {
		for (int pair = 0; pair < 10; pair++) {
			writer.Write(record[first + pair * 2]);
		}

		for (int pair = 0; pair < 10; pair++) {
			writer.Write(record[first + pair * 2 + 1]);
		}
	}

	/// <summary>A row-8 record: its five words, then its resolved point list.</summary>
	private sealed class WaypointGroup {
		public WaypointGroup(short[] words) => Words = words;

		public short[] Words { get; }

		public List<short> Points { get; } = new();
	}

	/// <summary>A little-endian reader that answers zeros past the end, as a short read leaves a buffer.</summary>
	private sealed class Cursor {
		private readonly byte[] _bytes;
		private int _at;

		public Cursor(byte[] bytes) => _bytes = bytes;

		public bool AtEnd => _at >= _bytes.Length;

		public int Remaining => Math.Max(_bytes.Length - _at, 0);

		public short Short() {
			short value = _at + 2 <= _bytes.Length ? BitConverter.ToInt16(_bytes, _at) : (short)0;
			_at += 2;
			return value;
		}

		public short[] Words(int count) {
			var words = new short[count];
			for (int i = 0; i < count; i++) {
				words[i] = Short();
			}

			return words;
		}

		public byte[] Bytes(int count) {
			var bytes = new byte[Math.Max(count, 0)];
			if (_at < _bytes.Length) {
				Array.Copy(_bytes, _at, bytes, 0, Math.Min(bytes.Length, _bytes.Length - _at));
			}

			_at += bytes.Length;
			return bytes;
		}

		public void Skip(int count) => _at += Math.Max(count, 0);
	}
}

/// <summary>
/// One row-12 record as <c>MsnGen_LoadMission</c> builds a squad machine from it: its chassis, ten
/// weapons and ten ammunition types.
/// </summary>
/// <param name="Chassis"><inheritdoc cref="MechRosterEntry144.TypeIndex"/></param>
/// <param name="Weapons"><inheritdoc cref="MechRosterEntry144.WeaponRefs"/></param>
/// <param name="AmmoTypes"><inheritdoc cref="MechRosterEntry144.WeaponSecondary"/></param>
public sealed record MissionHerc(short Chassis, short[] Weapons, short[] AmmoTypes);

/// <summary>
/// What the debrief's reload of a flown mission gives the career: row 5's thirty <c>mission.str</c> lines
/// (<c>-1</c> empty), which <c>Career_SetDebriefLines</c> (<c>00413386</c>) copies to <c>0048407c</c>; its movie id,
/// to <c>004840ba</c>; and the <c>data\mission.str</c> the reload writes, which those lines index.
/// </summary>
public sealed record MissionDebriefText(short[] Lines, short Movie, byte[] MissionText);
