using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Hangar;

/// <summary>
/// The display a sprite draws on and the dirty list its draws add to: the shell's display <c>000ab616</c> and its dirty
/// list. This engine's bundle of the two globals the sprites' file reads.
/// </summary>
public sealed record Es1SpriteSurface(Es1VideoMemory Display, Es1DirtyList Dirty);

/// <summary>
/// The 34-byte sprite of the sprites' file (docs/es1/formats/herc-diagrams.md#sprites): a bitmap at a point that saves
/// what is under it when drawn and puts it back when erased. Its <c>GLRectangleRegion</c> at <c>+6</c> holds the rect
/// <see cref="Rect"/>, both corners inside it, as every region rect the raster copies.
/// </summary>
public sealed class Es1Sprite {
	/// <summary>The page <c>0007fcce</c>, 3 in the image, that a sprite with <see cref="Keep"/> other than 0 keeps its background on.</summary>
	public const int KeepPage = 3;

	/// <summary><c>+0</c>, set once drawn.</summary>
	public bool Drawn { get; private set; }

	/// <summary><c>+1</c>, how the background is kept: 0 in <see cref="Background"/>, any other on <see cref="KeepPage"/>.</summary>
	public byte Keep { get; private set; }

	/// <summary><c>+2</c>, the bitmap, or null.</summary>
	public DynamixBitmap? Bitmap { get; private set; }

	/// <summary>The region's rect, <c>+0xe</c> to <c>+0x1a</c>; <c>Sprite_Ctor</c> (<c>00023f2c</c>) zeroes it.</summary>
	public Es1Rect Rect { get; private set; }

	/// <summary><c>+0x1e</c>, the saved background, <c>Region_PixelCount</c> cells of the region; null for none.</summary>
	public ushort[]? Background { get; private set; }

	/// <summary>
	/// <c>Sprite_Set</c> (<c>000216e0</c>)(sprite, <paramref name="keep"/>, <paramref name="x"/>, <paramref name="y"/>,
	/// <paramref name="bitmap"/>): the bitmap and <paramref name="keep"/> stored, the rect's corner (<i>x</i>, <i>y</i>), the
	/// buffer freed; with a bitmap the far corner (<i>x</i> + its width <c>+6</c>, <i>y</i> + its height <c>+4</c>) and, for
	/// <paramref name="keep"/> 0, a buffer of the region's cells; with none the right edge 0, the bottom left as it was; the
	/// drawn byte cleared.
	/// </summary>
	public void Set(byte keep, int x, int y, DynamixBitmap? bitmap) {
		Bitmap = bitmap;
		Keep = keep;
		Background = null;
		if (bitmap == null) {
			Rect = new Es1Rect(x, y, 0, Rect.Y1);
		} else {
			Rect = new Es1Rect(x, y, x + bitmap.Cols, y + bitmap.Rows);
			if (keep == 0) {
				Background = new ushort[(Rect.X1 - Rect.X0 + 1) * (Rect.Y1 - Rect.Y0 + 1)];
			}
		}

		Drawn = false;
	}

	/// <summary><c>Sprite_SetBitmap</c> (<c>000216c7</c>)(sprite, <paramref name="bitmap"/>): the bitmap stored alone, the rect left, the drawn byte cleared.</summary>
	public void SetBitmap(DynamixBitmap? bitmap) {
		Bitmap = bitmap;
		Drawn = false;
	}

	/// <summary>
	/// <c>Sprite_Draw</c> (<c>000217a9</c>)(sprite), with a bitmap: the source page set to page 2; for <see cref="Keep"/> 0
	/// the region of page 2 saved into <see cref="Background"/> (<c>Region_Save</c>), otherwise copied onto
	/// <see cref="KeepPage"/>; the bitmap blitted at the corner on page 2; the region copied from page 2 onto the draw page
	/// the display held; a dirty box of delay 0 of the bitmap's size at the corner; the source page put back and the drawn
	/// byte set. The raster's routines draw in the full-screen render context, as <see cref="Menus.Es1Menu.SaveBackground"/>
	/// takes it.
	/// </summary>
	public void Draw(Es1SpriteSurface surface) {
		if (Bitmap is not { } bitmap) {
			return;
		}

		var display = surface.Display;
		int keptSource = display.SourcePage, keptDraw = display.DrawPage;
		var canvas = new Es1PanelCanvas(display, Es1ShellScreens.FullScreen);
		display.SourcePage = Es1ShellBackdrop.BackgroundPage;
		if (Keep == 0) {
			if (Background is { } buffer) {
				var cells = canvas.SaveRegion(Rect);
				Array.Copy(cells, buffer, Math.Min(cells.Length, buffer.Length));
			}
		} else {
			display.DrawPage = KeepPage;
			canvas.CopyRegion(Rect, 0, 0);
		}

		display.DrawPage = Es1ShellBackdrop.BackgroundPage;
		canvas.Blit(bitmap, Rect.X0, Rect.Y0);
		display.DrawPage = keptDraw;
		canvas.CopyRegion(Rect, 0, 0);
		surface.Dirty.Add(Rect.X0, Rect.Y0, bitmap.Cols, bitmap.Rows, 0);
		display.SourcePage = keptSource;
		Drawn = true;
	}

	/// <summary>
	/// <c>Sprite_Erase</c> (<c>00021a6e</c>)(sprite), with a bitmap and the drawn byte set: the draw page set to page 2 and
	/// the background put back there, from <see cref="Background"/> with <c>Region_Restore</c> at the corner for
	/// <see cref="Keep"/> 0, otherwise copied from <see cref="KeepPage"/>; the region copied from page 2 onto the draw page
	/// the display held; the dirty box <see cref="Draw"/> adds; the source page put back. The drawn byte stays set.
	/// </summary>
	public void Erase(Es1SpriteSurface surface) {
		if (Bitmap is not { } bitmap || !Drawn) {
			return;
		}

		var display = surface.Display;
		int keptSource = display.SourcePage, keptDraw = display.DrawPage;
		var canvas = new Es1PanelCanvas(display, Es1ShellScreens.FullScreen);
		display.DrawPage = Es1ShellBackdrop.BackgroundPage;
		if (Keep == 0) {
			if (Background is { } buffer) {
				canvas.RestoreRegion(Rect, Rect.X0, Rect.Y0, buffer);
			}
		} else {
			display.SourcePage = KeepPage;
			canvas.CopyRegion(Rect, 0, 0);
		}

		display.SourcePage = Es1ShellBackdrop.BackgroundPage;
		display.DrawPage = keptDraw;
		canvas.CopyRegion(Rect, 0, 0);
		surface.Dirty.Add(Rect.X0, Rect.Y0, bitmap.Cols, bitmap.Rows, 0);
		display.SourcePage = keptSource;
	}

	/// <summary>
	/// <c>Sprite_Dtor</c> (<c>00021519</c>)(sprite, flags)'s <c>Sprite_FreeBackground</c> (<c>00021698</c>): the buffer
	/// freed and the drawn byte cleared; the region's destructors and the free are the collector's here.
	/// </summary>
	public void Dtor() {
		Background = null;
		Drawn = false;
	}
}
