using System.Buffers.Binary;
using System.Text;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Numerics;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Ttm;

/// <summary>
/// One of the simulator's <c>.TTM</c> scripts, <c>TtmScript_Ctor</c> (<c>ES1DBSIM 000797ac</c>)'s <c>0x1c1</c> bytes, and
/// its interpreter: <c>TtmScript_Load</c> (<c>0007a54c</c>), <c>TtmScript_Run</c> (<c>00079a14</c>),
/// <c>TtmScript_RunPage</c> (<c>0007aa58</c>) and <c>TtmScript_RunDrawCommand</c> (<c>0007c01c</c>)
/// (docs/es1/formats/ttm-scripts.md#the-simulator, the commands in docs/es1/formats/ttm-scripts.md#the-commands). The file
/// is read through <see cref="Es1TtmScriptTransformer"/>.
///
/// <para>A draw goes through the script's render context <c>+0x1bd</c>, <see cref="Context"/>, the clock's being the
/// cockpit's canvas context, so a script draws in the canvas context's coordinates on the display's draw page, cut to its
/// rect. PLACEHOLDER: <c>DirtyBox_Add</c>'s boxes are not kept; <c>es2_xref.py</c> finds no reference to the simulator's
/// <c>DirtyList_Flush</c> (<c>00083e80</c>), the copy that would read them (docs/es1/rendering/screen-pages.md#open).</para>
///
/// <para>Engine guards, where retail reads or writes past its tables: a slot number past the six or twelve wraps, and a
/// bitmap draw's slot or frame out of range draws nothing.</para>
/// </summary>
public sealed class Es1TtmPlayer {
	/// <summary>The modes <c>+0xe</c>: playing, paused, hidden and finished.</summary>
	public const int Playing = 0, Paused = 1, Hidden = 2, Finished = 3;

	/// <summary>The bitmap-array and palette slots, and the regions.</summary>
	public const int SlotCount = 6, RegionCount = 12;

	/// <summary><c>TtmScript_CopyArgBytes</c>' most bytes, the point lists' buffer <c>000bbc18</c>.</summary>
	private const int PointBufferLength = 100;

	private readonly Es1TtmManager _manager;
	private readonly IReadOnlyList<DynamixBitmap>?[] _bitmaps = new IReadOnlyList<DynamixBitmap>?[SlotCount];
	private readonly uint[]?[] _palettes = new uint[]?[SlotCount];
	private readonly ushort[]?[] _saved = new ushort[]?[RegionCount];
	private readonly Es1Rect[] _regions = new Es1Rect[RegionCount];
	private readonly (short First, short Count, short Direction)[] _ranges = new (short, short, short)[3];
	private readonly byte[] _pointBuffer = new byte[PointBufferLength];
	private byte[] _script = [];
	private int[] _pages = [];

	// The context the run in progress draws through.
	private Es1PanelCanvas? _canvas;

	/// <summary>
	/// <c>TtmScript_Ctor</c>(0): the brushes' modes 0, 0 and 4 and the origin and context 0. The twelve regions'
	/// rects, and the bitmap-array and region slots <c>+0x26</c> and <c>+0x27</c>, which neither the constructor nor the
	/// load writes, start at 0 here.
	/// </summary>
	internal Es1TtmPlayer(Es1TtmManager manager) {
		_manager = manager;
	}

	/// <summary><c>+0</c>, the page count, a byte: the low byte of <c>PAG:</c>'s word.</summary>
	public byte PageCount { get; private set; }

	/// <summary><c>+1</c>, the current page.</summary>
	public byte Page { get; private set; }

	/// <summary><c>+0xe</c>, the mode, <i>a</i> of the load.</summary>
	public int Mode { get; private set; }

	/// <summary><c>+0x12</c>, set once a wait command has acted on this page; a step to the next page clears it, a jump leaves it.</summary>
	public bool Once { get; private set; }

	/// <summary><c>+0x13</c>, the loop flag, <i>b</i> of the load: with 1 the script starts again when it ends.</summary>
	public int Loop { get; private set; }

	/// <summary><c>+0x25</c>, <c>+0x26</c>, <c>+0x27</c>: the selected palette, bitmap-array and region slots.</summary>
	public byte PaletteSlot { get; private set; }

	/// <inheritdoc cref="PaletteSlot"/>
	public byte BitmapSlot { get; private set; }

	/// <inheritdoc cref="PaletteSlot"/>
	public byte RegionSlot { get; private set; }

	/// <summary><c>+0x178</c>, the page a jump goes to next; −1 for none.</summary>
	public short NextPage { get; private set; } = -1;

	/// <summary><c>+0x17a</c>, the call depth; <c>+0x17c</c> and <c>+0x17e</c>, the x and y a call adds; <c>+0x180</c>, <c>0x3303</c>'s base page.</summary>
	public short CallDepth { get; private set; }

	/// <inheritdoc cref="CallDepth"/>
	public short CallX { get; private set; }

	/// <inheritdoc cref="CallDepth"/>
	public short CallY { get; private set; }

	/// <inheritdoc cref="CallDepth"/>
	public short BasePage { get; private set; }

	/// <summary><c>+0x182</c>, the wait in ticks, and <c>+0x184</c>, the count it reloads from.</summary>
	public short Wait { get; private set; }

	/// <inheritdoc cref="Wait"/>
	public short WaitReload { get; private set; }

	/// <summary><c>+0x186</c>, <c>+0x188</c>: the colour cycle's count of ticks and its reload; <c>+0x18a</c>, set while colours cycle.</summary>
	public short CycleCount { get; private set; }

	/// <inheritdoc cref="CycleCount"/>
	public short CycleReload { get; private set; }

	/// <inheritdoc cref="CycleCount"/>
	public bool Cycling { get; private set; }

	/// <summary>The brushes' colours: <c>+0x1a1</c> of the pen <c>+0x19d</c> (mode 0), <c>+0x1a9</c> of the fill <c>+0x1a5</c> (mode 0) and <c>+0x1b1</c> of the outline <c>+0x1ad</c> (mode 4).</summary>
	public int Foreground { get; private set; } = 10;

	/// <inheritdoc cref="Foreground"/>
	public int Background { get; private set; } = 10;

	/// <inheritdoc cref="Foreground"/>
	public int Outline { get; private set; } = 10;

	/// <summary>
	/// <c>+0x1b5</c>, <c>+0x1b9</c>: the origin the bitmap draws add, which only the constructor writes, 0
	/// (docs/es1/formats/ttm-scripts.md#open).
	/// </summary>
	public (int X, int Y) Origin { get; } = (0, 0);

	/// <summary><c>+0x1bd</c>, the render context the script draws through, made afresh for each run; none draws nothing (see <see cref="Es1TtmManager"/>).</summary>
	public Func<Es1PanelCanvas>? Context { get; set; }

	/// <summary>The bitmap array in slot <paramref name="slot"/>, or none.</summary>
	public IReadOnlyList<DynamixBitmap>? Bitmaps(int slot) => _bitmaps[slot];

	/// <summary>
	/// <c>TtmScript_Load</c>(script, <paramref name="mode"/>, <paramref name="loop"/>, <paramref name="name"/>): 3 when
	/// <c>TtmScript_BuildPath</c>'s path does not open; the file read through <see cref="Es1TtmScriptTransformer"/>, 2
	/// when <c>TT3:</c>'s method byte is not 1 and 1 when the script does not unpack; the page count's low byte, the pages
	/// indexed as <c>TtmScript_IndexPages</c> (<c>0007a940</c>) does, 2 when that fails; the slots, saved regions and
	/// cycling directions emptied, the loop flag and mode set, the once flag, page and palette slot 0, no jump, the call,
	/// wait and cycle words 0, cycling off and the three colours 10. Answers 0 when loaded.
	/// </summary>
	internal int Load(int mode, int loop, string name) {
		if (_manager.Read(_manager.BuildPath(name)) is not { } bytes) {
			return 3;
		}

		HercWorks.Core.Data.File.Es1TtmScript? file;
		try {
			file = new Es1TtmScriptTransformer().Parse(bytes);
		} catch (InvalidDataException) {
			return 1;
		}

		if (file?.PackMethod is not { } method || file.PageCount is not { } pageCount) {
			return 1;
		}

		if (method != 1) {
			return 2;
		}

		if (file.Script is not { } script) {
			return 1;
		}

		_script = script;
		PageCount = (byte)pageCount;
		Array.Clear(_palettes);
		Array.Clear(_bitmaps);
		Array.Clear(_saved);
		for (int i = 0; i < _ranges.Length; i++) {
			_ranges[i].Direction = 0;
		}

		Loop = loop;
		Mode = mode;
		Once = false;
		Page = 0;
		PaletteSlot = 0;
		NextPage = -1;
		CallDepth = CallX = CallY = BasePage = 0;
		Wait = WaitReload = CycleReload = CycleCount = 0;
		Cycling = false;
		Foreground = Background = Outline = 10;
		if (Es1TtmScriptTransformer.IndexPages(script, PageCount) is not { } pages) {
			return 2;
		}

		_pages = pages;
		return 0;
	}

	/// <summary>
	/// <c>TtmScript_Run</c>(script, <paramref name="ticks"/>), in the script's context: the ticks taken off the wait; while
	/// it stays above 0, the page run unless the mode is <see cref="Hidden"/>; once it is 0 or less, the cycle's count
	/// taken down by the ticks while colours cycle, reloaded and the palette stepped when it falls below 0
	/// (<c>TtmScript_CyclePalette</c>, <c>00079d90</c>), the wait reloaded, then by the mode:
	/// <see cref="Playing"/> runs the page and moves on, to the jump's page when one is set (clearing it) or else the next
	/// (clearing <see cref="Once"/>), and past the last page ends the script; <see cref="Paused"/> runs the page;
	/// <see cref="Finished"/> ends the script; <see cref="Hidden"/> does nothing
	/// (docs/es1/formats/ttm-scripts.md#the-simulators-run). The script's end: with the loop flag 1,
	/// <c>TtmScript_Reset</c> and <see cref="Playing"/>; otherwise <see cref="Hidden"/> alone, the script not reset.
	/// </summary>
	public void Run(short ticks) {
		_canvas = Context?.Invoke();
		Wait -= ticks;
		if (Wait >= 1) {
			if (Mode != Hidden) {
				RunPage(Page);
			}

			_canvas = null;
			return;
		}

		if (Cycling) {
			CycleCount -= ticks;
			if (CycleCount < 0) {
				CycleCount = CycleReload;
				CyclePalette();
			}
		}

		Wait = WaitReload;
		switch (Mode) {
			case Playing:
				RunPage(Page);
				if (NextPage == -1) {
					Page++;
					Once = false;
				} else {
					Page = (byte)NextPage;
					NextPage = -1;
				}

				if (Page >= PageCount) {
					End();
				}

				break;
			case Paused:
				RunPage(Page);
				break;
			case Finished:
				End();
				break;
		}

		_canvas = null;

		void End() {
			if (Loop == 1) {
				Reset();
				Mode = Playing;
			} else {
				Mode = Hidden;
			}
		}
	}

	/// <summary>
	/// <c>TtmScript_Reset</c> (<c>0007ca44</c>): the six palettes, the six bitmap arrays and the twelve saved regions
	/// destroyed and their slots emptied; the cycling directions 0; the mode <see cref="Hidden"/>; the page and palette slot
	/// 0; no jump; the call, wait and cycle words 0 and cycling off; the three colours 10. It leaves <see cref="Once"/>.
	/// </summary>
	public void Reset() {
		Array.Clear(_palettes);
		Array.Clear(_bitmaps);
		Array.Clear(_saved);
		for (int i = 0; i < _ranges.Length; i++) {
			_ranges[i].Direction = 0;
		}

		Mode = Hidden;
		Page = 0;
		PaletteSlot = 0;
		NextPage = -1;
		CallDepth = CallX = CallY = BasePage = 0;
		Wait = WaitReload = CycleReload = CycleCount = 0;
		Cycling = false;
		Foreground = Background = Outline = 10;
	}

	/// <summary>
	/// <c>TtmScript_FindLabel</c> (<c>00079ef4</c>)(script, <paramref name="label"/>): the first page whose first command
	/// is <c>0x1101</c> or <c>0x1111</c> with the label, as a byte; 0 when none is.
	/// </summary>
	public byte FindLabel(short label) {
		for (int page = 0; page < PageCount; page++) {
			ushort opcode = Word(_pages[page]);
			if (opcode is 0x1101 or 0x1111 && (short)Word(_pages[page] + 2) == label) {
				return (byte)page;
			}
		}

		return 0;
	}

	/// <summary>
	/// <c>TtmScript_RunPage</c>(script, <paramref name="page"/>), the page's commands from its start until <c>0x0ff0</c>
	/// (docs/es1/formats/ttm-scripts.md#the-simulators-commands): <c>0x1301</c> and <c>0x1311</c> renumbered to
	/// <c>0xc051</c> and <c>0xc061</c>; the seven words after the opcode read as <i>a1</i> to <i>a7</i>, whatever its
	/// count, and the walk stepped past its arguments by its low four bits, past a point list's count and 2<i>n</i> words,
	/// <c>TtmScript_CopyArgBytes</c> (<c>00079fdc</c>) copying its first (4<i>n</i> &amp; <c>0xff</c>) bytes, at most
	/// 100, to the buffer, or past a string as <c>TtmScript_SkipString</c> (<c>00079fb0</c>) steps; during a call,
	/// <i>a1</i> and <i>a2</i> of a draw other than <c>0xa601</c>, <c>0xaf1f</c> and <c>0xaf2f</c> moved by the call's x
	/// and y, and <i>a3</i> and <i>a4</i> too for <c>0xa0a4</c>; then <c>0xc000</c>–<c>0xcfff</c> stepped over,
	/// <c>0xa000</c>–<c>0xafff</c> to <see cref="RunDrawCommand"/>, and any other to its case or, with none,
	/// stepped over. A word read past the script's end reads 0 here.
	/// </summary>
	private void RunPage(byte page) {
		if (page >= _pages.Length) {
			return;
		}

		int p = _pages[page];
		while (true) {
			ushort opcode = Word(p);
			if (opcode == 0x1301) {
				opcode = 0xc051;
			} else if (opcode == 0x1311) {
				opcode = 0xc061;
			}

			if (opcode == 0x0ff0) {
				return;
			}

			short a1 = (short)Word(p + 2), a2 = (short)Word(p + 4), a3 = (short)Word(p + 6), a4 = (short)Word(p + 8);
			short a5 = (short)Word(p + 10), a6 = (short)Word(p + 12), a7 = (short)Word(p + 14);
			int arguments = opcode & 0xf;
			string? text = null;
			if (arguments == 0xf) {
				if (opcode is 0xaf1f or 0xaf2f) {
					int length = Math.Min((int)(byte)(a1 << 2), PointBufferLength);
					for (int i = 0; i < length; i++) {
						_pointBuffer[i] = p + 4 + i < _script.Length ? _script[p + 4 + i] : (byte)0;
					}

					p += 4 + 4 * a1;
				} else {
					text = ReadString(p + 2, out p);
				}
			} else {
				p += 2 + 2 * arguments;
			}

			if (CallDepth != 0 && opcode is >= 0xa000 and < 0xb000 && opcode is not (0xa601 or 0xaf1f or 0xaf2f)) {
				if (opcode == 0xa0a4) {
					a3 += CallX;
					a4 += CallY;
				}

				a1 += CallX;
				a2 += CallY;
			}

			switch (opcode & 0xf000) {
				case 0xc000:
					continue;
				case 0xa000:
					RunDrawCommand(opcode, a1, a2, a3, a4, a5, a6, a7);
					continue;
			}

			RunCommand(opcode, a1, a2, a3, a4, text);
		}
	}

	/// <summary>One of the simulator's table of commands at <c>0009ffcc</c>, the shell's less <c>0x5005</c> (docs/es1/formats/ttm-scripts.md#the-simulators-commands).</summary>
	private void RunCommand(ushort opcode, short a1, short a2, short a3, short a4, string? text) {
		bool noWait = Wait < 1;
		switch (opcode) {
			case 0x2002:
				Foreground = a1;
				Background = a2;
				Outline = a1;
				break;
			case 0xf01f when noWait:
				if (text != null && LoadBitmap(text) is { } screen) {
					_canvas?.Blit(screen, 0, 0);
					int columns = screen.Cols, rows = screen.Rows;
					FixRect(new Es1Rect(Math.Min(0, columns), Math.Min(0, rows), Math.Max(0, columns), Math.Max(0, rows)));
				}

				break;
			case 0x1051:
				BitmapSlot = (byte)a1;
				break;
			case 0xf02f when noWait:
				_bitmaps[BitmapSlot % SlotCount] = text != null ? LoadBitmapArray(text) : null;
				break;
			case 0x0080 when noWait:
				_bitmaps[BitmapSlot % SlotCount] = null;
				break;
			case 0x1121:
				RegionSlot = (byte)a1;
				break;
			case 0x4214 when noWait: {
				int slot = RegionSlot % RegionCount;
				_regions[slot] = new Es1Rect(a1, a2, a3 + a1, a4 + a2);
				_saved[slot] = _canvas?.SaveRegion(_regions[slot]);
				break;
			}
			case 0x00c0 when noWait:
				_saved[RegionSlot % RegionCount] = null;
				break;
			case 0x1061 when noWait:
				PaletteSlot = (byte)a1;
				if (SelectedPalette is { } selected) {
					Upload(selected);
				}

				break;
			case 0xf05f when noWait:
				SelectedPalette = text != null ? LoadPalette(text) : null;
				if (SelectedPalette is { } loaded) {
					Upload(loaded);
				}

				break;
			case 0x0070 when noWait:
				SelectedPalette = null;
				break;
			case 0x4114:
				FadeOut(a1, a2, a4);
				break;
			case 0x4124 when noWait:
				FadeIn(a1, a2, a4);
				break;
			case 0x2302 or 0x2312 or 0x2322:
				_ranges[(opcode - 0x2302) / 0x10] = (a1, (short)(a2 - a1 + 1), _ranges[(opcode - 0x2302) / 0x10].Direction);
				break;
			case 0x2402 when noWait: {
				Cycling = true;
				short speed = (short)Math.Abs((int)a2);
				CycleReload = CycleCount = speed;
				// A speed of 0 divides by zero in retail; no direction here, an engine guard.
				short direction = speed == 0 ? (short)0 : (short)(a2 / speed);
				for (int i = 0; i < _ranges.Length; i++) {
					_ranges[i].Direction = (a1 & (1 << i)) != 0 ? direction : (short)0;
				}

				break;
			}
			case 0x0400 when noWait:
				for (int i = 0; i < _ranges.Length; i++) {
					_ranges[i].Direction = 0;
				}

				Cycling = false;
				CycleReload = CycleCount = 0;
				break;
			case 0x2022:
				if (!Once) {
					Once = true;
					// max equal to min divides by zero in retail; min here, an engine guard.
					int span = a2 - a1;
					WaitReload = Wait = (short)(a1 + (span == 0 ? 0 : Math.Abs(_manager.Rand() % span)));
				}

				break;
			case 0x1021:
				if (!Once) {
					Once = true;
					WaitReload = Wait = a1;
				}

				break;
			case 0x0020 when noWait:
				FixRect(new Es1Rect(0, 0, Es1CockpitViews.ScreenWidth - 1, Es1CockpitViews.ScreenHeight - 1));
				break;
			case 0x4204 when noWait:
				FixRect(Normalized(a1, a2, a3, a4));
				break;
			case 0x0110:
				Mode = Finished;
				break;
			case 0x3003:
				CallDepth++;
				CallX = a1;
				CallY = a2;
				RunPage(FindLabel(a3));
				CallDepth--;
				break;
			case 0x3201:
				BasePage = FindLabel(a1);
				break;
			case 0x3303:
				if (noWait && CallDepth == 0 && BasePage + a3 != Page) {
					CallDepth++;
					CallX = a1;
					CallY = a2;
					RunPage((byte)(BasePage + a3));
					CallDepth--;
				}

				break;
			case 0x1201:
				NextPage = FindLabel(a1);
				break;
		}
	}

	/// <summary>
	/// <c>TtmScript_RunDrawCommand</c>, the table of 22 at <c>000a00b4</c>, in the script's context, each colour taken by
	/// the brush rule (<see cref="Colour"/>): <c>0xa002</c> a pixel and <c>0xa0a4</c> a line in the pen's;
	/// <c>0xa104</c> and <c>0xa404</c> the rect or ellipse of (<i>x</i>, <i>y</i>) to (<i>x</i> + <i>w</i>, <i>y</i> +
	/// <i>h</i>) filled in the fill's and then <c>0xa114</c>'s and <c>0xa424</c>'s outline in the outline's; the bitmap
	/// draws; <c>0xa601</c>, while the wait is 0 or less, the selected region's saved pixels put back at (0, 0) and its rect
	/// fixed; and the polygons (docs/es1/formats/ttm-scripts.md#the-commands).
	/// </summary>
	private void RunDrawCommand(ushort opcode, short a1, short a2, short a3, short a4, short a5, short a6, short a7) {
		_ = a7;
		var canvas = _canvas;
		switch (opcode) {
			case 0xa0a4:
				if (Colour(Foreground) is { } line) {
					canvas?.DrawLine(a1, a2, a3, a4, line);
				}

				break;
			case 0xa104:
				if (Colour(Background) is { } fill) {
					var box = Normalized(a1, a2, a3, a4);
					canvas?.FillRect(box.X0, box.Y0, box.X1, box.Y1, fill);
				}

				goto case 0xa114;
			case 0xa114:
				if (Colour(Outline) is { } edge) {
					var box = Normalized(a1, a2, a3, a4);
					canvas?.OutlineRect(box.X0, box.Y0, box.X1, box.Y1, edge);
				}

				break;
			case 0xa404:
				if (Colour(Background) is { } inside) {
					canvas?.DrawEllipse(Normalized(a1, a2, a3, a4), inside, outline: false);
				}

				goto case 0xa424;
			case 0xa424:
				if (Colour(Outline) is { } ring) {
					canvas?.DrawEllipse(Normalized(a1, a2, a3, a4), ring, outline: true);
				}

				break;
			case 0xaf1f or 0xaf2f:
				DrawPolygon(opcode, a1);
				break;
			case 0xa002:
				if (Colour(Foreground) is { } dot) {
					canvas?.Plot(a1, a2, dot);
				}

				break;
			case >= 0xa504 and <= 0xa536 when (opcode & 0xf) is 4 or 5 or 6 && ((opcode >> 4) & 0xf) <= 3:
			case 0xa5a7:
				DrawBitmap(opcode, a1, a2, a3, a4, a5, a6);
				break;
			case 0xa601:
				if (Wait < 1 && _saved[RegionSlot % RegionCount] is { } cells) {
					var region = _regions[RegionSlot % RegionCount];
					canvas?.RestoreRegion(region, 0, 0, cells);
					FixRect(region);
				}

				break;
		}
	}

	/// <summary>
	/// The bitmap draws' one case, <c>0007c674</c>: the flags 0, 1 and 2 from bits 4 to 7 of the opcode, 3 for any other
	/// value; when slot <paramref name="slot"/> holds an array of more than <paramref name="frame"/> bitmaps, by the low
	/// four bits: 4 <c>Bitmap_Blit</c> at (<i>x</i>, <i>y</i>) plus the origin, 6 <c>Bitmap_BlitScaled</c> to
	/// <paramref name="width"/> × <paramref name="height"/> there, 5 nothing, and 7 the turned draw.
	/// </summary>
	private void DrawBitmap(ushort opcode, short x, short y, short frame, short slot, short width, short height) {
		int bits = (opcode & 0xf0) >> 4;
		int flags = bits <= 2 ? bits : 3;
		if (slot < 0 || slot >= SlotCount || _bitmaps[slot] is not { } array || frame < 0 || frame >= array.Count) {
			return;
		}

		var bitmap = array[frame];
		switch (opcode & 0xf) {
			case 7:
				// PLACEHOLDER: TOKEN_DRAW_ROT_BRUSH's Bitmap_BlitRotatedUnpacked (00067214), which fills through brush mode 5
				// (docs/es1/rendering/ground-and-sky.md); no 2D bitmap fill is ported, and no retail script uses the opcode.
				break;
			case 6:
				_canvas?.BlitScaled(bitmap, width, height, flags, x + Origin.X, y + Origin.Y);
				break;
			case 5:
				break;
			default:
				_canvas?.Blit(bitmap, x + Origin.X, y + Origin.Y, flags);
				break;
		}
	}

	/// <summary>
	/// <c>0xaf1f</c> and <c>0xaf2f</c>: <paramref name="count"/> points from the buffer, the x values and then the y
	/// values; <c>0xaf2f</c> fills them in the fill's colour first; then the outline's line loop, each through
	/// <c>Raster_DrawPolygonEitherWinding</c>. A value past the buffer's 100 bytes reads 0 here, where retail reads on
	/// past it.
	/// </summary>
	private void DrawPolygon(ushort opcode, short count) {
		var points = new List<(int X, int Y)>();
		for (int i = 0; i < count; i++) {
			points.Add((PointWord(i), PointWord(count + i)));
		}

		if (opcode == 0xaf2f && Colour(Background) is { } fill) {
			_canvas?.DrawPolygon(points, fill, outline: false);
		}

		if (Colour(Outline) is { } edge) {
			_canvas?.DrawPolygon(points, edge, outline: true);
		}

		short PointWord(int index) => 2 * index + 1 < PointBufferLength
			? BinaryPrimitives.ReadInt16LittleEndian(_pointBuffer.AsSpan(2 * index))
			: (short)0;
	}

	/// <summary>
	/// <c>TtmScript_FixRect</c> (<c>00079c38</c>)(script, <paramref name="rect"/>): with the display's source page made
	/// the low byte of its draw page and its draw page <see cref="Es1TtmManager.BackgroundPage"/>, the rect copied between
	/// them at (0, 0) through the script's context, and the low bytes of the two pages put back.
	/// </summary>
	private void FixRect(Es1Rect rect) {
		var display = _manager.Display;
		int source = display.SourcePage, draw = display.DrawPage;
		display.SourcePage = draw & 0xff;
		display.DrawPage = _manager.BackgroundPage;
		_canvas?.CopyRegion(rect, 0, 0);
		display.SourcePage = source & 0xff;
		display.DrawPage = draw & 0xff;
	}

	/// <summary>
	/// <c>TtmScript_CyclePalette</c>: each range whose direction <i>d</i> is not 0 rotated by one place in the selected
	/// palette, from <i>first</i>, or <i>first</i> + <i>count</i> − 1 with <i>d</i> −1
	/// (docs/es1/formats/ttm-scripts.md#cycling-colours), then the palette uploaded. A colour past the palette's 256 is
	/// passed over here, where retail reads and writes beyond it; with no palette selected nothing is done, where retail
	/// reads through the empty slot.
	/// </summary>
	private void CyclePalette() {
		if (SelectedPalette is not { } colours) {
			return;
		}

		foreach (var (first, count, direction) in _ranges) {
			if (direction == 0) {
				continue;
			}

			int at = direction == -1 ? first + count - 1 : first;
			uint kept = ColourAt(count * direction + at - direction);
			for (int c = count; c >= 0; c--) {
				int target = c * direction + at - direction;
				if (target is >= 0 and < 256) {
					colours[target] = ColourAt(target - direction);
				}
			}

			if (at is >= 0 and < 256) {
				colours[at] = kept;
			}
		}

		Upload(colours);

		uint ColourAt(int index) => index is >= 0 and < 256 ? colours[index] : 0;
	}

	/// <summary>
	/// <c>0x4114</c>, <c>0007b348</c>: a copy of the selected palette (<c>GLPalette_Assign</c>, <c>0007bfdc</c>);
	/// for <i>percent</i> from 100 down to 0 by 6 − <paramref name="speed"/>, colours <paramref name="first"/> to
	/// <paramref name="last"/> scaled into the copy (<see cref="ScaleRange"/>) and uploaded; then with the source page the
	/// low byte of the draw page and the draw page the visible page, the full screen (0, 0) to (319, 199) copied through
	/// the context, and again with the draw page <see cref="Es1TtmManager.BackgroundPage"/>; the two pages' low bytes put
	/// back and the selected palette uploaded (docs/es1/formats/ttm-scripts.md#the-simulators-fades). With no palette
	/// selected nothing is done, where retail reads through the empty slot; a step of 0 or less, which loops for ever in
	/// retail, makes one pass here.
	/// </summary>
	private void FadeOut(short first, short last, short speed) {
		if (SelectedPalette is not { } selected) {
			return;
		}

		var copy = (uint[])selected.Clone();
		int step = 6 - speed;
		for (short percent = 100; percent >= 0; percent = (short)(percent - step)) {
			ScaleRange(percent, last, first, selected, copy);
			Upload(copy);
			if (step <= 0) {
				break;
			}
		}

		var display = _manager.Display;
		int source = display.SourcePage, draw = display.DrawPage;
		display.SourcePage = draw & 0xff;
		display.DrawPage = display.VisiblePage;
		_canvas?.CopyRegion(FullScreen, 0, 0);
		display.DrawPage = _manager.BackgroundPage;
		_canvas?.CopyRegion(FullScreen, 0, 0);
		display.SourcePage = source & 0xff;
		display.DrawPage = draw & 0xff;
		Upload(selected);
	}

	/// <summary>
	/// <c>0x4124</c>, <c>0007b73a</c>: a copy of the selected palette with all 256 colours <c>0x01000000</c>, uploaded;
	/// the selected colours copied in; the full screen copied through the context from
	/// <see cref="Es1TtmManager.BackgroundPage"/> onto the visible page, the pages' low bytes put back; for <i>percent</i>
	/// from 0 up to 100 by 6 − <paramref name="speed"/>, the colours scaled and uploaded; and the selected palette
	/// uploaded. The guards are <see cref="FadeOut"/>'s.
	/// </summary>
	private void FadeIn(short first, short last, short speed) {
		if (SelectedPalette is not { } selected) {
			return;
		}

		var copy = new uint[256];
		Array.Fill(copy, 0x01000000u);
		Upload(copy);
		selected.CopyTo(copy, 0);
		var display = _manager.Display;
		int source = display.SourcePage, draw = display.DrawPage;
		display.SourcePage = _manager.BackgroundPage;
		display.DrawPage = display.VisiblePage;
		_canvas?.CopyRegion(FullScreen, 0, 0);
		display.SourcePage = source & 0xff;
		display.DrawPage = draw & 0xff;
		int step = 6 - speed;
		for (short percent = 0; percent < 101; percent = (short)(percent + step)) {
			ScaleRange(percent, last, first, selected, copy);
			Upload(copy);
			if (step <= 0) {
				break;
			}
		}

		Upload(selected);
	}

	/// <summary>
	/// <c>Palette_ScaleRange</c> (<c>0007cc24</c>)(script, <paramref name="percent"/>, <paramref name="last"/>,
	/// <paramref name="first"/>, source, target): each colour from the low byte of <paramref name="first"/> to that of
	/// <paramref name="last"/> set in the target to the source's with each channel multiplied by
	/// <c>Math_Q10Multiply</c> by (<i>percent</i> &lt;&lt; 10) / 100, and its fourth byte 1.
	/// </summary>
	private static void ScaleRange(short percent, short last, short first, uint[] source, uint[] target) {
		short scale = (short)(((sbyte)percent << 10) / 100);
		for (int index = (byte)first; index <= (byte)last; index++) {
			uint colour = source[index];
			int r = SimMath.Q10MultiplyRounded((int)(colour & 0xff), scale);
			int g = SimMath.Q10MultiplyRounded((int)((colour >> 8) & 0xff), scale);
			int b = SimMath.Q10MultiplyRounded((int)((colour >> 16) & 0xff), scale);
			target[index] = (uint)((b << 16 | g << 8 | r) & 0xffffff) | 0x01000000;
		}
	}

	/// <summary>The full-screen region the fades copy, (0, 0) to (319, 199).</summary>
	private static Es1Rect FullScreen => new(0, 0, Es1CockpitViews.ScreenWidth - 1, Es1CockpitViews.ScreenHeight - 1);

	/// <summary>The palette in the selected slot <c>+0x25</c>, 256 colours of red, green, blue and a flag byte.</summary>
	private uint[]? SelectedPalette {
		get => _palettes[PaletteSlot % SlotCount];
		set => _palettes[PaletteSlot % SlotCount] = value;
	}

	/// <summary>
	/// The brush's colour as <c>Raster_FillRect</c> and the line and pixel routines resolve it
	/// (docs/retail/rendering/polygon-fill.md#the-brush-colour): with a top byte of 0 the palette index; a colour from a
	/// script's word, sign-extended, never has the ramp's top byte 4, and any other draws nothing.
	/// </summary>
	private static byte? Colour(int value) => (value >> 24) == 0 ? (byte)value : null;

	/// <summary>The inclusive rect of (<paramref name="x"/>, <paramref name="y"/>) and (<i>x</i> + <paramref name="w"/>, <i>y</i> + <paramref name="h"/>), each axis smaller end first, as the commands build it.</summary>
	private static Es1Rect Normalized(int x, int y, int w, int h) =>
		new(Math.Min(x, x + w), Math.Min(y, y + h), Math.Max(x, x + w), Math.Max(y, y + h));

	/// <summary>Sends <paramref name="colours"/> to <see cref="Es1TtmManager.UploadPalette"/>.</summary>
	private void Upload(uint[] colours) {
		if (_manager.UploadPalette is not { } upload) {
			return;
		}

		var palette = new DynamixPalette { ColorCount = 256 };
		for (int index = 0; index < 256; index++) {
			byte r = (byte)colours[index], g = (byte)(colours[index] >> 8), b = (byte)(colours[index] >> 16);
			var entry = new ColorBytes(r, g, b, 255);
			entry.SetColor(RgbaColor.FromArgb(255, r, g, b));
			palette.Colors[index] = entry;
		}

		upload(palette);
	}

	/// <summary><c>0xf01f</c>'s load in place, <c>VolRStream_CtorFromPath</c> and <c>ClassItem_LoadResource</c>: the <c>.DBM</c>, or none.</summary>
	private DynamixBitmap? LoadBitmap(string name) =>
		_manager.Read(_manager.BuildPath(name)) is { } bytes ? new DynamixBitmapTransformer().Parse(bytes) : null;

	/// <summary><c>0xf02f</c>'s load in place: the <c>.DBA</c>'s frames, or none.</summary>
	private IReadOnlyList<DynamixBitmap>? LoadBitmapArray(string name) =>
		_manager.Read(_manager.BuildPath(name)) is { } bytes ? new DynamixBitmapArrayTransformer().Parse(bytes)?.Images : null;

	/// <summary><c>0xf05f</c>'s load in place: the <c>.DPL</c>'s 256 colours, red, green and blue as the palette reader resolves them and a flag byte, or none.</summary>
	private uint[]? LoadPalette(string name) {
		if (_manager.Read(_manager.BuildPath(name)) is not { } bytes || new DynamixPaletteTransformer().Parse(bytes) is not { } palette) {
			return null;
		}

		var colours = new uint[256];
		foreach (var (index, entry) in palette.Colors) {
			if (index is >= 0 and < 256) {
				var c = entry.Array;
				colours[index] = c[0] | (uint)c[1] << 8 | (uint)c[2] << 16 | (c[3] != 0 ? 0x01000000u : 0);
			}
		}

		return colours;
	}

	/// <summary>The word at <paramref name="offset"/> of the script, 0 past its end.</summary>
	private ushort Word(int offset) =>
		offset >= 0 && offset + 1 < _script.Length ? BinaryPrimitives.ReadUInt16LittleEndian(_script.AsSpan(offset)) : (ushort)0;

	/// <summary>
	/// A string argument at <paramref name="offset"/>, as Latin-1 up to its NUL, and in <paramref name="next"/> the offset
	/// past it as <c>TtmScript_SkipString</c> steps: past the NUL and one byte more when the string's length is even.
	/// </summary>
	private string ReadString(int offset, out int next) {
		int end = offset;
		while (end < _script.Length && _script[end] != 0) {
			end++;
		}

		next = (end - offset) % 2 == 0 ? end + 2 : end + 1;
		return Encoding.Latin1.GetString(_script, offset, end - offset);
	}
}
