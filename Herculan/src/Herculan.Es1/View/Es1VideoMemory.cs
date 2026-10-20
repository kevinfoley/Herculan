using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Render;

namespace Herculan.Es1.View;

/// <summary>
/// What draws the world layer under <see cref="Es1VideoMemory"/>'s cells: the 3D pass into a rect of the store, and a
/// copy of a rect from one place of the store to another. Positions and sizes are in the store's cells, rows counted
/// down from its top.
/// </summary>
public interface IEs1WorldLayer {
	/// <summary>Draws the world into the rect of <paramref name="width"/> × <paramref name="height"/> cells at (<paramref name="x"/>, <paramref name="y"/>).</summary>
	void DrawWorld(int x, int y, int width, int height);

	/// <summary>Copies the world pixels of one rect of cells onto another; the two never overlap.</summary>
	void Copy(int sourceX, int sourceY, int targetX, int targetY, int width, int height);
}

/// <summary>
/// The simulator's display during a mission and the VGA memory it draws in (docs/es1/rendering/screen-pages.md#the-video-memory):
/// a screen 640 pixels wide over the VGA's 256 KB, the four page origins, the display's page pair <c>+0x88</c>/<c>+0x8c</c>,
/// its visible, source and draw pages <c>+0x34</c>, <c>+0x38</c> and <c>+0x3c</c>, and <c>VGA_8</c>'s page routines on
/// them (docs/es1/rendering/screen-pages.md#the-simulators-own-page-routines). The monitor shows the 320 × 200 window at
/// the start address and pan <see cref="ShowPage"/> last wrote.
///
/// <para>Retail keeps one store of palette indices. This engine keeps that store in <see cref="Cells"/>, where a cell
/// the world was drawn into holds <see cref="WorldCell"/> and the world's own pixels are in a layer of the same shape
/// drawn at the window's resolution (<see cref="IEs1WorldLayer"/>, <see cref="IndexedWorldSurface"/>). Every write goes
/// to the cells, the last writer winning as in retail's store, and a copy between pages copies the cells and the world
/// layer's pixels under them alike, so a copy carries whatever the source page holds. The 2D drawing's indices stay
/// indices until they are shown, through the live palette.</para>
///
/// <para>Every 2D routine here draws as its callers run it in the view changes, with the clip block's clip mode and
/// translation 0: unclipped, at the draw page's origin.</para>
/// </summary>
public sealed class Es1VideoMemory {
	/// <summary>The screen's width, <c>GLDisplay_SetAttribute</c>(display, 640, 2) in <c>Sim_StartMission</c> step 11.</summary>
	public const int Width = 640;

	/// <summary>The VGA's 256 KB, one pixel a byte across its four planes, where its addresses wrap.</summary>
	public const int CellCount = 0x40000;

	/// <summary>The whole rows the memory holds at 640 pixels a row, 409; the 410th is partly addressable.</summary>
	public const int Rows = CellCount / Width;

	/// <summary>The rows <see cref="Cells"/> is laid out in, the partial last row included.</summary>
	public const int StoreRows = (CellCount + Width - 1) / Width;

	/// <summary>The monitor's window, one page: 320 × 200.</summary>
	public const int PageWidth = Es1CockpitViews.ScreenWidth;

	/// <inheritdoc cref="PageWidth"/>
	public const int PageHeight = Es1CockpitViews.ScreenHeight;

	/// <summary>The pages the display holds origins for.</summary>
	public const int PageCount = 4;

	/// <summary>The cell value of a pixel the world was drawn into.</summary>
	public const ushort WorldCell = IndexedWorldSurface.WorldCell;

	private readonly (int X, int Y)[] _pageOrigins = new (int, int)[PageCount];
	private readonly (int X, int Y)[] _pageBases = new (int, int)[PageCount];

	// The CRTC's start address in pixels, 4 to an address, and the attribute controller's pan in pixels.
	private int _startOffset;
	private int _pan;

	private int _dirtyFirst = 0, _dirtyLast = StoreRows - 1;

	/// <summary>The store, <see cref="StoreRows"/> rows of <see cref="Width"/> cells, a palette index or <see cref="WorldCell"/> each, from index 0.</summary>
	public ushort[] Cells { get; } = new ushort[Width * StoreRows];

	/// <summary>The world layer under the cells, or none.</summary>
	public IEs1WorldLayer? Layer { get; set; }

	/// <summary>The display library's cursor, which the driver's slots 30 and 31 draw on the visible page.</summary>
	public Es1Cursor Cursor { get; } = new();

	/// <summary>The display's <c>+0x48</c>, page <paramref name="page"/>'s own origin, which a slide's steps and a view's canvas origin are added to.</summary>
	public (int X, int Y) PageBase(int page) => _pageBases[page];

	/// <summary>Where page <paramref name="page"/> lies now, the offset and pan <c>VGA8_SetPageOrigin</c> last stored for it.</summary>
	public (int X, int Y) PageOrigin(int page) => _pageOrigins[page];

	/// <summary><c>+0x88</c>: during a frame, the page on the monitor.</summary>
	public int PairVisible { get; set; }

	/// <summary><c>+0x8c</c>: during a frame, the page drawn.</summary>
	public int PairDrawn { get; set; }

	/// <summary><c>+0x34</c>, the visible page, which <see cref="ShowPage"/> sets.</summary>
	public int VisiblePage { get; private set; }

	/// <summary><c>+0x38</c>, the page a copy between pages reads.</summary>
	public int SourcePage { get; set; }

	/// <summary><c>+0x3c</c>, the page drawing writes.</summary>
	public int DrawPage { get; set; }

	/// <summary>
	/// <c>Display_FlipWithoutPan</c> (<c>000bda96</c>): set, <see cref="ShowPage"/> writes the start address and returns;
	/// clear, it waits for the next vertical retrace and writes the pan too (docs/es1/rendering/screen-pages.md#the-simulators-own-page-routines).
	/// </summary>
	public bool FlipWithoutPan { get; set; }

	/// <summary>The cell the monitor's top-left pixel shows, as a linear address.</summary>
	public int MonitorStart => Wrap(_startOffset + _pan);

	/// <summary>The monitor's top-left pixel as a column and row of the store.</summary>
	public (int X, int Y) MonitorOrigin => (MonitorStart % Width, MonitorStart / Width);

	/// <summary>Sets page <paramref name="page"/>'s own origin, the display's <c>+0x48</c> entry, and puts the page there.</summary>
	public void SetPageBase(int page, int x, int y) {
		_pageBases[page] = (x, y);
		SetPageOrigin(page, x, y);
	}

	/// <summary>
	/// <c>VGA8_SetPageOrigin</c> (<c>ES1DBSIM 0005279d</c>), the driver's slot 15: page <paramref name="page"/> lies at
	/// (<paramref name="x"/>, <paramref name="y"/>) from now on; the draw and source pages are addressed through their
	/// origins as each draw runs, and the visible page is shown again (<see cref="ShowPage"/>), which scrolls the
	/// monitor. Returns whether that show waited for a retrace.
	/// </summary>
	public bool SetPageOrigin(int page, int x, int y) {
		_pageOrigins[page] = (x, y);
		return page == VisiblePage && ShowPage(page);
	}

	/// <summary>
	/// The driver's slot 18, <c>VGA8_ShowPage</c> (<c>00052690</c>), through a store to the visible page: the CRTC's
	/// start address to the page's origin, a multiple of 4 pixels, and while <see cref="FlipWithoutPan"/> is clear, after
	/// waiting for the next vertical retrace, the pan to the origin's other (<i>x</i> &amp; 3) pixels. Returns whether it
	/// waits, the caller going on when the next retrace starts.
	/// </summary>
	public bool ShowPage(int page) {
		VisiblePage = page;
		int address = Address(page, 0, 0);
		_startOffset = address & ~3;
		if (FlipWithoutPan) {
			return false;
		}

		_pan = address & 3;
		return true;
	}

	/// <summary>
	/// <c>Raster_FillRect</c> with the brush {0, <paramref name="color"/>} on the draw page, the inclusive rect
	/// {<paramref name="x0"/>, <paramref name="y0"/>, <paramref name="x1"/>, <paramref name="y1"/>}.
	/// </summary>
	public void FillRect(int x0, int y0, int x1, int y1, byte color) {
		for (int y = y0; y <= y1; y++) {
			for (int x = x0; x <= x1; x++) {
				Write(Address(DrawPage, x, y), color);
			}
		}
	}

	/// <summary>
	/// <c>Bitmap_Blit</c> of <paramref name="art"/> at (0, 0) on the draw page, flags 2 mirroring it left to right. Every
	/// pixel is written, as ES2's canopy blit writes every pixel (docs/retail/simulation/cockpit-views.md#the-viewport-cutout).
	/// </summary>
	public void Blit(DynamixBitmap art, bool mirrored) {
		var pixels = art.ImageData ?? [];
		for (int y = 0; y < art.Rows; y++) {
			for (int x = 0; x < art.Cols; x++) {
				int source = y * art.Cols + (mirrored ? art.Cols - 1 - x : x);
				if (source < pixels.Length) {
					Write(Address(DrawPage, x, y), pixels[source]);
				}
			}
		}
	}

	/// <summary>
	/// A bitmap of <paramref name="width"/> × <paramref name="rows"/> bytes, row by row, blitted with its top-left at
	/// (<paramref name="x"/>, <paramref name="y"/>) of the draw page, a 0 byte leaving the pixel as it was, as the 8-bit
	/// drivers' blits do for a bitmap without the opaque bit (docs/es1/rendering/ground-and-sky.md).
	/// </summary>
	public void BlitTransparent(ReadOnlySpan<byte> pixels, int width, int rows, int x, int y) {
		for (int row = 0; row < rows; row++) {
			for (int column = 0; column < width; column++) {
				int source = row * width + column;
				if (source < pixels.Length && pixels[source] != 0) {
					Write(Address(DrawPage, x + column, y + row), pixels[source]);
				}
			}
		}
	}

	/// <summary>One cell of the draw page at (<paramref name="x"/>, <paramref name="y"/>) set to <paramref name="value"/>, a palette index or <see cref="WorldCell"/>.</summary>
	public void Plot(int x, int y, ushort value) => Write(Address(DrawPage, x, y), value);

	/// <summary>
	/// The cells of the source page's inclusive rect {<paramref name="x0"/>, <paramref name="y0"/>, <paramref name="x1"/>,
	/// <paramref name="y1"/>}, row by row: what <c>Region_SaveRect</c> (<c>00066470</c>) grabs into a region's buffer
	/// through the driver. A cell the world was drawn into keeps <see cref="WorldCell"/>, whose pixels the world layer
	/// holds.
	/// </summary>
	public ushort[] ReadRect(int x0, int y0, int x1, int y1) {
		int width = x1 - x0 + 1, rows = y1 - y0 + 1;
		var cells = new ushort[Math.Max(0, width) * Math.Max(0, rows)];
		for (int y = 0; y < rows; y++) {
			for (int x = 0; x < width; x++) {
				cells[y * width + x] = Cells[Address(SourcePage, x0 + x, y0 + y)];
			}
		}

		return cells;
	}

	/// <summary>
	/// <paramref name="cells"/>, as <see cref="ReadRect"/> read them, written to the draw page's inclusive rect
	/// {<paramref name="x0"/>, <paramref name="y0"/>, <paramref name="x1"/>, <paramref name="y1"/>}, every cell, as
	/// <c>Region_RestoreRect</c> (<c>00066354</c>) blits a region's buffer with the opaque flag <c>0x10</c>.
	/// </summary>
	public void WriteRect(int x0, int y0, int x1, int y1, ushort[] cells) {
		int width = x1 - x0 + 1, rows = y1 - y0 + 1;
		for (int y = 0; y < rows; y++) {
			for (int x = 0; x < width; x++) {
				int source = y * width + x;
				if (source < cells.Length) {
					Write(Address(DrawPage, x0 + x, y0 + y), cells[source]);
				}
			}
		}
	}

	/// <summary>
	/// <c>Region_CopyBetweenPages</c> (<c>00066914</c>) of the type-0 region {<paramref name="x0"/>, <paramref name="y0"/>,
	/// <paramref name="x1"/>, <paramref name="y1"/>} at the offset (<paramref name="offsetX"/>, <paramref name="offsetY"/>)
	/// in clip mode 0 with no translation, which hands the rect moved by the offset straight to <c>VGA8_CopyRectBetweenPages</c>
	/// (<c>00051c3c</c>, the driver's slot 20): the source page's rect onto the draw page's rect moved by the offset, rows
	/// and columns included (docs/es1/rendering/screen-pages.md#copying-between-pages).
	///
	/// <para>The driver copies through the latches a row at a time, left to right, when the two pages' pans agree, so a
	/// copy whose ends meet reads what it has just written, as here; when they disagree it reads each row into a buffer
	/// before writing it. A rect within one group of four columns reads source row <i>y</i> + the offset's <i>y</i> where
	/// the others read <i>y</i> − it.</para>
	/// </summary>
	public void CopyRect(int x0, int y0, int x1, int y1, int offsetX, int offsetY) {
		if (x1 < x0 || y1 < y0) {
			return;
		}

		int targetX0 = x0 + offsetX, targetX1 = x1 + offsetX;
		int width = x1 - x0 + 1;
		bool rowBuffered = ((offsetX + (Address(DrawPage, 0, 0) & 3) - (Address(SourcePage, 0, 0) & 3)) & 3) != 0;
		bool oneGroup = !rowBuffered && (Address(DrawPage, targetX0, 0) >> 2) == (Address(DrawPage, targetX1, 0) >> 2);
		var row = rowBuffered ? new ushort[width] : null;
		for (int y = y0 + offsetY; y <= y1 + offsetY; y++) {
			int sourceRow = oneGroup ? y + offsetY : y - offsetY;
			int source = Address(SourcePage, x0, sourceRow);
			int target = Address(DrawPage, targetX0, y);
			if (row != null) {
				for (int i = 0; i < width; i++) {
					row[i] = Cells[Wrap(source + i)];
				}

				for (int i = 0; i < width; i++) {
					Write(Wrap(target + i), row[i]);
				}
			} else {
				for (int i = 0; i < width; i++) {
					Write(Wrap(target + i), Cells[Wrap(source + i)]);
				}
			}

			CopyWorldRun(source, target, width);
		}
	}

	/// <summary>
	/// The 3D pass of <c>Sim_RenderFrame</c> into the draw page: the world drawn in <paramref name="window"/>'s rect, held
	/// to the page, of which the pixels inside <paramref name="clip"/>'s regions take it, clip mode 2.
	/// </summary>
	public void DrawWorld(Es1ViewWindow window, Es1ClipBlock clip) {
		int x1 = Math.Min(window.X1, PageWidth - 1), y1 = Math.Min(window.Y1, PageHeight - 1);
		if (x1 < window.X0 || y1 < window.Y0) {
			return;
		}

		int width = x1 - window.X0 + 1, rows = y1 - window.Y0 + 1;
		for (int y = window.Y0; y <= y1; y++) {
			for (int x = window.X0; x <= x1; x++) {
				if (clip.Contains(x, y)) {
					Write(Address(DrawPage, x, y), WorldCell);
				}
			}
		}

		int corner = Address(DrawPage, window.X0, window.Y0);
		int left = corner % Width, top = corner / Width;
		if (Layer != null && left + width <= Width && top + rows <= StoreRows) {
			Layer.DrawWorld(left, top, width, rows);
		}
	}

	/// <summary>The store's rows written since the last call, for the GPU's copy of the cells; (0, 0) for none.</summary>
	public (int FirstRow, int RowCount) TakeDirtyRows() {
		if (_dirtyLast < _dirtyFirst) {
			return (0, 0);
		}

		var rows = (_dirtyFirst, _dirtyLast - _dirtyFirst + 1);
		_dirtyFirst = StoreRows;
		_dirtyLast = -1;
		return rows;
	}

	/// <summary>The cell at (<paramref name="x"/>, <paramref name="y"/>) of page <paramref name="page"/> as it lies now.</summary>
	public ushort Cell(int page, int x, int y) => Cells[Address(page, x, y)];

	/// <summary>The linear address of (<paramref name="x"/>, <paramref name="y"/>) of page <paramref name="page"/>, as the row table and the pan give it, wrapped to the memory.</summary>
	public int Address(int page, int x, int y) {
		var origin = _pageOrigins[page];
		return Wrap((origin.Y + y) * Width + origin.X + x);
	}

	private static int Wrap(int address) {
		int wrapped = address % CellCount;
		return wrapped < 0 ? wrapped + CellCount : wrapped;
	}

	private void Write(int address, ushort value) {
		Cells[address] = value;
		int row = address / Width;
		_dirtyFirst = Math.Min(_dirtyFirst, row);
		_dirtyLast = Math.Max(_dirtyLast, row);
	}

	/// <summary>The world layer's copy of one row's run of <paramref name="length"/> cells, cut where either end leaves a row of the store or wraps.</summary>
	private void CopyWorldRun(int source, int target, int length) {
		if (Layer == null) {
			return;
		}

		while (length > 0) {
			int run = Math.Min(length, Math.Min(RunLeft(source), RunLeft(target)));
			Layer.Copy(source % Width, source / Width, target % Width, target / Width, run, 1);
			source = Wrap(source + run);
			target = Wrap(target + run);
			length -= run;
		}
	}

	private static int RunLeft(int address) => Math.Min(Width - address % Width, CellCount - address);
}
