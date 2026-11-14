using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;

namespace Herculan.Es1.View;

/// <summary>
/// A <c>dci\</c> cursor as <c>Cursor_SetImage</c> takes it (docs/es1/formats/dfn-hfn-dci.md#cursors): the bitmap's
/// rows of palette indices and the hotspot. A bitmap whose flags byte has bit 4 clear, as all eight of
/// <c>SIMVOL0.VOL</c>'s have, is drawn with its index-0 pixels leaving the picture under them, as
/// <c>VGA8_BlitBitmap</c> (<c>ES1DBSIM 0008b9a8</c>), the driver's slot 26 that <c>VGA_8</c>'s slot 30 draws it with,
/// skips a 0 byte.
/// </summary>
public sealed class Es1CursorImage {
	/// <summary>The bitmap flag that has every byte written.</summary>
	private const byte OpaqueFlag = 0x10;

	private Es1CursorImage(int width, int rows, byte[] pixels, int hotspotX, int hotspotY, bool opaque) {
		Width = width;
		Rows = rows;
		Pixels = pixels;
		HotspotX = hotspotX;
		HotspotY = hotspotY;
		Opaque = opaque;
	}

	public int Width { get; }

	public int Rows { get; }

	/// <summary><see cref="Rows"/> rows of <see cref="Width"/> palette indices.</summary>
	public byte[] Pixels { get; }

	/// <summary>The hotspot, the record's <c>+8</c>: the pixel of the image that lies on the pointer.</summary>
	public int HotspotX { get; }

	/// <inheritdoc cref="HotspotX"/>
	public int HotspotY { get; }

	/// <summary>Whether index 0 is drawn too.</summary>
	public bool Opaque { get; }

	/// <summary>The cursor's image; null for a cursor with no bitmap, which its record's <c>+4</c> holds as 0.</summary>
	public static Es1CursorImage? From(DynamixCursor? cursor) {
		if (cursor?.Bitmap is not { ImageData: { } data } bitmap || bitmap.Rows <= 0 || bitmap.Cols <= 0) {
			return null;
		}

		var pixels = new byte[bitmap.Rows * bitmap.Cols];
		Array.Copy(data, pixels, Math.Min(data.Length, pixels.Length));
		return new Es1CursorImage(bitmap.Cols, bitmap.Rows, pixels, cursor.HotspotX, cursor.HotspotY,
			(bitmap.Flags & OpaqueFlag) != 0);
	}

	/// <summary>A cursor of <paramref name="width"/> × <paramref name="rows"/> pixels, for a test.</summary>
	internal static Es1CursorImage Of(int width, int rows, int hotspotX, int hotspotY) =>
		new(width, rows, new byte[width * rows], hotspotX, hotspotY, false);

	/// <summary><c>dci\&lt;name&gt;</c> through <c>Cursor_LoadWrapper</c>'s load; null when the file is missing or has no bitmap.</summary>
	public static Es1CursorImage? Load(GameContent content, string name) =>
		content.Read("dci", name + ".DCI") is { } bytes ? From(new DynamixCursorTransformer().Parse(bytes)) : null;
}

/// <summary>
/// The display library's cursor: the image <c>Cursor_SetImage</c> (<c>ES1DBSIM 00066948</c>) installs, the position
/// <c>Cursor_SyncPosition</c> (<c>000669dc</c>) moves and <c>Cursor_GetPosition</c> (<c>00066a2e</c>) reads, and
/// <c>VGA_8</c>'s slots 30 and 31, <c>00088728</c> and <c>000887ff</c>, which draw it on the visible page and take it
/// off again (docs/es1/rendering/screen-pages.md#the-simulators-own-page-routines).
///
/// <para>Retail draws the cursor into video memory on the page on show, the pixels under it saved and put back
/// as it moves, so on the screen it is the image over the picture at the position less the hotspot while it is shown.
/// This engine draws it there as the presenter shows the screen, over the window, in the live palette
/// (<see cref="Es1ScreenPresenter"/>); the save-under is not kept. A page change hides and shows it around itself, which
/// leaves it where it was on the screen, so that is not kept either.</para>
/// </summary>
public sealed class Es1Cursor {
	/// <summary><c>00098d68</c> and <c>00098d78</c>, the image and its hotspot.</summary>
	public Es1CursorImage? Image { get; private set; }

	/// <summary><c>00098d6c</c>, set while the cursor is drawn; clear in the image.</summary>
	public bool Shown { get; private set; }

	/// <summary><c>00098d70</c>, the pointer's position the drawn cursor sits at.</summary>
	public (int X, int Y) Position { get; private set; }

	/// <summary><c>00098d9a</c>, where a position given during a page change waits until it ends.</summary>
	public (int X, int Y) PendingPosition { get; private set; }

	/// <summary><c>VGA8_ShowPageIdle</c> (<c>00098d96</c>): clear while a page change runs; 1 in the image.</summary>
	public bool ShowPageIdle { get; private set; } = true;

	/// <summary>Where the image's top-left lies on the screen while it is shown, the position less the hotspot; null when nothing is drawn.</summary>
	public (int X, int Y)? DrawnAt =>
		Shown && Image is { } image ? (Position.X - image.HotspotX, Position.Y - image.HotspotY) : null;

	/// <summary>
	/// <c>Cursor_SetImage</c>(bitmap, hotspot): the cursor taken off while shown, the image installed, and put back when
	/// it was shown and the image is not null.
	/// </summary>
	public void SetImage(Es1CursorImage? image) {
		bool wasShown = Shown;
		Hide();
		Image = image;
		if (wasShown && image != null) {
			Show();
		}
	}

	/// <summary>
	/// <c>Cursor_SyncPosition</c>(point): while no page change runs, the drawn cursor moved to the point; during one,
	/// the point kept at <see cref="PendingPosition"/>.
	/// </summary>
	public void SyncPosition(int x, int y) {
		if (ShowPageIdle) {
			Position = (x, y);
		} else {
			PendingPosition = (x, y);
		}
	}

	/// <summary><c>Cursor_GetPosition</c>(out): <see cref="Position"/>.</summary>
	public (int X, int Y) GetPosition() => Position;

	/// <summary><c>VGA_8</c>'s slot 30 (<c>00088728</c>): with an image and not shown, the cursor drawn.</summary>
	public void Show() {
		if (Image != null && !Shown) {
			Shown = true;
		}
	}

	/// <summary><c>VGA_8</c>'s slot 31 (<c>000887ff</c>): with an image and shown, the cursor taken off.</summary>
	public void Hide() {
		if (Image != null && Shown) {
			Shown = false;
		}
	}

	/// <summary>
	/// The start of a page change: <see cref="ShowPageIdle"/> cleared, and with the cursor shown its position kept at
	/// <see cref="PendingPosition"/>, as <c>VGA8_ShowPage</c> (<c>00052690</c>) does on entry and
	/// <c>CockpitView_StepViewTransition</c> before a slide.
	/// </summary>
	public void BeginPageChange() {
		ShowPageIdle = false;
		if (Shown) {
			PendingPosition = Position;
		}
	}

	/// <summary>
	/// The end of a page change: with the cursor shown it moves to <see cref="PendingPosition"/>, so a move during the
	/// change lands when it ends, and <see cref="ShowPageIdle"/> is set.
	/// </summary>
	public void EndPageChange() {
		if (Shown) {
			Position = PendingPosition;
		}

		ShowPageIdle = true;
	}
}
