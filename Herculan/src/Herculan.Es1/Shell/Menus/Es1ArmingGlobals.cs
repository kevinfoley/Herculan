using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dyn;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// ARMING's statics, which keep their values from one visit to the next (docs/es1/shell/arming.md#entering-it), and the
/// weapon list's functions (docs/es1/shell/arming.md#the-weapon-list): <c>Arming_BuildWeaponList</c> and its two helpers,
/// with the word lists <c>Menu_ListLines</c> gives the window's lists. The class is this engine's bundle of those
/// globals, one per menu context (<see cref="Es1MenuContext.Arming"/>); every member is retail's.
/// </summary>
public sealed class Es1ArmingGlobals {
	/// <summary>The weapon an empty mount carries, 17, the tables' last row before the end.</summary>
	public const short EmptyMount = Es1HercRecord.EmptyMount;

	/// <summary>The weapons the tables hold a line for, 0 to 17.</summary>
	private const int TableRows = 18;

	/// <summary>
	/// <c>Arming_CareerWeaponLines</c> (<c>00081274</c>): per weapon 0 to 16 its <c>eng\watext.bin</c> line in a career,
	/// <c>0x2e</c> + the weapon, the bracketed lines whose count <see cref="FormatCounts"/> writes, and 17's, <c>0x2d</c>
	/// NONE.
	/// </summary>
	private static readonly short[] CareerWeaponLines = [
		0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x2d,
	];

	/// <summary>
	/// <c>Arming_SingleWeaponLines</c> (<c>0008129c</c>): per weapon its line in a single mission, <c>0x47</c> to
	/// <c>0x54</c> for 0 to 13, the career's lines <c>0x3c</c> to <c>0x3e</c> for 14 to 16, and <c>0x2d</c> for 17.
	/// </summary>
	private static readonly short[] SingleWeaponLines = [
		0x47, 0x48, 0x49, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53, 0x54, 0x3c, 0x3d, 0x3e, 0x2d,
	];

	/// <summary>
	/// The word before each table, which <see cref="WeaponListPosition"/> reads for weapon −1: <c>00081272</c>, the −1
	/// ending the SINGLE MISSION menu's list <c>00081264</c>, before the career table, and <c>0008129a</c>, 0, before the
	/// single mission's.
	/// </summary>
	private const short CareerLineBefore = -1, SingleLineBefore = 0;

	/// <summary>The list 1809's words, <c>000812c4</c>: SARH, ARH, ARM and EO, the missile types 0 to 3.</summary>
	public static readonly short[] MissileTypeLines = [0x40, 0x41, 0x42, 0x44, -1];

	/// <summary>The list 1903's words, <c>00081250</c>: the eight chassis' names.</summary>
	public static readonly short[] ChassisLines = [0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, -1];

	/// <summary>The list 1904's words, <c>000811e8</c>: <c>0x55</c> PLAYER, the 36 squadmates' names and <c>0x7a</c> UNASSIGNED.</summary>
	public static readonly short[] PilotLines = [.. Enumerable.Range(0x55, 0x7a - 0x55 + 1).Select(line => (short)line), -1];

	/// <summary>The words <see cref="ListLines"/> has room for here; retail's static runs on unchecked.</summary>
	private const int ListRoom = 0x20;

	/// <summary>
	/// <c>Arming_ListLines</c> (<c>000ab30c</c>), zeroes in the image: the line numbers <see cref="BuildWeaponList"/>
	/// last wrote, ended by −1, with the words past the end as earlier builds left them, which the two position lookups
	/// read.
	/// </summary>
	public short[] ListLines { get; } = new short[ListRoom];

	/// <summary><c>Arming_ShownWeapon</c> (<c>00080d64</c>), 0 in the image: the weapon the panel describes.</summary>
	public short ShownWeapon { get; set; }

	/// <summary><c>Arming_ShownMissileType</c> (<c>00080d66</c>), 0 in the image: the missile type the panel describes.</summary>
	public short ShownMissileType { get; set; }

	/// <summary><c>Arming_AnimatedWeapon</c> (<c>00080d68</c>), 0 in the image: the weapon last shown, against which a pick is a change.</summary>
	public short AnimatedWeapon { get; set; }

	/// <summary><c>Arming_LowMemPictures</c> (<c>00080d6c</c>): <c>res32\allweap.dba</c> while memory is low, none otherwise.</summary>
	public IReadOnlyList<DynamixBitmap>? LowMemPictures { get; set; }

	/// <summary><c>Arming_WeaponLines</c> (<c>00080d60</c>): the lines <c>Arming_SelectWeapon</c> and <c>Arming_Refresh</c> last gave the list.</summary>
	public string?[]? WeaponLines { get; set; }

	/// <summary>
	/// The byte <c>Shell_LowMemory</c> (<c>0007fd09</c>), which the start-up sets when the heap is short; this engine's
	/// memory is never that short, so it stays clear (<see cref="Es1ShellHost"/>'s step 6).
	/// </summary>
	public static bool LowMemory => false;

	/// <summary>The table <see cref="WeaponListPosition"/> and the others read: the single mission's in <c>Shell_Mode</c> 0, the career's in any other.</summary>
	private static short[] Table(short mode) => mode == 0 ? SingleWeaponLines : CareerWeaponLines;

	/// <summary>The table's word for <paramref name="weapon"/>, the word before it for −1; retail reads further outside unchecked, which reads 0 here, an engine guard.</summary>
	private static short LineOf(short mode, short weapon) {
		if (weapon == -1) {
			return mode == 0 ? SingleLineBefore : CareerLineBefore;
		}

		return weapon >= 0 && weapon < TableRows ? Table(mode)[weapon] : (short)0;
	}

	/// <summary>
	/// <c>Arming_WeaponListPosition</c> (<c>000370ec</c>)(<paramref name="weapon"/>): the first <i>i</i> below 17 whose
	/// word of <see cref="ListLines"/> is not 0 and equals the weapon's line, or 17. The words past the list's end take
	/// part as they stand.
	/// </summary>
	public short WeaponListPosition(short mode, short weapon) {
		short line = LineOf(mode, weapon);
		for (short i = 0; i < EmptyMount; i++) {
			if (ListLines[i] != 0 && ListLines[i] == line) {
				return i;
			}
		}

		return EmptyMount;
	}

	/// <summary>
	/// <c>Arming_WeaponAtListPosition</c> (<c>0003714e</c>)(<paramref name="position"/>): the first weapon 0 to 17 whose
	/// line equals word <paramref name="position"/> of <see cref="ListLines"/>, or 17. Retail reads a position past the
	/// list's room unchecked; such a position answers 17 here, an engine guard.
	/// </summary>
	public short WeaponAtListPosition(short mode, short position) {
		if (position < 0 || position >= ListLines.Length) {
			return EmptyMount;
		}

		short[] table = Table(mode);
		for (short weapon = 0; weapon < TableRows; weapon++) {
			if (ListLines[position] == table[weapon]) {
				return weapon;
			}
		}

		return EmptyMount;
	}

	/// <summary>
	/// <c>Arming_BuildWeaponList</c> (<c>000371ad</c>)(<paramref name="mount"/>, <paramref name="chassis"/>)
	/// (docs/es1/shell/arming.md#the-weapon-list): <see cref="ListLines"/> filled from the mode's table and ended with −1,
	/// and returned. A weapon is offered when <c>CareerRecord_Current</c>'s equipment state makes it available
	/// (<c>00037383</c>, a byte-for-byte copy of <c>Tech_IsAvailable</c>, <see cref="Es1EquipmentState.IsAvailable"/>) or
	/// its stack in the stock <c>[000aad62]</c> is not empty (<see cref="Es1WeaponStock.CountIn"/>). For mount −1 each
	/// weapon 0 to 16 offered; otherwise 17's line, NONE, then for the chassis' layout records of the mount from the second
	/// on, the record's weapon when it is offered or is the selected HERC's current mount's weapon (−1 with none). With no
	/// record nothing is available, a layout the tables lack has no records, and the list stops at its room, engine
	/// guards.
	/// </summary>
	public short[] BuildWeaponList(Es1MenuContext context, short mount, short chassis) {
		var mission = context.Mission;
		var equipment = mission.CurrentRecord?.Equipment;
		var stock = mission.Roster.Stocks[0];
		short[] table = Table(mission.Mode);
		int count = 0;
		bool Offered(short weapon) => (equipment?.IsAvailable(weapon, 1) ?? 0) != 0 || stock.CountIn(weapon) != 0;
		void Add(short weapon) {
			if (count < ListLines.Length - 1) {
				ListLines[count++] = weapon >= 0 && weapon < TableRows ? table[weapon] : (short)0;
			}
		}

		if (mount == -1) {
			for (short weapon = 0; weapon < EmptyMount; weapon++) {
				if (Offered(weapon)) {
					Add(weapon);
				}
			}
		} else {
			Add(EmptyMount);
			var layout = chassis is >= 0 and < Hangar.Es1HercArt.ChassisCount ? context.Data.HercDiagrams.Art.Layouts[chassis] : null;
			var records = layout != null && mount >= 0 && mount < layout.Mounts.Length ? layout.Mounts[mount].Items : [];
			for (int record = 1; record < records.Length; record++) {
				short weapon = records[record].Weapon;
				if (Offered(weapon) || weapon == SelectedMountWeapon(context)) {
					Add(weapon);
				}
			}
		}

		ListLines[count] = -1;
		return ListLines;
	}

	/// <summary><c>Herc_CurrentMountWeapon</c> of the HERC in <c>Hangar_SelectedSlot</c>, −1 with none.</summary>
	private static short SelectedMountWeapon(Es1MenuContext context) =>
		context.Screens.Bay is { } bay && bay.SelectedHerc is { } herc ? bay.Diagrams.CurrentMountWeapon(herc) : (short)-1;

	/// <summary>
	/// <c>Inventory_FormatCounts</c> (<c>00028242</c>)(the stock <c>[000aad62]</c>, <paramref name="lines"/>)
	/// (<see cref="Es1WeaponStock.FormatCounts"/>), each line's weapon <see cref="WeaponAtListPosition"/>. Retail's lines
	/// are the text table's own, so a count stays written in <c>eng\watext.bin</c>'s line until the next; here each
	/// changed line is written back to <see cref="Es1ShellData.Text"/> at the number <see cref="ListLines"/> gives it.
	/// </summary>
	public void FormatCounts(Es1MenuContext context, string?[] lines) {
		short mode = context.Mission.Mode;
		context.Mission.Roster.Stocks[0].FormatCounts(mode, lines, position => WeaponAtListPosition(mode, position));
		if (context.Data.Text is not { } text) {
			return;
		}

		for (int i = 0; i < lines.Length && i < ListLines.Length && lines[i] is { } line; i++) {
			short number = ListLines[i];
			if (number >= 0 && number < text.Length) {
				text[number] = line;
			}
		}
	}
}
