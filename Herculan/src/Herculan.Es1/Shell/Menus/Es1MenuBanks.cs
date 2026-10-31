using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The bitmap arrays the menus' picture tiles and CREW's hotspots draw from, retail's statics: the build's banks
/// <c>000813d4</c>, <c>000813d8</c> and <c>000813dc</c>, which <see cref="ForControl"/> loads and <see cref="Free"/>
/// releases, and CREW's <c>CrewAssignMenu_Portraits</c> (<c>0007ebcc</c>) and <c>CrewAssignMenu_Mesh</c>
/// (<c>0007ebd0</c>) (docs/es1/shell/hangar.md#the-screen). The class is this engine's bundle of those globals, one per
/// menu context; every member is retail's. A bank is a <c>.DBA</c>'s frames, none when the file does not read.
/// </summary>
public sealed class Es1MenuBanks {
	/// <summary>The kind-16 ids <see cref="ForControl"/> loads <c>res32\insignia.dba</c> for: CREW's insignia tile.</summary>
	private const short InsigniaTile = 1611;

	/// <summary>The kind-16 ids <see cref="ForControl"/> loads <see cref="Mechs"/> for: CREW's four HERC tiles.</summary>
	private const short FirstHercTile = 1615, LastHercTile = 1618;

	/// <summary>The id <see cref="ForControl"/> loads <c>res32\allweap.dba</c> for: ARMING's animation, 1803.</summary>
	private const short ArmingAnimation = 1803;

	/// <summary>The kind-16 ids <see cref="ForControl"/> loads <c>res32\check.dba</c> for: PREFERENCES' three tiles, 1914 to 1916.</summary>
	private const short FirstCheckTile = Es1RepairPrefsAlert.FirstTile, LastCheckTile = Es1RepairPrefsAlert.LastTile;

	private readonly GameContent _content;

	public Es1MenuBanks(GameContent content) {
		_content = content;
	}

	/// <summary>
	/// The dword <c>000813d4</c>, none in the image: the bank the first of <see cref="ForControl"/>'s cases for 1611, 1803
	/// and 1914 to 1916 loaded, <c>res32\insignia.dba</c>, <c>res32\allweap.dba</c> or <c>res32\check.dba</c>; each of those
	/// cases loads its file only while the dword is empty and otherwise returns what it holds.
	/// </summary>
	public IReadOnlyList<DynamixBitmap>? SharedBank { get; private set; }

	/// <summary>The dword <c>000813dc</c>, <c>res32\mechs.dba</c> once <see cref="ForControl"/> has loaded it; none in the image.</summary>
	public IReadOnlyList<DynamixBitmap>? Mechs { get; private set; }

	/// <summary><c>CrewAssignMenu_Portraits</c> (<c>0007ebcc</c>), <c>res32\pilots.dba</c> from CREW's opening to its closing (<see cref="Es1CrewAssignMenu"/>).</summary>
	public IReadOnlyList<DynamixBitmap>? CrewPortraits { get; set; }

	/// <summary><c>CrewAssignMenu_Mesh</c> (<c>0007ebd0</c>), <c>res32\mesh.dba</c> from CREW's opening to its closing.</summary>
	public IReadOnlyList<DynamixBitmap>? CrewMesh { get; set; }

	/// <summary>
	/// <c>Resource_LoadBitmapArray</c> (<c>0004b7df</c>) of <c>res32\</c><paramref name="name"/>: the file's frames. A file
	/// that does not read gives none here; what retail's load returns then is dlg-menu-dialogs.md's Open.
	/// </summary>
	public IReadOnlyList<DynamixBitmap>? Load(string name) =>
		_content.Read("res32", name) is { } bytes ? new DynamixBitmapArrayTransformer().Parse(bytes)?.Images : null;

	/// <summary>
	/// <c>00037827</c>(<paramref name="id"/>), the bank <c>Menu_BuildFromDlg</c> hands a kind-16 control and ARMING's case
	/// takes while memory is low: the id looked up among the eleven at <c>0008144c</c> (targets at <c>00081478</c>); 1611
	/// (<c>0003784a</c>) <c>res32\insignia.dba</c>, 1803 (<c>000378d6</c>) <c>res32\allweap.dba</c> and 1914 to 1916
	/// (<c>000378f9</c>) <c>res32\check.dba</c> into <see cref="SharedBank"/>, 1615 to 1618 (<c>0003786d</c>)
	/// <see cref="Mechs"/>, each loaded when its dword is empty and then the dword returned as it stands; any id outside the
	/// eleven none, the <c>XOR EAX,EAX</c> at <c>0003791c</c>.
	/// </summary>
	public IReadOnlyList<DynamixBitmap>? ForControl(short id) {
		if (id == InsigniaTile) {
			return SharedBank ??= Load("INSIGNIA.DBA");
		}

		if (id == ArmingAnimation) {
			return SharedBank ??= Load("ALLWEAP.DBA");
		}

		if (id is >= FirstCheckTile and <= LastCheckTile) {
			return SharedBank ??= Load("CHECK.DBA");
		}

		if (id is >= FirstHercTile and <= LastHercTile) {
			return Mechs ??= Load("MECHS.DBA");
		}

		// PLACEHOLDER: the two other ids at 0008144c, 1321 (00037890, res32\controls.dba) and 1316 (000378b3,
		// res32\bluebar.dba), PREFERENCES', each loading its file into 000813d4 as 1611's case does; their menu is the
		// preferences and controls module's, after S5, and they get none here.
		return null;
	}

	/// <summary>Whether <see cref="ForControl"/> gives a bank of its own for <paramref name="id"/>, the ids whose cases are ported.</summary>
	public static bool HasBankFor(short id) =>
		id is InsigniaTile or ArmingAnimation || id is >= FirstHercTile and <= LastHercTile || id is >= FirstCheckTile and <= LastCheckTile;

	/// <summary>
	/// <c>Shell_FreeBitmapArraySlot</c> (<c>00037924</c>)(<paramref name="bank"/>): the first of <c>000813d4</c>,
	/// <c>000813d8</c> and <c>000813dc</c> that holds the bank destroyed (<c>GLBitmapArray_Dtor</c>(bank, 3)) and cleared;
	/// any other bank left. So the first of the four HERC tiles destroyed frees <see cref="Mechs"/> and the next ones find
	/// it cleared. None of <see cref="ForControl"/>'s cases loads <c>000813d8</c>, which holds none here.
	/// </summary>
	public void Free(IReadOnlyList<DynamixBitmap>? bank) {
		if (bank == null) {
			return;
		}

		if (ReferenceEquals(SharedBank, bank)) {
			SharedBank = null;
		} else if (ReferenceEquals(Mechs, bank)) {
			Mechs = null;
		}
	}
}
