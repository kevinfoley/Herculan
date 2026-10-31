using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// The full-screen picture behind every menu, with its own palette and music, one for each group of screens
/// (docs/es1/shell/menus.md#the-backdrop): the object <c>Shell_Backdrop</c> (<c>000a09a0</c>), its picture
/// <c>ShellBackdrop_Picture</c> (<c>000aa7ac</c>) and the palette <c>0007fcd0</c>, which the next frame uploads.
/// </summary>
public sealed class Es1ShellBackdrop {
	/// <summary>The background page, the word <c>0007fccc</c>, 2 in the image.</summary>
	public const int BackgroundPage = 2;

	/// <summary>The music track of each group, 1000 to 8000: the eight words at <c>000817d0</c>.</summary>
	private static readonly short[] GroupTracks = [1, 1, 3, 4, 3, 3, 1, 0];

	/// <summary>
	/// The ten words at <c>0007ea94</c>, one per campaign, that groups 3000 and 5000 add to their number, and that
	/// <c>BattleSet_ReadFile</c> gives each campaign of a battle set as its <c>+0</c> word, the <i>k</i> of its talking-head
	/// files (docs/es1/formats/bst-battle-sets.md#battle-sets).
	/// </summary>
	internal static readonly short[] CampaignWords = [0, 1, 2, 3, 1, 2, 3, 1, 2, 3];

	/// <summary>
	/// <c>CareerRecord_Current</c> (<see cref="Es1ShellMission.CurrentRecord"/>), which the shell's start-up hands the
	/// backdrop once the mission globals exist; none before, and none in mode 0 before a single mission's record is made,
	/// which read as retail's zeroed records' campaign 0.
	/// </summary>
	public Func<HercWorks.Core.Data.File.Dat.Shell.Es1CareerRecord?>? CurrentRecord { get; set; }

	/// <summary>
	/// The word at <c>0007ea94</c> + 2 × <c>CareerRecord_Current</c>'s campaign <c>+6</c> (docs/es1/shell/menus.md#the-backdrop).
	/// Retail indexes the ten words with no bounds test; a campaign outside them reads 0 here, an engine guard.
	/// </summary>
	internal short CampaignWord => CurrentRecord?.Invoke()?.Campaign is { } campaign && campaign >= 0 && campaign < CampaignWords.Length
		? CampaignWords[campaign]
		: (short)0;

	private readonly Es1VideoMemory _display;
	private readonly GameContent _content;
	private readonly Es1DirtyList _dirty;

	public Es1ShellBackdrop(Es1VideoMemory display, GameContent content, Es1DirtyList dirty) {
		_display = display;
		_content = content;
		_dirty = dirty;
	}

	/// <summary>The region's rect at <c>+8</c>–<c>+0x14</c>, inclusive; <c>ShellBackdrop_Ctor</c> (<c>0003918e</c>) zeroes it.</summary>
	public (int X0, int Y0, int X1, int Y1) Rect { get; private set; }

	/// <summary><c>ShellBackdrop_Picture</c> (<c>000aa7ac</c>).</summary>
	public DynamixBitmap? Picture { get; private set; }

	/// <summary>
	/// The palette <c>0007fcd0</c>, which <c>Shell_PresentFrame</c> uploads while <see cref="PaletteDue"/> is set: the
	/// backdrop's, or the one a script's palette command left (<see cref="Ttm.Es1TtmManager.PresentPalette"/>), which
	/// <c>Shell_PlayTtm</c> clears.
	/// </summary>
	public DynamixPalette? Palette { get; set; }

	/// <summary>The byte <c>0007fcd4</c>: <see cref="Palette"/> is to be uploaded.</summary>
	public bool PaletteDue { get; set; }

	/// <summary><c>Shell_MusicTrack</c> (<c>00081938</c>), −1 in the image; <see cref="Show"/> and <c>Shell_HandleMessage</c> set it.</summary>
	public short MusicTrack { get; set; } = -1;

	/// <summary>The word <c>00081936</c>, the track last asked for, −1 in the image.</summary>
	public short LastTrack { get; private set; } = -1;

	/// <summary>The byte <c>0007fcd5</c>: the next frame starts <see cref="MusicTrack"/>.</summary>
	public bool MusicDue { get; set; }

	/// <summary>
	/// <c>ShellBackdrop_Show</c> (<c>000395a0</c>)(backdrop, <paramref name="group"/>): the group's track, flagged for the
	/// next frame when it differs from the last; the previous picture released; the palette <c>res32\regn&lt;group&gt;.dpl</c>
	/// (<see cref="LoadPalette"/>); the picture <c>res32\regn&lt;group&gt;.dbm</c> drawn at (0, 0) on page 2
	/// (<see cref="SetPicture"/>).
	/// </summary>
	public void Show(short group) {
		MusicTrack = GroupTracks[group / 1000 - 1];
		if (MusicTrack != LastTrack) {
			MusicDue = true;
			LastTrack = MusicTrack;
		}

		Picture = null;
		LoadPalette(group);
		string name = $"REGN{group}.DBM";
		var picture = _content.Read("res32", name) is { } bytes ? new DynamixBitmapTransformer().Parse(bytes) : null;
		if (picture == null) {
			Console.Error.WriteLine($"res32\\{name} is missing or unreadable: the backdrop is not drawn.");
		}

		Picture = picture;
		SetPicture(0, 0, picture);
		OpenGroupTtms(group);
		if (group == HangarGroup && AssignHangarScripts?.Invoke() is { } waits) {
			// HangarBay_AssignScripts (000122e0). Show is no iterator; a fade's retrace waits in its script runs are passed
			// over, as Es1TtmPlayer.Run passes them, an engine reading.
			foreach (var _ in waits) {
			}
		}
	}

	/// <summary>The service bay's group, whose show gives the scripts their bays.</summary>
	public const short HangarGroup = 4000;

	/// <summary>
	/// <c>HangarBay_AssignScripts</c> (<c>000122e0</c>), the bay's (<see cref="Hangar.Es1HangarBay.AssignScripts"/>), which
	/// <see cref="Show"/> runs for <see cref="HangarGroup"/>; set by the screens once the bay exists, none before.
	/// </summary>
	public Func<IEnumerable<Es1ShellWait>>? AssignHangarScripts { get; set; }

	/// <summary>The shell's scripts (<see cref="Es1ShellScreens.Scripts"/>), which the group's scripts open in; none before the first screen.</summary>
	public Func<Ttm.Es1TtmManager?>? Scripts { get; set; }

	/// <summary><c>TtmLists_Load</c>'s lists (<see cref="Es1ShellScreens.TtmLists"/>), the array at <c>0007e710</c>.</summary>
	public Func<HercWorks.Core.Data.File.Dat.Shell.Es1NameList[]?>? TtmLists { get; set; }

	/// <summary>
	/// <c>ShellBackdrop_GroupTtms</c> (<c>0007e718</c>), the array of 3-byte entries of the group's scripts, none in the
	/// image (docs/es1/shell/menus.md#the-backdrop), its count <c>ShellBackdrop_GroupTtmCount</c> (<c>0007e71c</c>) its
	/// length. The message box's kinds 5 to 11 set entries 3 and 6 to mode 2 while their line is up
	/// (<see cref="MessageBox.Es1MessageBox"/>), and the bay gives the entries their slots
	/// (<see cref="Hangar.Es1HangarBay"/>).
	/// </summary>
	public Es1TtmEntry[]? GroupTtms { get; private set; }

	/// <summary><c>ShellBackdrop_GroupTtmList</c> (<c>0007e716</c>), −1 in the image: the list of <c>cfg\tm_files.dat</c> the group's scripts came from.</summary>
	public short GroupTtmList { get; private set; } = -1;

	/// <summary>
	/// <c>ShellBackdrop_OpenGroupTtms</c> (<c>00011f19</c>)(<paramref name="group"/>) (docs/es1/shell/menus.md#the-backdrop):
	/// with entries, each destroyed with <c>TtmEntry_Dtor</c> (<c>00011edd</c>), which closes its script, and the array
	/// cleared; the list group / 1000 − 1; when the lists' count is not 0, that list's count of entries made with
	/// <c>TtmEntry_Ctor</c> (<c>0001201b</c>), each with its name opened playing, not looping, on the default render
	/// context (<c>Ttm_Open</c>(0, 0, 0, name)), its handle in the entry. A list of no names leaves no array. Retail indexes
	/// the lists with no bounds test; a group past them opens none here, an engine guard.
	/// </summary>
	private void OpenGroupTtms(short group) {
		var scripts = Scripts?.Invoke();
		if (GroupTtms is { } entries) {
			foreach (var entry in entries) {
				if (entry.Handle != Ttm.Es1TtmManager.NoHandle) {
					scripts?.Close(entry.Handle);
					entry.Handle = Ttm.Es1TtmManager.NoHandle;
				}

				entry.Slot = Es1TtmEntry.NoSlot;
			}

			GroupTtms = null;
		}

		GroupTtmList = (short)(group / 1000 - 1);
		if (TtmLists?.Invoke() is not { Length: > 0 } lists || GroupTtmList < 0 || GroupTtmList >= lists.Length) {
			return;
		}

		string[] names = lists[GroupTtmList].Names;
		if (names.Length == 0) {
			return;
		}

		GroupTtms = names.Select(name => new Es1TtmEntry {
			Handle = scripts?.Open(Ttm.Es1TtmPlayer.Playing, 0, null, name) ?? Ttm.Es1TtmManager.NoHandle,
		}).ToArray();
	}

	/// <summary>
	/// <c>ShellBackdrop_LoadPalette</c> (<c>0003950a</c>)(backdrop, <paramref name="group"/>): the palette released, then
	/// <c>res32\regn&lt;group&gt;.dpl</c> loaded into it, group 3000 taking 3000 plus <see cref="CampaignWord"/>, and
	/// <see cref="PaletteDue"/> set.
	/// </summary>
	public void LoadPalette(short group) {
		Palette = null;
		string name = $"REGN{(group == 3000 ? group + CampaignWord : group)}.DPL";
		Palette = _content.Read("res32", name) is { } bytes ? new DynamixPaletteTransformer().Parse(bytes) : null;
		if (Palette == null) {
			Console.Error.WriteLine($"res32\\{name} is missing or unreadable: the palette is not changed.");
		}

		PaletteDue = true;
	}

	/// <summary>
	/// <c>ShellBackdrop_SetPicture</c> (<c>00021334</c>)(region, <paramref name="x"/>, <paramref name="y"/>,
	/// <paramref name="bitmap"/>): with a bitmap, drawn at the point on page 2 with the draw page put back after, and the
	/// rect (<i>x</i>, <i>y</i>)–(<i>x</i> + columns − 1, <i>y</i> + rows − 1); with none, the right edge zeroed.
	/// </summary>
	public void SetPicture(int x, int y, DynamixBitmap? bitmap) {
		if (bitmap == null) {
			Rect = (x, y, 0, Rect.Y1);
			return;
		}

		int kept = _display.DrawPage;
		_display.DrawPage = BackgroundPage;
		// Bitmap_Blit with flags 0 at (0, 0), the one point ShellBackdrop_Show passes.
		_display.Blit(bitmap, mirrored: false);
		_display.DrawPage = kept;
		Rect = (x, y, x + bitmap.Cols - 1, y + bitmap.Rows - 1);
	}

	/// <summary>
	/// <c>ShellBackdrop_Draw</c> (<c>0002141c</c>)(backdrop): the source page set to page 2, the region's rect copied onto
	/// the draw page at offset (0, 0), the source page put back, and the box added with delay 0, retail passing the rect's
	/// right and bottom as its width and height.
	/// </summary>
	public void Draw() {
		int kept = _display.SourcePage;
		_display.SourcePage = BackgroundPage;
		var (x0, y0, x1, y1) = Rect;
		_display.CopyRect(x0, y0, x1, y1, 0, 0);
		_display.SourcePage = kept;
		_dirty.Add(x0, y0, x1, y1, 0);
	}

	/// <summary>
	/// <c>Shell_BackdropForScreen</c> (<c>00040afa</c>)(<paramref name="current"/>, <paramref name="screen"/>): the group
	/// of the screen among the 20 numbers at <c>000821a8</c>, and <paramref name="current"/> for any other.
	/// </summary>
	public short ForScreen(short current, short screen) => screen switch {
		1000 or 1200 or 1100 or 1310 or 1320 or 9999 => 1000,
		1490 => 7000,
		1499 => 3000,
		1500 or 1501 => 6000,
		1510 or 1520 => (short)(5000 + CampaignWord),
		1700 or 1710 or 1900 or 1800 or 1820 or 2000 or 1610 or 5555 => 4000,
		_ => current,
	};
}

/// <summary>
/// A 3-byte entry of <see cref="Es1ShellBackdrop.GroupTtms"/> (docs/es1/shell/menus.md#the-backdrop): the script's handle
/// byte <c>+0</c> and the word <c>+1</c>, the hangar slot the script stands for, 99 for one that always plays and −99 for
/// one that shows nothing yet (docs/es1/shell/hangar.md#the-service-bays-display). <c>TtmEntry_Ctor</c> (<c>0001201b</c>)
/// sets −1 and −99.
/// </summary>
public sealed class Es1TtmEntry {
	/// <summary>The word <c>+1</c> of an entry that shows nothing yet, −99.</summary>
	public const short NoSlot = -99;

	/// <summary>The word <c>+1</c> of an entry that always plays, 99.</summary>
	public const short Always = 99;

	/// <summary><c>+0</c>, the script's handle, <see cref="Ttm.Es1TtmManager.NoHandle"/> for none.</summary>
	public byte Handle { get; set; } = Ttm.Es1TtmManager.NoHandle;

	/// <summary><c>+1</c>, the slot word.</summary>
	public short Slot { get; set; } = NoSlot;
}
