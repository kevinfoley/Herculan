namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - ES1's <c>car\CAR_INIT.DAT</c> (a new career) and <c>car\SM_INIT.DAT</c> (a single mission) in <c>SHELL0.VOL</c>: the
/// HERCs the player starts with, five header fields and a counted list of HERCs each with the hangar slot it goes in
/// (docs/es1/formats/car-hercs-and-stock.md#the-herc-lists). Read by <see cref="Io.Transform.Shell.Es1HercListTransformer"/>.
/// </summary>
public class Es1HercList {
	/// <summary>The number of hangar slots, the word <c>ES1GO 00080890</c>.</summary>
	public short SlotCount { get; set; }

	/// <summary>
	/// The word read into <c>ES1GO 00080894</c>, <c>Hangar_SelectedSlot</c> (<see cref="Es1Hangar.SelectedSlot"/>), which
	/// <c>Hangar_Write</c> writes back (docs/es1/formats/car-hercs-and-stock.md#the-herc-lists).
	/// </summary>
	public short Word1 { get; set; }

	/// <summary>The slot whose HERC is read with <see cref="MarkedSlotWord"/>, the word <c>ES1GO 00080896</c>; −1 in both retail files.</summary>
	public short MarkedSlot { get; set; }

	/// <summary>The word passed to the picture build of <see cref="MarkedSlot"/>'s HERC when it is not 0, <c>ES1GO 00080898</c>.</summary>
	public short MarkedSlotWord { get; set; }

	/// <summary>The dword read into <c>ES1GO 0008089c</c>.</summary>
	public int Dword { get; set; }

	/// <summary>The HERCs, in file order; the count word is their number.</summary>
	public Es1HercListEntry[] Hercs { get; set; } = [];
}

/// <summary>One HERC of an <see cref="Es1HercList"/> and its hangar slot.</summary>
public class Es1HercListEntry {
	/// <summary>The hangar slot, an index into <see cref="Es1HercList.SlotCount"/> slots with no bounds test.</summary>
	public short Slot { get; set; }

	public Es1HercRecord Herc { get; set; } = new();
}

/// <summary>
/// A HERC as <c>Herc_ReadFromStream</c> (<c>ES1GO 0002352f</c>) reads it and <c>Herc_Write</c> (<c>000233ce</c>) writes it
/// (docs/es1/formats/car-hercs-and-stock.md#the-herc-record).
/// </summary>
public class Es1HercRecord {
	/// <summary>The status block's length in <c>int16</c>, 66 bytes.</summary>
	public const int StatusLength = 33;

	/// <summary>The chassis, 0–7.</summary>
	public short Chassis { get; set; }

	/// <summary>Per mount, the weapon it carries; 17 (<c>0x11</c>) is an empty mount. The count word is their number.</summary>
	public short[] Weapons { get; set; } = [];

	/// <summary>Per mount, the word read after the weapons (docs/es1/formats/car-hercs-and-stock.md#open).</summary>
	public short[] MountWords { get; set; } = [];

	/// <summary>The word at the HERC's <c>+0x2c</c>; the constructor sets 2.</summary>
	public short Word { get; set; }

	/// <summary>The 66-byte block at the HERC's <c>+0x2e</c>, <see cref="StatusLength"/> <c>int16</c>.</summary>
	public short[] Status { get; set; } = new short[StatusLength];

	/// <summary>The weapon an empty mount carries, <c>0x11</c>.</summary>
	public const short EmptyMount = 17;

	/// <summary>The status word every write sets in a single mission, and the value of every word <c>HercStatus_Ctor</c> sets.</summary>
	public const short FullStatus = 100;

	/// <summary><see cref="Status"/>' index of internal word 9 (<c>+0x5a</c> in the HERC), which carries the pilot's condition.</summary>
	public const int PilotConditionWord = 13 + 9;

	/// <summary><see cref="Status"/>' index of the first per-mount word (<c>+0x5c</c> in the HERC).</summary>
	public const int MountStatusWords = 13 + 10;

	/// <summary>The per-mount words the block has room for.</summary>
	private const int MountStatusCount = 10;

	/// <summary>Internal word 9, the pilot's condition: <c>Herc_GetStatus</c> (<c>00024b86</c>)(<i>h</i>, 9, <c>0x784</c>) and <c>Herc_SetStatus</c> (<c>00024c14</c>).</summary>
	public short PilotCondition {
		get => Status[PilotConditionWord];
		set => Status[PilotConditionWord] = value;
	}

	/// <summary>
	/// <c>Herc_SetAllStatus</c> (<c>00024c3c</c>)(<i>h</i>, <paramref name="value"/>): the external words, internal words 0
	/// to 8 and the per-mount words of the mounts below the mount count set to the value, or to 100 for an empty mount;
	/// internal word 9 left as it is.
	/// </summary>
	public void SetAllStatus(short value) {
		for (int word = 0; word < MountStatusWords; word++) {
			if (word != PilotConditionWord) {
				Status[word] = value;
			}
		}

		for (int mount = 0; mount < Weapons.Length && mount < MountStatusCount; mount++) {
			Status[MountStatusWords + mount] = Weapons[mount] == EmptyMount ? FullStatus : value;
		}
	}

	/// <summary>
	/// <c>Herc_IsUnarmed</c> (<c>00022ebf</c>): every mount below the mount count carries <see cref="EmptyMount"/>, so also
	/// a HERC with no mounts.
	/// </summary>
	public bool IsUnarmed() => Weapons.All(weapon => weapon == EmptyMount);

	/// <summary><see cref="Status"/>' index of internal word 0, the first after the 13 external words.</summary>
	private const int FirstInternalWord = 13;

	/// <summary>The condition below which <see cref="IsDisabled"/> counts an internal word as disabled, 50 (<c>0x32</c>).</summary>
	private const short DisabledBelow = 50;

	/// <summary>
	/// <c>Herc_IsDisabled</c> (<c>00022f05</c>) (docs/es1/shell/between-missions.md#next-mission): internal word 0, 1 or 5
	/// (<c>Herc_GetStatus</c> (<c>00024b86</c>)(<i>h</i>, <i>i</i>, <c>0x784</c>)) below 50.
	/// </summary>
	public bool IsDisabled() =>
		Status[FirstInternalWord] < DisabledBelow || Status[FirstInternalWord + 1] < DisabledBelow || Status[FirstInternalWord + 5] < DisabledBelow;

	/// <summary>
	/// What <c>Herc_Write</c> (<c>000233ce</c>) does to the HERC before it writes the mount words: the word of each mount
	/// whose weapon is 9, 10 or 11 and whose word is 5 set to 0.
	/// </summary>
	public void PrepareWrite() {
		for (int mount = 0; mount < Weapons.Length; mount++) {
			if (Weapons[mount] is >= 9 and <= 11 && MountWords[mount] == 5) {
				MountWords[mount] = 0;
			}
		}
	}

	/// <summary>
	/// What <c>Herc_BuildDiagram</c> (<c>00023883</c>) does to the HERC's record as it builds the diagram, per mount below
	/// the mount count: a mount whose status word is 0 (<c>Herc_GetMountStatus</c>, <c>00024b23</c>) made empty, its
	/// status word set back to 100 (<c>Herc_SetStatus</c>(<i>h</i>, 100, mount, <c>0x786</c>)); and the word of a mount
	/// whose weapon is 9, 10 or 11 and whose word is 5 set to 0. The diagram itself is the shell's
	/// (docs/es1/formats/herc-diagrams.md#the-mount-pictures).
	/// </summary>
	public void BuildDiagramMounts() {
		for (int mount = 0; mount < Weapons.Length; mount++) {
			if (mount < MountStatusCount && Status[MountStatusWords + mount] == 0) {
				Weapons[mount] = EmptyMount;
				Status[MountStatusWords + mount] = FullStatus;
			}

			if (Weapons[mount] is >= 9 and <= 11 && MountWords[mount] == 5) {
				MountWords[mount] = 0;
			}
		}
	}

	/// <summary>A copy of the record, its arrays copied.</summary>
	public Es1HercRecord Clone() => new() {
		Chassis = Chassis,
		Weapons = (short[])Weapons.Clone(),
		MountWords = (short[])MountWords.Clone(),
		Word = Word,
		Status = (short[])Status.Clone(),
	};
}

/// <summary>
/// FILE - ES1's <c>car\C_ARMORY.DAT</c> (a new career) and <c>car\S_ARMORY.DAT</c> (a single mission) in <c>SHELL0.VOL</c>: the
/// weapon stock, a counted list of records each pushing words onto one slot's stack
/// (docs/es1/formats/car-hercs-and-stock.md#the-weapon-stock). Read by <see cref="Io.Transform.Shell.Es1InventoryTransformer"/>.
/// </summary>
public class Es1Inventory {
	public Es1InventoryRecord[] Records { get; set; } = [];
}

/// <summary>One record of an <see cref="Es1Inventory"/>.</summary>
public class Es1InventoryRecord {
	/// <summary>The slot, with no bounds test.</summary>
	public short Slot { get; set; }

	/// <summary>The words pushed onto the slot's stack, in file order, so the last is its head; 100 in every retail record.</summary>
	public short[] Values { get; set; } = [];
}

/// <summary>
/// A weapon stock as the shell holds it, one of the 41 the dwords from <c>ES1GO 000aad62</c> point to, which
/// <c>00027c4c</c> makes at the start-up with 17 slots each (<c>00027d0d</c>(stock, <c>0x11</c>)): per slot a stack of words
/// (docs/es1/formats/car-hercs-and-stock.md#the-weapon-stock). Its file form is <see cref="Es1Inventory"/>, read with
/// <see cref="Read"/> and written with <see cref="ToFile"/>. The class is this engine's form of the linked stacks; every
/// member is retail's.
/// </summary>
public class Es1WeaponStock {
	/// <summary>The slots <c>00027c4c</c> gives each stock.</summary>
	public const int SlotCount = 17;

	/// <summary>Per slot, its stack's words from the head.</summary>
	public List<short>[] Slots { get; } = Enumerable.Range(0, SlotCount).Select(_ => new List<short>()).ToArray();

	/// <summary>
	/// <c>Inventory_Read</c> (<c>00027e3c</c>) of <paramref name="file"/>: every stack emptied with <c>WordStack_Clear</c>
	/// (<see cref="ClearStack"/>), then each record's words pushed onto its slot's stack in file order, so the last is the
	/// head. Retail pushes with no bounds test on the slot; a record outside the 17 is dropped here, an engine guard.
	/// <c>Inventory_TraceSlots</c>, before and after, calls a function that does nothing.
	/// </summary>
	public void Read(Es1Inventory file) {
		foreach (var stack in Slots) {
			ClearStack(stack);
		}

		foreach (var record in file.Records) {
			if (record.Slot is >= 0 and < SlotCount) {
				foreach (short value in record.Values) {
					Slots[record.Slot].Insert(0, value);
				}
			}
		}
	}

	/// <summary>
	/// <c>Inventory_Write</c> (<c>00027f88</c>)'s file: the slot count of records, every slot in order with its words from
	/// the head (<c>WordStack_Write</c>, <c>00040ca2</c>), so a write read back reverses each slot's words.
	/// </summary>
	public Es1Inventory ToFile() => new() {
		Records = Slots.Select((stack, slot) => new Es1InventoryRecord { Slot = (short)slot, Values = stack.ToArray() }).ToArray(),
	};

	/// <summary>
	/// <c>WordStack_Clear</c> (<c>00040d19</c>): pops until a pop returns −1, which <c>WordStack_Pop</c> (<c>00040bfd</c>)
	/// returns for an empty stack, so a stored −1 also ends it and leaves the words under it.
	/// </summary>
	private static void ClearStack(List<short> stack) {
		while (stack.Count > 0) {
			short word = stack[0];
			stack.RemoveAt(0);
			if (word == -1) {
				return;
			}
		}
	}
}

/// <summary>
/// FILE - ES1's <c>car\CAR_</c><i>n</i><c>.CAR</c>, a career file in the install: the roster's header and its 41 career
/// records, each followed by its weapon stock (docs/es1/shell/career.md#the-files). Read by
/// <see cref="Io.Transform.Shell.Es1CareerFileTransformer"/>.
/// </summary>
public class Es1CareerFile {
	/// <summary>The records a roster holds: the career and its 40 backups.</summary>
	public const int RecordCount = 41;

	/// <summary>The name groups, three rows of four.</summary>
	public const int NameGroupCount = 12;

	/// <summary>The roster's byte <c>+0</c>.</summary>
	public byte Byte0 { get; set; }

	/// <summary>The roster's word <c>+5</c>, the battle set.</summary>
	public short BattleSet { get; set; }

	/// <summary>The roster's 12 words from <c>+0xb</c>, the squadmates' name groups of <c>car\names.dat</c>.</summary>
	public short[] NameGroups { get; set; } = new short[NameGroupCount];

	/// <summary>The 41 records, record 0 the career.</summary>
	public Es1CareerFileRecord[] Records { get; set; } = [];

	/// <summary>
	/// Bytes after the 41 records, which <c>Career_LoadCurrent</c> (<c>ES1GO 00016c1e</c>) does not read: what a longer
	/// earlier file left, since <c>Career_New</c>'s <c>FileRWStream</c> does not truncate. Empty in a file
	/// <c>Career_Save</c> wrote, which deletes the file first.
	/// </summary>
	public byte[] Tail { get; set; } = [];
}

/// <summary>One record of an <see cref="Es1CareerFile"/>: a <c>0x1f7</c>-byte career record and the stock after it.</summary>
public class Es1CareerFileRecord {
	public Es1CareerRecord Record { get; set; } = new();

	/// <summary>The record's weapon stock, as <c>Inventory_Write</c> writes it.</summary>
	public Es1Inventory Stock { get; set; } = new();
}

/// <summary>
/// FILE - ES1's <c>car\CAR_</c><i>n</i><c>.MEC</c>, a career's hangar file in the install: a hangar list for each of the
/// roster's 41 records, list <i>n</i> record <i>n</i>'s (docs/es1/shell/career.md#the-files). Read by
/// <see cref="Io.Transform.Shell.Es1CareerHangarFileTransformer"/>.
/// </summary>
public class Es1CareerHangarFile {
	/// <summary>The 41 lists.</summary>
	public Es1HercList[] Lists { get; set; } = [];

	/// <summary>
	/// Bytes after the 41 lists, which no reader reaches: what a longer earlier file left, since <c>Career_New</c>'s
	/// <c>FileRWStream</c> does not truncate.
	/// </summary>
	public byte[] Tail { get; set; } = [];
}

/// <summary>
/// FILE - ES1's <c>car\NAMES.LOG</c> in <c>SHELL0.VOL</c>: the careers log, a count, the current entry and that many
/// 100-byte entries each naming a career and its two files (docs/es1/formats/career-log-and-player-mec.md#the-careers-log).
/// Read by <see cref="Io.Transform.Shell.Es1CareerLogTransformer"/>.
/// </summary>
public class Es1CareerLog {
	/// <summary>
	/// The entry the log selects, written only when there are entries; <c>CareerLog_Load</c> (<c>ES1GO 00017e7f</c>) sets −1
	/// when there are none, and the parse does the same.
	/// </summary>
	public short Current { get; set; } = -1;

	/// <summary>The entries, in file order; the count word is their number.</summary>
	public Es1CareerLogEntry[] Entries { get; set; } = [];
}

/// <summary>
/// One 100-byte entry of an <see cref="Es1CareerLog"/>, written from memory whole: each name is NUL-terminated in its
/// buffer, and the buffer's bytes after the NUL are kept as read.
/// </summary>
public class Es1CareerLogEntry {
	/// <summary>An entry's length.</summary>
	public const int Length = 100;

	/// <summary>The career name's buffer length.</summary>
	public const int NameLength = 0x40;

	/// <summary>Each file path's buffer length.</summary>
	public const int PathLength = 17;

	/// <summary>The career's name, <c>+0x00</c>.</summary>
	public byte[] Name { get; set; } = new byte[NameLength];

	/// <summary>The word <c>+0x40</c>, which <c>CareerLog_FindOrAdd</c> (<c>ES1GO 00018099</c>) matches with the name.</summary>
	public short Word { get; set; }

	/// <summary>A NUL-terminated buffer's text up to its NUL, or the whole buffer with none.</summary>
	public static string Text(byte[] buffer) {
		int end = Array.IndexOf(buffer, (byte)0);
		return System.Text.Encoding.Latin1.GetString(buffer, 0, end < 0 ? buffer.Length : end);
	}

	/// <summary>The career file's path, <c>CAR\CAR_</c><i>n</i><c>.CAR</c>, <c>+0x42</c>.</summary>
	public byte[] CareerFile { get; set; } = new byte[PathLength];

	/// <summary>The hangar file's path, <c>CAR\CAR_</c><i>n</i><c>.MEC</c>, <c>+0x53</c>.</summary>
	public byte[] HangarFile { get; set; } = new byte[PathLength];
}

/// <summary>
/// FILE - ES1's <c>car\NAMES.DAT</c> in <c>SHELL0.VOL</c>: a count byte of groups, each a count byte of pilot names
/// (docs/es1/formats/bst-battle-sets.md#battle-sets). Read by <see cref="Io.Transform.Shell.Es1PilotNamesTransformer"/>.
/// </summary>
public class Es1PilotNames {
	/// <summary>The longest name, its NUL included: <c>PilotNames_Load</c> (<c>ES1GO 00015070</c>) reads each into 16 bytes.</summary>
	public const int NameBufferLength = 16;

	/// <summary>The groups, each its names in file order.</summary>
	public string[][] Groups { get; set; } = [];
}
