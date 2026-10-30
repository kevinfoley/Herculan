namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - ES1's career record, <c>0x1f7</c> bytes: a career's position, its four pilot entries, its equipment state and its
/// flags (docs/es1/formats/bst-battle-sets.md#the-career-record). A single mission's, <c>SingleMission_Record</c>
/// (<c>ES1GO 0009b014</c>), is written whole to <c>car\sm_play.gam</c> by <c>SingleMission_SaveRecord</c> (<c>00014f54</c>)
/// and read back by <c>SingleMission_RestoreRecord</c> (<c>00014fd6</c>). Read and written by
/// <see cref="Io.Transform.Shell.Es1CareerRecordTransformer"/>. Every byte is kept, the fields the doc does not explain as
/// raw words, so a read and a write give back the file.
///
/// <para>The methods are the shell's functions on a record, each citing its retail symbol; the hangar they select slots in
/// is <see cref="Es1Hangar"/>.</para>
/// </summary>
public class Es1CareerRecord {
	/// <summary>A record's length.</summary>
	public const int Length = 0x1f7;

	/// <summary>The pilot entries a record holds.</summary>
	public const int PilotCount = 4;

	/// <summary>The <c>int16</c> at <c>+0x12d</c>.</summary>
	public const int FlagCount = 100;

	/// <summary><c>+0x00</c>, nonzero for a record in use.</summary>
	public byte InUse { get; set; }

	/// <summary><c>+0x01</c>, which <see cref="InitSingleMission"/> zeroes.</summary>
	public byte Byte1 { get; set; }

	/// <summary><c>+0x02</c>, which <see cref="InitSingleMission"/> zeroes.</summary>
	public short Word2 { get; set; }

	/// <summary><c>+0x04</c>: <c>0x6a4</c> after the init, <c>0x6ae</c> after each career mission.</summary>
	public short Word4 { get; set; }

	/// <summary><c>+0x06</c>, the campaign.</summary>
	public short Campaign { get; set; }

	/// <summary><c>+0x08</c>, the position in the campaign.</summary>
	public short Position { get; set; }

	/// <summary><c>+0x0a</c>, the missions played.</summary>
	public short MissionsPlayed { get; set; }

	/// <summary><c>+0x0c</c>, header word 3 of the career mission last started; the init leaves it.</summary>
	public short LastMissionKind { get; set; }

	/// <summary><c>+0x0e</c>, the player's pilot entry.</summary>
	public short PlayerEntry { get; set; }

	/// <summary><c>+0x10</c>, the pilot count, 4 after the init.</summary>
	public short PilotCountWord { get; set; }

	/// <summary><c>+0x12</c>, the current pilot entry, whose slot is the hangar's selected slot.</summary>
	public short CurrentPilot { get; set; }

	/// <summary><c>+0x14</c>, the four pilot entries.</summary>
	public Es1PilotEntry[] Pilots { get; set; } = NewPilots();

	/// <summary><c>+0xa8</c>, the equipment state.</summary>
	public Es1EquipmentState Equipment { get; set; } = new();

	/// <summary><c>+0x129</c>, a dword set to <c>0007fcf0</c> when a career starts; the init leaves it.</summary>
	public int Dword129 { get; set; }

	/// <summary><c>+0x12d</c>, 100 <c>int16</c> a career starts its flags from; the init zeroes them.</summary>
	public short[] Flags { get; set; } = new short[FlagCount];

	/// <summary><c>+0x1f5</c>, a word set to <c>0007fcf8</c> when a career starts; the init leaves it.</summary>
	public short Word1F5 { get; set; }

	private static Es1PilotEntry[] NewPilots() => Enumerable.Range(0, PilotCount).Select(_ => new Es1PilotEntry()).ToArray();

	/// <summary>
	/// <c>CareerRecord_Init</c> (<c>000158ff</c>) in <c>Shell_Mode</c> 0, on the record as it stands: bytes <c>+0</c> and
	/// <c>+1</c> and the words <c>+2</c>, <c>+6</c>, <c>+8</c>, <c>+0xa</c>, <c>+0xe</c> and <c>+0x12</c> zeroed, <c>+4</c>
	/// <c>0x6a4</c>, the flags zeroed (<c>CareerRecord_ClearWords</c>, <c>000161ae</c>), the equipment state loaded from
	/// <paramref name="tech"/>, <c>bst\tech_sng.dat</c> (<see cref="Es1EquipmentState.LoadSingleMission"/>) and its words
	/// <c>+0</c> and <c>+2</c> set to −1, the pilot count 4 and every pilot entry set up with
	/// <see cref="Es1PilotEntry.Init"/>. The init also zeroes the shell's word <c>000aad58</c>, the outcome, which the shell
	/// holds, not the record. Mode 1's is <see cref="InitCareer"/>.
	/// </summary>
	public void InitSingleMission(Es1TechFile tech) {
		InitFields(tech);
		foreach (var pilot in Pilots) {
			pilot.Init();
		}
	}

	/// <summary>
	/// <c>CareerRecord_Init</c> (<c>000158ff</c>) in <c>Shell_Mode</c> 1, as <c>Career_New</c> runs it on each roster record
	/// (docs/es1/shell/career.md#career_new): as <see cref="InitSingleMission"/>, the equipment state loaded from
	/// <paramref name="tech"/>, <c>bst\tech_0.dat</c>, and every pilot entry but the one <see cref="PlayerEntry"/> names (0,
	/// as the init leaves it) set up with <see cref="Es1PilotEntry.InitRandom"/>'s draws on <paramref name="randomBelow"/>,
	/// <c>Math_RandomBelow</c> on <c>Shell_RandomState</c>, in entry order.
	/// </summary>
	public void InitCareer(Es1TechFile tech, Func<short, int> randomBelow) {
		InitFields(tech);
		for (int i = 0; i < PilotCountWord; i++) {
			if (i == PlayerEntry) {
				Pilots[i].Init();
			} else {
				Pilots[i].InitRandom(randomBelow);
			}
		}
	}

	/// <summary><c>CareerRecord_Init</c>'s fields before its pilot entries, which its two modes share.</summary>
	private void InitFields(Es1TechFile tech) {
		InUse = 0;
		Byte1 = 0;
		Word2 = 0;
		Word4 = 0x6a4;
		Campaign = 0;
		Position = 0;
		MissionsPlayed = 0;
		PlayerEntry = 0;
		CurrentPilot = 0;
		ClearWords();
		Equipment.LoadTech(tech);
		Equipment.LastKind = -1;
		Equipment.LastIndex = -1;
		PilotCountWord = PilotCount;
	}

	/// <summary><c>CareerRecord_ClearWords</c> (<c>000161ae</c>): the 100 words <see cref="Flags"/> zeroed.</summary>
	public void ClearWords() => Array.Clear(Flags);

	/// <summary>Whether entry <paramref name="index"/> is on strength with a hangar slot, the test the pilot functions share.</summary>
	public bool OnStrengthWithSlot(int index) => Pilots[index].OnStrength != 0 && Pilots[index].Slot != -1;

	/// <summary><c>CareerRecord_CountPilots</c> (<c>00015bfb</c>): the entries on strength with a slot.</summary>
	public short CountPilots() => (short)Enumerable.Range(0, PilotCount).Count(OnStrengthWithSlot);

	/// <summary>
	/// <c>CareerRecord_SelectPilot</c> (<c>00015ad4</c>)(record, <paramref name="entry"/>): <see cref="CurrentPilot"/> set to
	/// it and, when it is not −1, <paramref name="hangar"/>'s selected slot to its slot. Retail reads the entry with no bounds
	/// test; an entry past the four leaves the slot here, an engine guard.
	/// </summary>
	public void SelectPilot(short entry, Es1Hangar hangar) {
		CurrentPilot = entry;
		if (entry >= 0 && entry < PilotCount) {
			hangar.SelectedSlot = Pilots[entry].Slot;
		}
	}

	/// <summary>
	/// <c>CareerRecord_NextPilot</c> (<c>00015b0a</c>): <see cref="CurrentPilot"/> stepped by 1, modulo 4, until it names an
	/// entry on strength with a slot, which can be the one it started from, and <paramref name="hangar"/>'s selected slot
	/// set to that entry's when it changed. Retail loops for ever when no entry qualifies; here the current pilot then stays,
	/// an engine guard.
	/// </summary>
	public void NextPilot(Es1Hangar hangar) {
		short start = CurrentPilot;
		for (int step = 1; step <= PilotCount; step++) {
			int index = ((start + step) % PilotCount + PilotCount) % PilotCount;
			if (OnStrengthWithSlot(index)) {
				CurrentPilot = (short)index;
				if (CurrentPilot != start) {
					hangar.SelectedSlot = Pilots[index].Slot;
				}

				return;
			}
		}
	}

	/// <summary>
	/// <c>CareerRecord_PlayerHasNoHerc</c> (<c>000161dc</c>): the player's entry <see cref="PlayerEntry"/> has no slot. Retail
	/// reads the entry with no bounds test; an entry past the four answers true here, an engine guard.
	/// </summary>
	public bool PlayerHasNoHerc() => PlayerEntry is < 0 or >= PilotCount || Pilots[PlayerEntry].Slot == -1;

	/// <summary>
	/// <c>CareerRecord_AnyPilotUnarmed</c> (<c>0001620d</c>): as many times as <see cref="CountPilots"/> counts, the current
	/// pilot's slot tested with <see cref="Es1Hangar.SlotIsUnarmed"/> and the current pilot stepped with
	/// <see cref="NextPilot"/>, which moves the current pilot and the hangar's selected slot as retail's does; true when any
	/// test was. An entry past the four tests a slot of −1 here, an engine guard where retail reads past the entries.
	/// </summary>
	public bool AnyPilotUnarmed(Es1Hangar hangar) {
		bool any = false;
		int count = CountPilots();
		for (int i = 0; i < count; i++) {
			short slot = CurrentPilot is >= 0 and < PilotCount ? Pilots[CurrentPilot].Slot : (short)-1;
			any |= hangar.SlotIsUnarmed(slot);
			NextPilot(hangar);
		}

		return any;
	}

	/// <summary>
	/// <c>CareerRecord_PlayerHercDisabled</c> (<c>0001626d</c>) (docs/es1/shell/between-missions.md#next-mission):
	/// <see cref="Es1Hangar.SlotIsDisabled"/> of the slot of the player's entry <see cref="PlayerEntry"/>. Retail reads the
	/// entry with no bounds test; an entry past the four tests a slot of −1 here, an engine guard.
	/// </summary>
	public bool PlayerHercDisabled(Es1Hangar hangar) =>
		hangar.SlotIsDisabled(PlayerEntry is >= 0 and < PilotCount ? Pilots[PlayerEntry].Slot : (short)-1);

	/// <summary>
	/// <c>CareerRecord_AnyHercDisabled</c> (<c>00016294</c>) (docs/es1/shell/between-missions.md#next-mission): as
	/// <see cref="AnyPilotUnarmed"/>, each current pilot's slot tested with <see cref="Es1Hangar.SlotIsDisabled"/>, the current
	/// pilot and the hangar's selected slot stepped as retail's are; true when any test was.
	/// </summary>
	public bool AnyHercDisabled(Es1Hangar hangar) {
		bool any = false;
		int count = CountPilots();
		for (int i = 0; i < count; i++) {
			short slot = CurrentPilot is >= 0 and < PilotCount ? Pilots[CurrentPilot].Slot : (short)-1;
			any |= hangar.SlotIsDisabled(slot);
			NextPilot(hangar);
		}

		return any;
	}

	/// <summary>
	/// <c>CareerRecord_CountLostPilots</c> (<c>00015db7</c>): the entries on strength with a slot whose condition
	/// (<c>PilotEntry_Condition</c>, <c>0001618d</c>) is 0, which retail adds to the word <c>0007ebca</c>.
	/// </summary>
	public short CountLostPilots() =>
		(short)Enumerable.Range(0, PilotCount).Count(i => OnStrengthWithSlot(i) && Pilots[i].Condition == 0);

	/// <summary>
	/// <c>CareerRecord_MarkTopPilots</c> (<c>00015cbb</c>) (docs/es1/shell/career.md#a-careers-next-mission): the entries
	/// 1, 2 and 3 (the table at <c>0007ec9c</c>) bubble-sorted by their rank, greatest first, a pair swapped only when the
	/// first's rank is the smaller; then the entry in place <i>p</i> put on strength when <i>p</i> + 1 is below
	/// <paramref name="squadMemberCount"/>, <c>Squad_MemberCount</c> (<c>0007ebc8</c>), and off strength otherwise.
	/// </summary>
	public void MarkTopPilots(int squadMemberCount) {
		short[] order = [1, 2, 3];
		int end = order.Length;
		bool swapped = true;
		while (swapped) {
			end--;
			swapped = false;
			for (int i = 0; i < end; i++) {
				if (Pilots[order[i]].Rank < Pilots[order[i + 1]].Rank) {
					(order[i], order[i + 1]) = (order[i + 1], order[i]);
					swapped = true;
				}
			}
		}

		for (int place = 0; place < order.Length; place++) {
			Pilots[order[place]].OnStrength = place + 1 < squadMemberCount ? (byte)1 : (byte)0;
		}
	}
}

/// <summary>
/// A <c>0x25</c>-byte pilot entry of an <see cref="Es1CareerRecord"/> (docs/es1/formats/bst-battle-sets.md#pilot-entries).
/// </summary>
public class Es1PilotEntry {
	/// <summary>An entry's length.</summary>
	public const int Length = 0x25;

	/// <summary>The three gains and their sums.</summary>
	public const int GainCount = 3;

	/// <summary>
	/// <c>PilotRankThresholds</c> (<c>0007ebd4</c>), ended by −1: the rank is the number of them below
	/// <see cref="GainSums"/>' first, at most 4.
	/// </summary>
	public static readonly short[] RankThresholds = [0, 1, 5, 15, 30, -1];

	/// <summary><c>+0x00</c>, the name's group in <c>car\names.dat</c>.</summary>
	public short NameGroup { get; set; }

	/// <summary><c>+0x02</c>, the name within the group, 0 to 2.</summary>
	public short Name { get; set; }

	/// <summary><c>+0x04</c>.</summary>
	public short Word4 { get; set; }

	/// <summary><c>+0x06</c>.</summary>
	public short Word6 { get; set; }

	/// <summary><c>+0x08</c>, the hangar slot of the pilot's HERC, −1 for none.</summary>
	public short Slot { get; set; }

	/// <summary><c>+0x0a</c>, nonzero on strength.</summary>
	public byte OnStrength { get; set; }

	/// <summary><c>+0x0b</c>.</summary>
	public short WordB { get; set; }

	/// <summary><c>+0x0d</c>, the rank.</summary>
	public short Rank { get; set; }

	/// <summary><c>+0x0f</c>, the pilot's condition, 100 at the init; 0 a pilot lost.</summary>
	public short Condition { get; set; }

	/// <summary>
	/// <c>+0x11</c>, <c>+0x13</c>, <c>+0x15</c>: a mission's gains, which the debriefing shows as the player's HERC,
	/// structure and flyer kills (docs/es1/shell/debriefing.md#the-results-panel).
	/// </summary>
	public short[] Gains { get; set; } = new short[GainCount];

	/// <summary><c>+0x17</c>, <c>+0x19</c>, <c>+0x1b</c>: the gains' sums; the rank is read from the first.</summary>
	public short[] GainSums { get; set; } = new short[GainCount];

	/// <summary><c>+0x1d</c>.</summary>
	public short Word1D { get; set; }

	/// <summary><c>+0x1f</c>.</summary>
	public short Word1F { get; set; }

	/// <summary><c>+0x21</c>, the pilot's pay share.</summary>
	public int PayShare { get; set; }

	/// <summary>
	/// <c>PilotEntry_Init</c> (<c>000151c8</c>): the name words −1, <c>+4</c> and <c>+6</c> 0, no slot, on strength,
	/// <c>+0xb</c>, the gains, their sums, <c>+0x1d</c>, <c>+0x1f</c> and the pay share 0, the condition 100; then
	/// <see cref="ApplyGains"/>, which sets the rank to 0.
	/// </summary>
	public void Init() {
		NameGroup = -1;
		Name = -1;
		Word4 = 0;
		Word6 = 0;
		Slot = -1;
		Condition = 100;
		OnStrength = 1;
		Word1D = 0;
		WordB = 0;
		Word1F = 0;
		Array.Clear(Gains);
		Array.Clear(GainSums);
		PayShare = 0;
		ApplyGains(null);
	}

	/// <summary>
	/// <c>PilotEntry_InitRandom</c> (<c>00015228</c>) (docs/es1/formats/bst-battle-sets.md#pilot-entries): the name words −1,
	/// <c>+4</c> and <c>+6</c> 0, no slot, the condition 100, on strength, the gains and the pay share 0; then, by draws on
	/// <paramref name="randomBelow"/> in this order, <i>d</i> below 2 into <see cref="Word1D"/> and <see cref="WordB"/>,
	/// <see cref="Word1F"/> a draw below 5 plus 5<i>d</i>, the HERC kills (a draw below 3 + 1) × <see cref="Word1F"/>, the
	/// flyer kills (a draw below 2 + 1) × <see cref="Word1F"/> and the structure kills a draw below 2 × <see cref="Word1F"/>;
	/// then <see cref="ApplyGains"/>, which with the gains 0 sets the rank from the HERC kills.
	/// </summary>
	public void InitRandom(Func<short, int> randomBelow) {
		NameGroup = -1;
		Name = -1;
		Word4 = 0;
		Word6 = 0;
		Slot = -1;
		Condition = 100;
		OnStrength = 1;
		short d = (short)randomBelow(2);
		Word1D = d;
		WordB = d;
		Word1F = (short)(randomBelow(5) + d * 5);
		Array.Clear(Gains);
		GainSums[0] = (short)((randomBelow(3) + 1) * Word1F);
		GainSums[2] = (short)((randomBelow(2) + 1) * Word1F);
		GainSums[1] = (short)(randomBelow(2) * Word1F);
		PayShare = 0;
		ApplyGains(null);
	}

	/// <summary>
	/// <c>PilotEntry_ApplyGains</c> (<c>00015317</c>): when on strength, each gain added to its sum and to
	/// <paramref name="totals"/>' word, the words <c>0009ae5c</c>, <c>0009ae5e</c> and <c>0009ae60</c> (none when the
	/// caller's gains are all 0); then the rank set from <see cref="RankThresholds"/>.
	/// </summary>
	public void ApplyGains(short[]? totals) {
		if (OnStrength != 0) {
			for (int i = 0; i < GainCount; i++) {
				GainSums[i] += Gains[i];
				if (totals != null) {
					totals[i] += Gains[i];
				}
			}
		}

		short rank = 0;
		while (RankThresholds[rank] != -1 && RankThresholds[rank] < GainSums[0]) {
			rank++;
		}

		Rank = RankThresholds[rank] == -1 ? (short)(rank - 1) : rank;
	}

	/// <summary><c>PilotEntry_NameIndex</c> (<c>00015f50</c>) for entry <paramref name="index"/>: 3 × the name group plus the name, −1 for entry 0.</summary>
	public short NameIndex(int index) => index == 0 ? (short)-1 : (short)(3 * NameGroup + Name);
}

/// <summary>
/// The equipment state, the <c>0x81</c> bytes at a career record's <c>+0xa8</c>: the item last made available and an entry
/// per chassis and per weapon (docs/es1/formats/bst-battle-sets.md#equipment-availability).
/// </summary>
public class Es1EquipmentState {
	/// <summary>The state's length.</summary>
	public const int Length = 0x81;

	/// <summary>The chassis entries.</summary>
	public const int ChassisCount = 8;

	/// <summary>The weapon entries.</summary>
	public const int WeaponCount = 17;

	/// <summary><c>+0</c>, the kind of the item last made available, −1 once the init has loaded the file.</summary>
	public short LastKind { get; set; }

	/// <summary><c>+2</c>, its index.</summary>
	public short LastIndex { get; set; }

	/// <summary><c>+4</c>, the eight chassis entries.</summary>
	public Es1EquipmentEntry[] Chassis { get; set; } = NewEntries(ChassisCount);

	/// <summary><c>+0x2c</c>, the 17 weapon entries.</summary>
	public Es1EquipmentEntry[] Weapons { get; set; } = NewEntries(WeaponCount);

	private static Es1EquipmentEntry[] NewEntries(int count) => Enumerable.Range(0, count).Select(_ => new Es1EquipmentEntry()).ToArray();

	/// <summary>
	/// <c>Tech_Clear</c> (<c>00001ee3</c>), <c>Tech_Read</c> (<c>00001e16</c>) and <c>Tech_Unlock</c> (<c>00001f78</c>)(state,
	/// 0), as <c>CareerRecord_Init</c> runs them on <c>bst\tech_sng.dat</c> or, in a career, <c>bst\tech_0.dat</c>: the
	/// available bytes cleared, the counts left; each of <paramref name="tech"/>'s records' counts copied into the entry its
	/// kind and index name; then every entry not yet available and holding no −1 made available when either count is 0 (the
	/// grade test passing for a mission count of 0 in either mode), its kind and index stored at <c>+0</c> and <c>+2</c>. Retail writes an index past the entries unchecked; such a record
	/// is dropped here, an engine guard. <c>Tech_Unlock</c>'s copies of the item's name into <c>0007d7e0</c> are the
	/// shell's text, not the state's.
	/// </summary>
	public void LoadTech(Es1TechFile tech) {
		foreach (var entry in Chassis.Concat(Weapons)) {
			entry.Available = 0;
		}

		foreach (var record in tech.Records) {
			var entries = record.Kind switch { 0 => Chassis, 1 => Weapons, _ => null };
			if (entries != null && record.Index >= 0 && record.Index < entries.Length) {
				entries[record.Index].GoodGradeMissions = record.GoodGradeMissions;
				entries[record.Index].AnyGradeMissions = record.AnyGradeMissions;
			}
		}

		// Tech_Unlock(state, 0): <= for a chassis, == for a weapon, which agree at 0 for counts that are not -1.
		for (short kind = 0; kind <= 1; kind++) {
			var entries = kind == 0 ? Chassis : Weapons;
			for (short i = 0; i < entries.Length; i++) {
				var entry = entries[i];
				bool holdsNoMinusOne = entry.GoodGradeMissions != -1 && entry.AnyGradeMissions != -1;
				bool reached = kind == 0
					? entry.GoodGradeMissions <= 0 || entry.AnyGradeMissions <= 0
					: entry.GoodGradeMissions == 0 || entry.AnyGradeMissions == 0;
				if (entry.Available == 0 && holdsNoMinusOne && reached) {
					entry.Available = 1;
					LastKind = kind;
					LastIndex = i;
				}
			}
		}
	}

	/// <summary><c>Tech_WriteWeaponFlags</c> (<c>00001f2b</c>)'s bytes: each weapon's available byte.</summary>
	public byte[] WeaponsAvailable() => Weapons.Select(entry => entry.Available).ToArray();
}

/// <summary>One chassis or weapon entry of an <see cref="Es1EquipmentState"/>, 5 bytes.</summary>
public class Es1EquipmentEntry {
	/// <inheritdoc cref="Es1TechRecord.GoodGradeMissions"/>
	public short GoodGradeMissions { get; set; }

	/// <inheritdoc cref="Es1TechRecord.AnyGradeMissions"/>
	public short AnyGradeMissions { get; set; }

	/// <summary>1 when the item is available.</summary>
	public byte Available { get; set; }
}

/// <summary>
/// The shell's hangar, <c>mechman.cpp</c>'s globals: the slot array <c>000808a0</c> and the header words its files carry
/// (docs/es1/formats/car-hercs-and-stock.md#the-herc-lists). Its file form is <see cref="Es1HercList"/>: read with
/// <see cref="Read"/> and written in a single mission's form with <see cref="SingleMissionSave"/>. The class is this
/// engine's bundle of those globals; every member is retail's.
/// </summary>
public class Es1Hangar {
	/// <summary>The slots a single mission's save writes, 0 to 7.</summary>
	public const int SingleMissionSlots = 8;

	/// <summary>The slot count, the word <c>00080890</c>.</summary>
	public short SlotCount { get; set; }

	/// <summary><c>Hangar_SelectedSlot</c> (<c>00080894</c>), the slot of the career record's current pilot.</summary>
	public short SelectedSlot { get; set; }

	/// <inheritdoc cref="Es1HercList.MarkedSlot"/>
	public short MarkedSlot { get; set; }

	/// <inheritdoc cref="Es1HercList.MarkedSlotWord"/>
	public short MarkedSlotWord { get; set; }

	/// <inheritdoc cref="Es1HercList.Dword"/>
	public int Dword { get; set; }

	/// <summary>The HERC count, the word <c>00080892</c>.</summary>
	public short HercCount { get; set; }

	/// <summary>The slot array <c>000808a0</c>, <see cref="SlotCount"/> long; null an empty slot.</summary>
	public Es1HercRecord?[] Slots { get; set; } = [];

	/// <summary>
	/// <c>Hangar_Read</c> (<c>00028c50</c>) over <paramref name="list"/>: the header and the count as the file holds them,
	/// each HERC copied into its slot, and <c>Herc_Read</c>'s diagram build's changes to its mounts
	/// (<see cref="Es1HercRecord.BuildDiagramMounts"/>). Retail stores a slot with no bounds test; a slot outside the array
	/// is dropped here, an engine guard. The build runs unless the byte <c>Shell_LowMemory</c> (<c>0007fd09</c>) is set,
	/// which this engine does not set.
	/// </summary>
	public static Es1Hangar Read(Es1HercList list) {
		var hangar = new Es1Hangar {
			SlotCount = list.SlotCount, SelectedSlot = list.Word1, MarkedSlot = list.MarkedSlot, MarkedSlotWord = list.MarkedSlotWord,
			Dword = list.Dword, HercCount = (short)list.Hercs.Length, Slots = new Es1HercRecord?[Math.Max((int)list.SlotCount, 0)],
		};

		foreach (var entry in list.Hercs) {
			if (entry.Slot >= 0 && entry.Slot < hangar.Slots.Length) {
				var herc = entry.Herc.Clone();
				herc.BuildDiagramMounts();
				hangar.Slots[entry.Slot] = herc;
			}
		}

		return hangar;
	}

	/// <summary>The HERC in <paramref name="slot"/>, or null for −1, an empty slot or one outside the array.</summary>
	public Es1HercRecord? HercIn(short slot) => slot >= 0 && slot < Slots.Length ? Slots[slot] : null;

	/// <summary>
	/// <c>Hangar_AddHerc</c> (<c>00029d0f</c>): <paramref name="herc"/> stored in the first empty slot
	/// (<c>Hangar_FindFreeSlot</c>, <c>0002983a</c>), its diagram's changes to its mounts made
	/// (<see cref="Es1HercRecord.BuildDiagramMounts"/>, as <see cref="Read"/>'s), the HERC count raised by 1 and the slot
	/// returned; −1 with nothing stored when no slot is empty.
	/// </summary>
	public short AddHerc(Es1HercRecord herc) {
		int slot = Array.IndexOf(Slots, null);
		if (slot < 0) {
			return -1;
		}

		Slots[slot] = herc;
		herc.BuildDiagramMounts();
		HercCount++;
		return (short)slot;
	}

	/// <summary>
	/// <c>Hangar_SlotIsUnarmed</c> (<c>0002a354</c>): false for −1 or a slot holding no HERC, otherwise
	/// <see cref="Es1HercRecord.IsUnarmed"/>.
	/// </summary>
	public bool SlotIsUnarmed(short slot) => HercIn(slot) is { } herc && herc.IsUnarmed();

	/// <summary>
	/// <c>Hangar_SlotIsDisabled</c> (<c>0002a395</c>) (docs/es1/shell/between-missions.md#next-mission): false for −1 or a
	/// slot holding no HERC, otherwise <see cref="Es1HercRecord.IsDisabled"/>.
	/// </summary>
	public bool SlotIsDisabled(short slot) => HercIn(slot) is { } herc && herc.IsDisabled();

	/// <summary>
	/// <c>Hangar_HasMarkedHerc</c> (<c>0002a277</c>) (docs/es1/shell/between-missions.md#the-bars-buttons): <see cref="MarkedSlot"/>
	/// is not −1 and holds a HERC. Retail reads the array with no bounds test; a slot outside it holds none here, an engine
	/// guard.
	/// </summary>
	public bool HasMarkedHerc() => MarkedSlot != -1 && HercIn(MarkedSlot) != null;

	/// <summary>
	/// <c>Hangar_Write</c> (<c>00029107</c>) in <c>Shell_Mode</c> 0, as <c>Hangar_SaveFile</c> (<c>0002909c</c>) writes
	/// <c>car\sm_play.mec</c>: the header with <see cref="SelectedSlot"/> as its second word, then each of slots 0 to 7 that
	/// holds a HERC, in slot order, written by <c>Herc_Write</c> (<c>000233ce</c>), which changes the HERC's mount words
	/// first (<see cref="Es1HercRecord.PrepareWrite"/>); the list holds copies. Retail writes a count of 8 whatever the
	/// slots hold; the list's count is the HERCs it holds, the same for a hangar <c>car\sm_init.dat</c> fills, and one that
	/// reads back for a hangar with an empty slot among the eight, an engine guard.
	/// </summary>
	public Es1HercList SingleMissionSave() {
		var hercs = new List<Es1HercListEntry>();
		for (short slot = 0; slot < SingleMissionSlots; slot++) {
			if (HercIn(slot) is { } herc) {
				herc.PrepareWrite();
				hercs.Add(new Es1HercListEntry { Slot = slot, Herc = herc.Clone() });
			}
		}

		return new Es1HercList {
			SlotCount = SlotCount, Word1 = SelectedSlot, MarkedSlot = MarkedSlot, MarkedSlotWord = MarkedSlotWord, Dword = Dword,
			Hercs = hercs.ToArray(),
		};
	}

	/// <summary>
	/// <c>Hangar_Write</c> (<c>00029107</c>) outside <c>Shell_Mode</c> 0, as a career's files take it
	/// (docs/es1/shell/career.md#the-files): the header with <see cref="SelectedSlot"/> as its second word, then each slot
	/// below <see cref="SlotCount"/> that holds a HERC, in slot order, its HERC's mount words changed first
	/// (<see cref="Es1HercRecord.PrepareWrite"/>); the list holds copies. Retail writes <see cref="HercCount"/> as the count;
	/// the list's count is the HERCs it holds, the same while <see cref="Read"/> and <see cref="AddHerc"/> keep the count,
	/// an engine guard that keeps the file readable.
	/// </summary>
	public Es1HercList CareerSave() {
		var hercs = new List<Es1HercListEntry>();
		for (short slot = 0; slot < SlotCount && slot < Slots.Length; slot++) {
			if (HercIn(slot) is { } herc) {
				herc.PrepareWrite();
				hercs.Add(new Es1HercListEntry { Slot = slot, Herc = herc.Clone() });
			}
		}

		return new Es1HercList {
			SlotCount = SlotCount, Word1 = SelectedSlot, MarkedSlot = MarkedSlot, MarkedSlotWord = MarkedSlotWord, Dword = Dword,
			Hercs = hercs.ToArray(),
		};
	}
}
