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
/// One <c>.TTM</c> script and its interpreter, in its manager's <see cref="Es1TtmMode"/>: the shell's
/// <c>TtmScript_Ctor</c> (<c>ES1GO 00046bb8</c>)'s <c>0x1eb</c> bytes, <c>TtmScript_Load</c> (<c>00047c28</c>),
/// <c>TtmScript_Run</c> (<c>00046f2c</c>), <c>TtmScript_NextPage</c> (<c>00046e7c</c>), <c>TtmScript_RunPage</c>
/// (<c>00048218</c>) and <c>TtmScript_RunDrawCommand</c> (<c>0004a213</c>) (docs/es1/formats/ttm-scripts.md#the-shell,
/// docs/es1/formats/ttm-scripts.md#the-commands); or the simulator's copy, <c>TtmScript_Ctor</c> (<c>ES1DBSIM
/// 000797ac</c>)'s <c>0x1c1</c> bytes and its <c>0007a54c</c>, <c>00079a14</c>, <c>0007aa58</c> and <c>0007c01c</c>,
/// which differs where docs/es1/formats/ttm-scripts.md#the-simulator says. The file is read through
/// <see cref="Es1TtmScriptTransformer"/>. Each member names the offsets of both copies; the branches on
/// <see cref="Es1TtmMode"/> are the load, the run, the gates of the commands that act once, the random choice and wait,
/// the fades, the palette commands, the fixed rect and the dirty boxes.
///
/// <para>A draw goes through the script's render context, <see cref="Context"/>, or in the shell the manager's
/// <see cref="Es1TtmManager.DefaultContext"/> when it has none: the clock's is the cockpit's canvas context, the shell's
/// default the full-screen context. So a script draws in the context's coordinates on the display's draw page, cut to
/// its rect. The shell's draws add their boxes to the shell's dirty list (<see cref="Es1TtmManager.Dirty"/>).
/// PLACEHOLDER: the simulator's <c>DirtyBox_Add</c> boxes are not kept; <c>es2_xref.py</c> finds no reference to the
/// simulator's <c>DirtyList_Flush</c> (<c>00083e80</c>), the copy that would read them
/// (docs/es1/rendering/screen-pages.md#open).</para>
///
/// <para>A run is an iterator of the monitor's retraces (<see cref="RunWaiting"/>): the shell's fades upload a palette a
/// step at a time, each upload waiting for a vertical retrace as the driver's palette routine does
/// (docs/es1/formats/ttm-scripts.md#the-fades), where retail spins inside the command. A retrace's own length is taken as
/// nil here, so two uploads would land in one retrace; each is taken to fill its retrace and the next waits for the
/// following one, an engine reading of the hardware's timing. The simulator's run yields none.</para>
///
/// <para>Engine guards, where retail reads or writes past its tables: a slot number past the six or twelve wraps, and a
/// bitmap draw's slot or frame out of range draws nothing.</para>
/// </summary>
public sealed class Es1TtmPlayer {
	/// <summary>The modes <c>+0xe</c> or <c>+0x3c</c>: playing, paused, hidden and finished.</summary>
	public const int Playing = 0, Paused = 1, Hidden = 2, Finished = 3;

	/// <summary>The bitmap-array and palette slots, and the regions.</summary>
	public const int SlotCount = 6, RegionCount = 12;

	/// <summary><c>TtmScript_CopyArgBytes</c>' most bytes, the point lists' buffer <c>000bbc18</c> or <c>000a0a28</c>.</summary>
	private const int PointBufferLength = 100;

	/// <summary>The scale <c>new DynamixPalette</c> takes before a <c>.DPL</c> sets it, for a slot that never held one.</summary>
	private const int UnscaledPalette = 1;

	private readonly Es1TtmManager _manager;
	private readonly bool _shell;
	private readonly IReadOnlyList<DynamixBitmap>?[] _bitmaps = new IReadOnlyList<DynamixBitmap>?[SlotCount];
	private readonly uint[]?[] _palettes = new uint[]?[SlotCount];
	private readonly int[] _paletteScales = new int[SlotCount];
	private readonly ushort[]?[] _saved = new ushort[]?[RegionCount];
	private readonly Es1Rect[] _regions = new Es1Rect[RegionCount];
	private readonly (short First, short Count, short Direction)[] _ranges = new (short, short, short)[3];
	private readonly byte[] _pointBuffer = new byte[PointBufferLength];
	private byte[] _script = [];
	private int[] _pages = [];

	// The context the run in progress draws through.
	private Es1PanelCanvas? _canvas;

	/// <summary>
	/// <c>TtmScript_Ctor</c>(0): the brushes' modes 0, 0 and 4, and in the simulator the origin and context 0. The twelve
	/// regions' rects, the bitmap-array and region slots and the shell's page-ran byte <c>+0x42</c>, which neither the
	/// constructor nor the load writes, start at 0 here.
	/// </summary>
	internal Es1TtmPlayer(Es1TtmManager manager) {
		_manager = manager;
		_shell = manager.Mode == Es1TtmMode.Shell;
		Array.Fill(_paletteScales, UnscaledPalette);
	}

	/// <summary>The shell's <c>+4</c>, the name the load was given.</summary>
	public string? Name { get; private set; }

	/// <summary><c>+0</c>, the page count: in the shell <c>PAG:</c>'s word, in the simulator a byte, its low byte.</summary>
	public short PageCount { get; private set; }

	/// <summary><c>+1</c> (a byte) or <c>+2</c> (a word), the current page.</summary>
	public short Page { get; private set; }

	/// <summary><c>+0xe</c> or <c>+0x3c</c>, the mode, <i>a</i> of the load.</summary>
	public int Mode { get; internal set; }

	/// <summary>
	/// <c>+0x12</c> or <c>+0x40</c>, set once a wait command has acted on this page. In the simulator a step to the next
	/// page clears it and a jump leaves it; the shell's every move clears it.
	/// </summary>
	public bool Once { get; private set; }

	/// <summary>The shell's <c>+0x42</c>, set once the page has run: the commands that load and save act while it is clear.</summary>
	public bool PageRan { get; private set; }

	/// <summary><c>+0x13</c> or <c>+0x43</c>, the loop flag, <i>b</i> of the load: with 1 the script starts again when it ends.</summary>
	public int Loop { get; internal set; }

	/// <summary><c>+0x25</c>, <c>+0x26</c>, <c>+0x27</c> or <c>+0x55</c>, <c>+0x56</c>, <c>+0x57</c>: the selected palette, bitmap-array and region slots.</summary>
	public byte PaletteSlot { get; private set; }

	/// <inheritdoc cref="PaletteSlot"/>
	public byte BitmapSlot { get; private set; }

	/// <inheritdoc cref="PaletteSlot"/>
	public byte RegionSlot { get; private set; }

	/// <summary><c>+0x178</c> or <c>+0x1ac</c>, the page a jump goes to next; −1 for none.</summary>
	public short NextPage { get; private set; } = -1;

	/// <summary>The shell's <c>+0x1ae</c>, the page a random choice goes to next; −1 for none.</summary>
	public short ChoicePage { get; private set; } = -1;

	/// <summary>
	/// <c>+0x17a</c> or <c>+0x1b0</c>, the call depth; <c>+0x17c</c>/<c>+0x1b2</c> and <c>+0x17e</c>/<c>+0x1b4</c>, the x
	/// and y a call adds; <c>+0x180</c>/<c>+0x1b6</c>, <c>0x3303</c>'s base page.
	/// </summary>
	public short CallDepth { get; private set; }

	/// <inheritdoc cref="CallDepth"/>
	public short CallX { get; private set; }

	/// <inheritdoc cref="CallDepth"/>
	public short CallY { get; private set; }

	/// <inheritdoc cref="CallDepth"/>
	public short BasePage { get; private set; }

	/// <summary><c>+0x182</c> or <c>+0x1b8</c>, the wait in ticks, and <c>+0x184</c> or <c>+0x1ba</c>, the count it reloads from.</summary>
	public short Wait { get; private set; }

	/// <inheritdoc cref="Wait"/>
	public short WaitReload { get; private set; }

	/// <summary>
	/// <c>+0x186</c>/<c>+0x1bc</c>, <c>+0x188</c>/<c>+0x1be</c>: the colour cycle's count of ticks and its reload;
	/// <c>+0x18a</c>/<c>+0x1c0</c>, set while colours cycle.
	/// </summary>
	public short CycleCount { get; private set; }

	/// <inheritdoc cref="CycleCount"/>
	public short CycleReload { get; private set; }

	/// <inheritdoc cref="CycleCount"/>
	public bool Cycling { get; private set; }

	/// <summary>
	/// The brushes' colours: <c>+0x1a1</c>/<c>+0x1d7</c> of the pen (mode 0), <c>+0x1a9</c>/<c>+0x1df</c> of the fill
	/// (mode 0) and <c>+0x1b1</c>/<c>+0x1e7</c> of the outline (mode 4).
	/// </summary>
	public int Foreground { get; private set; } = 10;

	/// <inheritdoc cref="Foreground"/>
	public int Background { get; private set; } = 10;

	/// <inheritdoc cref="Foreground"/>
	public int Outline { get; private set; } = 10;

	/// <summary>
	/// The simulator's <c>+0x1b5</c>, <c>+0x1b9</c>: the origin the bitmap draws add, which only the constructor writes, 0
	/// (docs/es1/formats/ttm-scripts.md#open). The shell has none.
	/// </summary>
	public (int X, int Y) Origin { get; } = (0, 0);

	/// <summary>
	/// <c>+0x1bd</c> or <c>+0x2c</c>, the render context the script draws through, made afresh for each run: the clock
	/// stores the simulator's, the shell's load takes <i>c</i>. With none a simulator script draws nothing (see
	/// <see cref="Es1TtmManager"/>) and a shell script draws through the manager's default.
	/// </summary>
	public Func<Es1PanelCanvas>? Context { get; set; }

	/// <summary>The bitmap array in slot <paramref name="slot"/>, or none.</summary>
	public IReadOnlyList<DynamixBitmap>? Bitmaps(int slot) => _bitmaps[slot];

	/// <summary>
	/// <c>TtmScript_Load</c>(script, <paramref name="mode"/>, <paramref name="loop"/>[, <paramref name="context"/>],
	/// <paramref name="name"/>) (docs/es1/formats/ttm-scripts.md#loading): in the shell the name kept; the file read through
	/// <see cref="Es1TtmScriptTransformer"/> from <c>TtmScript_BuildPath</c>'s path, a path that does not open answering
	/// 3 in the simulator and 1 in the shell, whose only test of the open it is; the simulator's 2 when <c>TT3:</c>'s method
	/// byte is not 1, which the shell does not read; 1 when the script does not unpack; the page count, the shell's word or
	/// the simulator's low byte; the slots, saved regions and cycling directions emptied, the loop flag and mode set, the
	/// once flag, page and palette slot 0, no jump (nor, in the shell, random choice), the shell's render context
	/// <paramref name="context"/>, the call, wait and cycle words 0, cycling off and the three colours 10; the pages
	/// indexed as <c>TtmScript_IndexPages</c> (<c>000480d1</c>, <c>ES1DBSIM 0007a940</c>) does, 2 when that fails. Answers
	/// 0 when loaded.
	/// </summary>
	internal int Load(int mode, int loop, Func<Es1PanelCanvas>? context, string name) {
		if (_shell) {
			Name = name;
		}

		if (_manager.Read(_manager.BuildPath(name)) is not { } bytes) {
			return _shell ? 1 : 3;
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

		if (!_shell && method != 1) {
			return 2;
		}

		if (file.Script is not { } script) {
			return 1;
		}

		_script = script;
		PageCount = _shell ? (short)pageCount : (byte)pageCount;
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
		if (_shell) {
			ChoicePage = -1;
			Context = context;
		}

		CallDepth = CallX = CallY = BasePage = 0;
		Wait = WaitReload = CycleReload = CycleCount = 0;
		Cycling = false;
		Foreground = Background = Outline = 10;
		if (Es1TtmScriptTransformer.IndexPages(script, Math.Max((short)0, PageCount)) is not { } pages) {
			return 2;
		}

		_pages = pages;
		return 0;
	}

	/// <summary><c>TtmScript_Run</c>(script, <paramref name="ticks"/>) with its waits passed over (<see cref="RunWaiting"/>), as the simulator's run, which has none.</summary>
	public void Run(short ticks) {
		foreach (var _ in RunWaiting(ticks)) {
		}
	}

	/// <summary>
	/// <c>TtmScript_Run</c>(script, <paramref name="ticks"/>), the script's or the default render context installed for the
	/// run, in the manager's mode: <see cref="RunShell"/> or <see cref="RunSimulator"/>. The retraces are those the shell's
	/// fades wait for.
	/// </summary>
	public IEnumerable<Es1Retrace> RunWaiting(short ticks) {
		_canvas = (_shell ? Context ?? _manager.DefaultContext : Context)?.Invoke();
		try {
			foreach (var retrace in _shell ? RunShell(ticks) : RunSimulator(ticks)) {
				yield return retrace;
			}
		} finally {
			_canvas = null;
		}
	}

	/// <summary>
	/// The shell's run (docs/es1/formats/ttm-scripts.md#a-run): the ticks taken off the wait; while it stays above 0 or the
	/// page has not run, unless the mode is <see cref="Hidden"/>, the page run, <see cref="PageRan"/> set, and when the wait
	/// is then 0 or less, the wait reloaded and <see cref="StepPage"/>; once it has run out on a page that has run, the
	/// cycle's count taken down by the ticks while <see cref="Cycling"/> is 1, reloaded and the palette stepped when it falls
	/// below 0, the wait reloaded, then by the mode: <see cref="Playing"/> runs the page and steps; <see cref="Paused"/> runs
	/// the page; <see cref="Finished"/> with the loop flag 1 resets the script and plays it, and otherwise hides it.
	/// </summary>
	private IEnumerable<Es1Retrace> RunShell(short ticks) {
		Wait -= ticks;
		if (Wait < 1 && PageRan) {
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
					foreach (var retrace in RunPage(Page)) {
						yield return retrace;
					}

					StepPage();
					break;
				case Paused:
					foreach (var retrace in RunPage(Page)) {
						yield return retrace;
					}

					break;
				case Finished:
					if (Loop == 1) {
						Reset();
						Mode = Playing;
					} else {
						Mode = Hidden;
					}

					break;
			}

			yield break;
		}

		if (Mode == Hidden) {
			yield break;
		}

		foreach (var retrace in RunPage(Page)) {
			yield return retrace;
		}

		PageRan = true;
		if (Wait < 1) {
			Wait = WaitReload;
			StepPage();
		}
	}

	/// <summary>
	/// <c>TtmScript_NextPage</c> (<c>00046e7c</c>), the shell's: the jump's page when one is set, clearing it; else the
	/// random choice's likewise; else the next page unless the mode is <see cref="Finished"/>. The once, sound and
	/// page-ran flags cleared. At or past the page count, with the loop flag 1, the script reset and played, and
	/// otherwise hidden, the script left as it is.
	/// </summary>
	private void StepPage() {
		if (NextPage != -1) {
			Page = NextPage;
			NextPage = -1;
		} else if (ChoicePage != -1) {
			Page = ChoicePage;
			ChoicePage = -1;
		} else if (Mode != Finished) {
			Page++;
		}

		Once = false;
		PageRan = false;
		if (PageCount <= Page) {
			if (Loop == 1) {
				Reset();
				Mode = Playing;
			} else {
				Mode = Hidden;
			}
		}
	}

	/// <summary>
	/// The simulator's run (docs/es1/formats/ttm-scripts.md#the-simulators-run): the ticks taken off the wait; while it
	/// stays above 0, the page run unless the mode is <see cref="Hidden"/>; once it is 0 or less, the cycle's count taken
	/// down by the ticks while colours cycle, reloaded and the palette stepped when it falls below 0
	/// (<c>TtmScript_CyclePalette</c>, <c>00079d90</c>), the wait reloaded, then by the mode: <see cref="Playing"/> runs
	/// the page and moves on, to the jump's page when one is set (clearing it) or else the next (clearing
	/// <see cref="Once"/>), and past the last page ends the script; <see cref="Paused"/> runs the page;
	/// <see cref="Finished"/> ends the script; <see cref="Hidden"/> does nothing. The script's end: with the loop flag 1,
	/// <c>TtmScript_Reset</c> and <see cref="Playing"/>; otherwise <see cref="Hidden"/> alone, the script not reset.
	/// </summary>
	private IEnumerable<Es1Retrace> RunSimulator(short ticks) {
		Wait -= ticks;
		if (Wait >= 1) {
			if (Mode != Hidden) {
				foreach (var retrace in RunPage(Page)) {
					yield return retrace;
				}
			}

			yield break;
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
				foreach (var retrace in RunPage(Page)) {
					yield return retrace;
				}

				if (NextPage == -1) {
					Page = (byte)(Page + 1);
					Once = false;
				} else {
					Page = (byte)NextPage;
					NextPage = -1;
				}

				if ((byte)Page >= (byte)PageCount) {
					End();
				}

				break;
			case Paused:
				foreach (var retrace in RunPage(Page)) {
					yield return retrace;
				}

				break;
			case Finished:
				End();
				break;
		}

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
	/// <c>TtmScript_SetPage</c>: the shell's (<c>000470e7</c>), below the page count, moves to <paramref name="page"/>,
	/// reloads the wait, clears the once, sound and page-ran flags and sets the mode to <see cref="Playing"/>; the
	/// simulator's <c>Ttm_SetPage</c> (<c>ES1DBSIM 00069904</c>) only stores the page. Answers whether the page was below
	/// the count.
	/// </summary>
	internal bool SetPage(short page) {
		if (_shell ? (ushort)page >= PageCount : (byte)page >= (byte)PageCount) {
			return false;
		}

		Page = page;
		if (_shell) {
			Wait = WaitReload;
			Once = false;
			PageRan = false;
			Mode = Playing;
		}

		return true;
	}

	/// <summary>
	/// <c>TtmScript_Reset</c> (<c>0004b3c6</c>, <c>ES1DBSIM 0007ca44</c>): the six palettes, the six bitmap arrays and the
	/// twelve saved regions destroyed and their slots emptied; the cycling directions 0; the mode <see cref="Hidden"/>;
	/// the page and palette slot 0; no jump; the call, wait and cycle words 0 and cycling off; the three colours 10. The
	/// shell's also clears <see cref="Once"/>, which the simulator's leaves; both leave the shell's random choice and
	/// <see cref="PageRan"/>.
	/// </summary>
	public void Reset() {
		Array.Clear(_palettes);
		Array.Clear(_bitmaps);
		Array.Clear(_saved);
		for (int i = 0; i < _ranges.Length; i++) {
			_ranges[i].Direction = 0;
		}

		Mode = Hidden;
		if (_shell) {
			Once = false;
		}

		Page = 0;
		PaletteSlot = 0;
		NextPage = -1;
		CallDepth = CallX = CallY = BasePage = 0;
		Wait = WaitReload = CycleReload = CycleCount = 0;
		Cycling = false;
		Foreground = Background = Outline = 10;
	}

	/// <summary>
	/// <c>TtmScript_FindLabel</c> (<c>0004741a</c>, <c>ES1DBSIM 00079ef4</c>)(script, <paramref name="label"/>): the first
	/// page whose first command is <c>0x1101</c> or <c>0x1111</c> with the label, the simulator's as a byte; 0 when none
	/// is, the shell's message formatted into a buffer it does not use.
	/// </summary>
	public short FindLabel(short label) {
		for (int page = 0; page < PageCount && page < _pages.Length; page++) {
			ushort opcode = Word(_pages[page]);
			if (opcode is 0x1101 or 0x1111 && (short)Word(_pages[page] + 2) == label) {
				return PageNumber(page);
			}
		}

		return 0;
	}

	/// <summary>A page number as the copy keeps it: a word in the shell, a byte in the simulator.</summary>
	private short PageNumber(int page) => _shell ? (short)page : (byte)page;

	/// <summary>
	/// <c>TtmScript_RunPage</c>(script, <paramref name="page"/>), the page's commands from its start until <c>0x0ff0</c>
	/// (docs/es1/formats/ttm-scripts.md#the-interpreter, docs/es1/formats/ttm-scripts.md#the-simulators-commands):
	/// <c>0x1301</c> and <c>0x1311</c> renumbered to <c>0xc051</c> and <c>0xc061</c>; the seven words after the opcode
	/// read as <i>a1</i> to <i>a7</i>, whatever its count, and the walk stepped past its arguments by its low four bits,
	/// past a point list's count and 2<i>n</i> words, <c>TtmScript_CopyArgBytes</c> copying its first (4<i>n</i> &amp;
	/// <c>0xff</c>) bytes, at most 100, to the buffer, or past a string as <c>TtmScript_SkipString</c> steps; during a
	/// call, <i>a1</i> and <i>a2</i> of a draw other than <c>0xa601</c>, <c>0xaf1f</c> and <c>0xaf2f</c> moved by the
	/// call's x and y, and <i>a3</i> and <i>a4</i> too for <c>0xa0a4</c>; then <c>0xc000</c>–<c>0xcfff</c> to the sound
	/// commands, which the simulator steps over, <c>0xa000</c>–<c>0xafff</c> to <see cref="RunDrawCommand"/>, and any
	/// other to its case or, with none, stepped over. The random choice's five sums and flag are this call's own. A word
	/// read past the script's end reads 0 here.
	/// </summary>
	private IEnumerable<Es1Retrace> RunPage(short page) {
		if (page < 0 || page >= _pages.Length) {
			yield break;
		}

		var sums = new short[5];
		bool summed = false;
		int p = _pages[page];
		while (true) {
			ushort opcode = Word(p);
			if (opcode == 0x1301) {
				opcode = 0xc051;
			} else if (opcode == 0x1311) {
				opcode = 0xc061;
			}

			if (opcode == 0x0ff0) {
				yield break;
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
					// The shell's TtmScript_RunSoundCommand (0004a0e0) acts while the byte 0007fcb9 is set: 0xc21f makes the sound
					// object +0x1a8 through the sound manager and 0xc220 plays it, setting +0x41. PLACEHOLDER: ES1's sound is not
					// ported (Es1ShellDevices.Init's ShellSound_Init), so no sound object is made and a script plays nothing. The
					// simulator steps over these opcodes.
					continue;
				case 0xa000:
					RunDrawCommand(opcode, a1, a2, a3, a4, a5, a6, a7);
					continue;
			}

			switch (opcode) {
				case 0x3003: {
					CallDepth++;
					CallX = a1;
					CallY = a2;
					foreach (var retrace in RunPage(FindLabel(a3))) {
						yield return retrace;
					}

					CallDepth--;
					continue;
				}
				case 0x3303:
					if (Wait < 1 && CallDepth == 0 && BasePage + a3 != Page) {
						CallDepth++;
						CallX = a1;
						CallY = a2;
						foreach (var retrace in RunPage(PageNumber(BasePage + a3))) {
							yield return retrace;
						}

						CallDepth--;
					}

					continue;
				case 0x4114 when _shell:
					if (!PageRan) {
						foreach (var retrace in FadeOutShell(a1, a2, a3, a4)) {
							yield return retrace;
						}
					}

					continue;
				case 0x4124 when _shell:
					if (!PageRan) {
						foreach (var retrace in FadeInShell(a1, a2, a3, a4)) {
							yield return retrace;
						}
					}

					continue;
				case 0x5005 when _shell:
					ChooseRandomly(sums, ref summed, a1, a2, a3, a4, a5);
					continue;
			}

			RunCommand(opcode, a1, a2, a3, a4, text);
		}
	}

	/// <summary>
	/// <c>0x5005</c>, the shell's random choice (docs/es1/formats/ttm-scripts.md#the-random-choice): with
	/// <paramref name="summed"/> clear or a choice already made, the running sums of the five arguments kept and the flag
	/// set; otherwise <i>r</i> drawn below the fifth sum and the choice made the page labelled by the argument in the place
	/// of the first sum above <i>r</i>, or by the first argument when none is. The sums are words, compared signed.
	/// </summary>
	private void ChooseRandomly(short[] sums, ref bool summed, short a1, short a2, short a3, short a4, short a5) {
		if (!summed || ChoicePage != -1) {
			sums[0] = a1;
			sums[1] = (short)(sums[0] + a2);
			sums[2] = (short)(sums[1] + a3);
			sums[3] = (short)(sums[2] + a4);
			sums[4] = (short)(sums[3] + a5);
			summed = true;
			return;
		}

		short r = (short)(_manager.RandomBelow?.Invoke(sums[4]) ?? 0);
		short[] labels = [a1, a2, a3, a4, a5];
		int place = Array.FindIndex(sums, sum => sum > r);
		ChoicePage = FindLabel(labels[place < 0 ? 0 : place]);
	}

	/// <summary>
	/// The commands of the table at <c>00083880</c> (the shell's 30) or <c>0009ffcc</c> (the simulator's, less
	/// <c>0x5005</c>) other than the calls, the fades and the random choice (docs/es1/formats/ttm-scripts.md#the-interpreter,
	/// docs/es1/formats/ttm-scripts.md#the-simulators-commands). The loads, saves and fixed rects act on the shell's first
	/// run of the page and while the simulator's wait is 0 or less (<c>load</c> below); the palette commands put the
	/// shell's palette at <c>0007fcd0</c> for the next frame and upload the simulator's.
	/// </summary>
	private void RunCommand(ushort opcode, short a1, short a2, short a3, short a4, string? text) {
		bool noWait = Wait < 1;
		bool load = _shell ? !PageRan : noWait;
		switch (opcode) {
			case 0x2002:
				Foreground = a1;
				Background = a2;
				Outline = a1;
				break;
			case 0xf01f when load:
				if (text != null && LoadBitmap(text) is { } screen) {
					_canvas?.Blit(screen, 0, 0);
					int columns = screen.Cols, rows = screen.Rows;
					FixRect(new Es1Rect(Math.Min(0, columns), Math.Min(0, rows), Math.Max(0, columns), Math.Max(0, rows)));
				}

				break;
			case 0x1051:
				BitmapSlot = (byte)a1;
				break;
			case 0xf02f when load:
				_bitmaps[BitmapSlot % SlotCount] = text != null ? LoadBitmapArray(text) : null;
				break;
			case 0x0080 when _shell || noWait:
				_bitmaps[BitmapSlot % SlotCount] = null;
				break;
			case 0x1121:
				RegionSlot = (byte)a1;
				break;
			case 0x4214 when load: {
				int slot = RegionSlot % RegionCount;
				_regions[slot] = new Es1Rect(a1, a2, a3 + a1, a4 + a2);
				if (_shell) {
					// The display's source page made the background for the save, the render context's clip mode +0x208 0
					// while it saves; a full-screen context cuts nothing either way.
					var display = _manager.Display;
					int source = display.SourcePage;
					display.SourcePage = _manager.BackgroundPage;
					_saved[slot] = _canvas?.SaveRegion(_regions[slot]);
					display.SourcePage = source;
				} else {
					_saved[slot] = _canvas?.SaveRegion(_regions[slot]);
				}

				break;
			}
			case 0x00c0 when load:
				_saved[RegionSlot % RegionCount] = null;
				break;
			case 0x1061 when load:
				PaletteSlot = (byte)a1;
				if (SelectedPalette is { } selected) {
					if (_shell) {
						Present(selected);
					} else {
						Upload(selected);
					}
				}

				break;
			case 0xf05f when load:
				SelectedPalette = text != null ? LoadPalette(text, out _paletteScales[PaletteSlot % SlotCount]) : null;
				if (_shell) {
					Present(SelectedPalette);
				} else if (SelectedPalette is { } loaded) {
					Upload(loaded);
				}

				break;
			case 0x0070 when load:
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
					WaitReload = Wait = RandomWait(a1, a2);
				}

				break;
			case 0x1021:
				if (!Once) {
					Once = true;
					WaitReload = Wait = a1;
				}

				break;
			case 0x0020 when load:
				FixRect(new Es1Rect(0, 0, Es1CockpitViews.ScreenWidth - 1, Es1CockpitViews.ScreenHeight - 1));
				break;
			case 0x4204 when load:
				FixRect(Normalized(a1, a2, a3, a4));
				break;
			case 0x0110:
				Mode = Finished;
				break;
			case 0x3201:
				BasePage = FindLabel(a1);
				break;
			case 0x1201:
				NextPage = FindLabel(a1);
				break;
		}
	}

	/// <summary>
	/// <c>0x2022</c>'s wait from <paramref name="min"/> and <paramref name="max"/>: the shell's <i>min</i> +
	/// <c>Math_RandomBelow</c>(<c>Shell_RandomState</c>, <i>max</i> − <i>min</i> + 1), which reaches <i>max</i>; the
	/// simulator's <i>min</i> + |<c>rand</c>() mod (<i>max</i> − <i>min</i>)|, below it.
	/// </summary>
	private short RandomWait(short min, short max) {
		if (_shell) {
			return (short)(min + (_manager.RandomBelow?.Invoke((short)(max - min + 1)) ?? 0));
		}

		// max equal to min divides by zero in retail; min here, an engine guard.
		int span = max - min;
		return (short)(min + (span == 0 ? 0 : Math.Abs(_manager.Rand() % span)));
	}

	/// <summary>
	/// <c>TtmScript_RunDrawCommand</c>, the table of 22 at <c>000839b8</c> or <c>000a00b4</c>, in the script's context,
	/// each colour taken by the brush rule (<see cref="Colour"/>): <c>0xa002</c> a pixel and <c>0xa0a4</c> a line in the
	/// pen's; <c>0xa104</c> and <c>0xa404</c> the rect or ellipse of (<i>x</i>, <i>y</i>) to (<i>x</i> + <i>w</i>,
	/// <i>y</i> + <i>h</i>) filled in the fill's and then <c>0xa114</c>'s and <c>0xa424</c>'s outline in the outline's;
	/// the bitmap draws; <c>0xa601</c>, while the wait is 0 or less, the selected region's saved pixels put back, the
	/// shell's at the region's corner and the simulator's at (0, 0), and its rect fixed; and the polygons
	/// (docs/es1/formats/ttm-scripts.md#the-commands). In the shell each but <c>0xa601</c> adds the box it drew to the dirty
	/// list at a delay of 1 (<see cref="AddDrawnBox"/>), and <c>0xa601</c> its rect.
	/// </summary>
	private void RunDrawCommand(ushort opcode, short a1, short a2, short a3, short a4, short a5, short a6, short a7) {
		var canvas = _canvas;
		switch (opcode) {
			case 0xa0a4:
				if (Colour(Foreground) is { } line) {
					canvas?.DrawLine(a1, a2, a3, a4, line);
				}

				AddDrawnBox(Math.Min(a1, a3), Math.Min(a2, a4), Math.Abs(a3 - a1) + 1, Math.Abs(a4 - a2) + 1);
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

				AddDrawnBox(a1, a2, a3 + 1, a4 + 1);
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

				AddDrawnBox(a1, a2, a3 + 1, a4 + 1);
				break;
			case 0xaf1f or 0xaf2f:
				DrawPolygon(opcode, a1);
				break;
			case 0xa002:
				if (Colour(Foreground) is { } dot) {
					canvas?.Plot(a1, a2, dot);
				}

				AddDrawnBox(a1, a2, 1, 1);
				break;
			case >= 0xa504 and <= 0xa536 when (opcode & 0xf) is 4 or 5 or 6 && ((opcode >> 4) & 0xf) <= 3:
			case 0xa5a7:
				DrawBitmap(opcode, a1, a2, a3, a4, a5, a6, a7);
				break;
			case 0xa601:
				if (Wait < 1 && _saved[RegionSlot % RegionCount] is { } cells) {
					var region = _regions[RegionSlot % RegionCount];
					if (_shell) {
						canvas?.RestoreRegion(region, region.X0, region.Y0, cells);
						FixRect(region);
						_manager.Dirty?.AddRect(region.X0, region.Y0, region.X1, region.Y1, 1);
					} else {
						canvas?.RestoreRegion(region, 0, 0, cells);
						FixRect(region);
					}
				}

				break;
		}
	}

	/// <summary>
	/// <c>DirtyBox_Add</c>(1, <paramref name="height"/>, <paramref name="width"/>, <paramref name="y"/>,
	/// <paramref name="x"/>), a drawing command's box, kept in the shell's dirty list only (see the class summary).
	/// </summary>
	private void AddDrawnBox(int x, int y, int width, int height) => _manager.Dirty?.Add(x, y, width, height, 1);

	/// <summary>
	/// The bitmap draws' one case, <c>0004ad68</c> or <c>0007c674</c>: the flags 0, 1 and 2 from bits 4 to 7 of the
	/// opcode, 3 for any other value; when slot <paramref name="slot"/> holds an array of more than
	/// <paramref name="frame"/> bitmaps, by the low four bits: 4 <c>Bitmap_Blit</c> at (<i>x</i>, <i>y</i>) plus the
	/// simulator's origin, its box the bitmap's size; 6 <c>Bitmap_BlitScaled</c> to <paramref name="width"/> ×
	/// <paramref name="height"/> there, its box that size; 5 nothing; and 7 <see cref="DrawRotated"/>.
	/// </summary>
	private void DrawBitmap(ushort opcode, short x, short y, short frame, short slot, short width, short height, short angle) {
		int bits = (opcode & 0xf0) >> 4;
		int flags = bits <= 2 ? bits : 3;
		if (slot < 0 || slot >= SlotCount || _bitmaps[slot] is not { } array || frame < 0 || frame >= array.Count) {
			return;
		}

		var bitmap = array[frame];
		switch (opcode & 0xf) {
			case 7:
				DrawRotated(bitmap, x, y, width, height, angle);
				break;
			case 6:
				_canvas?.BlitScaled(bitmap, width, height, flags, x + Origin.X, y + Origin.Y);
				AddDrawnBox(x, y, width, height);
				break;
			case 5:
				break;
			default:
				_canvas?.Blit(bitmap, x + Origin.X, y + Origin.Y, flags);
				AddDrawnBox(x, y, bitmap.Cols, bitmap.Rows);
				break;
		}
	}

	/// <summary>
	/// <c>TOKEN_DRAW_ROT_BRUSH</c>'s draw (docs/es1/formats/ttm-scripts.md#the-commands): with <i>p</i> = (−<i>w</i>
	/// &gt;&gt; 1, −<i>h</i> &gt;&gt; 1), <c>Transform2D_ApplyToPoint</c> of <i>p</i> through
	/// <c>Math_BuildRotation2D</c>(<paramref name="angle"/>) with the translation <i>p</i>, added to (<i>x</i>, <i>y</i>)
	/// less <i>p</i>, and the script's origin, gives the corner, so the turned bitmap is centred on (<i>x</i>, <i>y</i>);
	/// there <c>Bitmap_BlitRotatedUnpacked</c>(bitmap, <paramref name="angle"/>, (<i>w</i>, <i>h</i>), 0, corner), the
	/// shell's or the simulator's (<see cref="Es1BitmapFill.BlitRotatedScaled"/>). Its box is the square of twice the
	/// larger of <i>w</i> and <i>h</i> about the corner, in words.
	/// </summary>
	private void DrawRotated(DynamixBitmap bitmap, short x, short y, short width, short height, short angle) {
		var half = (X: -(width >> 1), Y: -(height >> 1));
		var turned = Es1BitmapFill.Rotate2DPoint(half, Es1BitmapFill.Rotation2D(angle, _shell));
		var corner = unchecked((X: x - half.X + turned.X + half.X + Origin.X, Y: y - half.Y + turned.Y + half.Y + Origin.Y));
		if (_canvas is { } canvas) {
			Es1BitmapFill.BlitRotatedScaled(canvas, bitmap, angle, (width, height), 0, corner, _shell);
		}

		short larger = Math.Max(width, height);
		AddDrawnBox((short)(corner.X - larger), (short)(corner.Y - larger), (short)(larger * 2), (short)(larger * 2));
	}

	/// <summary>
	/// <c>0xaf1f</c> and <c>0xaf2f</c>: <paramref name="count"/> points from the buffer, the x values and then the y
	/// values; <c>0xaf2f</c> fills them in the fill's colour first; then the outline's line loop, each through
	/// <c>Raster_DrawPolygonEitherWinding</c>; the box of the points' least and greatest x and y, the least from
	/// <c>0x7fff</c> and the greatest from 0. A value past the buffer's 100 bytes reads 0 here, where retail reads on
	/// past it.
	/// </summary>
	private void DrawPolygon(ushort opcode, short count) {
		var points = new List<(int X, int Y)>();
		short minX = short.MaxValue, minY = short.MaxValue, maxX = 0, maxY = 0;
		for (int i = 0; i < count; i++) {
			short x = PointWord(i), y = PointWord(count + i);
			points.Add((x, y));
			minX = Math.Min(minX, x);
			maxX = Math.Max(maxX, x);
			minY = Math.Min(minY, y);
			maxY = Math.Max(maxY, y);
		}

		if (opcode == 0xaf2f && Colour(Background) is { } fill) {
			_canvas?.DrawPolygon(points, fill, outline: false);
		}

		if (Colour(Outline) is { } edge) {
			_canvas?.DrawPolygon(points, edge, outline: true);
		}

		AddDrawnBox(minX, minY, maxX - minX + 1, maxY - minY + 1);

		short PointWord(int index) => 2 * index + 1 < PointBufferLength
			? BinaryPrimitives.ReadInt16LittleEndian(_pointBuffer.AsSpan(2 * index))
			: (short)0;
	}

	/// <summary>
	/// <c>TtmScript_FixRect</c>(script, <paramref name="rect"/>), through the script's context. The shell's
	/// (<c>000472a2</c>) is <c>Display_CopyRectToPage</c>(<see cref="Es1TtmManager.BackgroundPage"/>, the draw page, rect)
	/// (docs/es1/rendering/screen-pages.md#fixing-a-rect): the source page made the draw page and the draw page the
	/// background, the rect copied at (0, 0), added to the dirty list at a delay of 0, and the pages' low bytes put back.
	/// The simulator's (<c>ES1DBSIM 00079c38</c>) makes the source page the low byte of the draw page and keeps no box.
	/// </summary>
	private void FixRect(Es1Rect rect) {
		var display = _manager.Display;
		int source = display.SourcePage, draw = display.DrawPage;
		display.SourcePage = _shell ? draw : draw & 0xff;
		display.DrawPage = _manager.BackgroundPage;
		_canvas?.CopyRegion(rect, 0, 0);
		if (_shell) {
			_manager.Dirty?.AddRect(rect.X0, rect.Y0, rect.X1, rect.Y1, 0);
		}

		display.SourcePage = source & 0xff;
		display.DrawPage = draw & 0xff;
	}

	/// <summary>
	/// <c>TtmScript_CyclePalette</c> (<c>000472d0</c>, <c>ES1DBSIM 00079d90</c>): each range whose direction <i>d</i> is
	/// not 0 rotated by one place in the selected palette, from <i>first</i>, or <i>first</i> + <i>count</i> − 1 with
	/// <i>d</i> −1 (docs/es1/formats/ttm-scripts.md#cycling-colours), then the palette uploaded. A colour past the
	/// palette's 256 is passed over here, where retail reads and writes beyond it; with no palette selected nothing is
	/// done, where retail reads through the empty slot.
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
	/// The shell's <c>0x4114</c> (<c>00048c35</c>), on the page's first run (docs/es1/formats/ttm-scripts.md#the-fades):
	/// the fade flag set; a copy of the selected palette; for <i>percent</i> from 100 down to 0 by 6 −
	/// <paramref name="speed"/>, colours <paramref name="first"/> to <paramref name="first"/> + <paramref name="count"/>
	/// blended toward the selected palette's colour <paramref name="colour"/> into the copy (<see cref="BlendRange"/>) and
	/// uploaded; then with the source page the low byte of the draw page and the draw page the visible page, the full
	/// screen copied through the context, and again with the draw page <see cref="Es1TtmManager.BackgroundPage"/>; the
	/// pages' low bytes put back, the selected palette uploaded and the flag cleared. Each upload waits for a retrace (see
	/// the class summary). With no palette selected nothing is done, where retail reads through the empty slot; a step of 0
	/// or less, which loops for ever in retail, makes one pass here.
	/// </summary>
	private IEnumerable<Es1Retrace> FadeOutShell(short first, short count, short colour, short speed) {
		if (SelectedPalette is not { } selected) {
			yield break;
		}

		_manager.FadeActive = true;
		var copy = (uint[])selected.Clone();
		short step = (short)(6 - speed);
		for (short percent = 100; percent >= 0; percent = (short)(percent - step)) {
			BlendRange(TargetColour(selected, colour), percent, count, first, selected, copy);
			yield return default;
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
		yield return default;
		Upload(selected);
		_manager.FadeActive = false;
	}

	/// <summary>
	/// The shell's <c>0x4124</c> (<c>0004933b</c>), on the page's first run: the fade flag set; a copy of the selected
	/// palette blended at 0 percent and uploaded; the full screen copied through the context from
	/// <see cref="Es1TtmManager.BackgroundPage"/> onto the visible page, the pages' low bytes put back; for <i>percent</i>
	/// from 6 − <paramref name="speed"/> while below 101 by that step, blended and uploaded; the selected palette uploaded
	/// and the flag cleared. The waits and guards are <see cref="FadeOutShell"/>'s.
	/// </summary>
	private IEnumerable<Es1Retrace> FadeInShell(short first, short count, short colour, short speed) {
		if (SelectedPalette is not { } selected) {
			yield break;
		}

		_manager.FadeActive = true;
		var copy = (uint[])selected.Clone();
		BlendRange(TargetColour(selected, colour), 0, count, first, selected, copy);
		yield return default;
		Upload(copy);
		var display = _manager.Display;
		int source = display.SourcePage, draw = display.DrawPage;
		display.SourcePage = _manager.BackgroundPage;
		display.DrawPage = display.VisiblePage;
		_canvas?.CopyRegion(FullScreen, 0, 0);
		display.SourcePage = source & 0xff;
		display.DrawPage = draw & 0xff;
		short step = (short)(6 - speed);
		for (short percent = step; percent < 101; percent = (short)(percent + step)) {
			BlendRange(TargetColour(selected, colour), percent, count, first, selected, copy);
			yield return default;
			Upload(copy);
			if (step <= 0) {
				break;
			}
		}

		yield return default;
		Upload(selected);
		_manager.FadeActive = false;
	}

	/// <summary>The selected palette's colour <paramref name="index"/>, the fades' target; 0 past the palette here, where retail reads beyond it.</summary>
	private static uint TargetColour(uint[] palette, short index) => index is >= 0 and < 256 ? palette[index] : 0;

	/// <summary>
	/// <c>Palette_BlendRange</c> (<c>0004b532</c>)(script, <paramref name="target"/>, <paramref name="percent"/>,
	/// <paramref name="count"/>, <paramref name="first"/>, <paramref name="from"/>, <paramref name="to"/>): with
	/// <i>k</i> = (the percent's low byte, signed, &lt;&lt; 8) / 100, each colour from the first's low byte to it plus the
	/// count's inclusive set in <paramref name="to"/> to <paramref name="from"/>'s with each channel the target's plus
	/// (<i>k</i> × (channel − target's)) shifted right 8 as unsigned, the three or'd at bits 0, 8 and 16, cut to 24 bits
	/// and or'd with <c>0x1000000</c>. A channel below the target's sets bits above its own
	/// (docs/es1/formats/ttm-scripts.md#the-fades). An index past the palette's 256 is passed over here.
	/// </summary>
	private static void BlendRange(uint target, short percent, short count, short first, uint[] from, uint[] to) {
		uint k = (uint)(((sbyte)percent << 8) / 100);
		uint r0 = target & 0xff, g0 = (target >> 8) & 0xff, b0 = (target >> 16) & 0xff;
		int last = (byte)first + (byte)count;
		for (int index = (byte)first; index <= last && index < 256; index++) {
			uint colour = from[index];
			uint r = ((k * ((colour & 0xff) - r0)) >> 8) + r0;
			uint g = ((k * (((colour >> 8) & 0xff) - g0)) >> 8) + g0;
			uint b = ((k * (((colour >> 16) & 0xff) - b0)) >> 8) + b0;
			to[index] = ((b << 16) | (g << 8) | r) & 0xffffff | 0x1000000;
		}
	}

	/// <summary>
	/// The simulator's <c>0x4114</c>, <c>0007b348</c>, on every run: a copy of the selected palette
	/// (<c>GLPalette_Assign</c>, <c>0007bfdc</c>); for <i>percent</i> from 100 down to 0 by 6 − <paramref name="speed"/>,
	/// colours <paramref name="first"/> to <paramref name="last"/> scaled into the copy (<see cref="ScaleRange"/>) and
	/// uploaded; then with the source page the low byte of the draw page and the draw page the visible page, the full
	/// screen (0, 0) to (319, 199) copied through the context, and again with the draw page
	/// <see cref="Es1TtmManager.BackgroundPage"/>; the two pages' low bytes put back and the selected palette uploaded
	/// (docs/es1/formats/ttm-scripts.md#the-simulators-fades). With no palette selected nothing is done, where retail reads
	/// through the empty slot; a step of 0 or less, which loops for ever in retail, makes one pass here.
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
	/// The simulator's <c>0x4124</c>, <c>0007b73a</c>, while the wait is 0 or less: a copy of the selected palette with all
	/// 256 colours <c>0x01000000</c>, uploaded; the selected colours copied in; the full screen copied through the context
	/// from <see cref="Es1TtmManager.BackgroundPage"/> onto the visible page, the pages' low bytes put back; for
	/// <i>percent</i> from 0 up to 100 by 6 − <paramref name="speed"/>, the colours scaled and uploaded; and the selected
	/// palette uploaded. The guards are <see cref="FadeOut"/>'s.
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
	/// <c>Palette_ScaleRange</c> (<c>ES1DBSIM 0007cc24</c>)(script, <paramref name="percent"/>, <paramref name="last"/>,
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

	/// <summary>The palette in the selected slot <c>+0x25</c> or <c>+0x55</c>, 256 colours of red, green, blue and a flag byte.</summary>
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

	/// <summary><c>Palette_UploadWhole</c> of <paramref name="colours"/>: <see cref="ToPalette"/> sent to <see cref="Es1TtmManager.UploadPalette"/>.</summary>
	private void Upload(uint[] colours) => _manager.UploadPalette?.Invoke(ToPalette(colours));

	/// <summary>The shell's store of <paramref name="colours"/> at <c>0007fcd0</c> with <c>0007fcd4</c> set, through <see cref="Es1TtmManager.PresentPalette"/>; none for a load that failed.</summary>
	private void Present(uint[]? colours) => _manager.PresentPalette?.Invoke(colours != null ? ToPalette(colours) : null);

	/// <summary>
	/// The palette of retail's 256 dwords <paramref name="colours"/>, six-bit red, green and blue from the low byte up,
	/// shown scaled by the selected slot's palette's <see cref="DynamixPalette.Scalar"/> as the palette reader scales a
	/// <c>.DPL</c>'s, each channel at most 255.
	/// </summary>
	private DynamixPalette ToPalette(uint[] colours) {
		int scale = _paletteScales[PaletteSlot % SlotCount];
		var palette = new DynamixPalette { ColorCount = 256, Scalar = scale };
		for (int index = 0; index < 256; index++) {
			byte r = (byte)colours[index], g = (byte)(colours[index] >> 8), b = (byte)(colours[index] >> 16);
			var entry = new ColorBytes(r, g, b, 255);
			entry.SetColor(RgbaColor.FromArgb(255, Math.Min(r * scale, 255), Math.Min(g * scale, 255), Math.Min(b * scale, 255)));
			palette.Colors[index] = entry;
		}

		return palette;
	}

	/// <summary><c>0xf01f</c>'s bitmap, <c>Resource_LoadBitmap</c> or the simulator's load in place (<c>VolRStream_CtorFromPath</c> and <c>ClassItem_LoadResource</c>): the <c>.DBM</c>, or none.</summary>
	private DynamixBitmap? LoadBitmap(string name) =>
		_manager.Read(_manager.BuildPath(name)) is { } bytes ? new DynamixBitmapTransformer().Parse(bytes) : null;

	/// <summary><c>0xf02f</c>'s bitmap array, <c>Resource_LoadBitmapArray</c> or the simulator's load in place: the <c>.DBA</c>'s frames, or none.</summary>
	private IReadOnlyList<DynamixBitmap>? LoadBitmapArray(string name) =>
		_manager.Read(_manager.BuildPath(name)) is { } bytes ? new DynamixBitmapArrayTransformer().Parse(bytes)?.Images : null;

	/// <summary>
	/// <c>0xf05f</c>'s palette, <c>Resource_LoadPalette</c> or the simulator's load in place: the <c>.DPL</c>'s 256 colours,
	/// red, green and blue as the file holds them and a flag byte, or none; <paramref name="scale"/> the reader's scale for
	/// showing them, kept when none loads.
	/// </summary>
	private uint[]? LoadPalette(string name, out int scale) {
		scale = _paletteScales[PaletteSlot % SlotCount];
		if (_manager.Read(_manager.BuildPath(name)) is not { } bytes || new DynamixPaletteTransformer().Parse(bytes) is not { } palette) {
			return null;
		}

		scale = palette.Scalar;
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
