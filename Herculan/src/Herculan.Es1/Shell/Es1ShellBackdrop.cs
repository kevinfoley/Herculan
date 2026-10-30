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

	/// <summary>The palette <c>0007fcd0</c>, which <c>Shell_PresentFrame</c> uploads while <see cref="PaletteDue"/> is set.</summary>
	public DynamixPalette? Palette { get; private set; }

	/// <summary>The byte <c>0007fcd4</c>: <see cref="Palette"/> is to be uploaded.</summary>
	public bool PaletteDue { get; set; }

	/// <summary><c>Shell_MusicTrack</c> (<c>00081938</c>), −1 in the image.</summary>
	public short MusicTrack { get; private set; } = -1;

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

		// PLACEHOLDER: 00011f19(group), and for group 4000 000122e0, are unread; later modules' (menus.md's Open).
	}

	/// <summary>
	/// <c>ShellBackdrop_LoadPalette</c> (<c>0003950a</c>)(backdrop, <paramref name="group"/>): the palette released, then
	/// <c>res32\regn&lt;group&gt;.dpl</c> loaded into it and <see cref="PaletteDue"/> set.
	/// </summary>
	public void LoadPalette(short group) {
		Palette = null;
		if (group == 3000) {
			// PLACEHOLDER: group 3000 takes 3000 plus the word at 0007ea94 + 2 × the current career record's +6, the
			// careers being S3's; no screen of this part opens it.
			throw new NotImplementedException("Backdrop group 3000 needs the current career record, which is not ported.");
		}

		string name = $"REGN{group}.DPL";
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
	public static short ForScreen(short current, short screen) => screen switch {
		1000 or 1200 or 1100 or 1310 or 1320 or 9999 => 1000,
		1490 => 7000,
		1499 => 3000,
		1500 or 1501 => 6000,
		// PLACEHOLDER: 5000 plus the word at 0007ea94 + 2 × the current career record's +6, the careers being S3's; no
		// screen of this part asks for it.
		1510 or 1520 => throw new NotImplementedException("Screens 1510 and 1520 need the current career record, which is not ported."),
		1700 or 1710 or 1900 or 1800 or 1820 or 2000 or 1610 or 5555 => 4000,
		_ => current,
	};
}
