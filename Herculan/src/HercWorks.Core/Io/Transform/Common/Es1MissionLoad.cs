using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Msn.Es1;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// The five MISSION PARAMS words a single mission takes for header words 5 to 9, <c>MissionParams_Ammo</c> to
/// <c>MissionParams_TimeOfDay</c> (<c>ES1GO 000aacbc</c>–<c>000aacc4</c>; docs/es1/formats/msn-mission-file.md#the-mode-and-the-mission-parameters).
/// </summary>
public readonly record struct Es1MissionParams(short Ammo, short Damage, short Difficulty, short Visibility, short TimeOfDay) {
	/// <summary>The first of the five options in <c>data\prefs.cfg</c>, AMMO's; the other four follow it.</summary>
	public const int FirstOption = 12;

	/// <summary>How many option bytes <c>Prefs_Load</c> (<c>ES1GO 000081ca</c>) reads.</summary>
	public const int OptionCount = 17;

	/// <summary>
	/// The words as the shell's start-up leaves them: <c>00020630</c> copies options 12 to 16 of <c>data\prefs.cfg</c> into
	/// them, and <c>Prefs_Load</c> zeroes every option when the file does not open, so a null <paramref name="prefs"/> gives
	/// five zeros. A file shorter than the 17 bytes the load reads gives 0 for each option past its end, which is this
	/// port's reading: what <c>Prefs_Load</c> leaves after a short read is not in the doc.
	/// </summary>
	public static Es1MissionParams FromPrefs(byte[]? prefs) {
		short Option(int index) => prefs != null && index < prefs.Length ? prefs[index] : (short)0;
		return new(Option(FirstOption), Option(FirstOption + 1), Option(FirstOption + 2), Option(FirstOption + 3),
			Option(FirstOption + 4));
	}
}

/// <summary>
/// What a mission's load writes and keeps: the three <c>data\</c> files the simulator reads, in a single mission the two
/// <c>car\</c> files the shell reads back after the mission, and the record and the hangar the shell goes on with.
/// </summary>
/// <param name="Script"><c>data\script.dat</c>.</param>
/// <param name="Flags"><c>data\mission.var</c>, its 100 <c>int16</c> flags, which the load leaves in the shell's flags <c>000ab244</c>.</param>
/// <param name="Player"><c>data\player.mec</c>.</param>
/// <param name="Record">
/// The record the load works on, as it leaves it: in mode 0 <c>SingleMission_Record</c> (<c>0009b014</c>), which is
/// <c>car\sm_play.gam</c>; in mode 1 the roster's current record.
/// </param>
/// <param name="Hangar">The hangar as the load leaves it, every slot.</param>
/// <param name="SavedHangar">
/// <c>car\sm_play.mec</c>, the hangar's slots 0 to 7 as the squad build's step 5 saves them; null in mode 1, which builds no
/// squad and writes no <c>car\sm_play.*</c> file.
/// </param>
/// <param name="SquadMemberCount"><c>Squad_MemberCount</c> (<c>0007ebc8</c>), step 8's count of the first group's members.</param>
public sealed record Es1MissionLoadResult(Es1ScriptDat Script, short[] Flags, Es1PlayerMec Player, Es1CareerRecord Record,
		Es1Hangar Hangar, Es1HercList? SavedHangar, int SquadMemberCount) {
	/// <summary><see cref="Flags"/> as the 200 bytes of <c>data\mission.var</c>.</summary>
	public byte[] MissionVarBytes() {
		var bytes = new byte[Flags.Length * 2];
		Buffer.BlockCopy(Flags, 0, bytes, 0, bytes.Length);
		return bytes;
	}
}

/// <summary>
/// The shell's load, <c>MsnGen_LoadMission</c> (<c>ES1GO 00035873</c>) with <c>WriteScriptDatFile</c> (<c>0003445b</c>),
/// the squad build and <c>PlayerMec_Write</c> (<c>0002299e</c>), in docs/es1/formats/msn-mission-file.md#loading-a-mission's
/// order, making every draw in the order docs/es1/formats/msn-mission-file.md#the-order-of-the-loads-draws gives: a single
/// mission's in <c>Shell_Mode</c> 0 (<see cref="Load"/>) and a career's in mode 1 (<see cref="LoadCareer"/>)
/// (docs/es1/formats/msn-mission-file.md#the-mode-and-the-mission-parameters).
///
/// <para>Records are kept as the load keeps them, as arrays of <c>int16</c> words, each index the <c>.MSN</c> offset halved.
/// The rules for filtering, random tables and repeated GUIDs are ES2's (<see cref="MissionGenerator"/>) with ES1's differences
/// (docs/es1/formats/msn-mission-file.md#filtering-random-tables-and-repeats); this walks <see cref="Es1MissionFile"/>'s records
/// in file order, which is the order the loader reads them in.</para>
///
/// <para>Where the original would read outside an array — an option with no template to copy, a flag index past the 100 —
/// this leaves the field as it was or reads 0, as <see cref="MissionGenerator"/> does, which is this port's choice.</para>
/// </summary>
public sealed class Es1MissionLoad {
	/// <summary>How many flags the load seeds and writes, the 100 <c>int16</c> at <c>000ab244</c>.</summary>
	public const int FlagCount = 100;

	/// <summary><c>Shell_Mode</c> for a single mission, which <c>WriteScriptDatFile</c> writes in place of header word 2.</summary>
	public const short SingleMissionMode = 0;

	/// <summary><c>Shell_Mode</c> for a career.</summary>
	public const short CareerMode = 1;

	/// <summary>The word mode 0 seeds flag 1 with.</summary>
	private const short SingleMissionFlag1 = 99;

	/// <summary>
	/// The words at <c>00080f14</c>, mode 1's theater, header word 0, by the career record's campaign <c>+6</c>
	/// (docs/es1/formats/msn-mission-file.md#header-patches).
	/// </summary>
	private static readonly short[] CareerTheaters = [1, 2, 2, 0, 0, 3, 3, 3];

	/// <summary>Mode 1's header words 5, 6 and 7.</summary>
	private const short CareerAmmo = 0, CareerDamage = 0, CareerDifficulty = 2;

	/// <summary>The bound of each of the three drawn flags, 4, 5 and 6.</summary>
	private const short FlagDrawBound = 12;

	/// <summary>The bound of a single mission's theater draw, header word 0.</summary>
	private const short TheaterDrawBound = 4;

	/// <summary>The four type-0 operator codes (docs/es1/formats/msn-mission-file.md#conditions).</summary>
	private const short OpEqual = 0x114;
	private const short OpNotEqual = 0x115;
	private const short OpLess = 0x116;
	private const short OpGreater = 0x117;

	/// <summary>What <c>Msn_ConditionParentValue</c> answers for a type-3 parent, as ES2's: a range test that always passes.</summary>
	private const int AnyRoll = -99;

	private readonly List<short[]> _conditions = new();
	private readonly List<short[]> _points = new();
	private readonly List<short[]> _headings = new();
	private readonly List<WaypointGroup> _waypointGroups = new();
	private readonly List<short[]> _areas = new();
	private readonly List<short[]> _actions = new();
	private readonly List<short[]> _mechs = new();
	private readonly List<short[]> _flyers = new();
	private readonly List<short[]> _bases = new();
	private readonly List<short[]> _groups = new();
	private readonly short[] _flags = new short[FlagCount];
	private readonly short[] _header = new short[Es1ScriptDat.HeaderLength];
	private readonly Func<short, int> _roll;

	private Es1MissionLoad(Func<short, int> roll) {
		_roll = roll;
	}

	/// <summary>
	/// Loads <paramref name="mission"/> as a single mission. <paramref name="roll"/> is the shell's <c>Math_RandomBelow</c>
	/// (<c>ES1GO 00038774</c>) on <c>Shell_RandomState</c>, a draw in [0, bound) that returns 0 without a draw for a bound of
	/// 0 (docs/es1/formats/msn-mission-file.md#the-shells-generator). <paramref name="hangar"/> is <c>car\sm_init.dat</c> and
	/// <paramref name="tech"/> <c>bst\tech_sng.dat</c>. <paramref name="record"/> is <c>SingleMission_Record</c> as the shell
	/// holds it, which the squad build sets up in place and the result carries; a new record, all zero as the image's
	/// static record is, when null.
	/// </summary>
	/// <exception cref="NotSupportedException">The mission is in the other layout, whose reader is Open; it sets a point's
	/// sum flag, which no retail point does and the doc does not cover; or its squad has more than four members.</exception>
	/// <exception cref="InvalidDataException">A squad member's ref names no mech record, or its type no chassis.</exception>
	public static Es1MissionLoadResult Load(Es1MissionFile mission, Es1MissionParams missionParams, Es1HercList hangar,
			Es1TechFile tech, Func<short, int> roll, Es1CareerRecord? record = null) {
		var load = Begin(mission, roll);

		// Step 4: the flags, mode 0's column. The draws are the load's first.
		load._flags[1] = SingleMissionFlag1;
		load.SeedFlags(missionParams.TimeOfDay);

		// Step 5: the eleven rows.
		load.ReadRows(mission);

		// Step 6: the mode's overrides. Words 3 and 4 also go to the shell's 000aad5a and 000aad5c, which no file carries.
		load._header[0] = (short)roll(TheaterDrawBound);
		load._header[5] = missionParams.Ammo;
		load._header[6] = missionParams.Damage;
		load._header[7] = missionParams.Difficulty;
		load._header[8] = missionParams.Visibility;
		load._header[9] = missionParams.TimeOfDay;

		// Steps 7 and 8.
		var script = load.WriteScriptDat(SingleMissionMode);
		var flags = (short[])load._flags.Clone();
		int members = load.SquadMemberCount();

		// Step 9, the squad build with its step 5, car\sm_play.mec; then step 10, PlayerMec_Write, whose
		// CareerRecord_SaveCurrent writes the record to car\sm_play.gam as it stands after the player file.
		var squad = new SquadBuild(load, members, hangar, tech, roll, record ?? new Es1CareerRecord());
		var player = WritePlayerMec(squad.Record, squad.Hangar, roll);
		return new Es1MissionLoadResult(script, flags, player, squad.Record, squad.Hangar, squad.SavedHangar, members);
	}

	/// <summary>
	/// Loads <paramref name="mission"/> as a career's, in <c>Shell_Mode</c> 1, on the roster's current record
	/// <paramref name="record"/> and the career's hangar <paramref name="hangar"/>, as the load leaves the shell's:
	/// <paramref name="flags"/> are the shell's 100 flags <c>000ab244</c> as they stand, which mode 1 keeps but for flags 1
	/// and 2, the record's campaign <c>+6</c> and position <c>+8</c>, and the three draws
	/// (docs/es1/formats/msn-mission-file.md#the-flags); <paramref name="timeOfDay"/> is <c>MissionParams_TimeOfDay</c>
	/// (<c>000aacc4</c>), flag 3. Only draws 1 to 3 are made (docs/es1/formats/msn-mission-file.md#the-order-of-the-loads-draws);
	/// header word 0 is the record's campaign's theater, words 5 to 7 are 0, 0 and 2 and words 8 and 9 the patches'
	/// (docs/es1/formats/msn-mission-file.md#header-patches); no squad is built, but <c>Squad_MemberCount</c> is counted;
	/// and <c>data\player.mec</c> is written from the record and the hangar in mode 1 (<see cref="WriteCareerPlayerMec"/>,
	/// <paramref name="playerNameWord"/> the careers-log entry's word <c>+0x40</c>). <c>PlayerMec_Write</c>'s
	/// <c>CareerRecord_SaveCurrent</c>, <c>Career_Save</c>, is the caller's. A campaign outside the eight indexes past
	/// <see cref="CareerTheaters"/> in retail; here it leaves word 0 as the patches left it, an engine guard.
	/// </summary>
	/// <exception cref="NotSupportedException">The mission is in the other layout, or sets a point's sum flag, as <see cref="Load"/>.</exception>
	public static Es1MissionLoadResult LoadCareer(Es1MissionFile mission, IReadOnlyList<short> flags, short timeOfDay,
			Es1CareerRecord record, Es1Hangar hangar, short playerNameWord, Func<short, int> roll) {
		var load = Begin(mission, roll);

		// Step 4: the flags, mode 1's column: kept as they are, flags 1 and 2 from the record.
		for (int flag = 0; flag < FlagCount && flag < flags.Count; flag++) {
			load._flags[flag] = flags[flag];
		}

		load._flags[1] = record.Campaign;
		load._flags[2] = record.Position;
		load.SeedFlags(timeOfDay);

		// Step 5.
		load.ReadRows(mission);

		// Step 6: mode 1's overrides; words 8 and 9 stay the patches'.
		load._header[5] = CareerAmmo;
		load._header[6] = CareerDamage;
		load._header[7] = CareerDifficulty;
		if (record.Campaign >= 0 && record.Campaign < CareerTheaters.Length) {
			load._header[0] = CareerTheaters[record.Campaign];
		}

		// Steps 7, 8 and 10; step 9 is mode 0's.
		var script = load.WriteScriptDat(CareerMode);
		var written = (short[])load._flags.Clone();
		int members = load.SquadMemberCount();
		var player = WriteCareerPlayerMec(record, hangar, playerNameWord);
		return new Es1MissionLoadResult(script, written, player, record, hangar, null, members);
	}

	/// <summary>The load made for <paramref name="mission"/>, refused for a file in the other layout.</summary>
	private static Es1MissionLoad Begin(Es1MissionFile mission, Func<short, int> roll) {
		if (mission.HeaderWords != null) {
			throw new NotSupportedException(
				"The mission is in the other layout (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout), which no reader is known for.");
		}

		return new Es1MissionLoad(roll);
	}

	/// <summary>Step 4's part both modes share: flag 3 TIME OF DAY, then flags 4, 5 and 6 each a draw below 12, the load's first.</summary>
	private void SeedFlags(short timeOfDay) {
		_flags[3] = timeOfDay;
		for (int flag = 4; flag <= 6; flag++) {
			_flags[flag] = (short)_roll(FlagDrawBound);
		}
	}

	/// <summary>Step 5: the eleven rows, in file order.</summary>
	private void ReadRows(Es1MissionFile mission) {
		ReadConditions(mission.Conditions);
		ApplyHeaderPatches(mission.HeaderPatches);
		ReadPoints(mission.Points);
		ReadHeadings(mission.Headings);
		ReadWaypointGroups(mission.WaypointGroups);
		ReadAreas(mission.TriggerAreas);
		ReadActions(mission.Actions);
		ReadMechs(mission.Mechs);
		ReadFlyers(mission.Flyers);
		ReadBases(mission.Bases);
		ReadGroups(mission.Groups);
	}

	/// <summary>
	/// Step 8, <c>Squad_MemberCount</c>: the first group's member refs before its first -1, read on past the eight into the
	/// words after them as the load does; 0 with no group, where retail reads the group array's pointer as it stands.
	/// </summary>
	private int SquadMemberCount() {
		var group = _groups.Count > 0 ? _groups[0] : null;
		int count = 0;
		while (group != null && 29 + count < group.Length && group[29 + count] != -1) {
			count++;
		}

		return count;
	}

	/// <summary>
	/// <c>PlayerMec_WriteEntries</c> (<c>00022a05</c>) in <c>Shell_Mode</c> 0
	/// (docs/es1/formats/career-log-and-player-mec.md#the-player-file), for the load's step 10 and BEGIN MISSION >'s
	/// write: with the record's current pilot and <paramref name="hangar"/>'s selected slot kept, the player's entry
	/// <c>+0xe</c>, the count of entries on strength with a slot (<see cref="Es1CareerRecord.CountPilots"/>), the player's
	/// entry made the current pilot; then, that many times, the current pilot's two words
	/// (<c>PlayerMec_WritePilotWords</c>, <c>0001548f</c>: for the player's entry a draw below 11 with
	/// <paramref name="roll"/> and 0, for any other its name index and its number), the HERC in the selected slot, when
	/// there is one, with every status word set to 100 (<see cref="Es1HercRecord.SetAllStatus"/> and internal word 9)
	/// and written with <c>Herc_Write</c> (<see cref="Es1HercRecord.PrepareWrite"/>, a copy kept), and the next pilot
	/// (<see cref="Es1CareerRecord.NextPilot"/>); then the record's weapons' available bytes, and the kept current pilot
	/// and selected slot put back. The HERCs' changes are made in the hangar, as retail's are.
	/// </summary>
	public static Es1PlayerMec WritePlayerMec(Es1CareerRecord record, Es1Hangar hangar, Func<short, int> roll) =>
		WriteEntries(record, hangar, SingleMissionMode, roll, 0);

	/// <summary>
	/// <c>PlayerMec_WriteEntries</c> (<c>00022a05</c>) in <c>Shell_Mode</c> 1, on the roster's current record, for a
	/// career's load and BEGIN MISSION >'s write: as <see cref="WritePlayerMec"/>, but the player's two words are
	/// <paramref name="playerNameWord"/>, the word <c>+0x40</c> of the roster's careers-log entry, and its rank <c>+0xd</c>
	/// after <c>CareerRecord_SelectPilot</c> on it; any other entry's are its name index and its rank; and each HERC
	/// written has only its internal word 9 set, to the condition of the player's entry (<c>PilotEntry_Condition</c>,
	/// <c>0001618d</c>), whichever entry is being written. Nothing is drawn.
	/// </summary>
	public static Es1PlayerMec WriteCareerPlayerMec(Es1CareerRecord record, Es1Hangar hangar, short playerNameWord) =>
		WriteEntries(record, hangar, CareerMode, null, playerNameWord);

	/// <summary>
	/// <c>PlayerMec_WriteEntries</c> in <paramref name="mode"/>. Retail reads an entry past the four with no bounds test;
	/// such an entry's rank and condition read 0 here, an engine guard.
	/// </summary>
	private static Es1PlayerMec WriteEntries(Es1CareerRecord record, Es1Hangar hangar, short mode, Func<short, int>? roll,
			short playerNameWord) {
		static Es1PilotEntry? Entry(Es1CareerRecord record, short index) =>
			index is >= 0 and < Es1CareerRecord.PilotCount ? record.Pilots[index] : null;

		short keptPilot = record.CurrentPilot;
		short keptSlot = hangar.SelectedSlot;
		short playerEntry = record.PlayerEntry;
		int count = record.CountPilots();
		record.SelectPilot(playerEntry, hangar);
		var entries = new Es1PlayerMecEntry[count];
		for (int written = 0; written < count; written++) {
			short current = record.CurrentPilot;
			if (mode == SingleMissionMode) {
				entries[written] = current == playerEntry
					? new Es1PlayerMecEntry { Word1 = (short)roll!(11), Word2 = 0 }
					: new Es1PlayerMecEntry { Word1 = record.Pilots[current].NameIndex(current), Word2 = current };
			} else if (current == playerEntry) {
				record.SelectPilot(playerEntry, hangar);
				entries[written] = new Es1PlayerMecEntry { Word1 = playerNameWord, Word2 = Entry(record, record.CurrentPilot)?.Rank ?? 0 };
			} else {
				entries[written] = new Es1PlayerMecEntry { Word1 = record.Pilots[current].NameIndex(current), Word2 = record.Pilots[current].Rank };
			}

			if (hangar.HercIn(hangar.SelectedSlot) is { } herc) {
				if (mode == SingleMissionMode) {
					herc.SetAllStatus(Es1HercRecord.FullStatus);
					herc.PilotCondition = Es1HercRecord.FullStatus;
				} else {
					herc.PilotCondition = Entry(record, playerEntry)?.Condition ?? 0;
				}

				herc.PrepareWrite();
				entries[written].Herc = herc.Clone();
			}

			record.NextPilot(hangar);
		}

		var player = new Es1PlayerMec { PlayerEntry = playerEntry, Entries = entries, WeaponsAvailable = record.Equipment.WeaponsAvailable() };
		record.SelectPilot(keptPilot, hangar);
		hangar.SelectedSlot = keptSlot;
		return player;
	}

	// ---- Row 1: the conditions ----------------------------------------------------------------------------------------

	private void ReadConditions(IEnumerable<Es1Condition> conditions) {
		foreach (var c in conditions) {
			short[] record = [c.GUID, c.ConditionRef, c.Type, c.FlagIndexOrRangeLower, c.OperatorOrRangeUpperOrResult,
				c.ComparisonOperand, c.LatestDraw];
			switch (record[2]) {
				case 0:
					if (Gate(record[1])) {
						record[4] = Compare(record[4], Flag(record[3]), record[5]) ? (short)1 : (short)0;
						if (record[4] != 0) {
							_conditions.Add(record);
						}
					}

					break;
				case 1:
					if (Gate(record[1])) {
						record[4] = (short)_roll(record[3]);
						_conditions.Add(record);
					}

					break;
				case 2:
					int value = ParentValue(record[1]);
					if (value == AnyRoll || ((ushort)record[3] <= value && value <= (ushort)record[4])) {
						_conditions.Add(record);
					}

					break;
				case 3:
					if (Gate(record[1])) {
						_conditions.Add(record);
					}

					break;
			}
		}
	}

	/// <summary>A flag by the index a type-0 condition names, which the load does not bound; past the 100 this reads 0.</summary>
	private short Flag(short index) => index >= 0 && index < FlagCount ? _flags[index] : (short)0;

	private static bool Compare(short op, short flag, short operand) => op switch {
		OpEqual => flag == operand,
		OpNotEqual => flag != operand,
		OpLess => flag < operand,
		OpGreater => flag > operand,
		_ => false,
	};

	/// <summary><c>Msn_ConditionFilterGate</c> (<c>00032711</c>), ES2's: no condition passes, and so does one a surviving condition carries.</summary>
	private bool Gate(short condition) => condition == -1 || _conditions.Exists(record => record[0] == condition);

	/// <summary>ES2's <c>Msn_ConditionParentValue</c>: the first survivor with the GUID, a type-1 one's draw or a type-3 one's pass; -1 for none.</summary>
	private int ParentValue(short condition) {
		foreach (var record in _conditions) {
			if (record[0] == condition && record[2] == 1) {
				return record[4];
			}

			if (record[0] == condition && record[2] == 3) {
				return AnyRoll;
			}
		}

		return -1;
	}

	/// <summary>
	/// <c>Msn_PickOption</c> (<c>000318d5</c>), ES2's: for each surviving type-3 condition whose <c>0x06</c> is the key, a
	/// draw below its <c>0x0a</c>, kept at its <c>0x0c</c>, until one has a type-2 child whose range holds the draw; the
	/// child's index, or -1.
	/// </summary>
	private int PickOption(short key) {
		foreach (var parent in _conditions) {
			if (parent[2] != 3 || parent[3] != key) {
				continue;
			}

			parent[6] = (short)_roll(parent[5]);
			for (int child = 0; child < _conditions.Count; child++) {
				var candidate = _conditions[child];
				if (candidate[2] == 2 && candidate[1] == parent[0]
						&& (ushort)candidate[3] <= parent[6] && parent[6] <= (ushort)candidate[4]) {
					return child;
				}
			}
		}

		return -1;
	}

	/// <summary>The first record of <paramref name="row"/>, read so far, whose condition is the picked option's range, its template, or -1.</summary>
	private int TemplateOf<T>(List<T> row, Func<T, short> conditionOf, short key) {
		int picked = PickOption(key);
		if (picked < 0) {
			return -1;
		}

		short guid = _conditions[picked][0];
		return row.FindIndex(record => conditionOf(record) == guid);
	}

	// ---- Row 2: the header patches -------------------------------------------------------------------------------------

	/// <summary><c>Msn_ApplyHeaderPatch</c> (<c>00031c84</c>) over every patch, from the words 1, 0, 0, … the load sets first.</summary>
	private void ApplyHeaderPatches(IEnumerable<Es1HeaderPatch> patches) {
		Array.Clear(_header);
		_header[0] = 1;
		foreach (var patch in patches) {
			if (!Gate(patch.ConditionRef)) {
				continue;
			}

			if (patch.HeaderWords[0] != 1) {
				_header[0] = patch.HeaderWords[0];
			}

			for (int word = 1; word < _header.Length; word++) {
				if (patch.HeaderWords[word] != 0) {
					_header[word] = patch.HeaderWords[word];
				}
			}
		}
	}

	// ---- Rows 3 to 11 --------------------------------------------------------------------------------------------------

	/// <summary>
	/// A GUID's index among <paramref name="row"/>'s records with a GUID, the index <c>data\script.dat</c> gives it, or -1
	/// for <c>-1</c> or none. It is how every ref resolves, and the slot a repeated GUID lands on in the six rows that
	/// find it this way (docs/es1/formats/msn-mission-file.md#filtering-random-tables-and-repeats).
	/// </summary>
	private static short Exported<T>(IReadOnlyList<T> row, Func<T, short> guidOf, short guid) {
		if (guid == -1) {
			return -1;
		}

		short index = 0;
		foreach (var record in row) {
			short own = guidOf(record);
			if (own == -1) {
				continue;
			}

			if (own == guid) {
				return index;
			}

			index++;
		}

		return -1;
	}

	private static short Ref(IReadOnlyList<short[]> row, short guid) => Exported(row, record => record[0], guid);

	/// <summary>The array slot of the first record with the GUID, or -1: where mechs, flyers and bases find an earlier one.</summary>
	private static int OwnSlot(List<short[]> row, short guid) =>
		guid == -1 ? -1 : row.FindIndex(record => record[0] == guid);

	/// <summary>
	/// Adds a survivor, or when an earlier one has its GUID hands the slot to <paramref name="repeat"/>. The slot is
	/// <see cref="Exported"/>'s index for points, headings, waypoint groups, trigger areas, actions and groups, and the earlier
	/// record's own slot for mechs, flyers and bases.
	/// </summary>
	private static void AddOrRepeat(List<short[]> row, short[] record, bool exportedSlot, Action<short[], short[]> repeat) {
		int slot = exportedSlot ? Ref(row, record[0]) : OwnSlot(row, record[0]);
		if (slot == -1) {
			row.Add(record);
		} else {
			repeat(record, row[slot]);
		}
	}

	private static void Replace(short[] source, short[] target) => Array.Copy(source, target, target.Length);

	/// <summary>Copies each word of the span that is not <paramref name="unset"/>.</summary>
	private static void Overlay(short[] source, short[] target, int first, int count, short unset) {
		for (int i = first; i < first + count; i++) {
			if (source[i] != unset) {
				target[i] = source[i];
			}
		}
	}

	private static void Copy(short[] source, short[] target, int first, int count) =>
		Array.Copy(source, first, target, first, count);

	/// <summary>Row 3. A template supplies X, Y and Z (words 5 to 10).</summary>
	private void ReadPoints(IEnumerable<Es1Point> points) {
		foreach (var p in points) {
			if (p.SumFlag != 0) {
				throw new NotSupportedException(
					$"Point {p.GUID} sets its sum flag; what ES1's load does with it is not in docs/es1/formats/msn-mission-file.md, and no retail point sets one.");
			}

			short[] record = [p.GUID, p.ConditionRef, p.RandomTableKey, p.Word06, p.SumFlag,
				(short)p.X, (short)(p.X >> 16), (short)p.Y, (short)(p.Y >> 16), (short)p.Z, (short)(p.Z >> 16)];
			if (!Gate(record[1])) {
				continue;
			}

			if (record[2] != -1 && TemplateOf(_points, r => r[1], record[2]) is >= 0 and var source) {
				Copy(_points[source], record, 5, 6);
			}

			AddOrRepeat(_points, record, exportedSlot: true, Replace);
		}
	}

	/// <summary>Row 4. A template supplies the heading.</summary>
	private void ReadHeadings(IEnumerable<Es1Heading> headings) {
		foreach (var h in headings) {
			short[] record = [h.GUID, h.ConditionRef, h.RandomTableKey, h.Word06, h.Value];
			if (!Gate(record[1])) {
				continue;
			}

			if (record[2] != -1 && TemplateOf(_headings, r => r[1], record[2]) is >= 0 and var source) {
				record[4] = _headings[source][4];
			}

			AddOrRepeat(_headings, record, exportedSlot: true, Replace);
		}
	}

	/// <summary>Row 5. Each waypoint resolves as it is read; a template supplies the list.</summary>
	private void ReadWaypointGroups(IEnumerable<Es1WaypointGroup> groups) {
		foreach (var g in groups) {
			var group = new WaypointGroup([g.GUID, g.ConditionRef, g.RandomTableKey, g.Word06],
				g.Waypoints.Select(waypoint => Ref(_points, waypoint)).ToList());
			if (!Gate(group.Words[1])) {
				continue;
			}

			if (group.Words[2] != -1 && TemplateOf(_waypointGroups, r => r.Words[1], group.Words[2]) is >= 0 and var source) {
				group.Points.Clear();
				group.Points.AddRange(_waypointGroups[source].Points);
			}

			int slot = Exported(_waypointGroups, r => r.Words[0], group.Words[0]);
			if (slot == -1) {
				_waypointGroups.Add(group);
			} else {
				_waypointGroups[slot] = group;
			}
		}
	}

	/// <summary>Row 6. Its point resolves, and its second point when its shape is 0.</summary>
	private void ReadAreas(IEnumerable<Es1TriggerArea> areas) {
		foreach (var a in areas) {
			short[] record = [a.GUID, a.ConditionRef, a.Word04, a.Shape, a.PointRef, a.Word0A];
			if (!Gate(record[1])) {
				continue;
			}

			record[4] = Ref(_points, record[4]);
			if (record[3] == 0) {
				record[5] = Ref(_points, record[5]);
			}

			AddOrRepeat(_areas, record, exportedSlot: true, Replace);
		}
	}

	/// <summary>Row 7. Its eight trigger areas resolve; a repeat overlays the fields that are not -1.</summary>
	private void ReadActions(IEnumerable<Es1Action> actions) {
		foreach (var a in actions) {
			short[] record = [a.GUID, a.ConditionRef, a.Word04, a.Type, a.Verb, .. a.AreaRefs, a.Word1A, a.Word1C];
			if (!Gate(record[1])) {
				continue;
			}

			for (int slot = 5; slot < 13; slot++) {
				record[slot] = Ref(_areas, record[slot]);
			}

			AddOrRepeat(_actions, record, exportedSlot: true, (source, target) => Overlay(source, target, 3, 12, -1));
		}
	}

	/// <summary>
	/// Row 8. With no random table key its point, heading and two actions resolve; with one it copies <c>0x08</c>–<c>0x69</c> from
	/// the template, refs already resolved. A repeat overlays each field that is not its unset value.
	/// </summary>
	private void ReadMechs(IEnumerable<Es1Mech> mechs) {
		foreach (var m in mechs) {
			if (m.Words4E.Length != 10) {
				throw new NotSupportedException("A mech in the other layout's 88-byte form.");
			}

			short[] record = [m.GUID, m.ConditionRef, m.RandomTableKey, m.Word06, .. m.Words08, m.Type, .. m.Words32, m.PointRef,
				m.HeadingRef, m.Word4A, m.Word4C, .. m.Words4E, m.Word62, m.ActionRef64, m.ActionRef66, m.Word68];
			if (!Gate(record[1])) {
				continue;
			}

			if (record[2] == -1) {
				record[35] = Ref(_points, record[35]);
				record[36] = Ref(_headings, record[36]);
				record[50] = Ref(_actions, record[50]);
				record[51] = Ref(_actions, record[51]);
			} else if (TemplateOf(_mechs, r => r[1], record[2]) is >= 0 and var source) {
				Copy(_mechs[source], record, 4, 49);
			}

			AddOrRepeat(_mechs, record, exportedSlot: false, (source, target) => {
				Overlay(source, target, 4, 20, 0);
				Overlay(source, target, 24, 15, -1);
				Overlay(source, target, 39, 10, 5);
				Overlay(source, target, 49, 1, 2);
				Overlay(source, target, 50, 2, -1);
				Overlay(source, target, 52, 1, 100);
			});
		}
	}

	/// <summary>Row 9, as row 8 one class down; a template supplies <c>0x08</c>–<c>0x3f</c>.</summary>
	private void ReadFlyers(IEnumerable<Es1Flyer> flyers) {
		foreach (var f in flyers) {
			short[] record = [f.GUID, f.ConditionRef, f.RandomTableKey, f.Word06, .. f.Words08, f.PointRef, f.HeadingRef, f.Type,
				f.Word36, f.Word38, f.ActionRef3A, f.ActionRef3C, f.Word3E];
			if (!Gate(record[1])) {
				continue;
			}

			if (record[2] == -1) {
				record[24] = Ref(_points, record[24]);
				record[25] = Ref(_headings, record[25]);
				record[29] = Ref(_actions, record[29]);
				record[30] = Ref(_actions, record[30]);
			} else if (TemplateOf(_flyers, r => r[1], record[2]) is >= 0 and var source) {
				Copy(_flyers[source], record, 4, 28);
			}

			AddOrRepeat(_flyers, record, exportedSlot: false, (source, target) => {
				Overlay(source, target, 4, 20, 0);
				Overlay(source, target, 24, 7, -1);
				Overlay(source, target, 31, 1, 100);
			});
		}
	}

	/// <summary>Row 10; a template supplies <c>0x08</c>–<c>0x17</c>.</summary>
	private void ReadBases(IEnumerable<Es1Base> bases) {
		foreach (var b in bases) {
			short[] record = [b.GUID, b.ConditionRef, b.RandomTableKey, b.Word06, b.Type, b.PointRef, b.HeadingRef, b.Word0E,
				b.Word10, b.ActionRef12, b.ActionRef14, b.Word16];
			if (!Gate(record[1])) {
				continue;
			}

			if (record[2] == -1) {
				record[5] = Ref(_points, record[5]);
				record[6] = Ref(_headings, record[6]);
				record[9] = Ref(_actions, record[9]);
				record[10] = Ref(_actions, record[10]);
			} else if (TemplateOf(_bases, r => r[1], record[2]) is >= 0 and var source) {
				Copy(_bases[source], record, 4, 8);
			}

			AddOrRepeat(_bases, record, exportedSlot: false, (source, target) => {
				Overlay(source, target, 4, 7, -1);
				Overlay(source, target, 11, 1, 100);
			});
		}
	}

	/// <summary>
	/// Row 11. Its point, heading, route and action resolve, its members into the row its kind names, and its target into
	/// the row <c>0x4c</c> names. A target in the group row is searched for among the groups read so far and this one; the
	/// load also searches the slots not read yet, whose contents the doc does not give, and this takes them to match nothing.
	/// No retail group names a group after it in the file.
	/// </summary>
	private void ReadGroups(IEnumerable<Es1Group> groups) {
		foreach (var g in groups) {
			short[] record = [g.GUID, g.ConditionRef, g.Word04, .. g.Words06, g.MemberKind, g.Word30, g.Word32, g.PointRef,
				g.HeadingRef, g.WaypointGroupRef, .. g.MemberRefs, g.Word4A, g.Ref4ERow, g.Ref4E, g.ActionRef, g.Word52, g.Word54,
				g.Word56];
			if (!Gate(record[1])) {
				continue;
			}

			record[26] = Ref(_points, record[26]);
			record[27] = Ref(_headings, record[27]);
			record[28] = Exported(_waypointGroups, r => r.Words[0], record[28]);
			for (int slot = 29; slot < 37; slot++) {
				if (record[slot] == -1) {
					continue;
				}

				record[slot] = record[23] switch {
					0 => Ref(_mechs, record[slot]),
					1 => Ref(_flyers, record[slot]),
					2 => Ref(_bases, record[slot]),
					_ => record[slot],
				};
			}

			record[39] = record[38] switch {
				0 => Ref([.. _groups, record], record[39]),
				1 => Ref(_mechs, record[39]),
				2 => Ref(_flyers, record[39]),
				3 => Ref(_bases, record[39]),
				_ => record[39],
			};
			record[40] = Ref(_actions, record[40]);

			AddOrRepeat(_groups, record, exportedSlot: true, (source, target) => {
				Overlay(source, target, 3, 20, 0);
				Overlay(source, target, 23, 21, -1);
			});
		}
	}

	// ---- Step 7: data\script.dat ----------------------------------------------------------------------------------------

	/// <summary>
	/// <c>WriteScriptDatFile</c> (<c>0003445b</c>): the header with <paramref name="mode"/> in place of word 2, then each
	/// row's records whose GUID is not -1, cut to the fields the simulator reads (docs/es1/formats/script-dat.md#writing).
	/// </summary>
	private Es1ScriptDat WriteScriptDat(short mode) {
		static IEnumerable<short[]> Kept(List<short[]> row) => row.Where(record => record[0] != -1);
		static short[] Span(short[] record, int first, int count) => record[first..(first + count)];
		static int Int(short[] record, int at) => (ushort)record[at] | (record[at + 1] << 16);

		var header = (short[])_header.Clone();
		header[2] = mode;
		return new Es1ScriptDat {
			Header = header,
			Points = Kept(_points).Select(r => new Es1ScriptPoint { X = Int(r, 5), Y = Int(r, 7), Z = Int(r, 9) }).ToArray(),
			Headings = Kept(_headings).Select(r => r[4]).ToArray(),
			WaypointGroups = _waypointGroups.Where(g => g.Words[0] != -1).Select(g => g.Points.ToArray()).ToArray(),
			TriggerAreas = Kept(_areas).Select(r => new Es1ScriptTriggerArea { Shape = r[3], PointRef = r[4], Word0A = r[5] })
				.ToArray(),
			Actions = Kept(_actions).Select(r => new Es1ScriptAction {
				Type = r[3], Verb = r[4], AreaRefs = Span(r, 5, 8), Word1A = r[13], Word1C = r[14],
			}).ToArray(),
			Mechs = Kept(_mechs).Select(r => new Es1ScriptMech {
				Words08 = Span(r, 4, 20), Type = r[24], Words32 = Span(r, 25, 10), PointRef = r[35], HeadingRef = r[36],
				Word4A = r[37], Word4C = r[38], Words4E = Span(r, 39, 10), Word62 = r[49], ActionRef64 = r[50],
				ActionRef66 = r[51], Word68 = r[52],
			}).ToArray(),
			Flyers = Kept(_flyers).Select(r => new Es1ScriptFlyer {
				Words08 = Span(r, 4, 20), PointRef = r[24], HeadingRef = r[25], Type = r[26], Word36 = r[27], Word38 = r[28],
				ActionRef3A = r[29], ActionRef3C = r[30], Word3E = r[31],
			}).ToArray(),
			Bases = Kept(_bases).Select(r => new Es1ScriptBase {
				Type = r[4], PointRef = r[5], HeadingRef = r[6], Word0E = r[7], Word10 = r[8], ActionRef12 = r[9],
				ActionRef14 = r[10], Word16 = r[11],
			}).ToArray(),
			Groups = Kept(_groups).Select(r => new Es1ScriptGroup {
				Words06 = Span(r, 3, 20), MemberKind = r[23], Word30 = r[24], Word32 = r[25], PointRef = r[26],
				HeadingRef = r[27], WaypointGroupRef = r[28], MemberRefs = Span(r, 29, 8), Word4A = r[37], Ref4ERow = r[38],
				Ref4E = r[39], ActionRef = r[40], Word52 = r[41], Word54 = r[42], Word56 = r[43],
			}).ToArray(),
		};
	}

	/// <summary>A row-5 record: its four leading words and its resolved waypoints.</summary>
	private sealed class WaypointGroup(short[] words, List<short> points) {
		public short[] Words { get; } = words;

		public List<short> Points { get; } = points;
	}

	// ---- Steps 8 and 9: the squad ---------------------------------------------------------------------------------------

	/// <summary>
	/// The mode-0 squad build (docs/es1/formats/msn-mission-file.md#the-squad) on <c>SingleMission_Record</c> and the
	/// hangar, through its step 5, <c>car\sm_play.mec</c>.
	/// </summary>
	private sealed class SquadBuild {
		private readonly Es1MissionLoad _load;
		private readonly Func<short, int> _roll;

		public SquadBuild(Es1MissionLoad load, int members, Es1HercList hangar, Es1TechFile tech, Func<short, int> roll,
				Es1CareerRecord record) {
			_load = load;
			_roll = roll;

			// 1. Hangar_LoadFile with car\sm_init.dat.
			Hangar = Es1Hangar.Read(hangar);

			// 2. CareerRecord_Init in mode 0: every entry through PilotEntry_Init, and the equipment from tech_sng.dat.
			Record = record;
			Record.InitSingleMission(tech);

			// 3. Each member's entry.
			BuildMembers(members);

			// 4. The player's entry made the current pilot, which selects its slot.
			Record.SelectPilot(Record.PlayerEntry, Hangar);

			// 5. Hangar_SaveFile(car\sm_play.mec).
			SavedHangar = Hangar.SingleMissionSave();
		}

		/// <summary><c>SingleMission_Record</c> (<c>0009b014</c>).</summary>
		public Es1CareerRecord Record { get; }

		/// <summary>The hangar, every slot.</summary>
		public Es1Hangar Hangar { get; }

		/// <summary>Step 5's <c>car\sm_play.mec</c>.</summary>
		public Es1HercList SavedHangar { get; }

		/// <summary>
		/// Step 3 for each of the first group's <paramref name="members"/>: an entry set up by <see cref="Es1PilotEntry.Init"/>,
		/// filled, and copied over pilot entry <i>i</i> of the record.
		/// </summary>
		private void BuildMembers(int members) {
			var group = _load._groups.Count > 0 ? _load._groups[0] : null;
			if (members > Es1CareerRecord.PilotCount) {
				throw new NotSupportedException(
					$"The squad has {members} members; the career record holds four pilot entries, and a fifth's lands on its equipment.");
			}

			for (int i = 0; i < members; i++) {
				var mech = MechRecord(group![29 + i]);
				var entry = new Es1PilotEntry();
				entry.Init();
				if (i == 0) {
					entry.NameGroup = 0;
					entry.Name = 0;
					entry.Slot = mech[24];
				} else {
					entry.NameGroup = (short)(_roll(4) + 4 * (i - 1));
					entry.Name = (short)_roll(3);
					entry.Rank = (short)_roll(6);
					DrawForRank(entry);
					entry.Slot = Hangar.AddHerc(NewHerc(mech));
				}

				Record.Pilots[i] = entry;
			}
		}

		/// <summary><c>MsnGen_HercArrayIndex</c> (<c>0003581a</c>): the mech record that is block-6 record <paramref name="exported"/>.</summary>
		private short[] MechRecord(short exported) {
			var kept = _load._mechs.Where(record => record[0] != -1).ToList();
			return exported >= 0 && exported < kept.Count
				? kept[exported]
				: throw new InvalidDataException($"A squad member's ref, {exported}, names no mech record of the {kept.Count} kept.");
		}

		/// <summary>
		/// <c>PilotEntry_DrawForRank</c> (<c>00015402</c>), its draws in the doc's order, with the −1 that ends
		/// <see cref="Es1PilotEntry.RankThresholds"/> as rank 5's threshold.
		/// </summary>
		private void DrawForRank(Es1PilotEntry entry) {
			short threshold = Es1PilotEntry.RankThresholds[entry.Rank];
			entry.GainSums[0] = (short)(threshold + _roll((short)(threshold + 1)));
			if (entry.Rank != 0) {
				entry.GainSums[1] = (short)_roll((short)(5 * entry.Rank));
				entry.GainSums[2] = (short)_roll((short)(10 * entry.Rank));
			} else {
				entry.GainSums[1] = 0;
				entry.GainSums[2] = 0;
			}
		}

		/// <summary>
		/// A squadmate's new HERC: <c>Herc_Ctor</c> for chassis -1 (<see cref="Es1HercRecord.Create"/>), then the mech
		/// record's chassis, that chassis' mount count, its weapons and mount words, its <c>0x62</c>, and
		/// <c>Herc_SetAllStatus</c>(<i>h</i>, 100).
		/// </summary>
		private static Es1HercRecord NewHerc(short[] mech) {
			short chassis = mech[24];
			if (chassis < 0 || chassis >= Es1HercRecord.MountCounts.Length) {
				throw new InvalidDataException($"A squadmate's mech type, {chassis}, is no chassis the table at 000803c8 covers.");
			}

			int mounts = Es1HercRecord.MountCounts[chassis];
			var herc = Es1HercRecord.Create(-1);
			herc.Chassis = chassis;
			herc.Weapons = mech[25..(25 + mounts)];
			herc.MountWords = mech[39..(39 + mounts)];
			herc.Word = mech[49];
			herc.SetAllStatus(Es1HercRecord.FullStatus);
			return herc;
		}
	}
}
