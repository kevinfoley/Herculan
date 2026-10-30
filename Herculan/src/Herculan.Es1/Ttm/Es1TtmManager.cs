using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Panels;
using Herculan.Es1.Shell;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Ttm;

/// <summary>
/// Which program's <c>.TTM</c> interpreter a <see cref="Es1TtmManager"/> and its scripts are: each program has its own copy,
/// the simulator's the reduced one (docs/es1/formats/ttm-scripts.md#the-simulator).
/// </summary>
public enum Es1TtmMode {
	/// <summary><c>ES1DBSIM</c>'s: the cockpit's script.</summary>
	Simulator,

	/// <summary><c>ES1GO</c>'s: the shell's intro, endings, talking heads and animated screens.</summary>
	Shell,
}

/// <summary>
/// A <c>.TTM</c> handle table and the module's globals. In <see cref="Es1TtmMode.Simulator"/> it is
/// <c>Ttm_ManagerCtor</c> (<c>ES1DBSIM 00069540</c>)'s <c>0xb</c>-byte object (docs/es1/formats/ttm-scripts.md#handles):
/// the handle count <c>+4</c> and table <c>+5</c>, the timer slot <c>+9</c>, the path prefix <c>0009f254</c>, the page
/// <c>0009f250</c> a fixed rect is copied to, and the tick flag <c>0009b404</c> and count <c>0009b406</c>
/// (docs/es1/formats/ttm-scripts.md#each-frame); <c>Cockpit_LoadPanel</c> makes the one a mission has. In
/// <see cref="Es1TtmMode.Shell"/> it is the shell's globals (docs/es1/formats/ttm-scripts.md#the-shell): the count
/// <c>000829dc</c>, the table <c>000829e0</c>, the timer slot <c>000829e4</c>, the default render context
/// <c>00082a98</c>, the page <c>00082a9c</c>, the display <c>00082a94</c>, the prefix <c>00082aa0</c>, and the fade
/// flag <c>000829d4</c>, tick flag <c>000829d5</c> and count <c>000829d6</c> (docs/es1/formats/ttm-scripts.md#the-timer);
/// <see cref="Shell.Es1ShellScreens"/> makes it at the first screen.
///
/// <para>The simulator's default render context <c>0009f248</c> (<c>Ttm_ManagerCtor</c>'s <i>b</i> and
/// <c>Ttm_SetDefaultContext</c>'s store) is not kept: <c>TtmScript_Run</c> draws through it only for a script whose own
/// context <c>+0x1bd</c> is 0, and the one script a mission opens, the clock's, always has its own; a simulator script
/// without one draws nothing here. <c>Ttm_ManagerDtor</c> and the simulator's <c>Ttm_GetPage</c>, <c>Ttm_GetMode</c>,
/// <c>Ttm_GetLoop</c>, <c>Ttm_SetMode</c>, <c>Ttm_SetLoop</c> and <c>Ttm_Close</c> have no caller <c>es2_xref.py</c>
/// finds (docs/es1/formats/ttm-scripts.md#open); the shell's, which have, are ported and answer as the shell's do in
/// either mode, but <see cref="SetPage"/>, whose two copies differ.</para>
///
/// <para>Retail installs a script's render context for its run and puts the installed one back after (the display's
/// clip block <c>+0x40</c> and <c>Raster_InstallRenderContext</c>); here a script's context is handed to what it draws,
/// so there is nothing to put back.</para>
/// </summary>
public sealed class Es1TtmManager {
	/// <summary><c>Ttm_Open</c>'s answer when no handle takes the script.</summary>
	public const byte NoHandle = 0xff;

	private readonly Func<string, string, byte[]?>? _read;
	private Es1TtmPlayer?[] _handles = [];
	private uint _randomState;

	/// <summary>
	/// <c>Ttm_ManagerCtor</c>(0, <paramref name="backgroundPage"/>, <i>b</i>, <paramref name="count"/>): a
	/// <see cref="Es1TtmMode.Simulator"/> manager with the count, the page <i>a</i>, the display <c>000bdd28</c>, the
	/// prefix cleared, and <c>rand</c>'s state seeded with <c>srand(clock())</c>. <c>clock()</c> counts the program's run
	/// in ticks; this engine seeds with the host's millisecond count, which changes only which draws a random wait makes.
	/// </summary>
	/// <param name="read">The resource reader, (folder, name) to the file's bytes or null; with none every load fails.</param>
	public Es1TtmManager(Es1VideoMemory display, int backgroundPage, byte count, Func<string, string, byte[]?>? read,
			uint? seed = null) {
		Mode = Es1TtmMode.Simulator;
		Display = display;
		BackgroundPage = backgroundPage;
		Count = count;
		_read = read;
		_randomState = seed ?? (uint)Environment.TickCount;
	}

	private Es1TtmManager(Es1VideoMemory display, Func<string, string, byte[]?>? read, Es1DirtyList dirty,
			Func<short, int> randomBelow) {
		Mode = Es1TtmMode.Shell;
		Display = display;
		_read = read;
		Dirty = dirty;
		RandomBelow = randomBelow;
	}

	/// <summary>
	/// The shell's globals as the image holds them, before <see cref="Configure"/>: no handles. The display is the shell's
	/// <c>000ab616</c>, the dirty list the one <c>Shell_PresentFrame</c> flushes, and <paramref name="randomBelow"/>
	/// <c>Math_RandomBelow</c> on <c>Shell_RandomState</c> (<c>0009aec0</c>).
	/// </summary>
	public static Es1TtmManager ForShell(Es1VideoMemory display, Func<string, string, byte[]?>? read, Es1DirtyList dirty,
			Func<short, int> randomBelow) => new(display, read, dirty, randomBelow);

	/// <summary>Which program's interpreter this is.</summary>
	public Es1TtmMode Mode { get; }

	/// <summary><c>0009f24c</c> or <c>00082a94</c>, the display the scripts draw on.</summary>
	public Es1VideoMemory Display { get; }

	/// <summary>
	/// <c>0009f250</c> or <c>00082a9c</c>, the page <i>a</i> of <c>Ttm_ManagerCtor</c> or <c>Ttm_Configure</c>: the page
	/// <c>TtmScript_FixRect</c> copies a rect onto and the fades copy the screen to; the shell's background page 2.
	/// </summary>
	public int BackgroundPage { get; private set; }

	/// <summary><c>+4</c> or <c>000829dc</c>, the handles the table holds.</summary>
	public byte Count { get; private set; }

	/// <summary><c>0009f254</c> or <c>00082aa0</c>, the prefix <c>TtmScript_BuildPath</c> puts before a name; none from the constructor.</summary>
	public string? PathPrefix { get; set; }

	/// <summary>
	/// The shell's <c>00082a98</c>, the render context a script whose own is 0 draws through: <c>Ttm_Configure</c>'s
	/// <i>b</i>, then <see cref="SetDefaultContext"/>'s; made afresh for each run. Not kept in the simulator (see the class
	/// summary).
	/// </summary>
	public Func<Es1PanelCanvas>? DefaultContext { get; private set; }

	/// <summary><c>+9</c> or <c>000829e4</c>, the timer slot + 1 that <see cref="AllocHandles"/> registered the tick in.</summary>
	public int TimerSlot { get; private set; }

	/// <summary>The shell's byte <c>000829d4</c>, set while a fade runs: the tick counts nothing. The simulator's fades set none.</summary>
	public bool FadeActive { get; internal set; }

	/// <summary><c>0009b404</c> or <c>000829d5</c>, set by each tick and cleared by the runs.</summary>
	public bool Ticked { get; private set; }

	/// <summary><c>0009b406</c> or <c>000829d6</c>, the ticks the runs have still to run.</summary>
	public short PendingTicks { get; private set; }

	/// <summary>
	/// <c>Palette_UploadWhole</c>: the palette sent to the VGA's colour registers at once, as the scripts' colour cycles and
	/// fades and the simulator's palette commands send it; none drops it.
	/// </summary>
	public Action<DynamixPalette>? UploadPalette { get; set; }

	/// <summary>
	/// The shell's palette commands' store: the palette put at <c>0007fcd0</c>, none for a load that failed, and the byte
	/// <c>0007fcd4</c> set, so that the next <c>Shell_PresentFrame</c> uploads it
	/// (docs/es1/rendering/screen-pages.md#presenting-a-frame).
	/// </summary>
	public Action<DynamixPalette?>? PresentPalette { get; set; }

	/// <summary>The shell's dirty list, which the drawing commands and the fixed rects add their boxes to; none in the simulator.</summary>
	public Es1DirtyList? Dirty { get; }

	/// <summary>The shell's <c>Math_RandomBelow</c>(<c>Shell_RandomState</c>, bound), which the random wait and the random choice draw from.</summary>
	public Func<short, int>? RandomBelow { get; }

	/// <summary>Handle <paramref name="handle"/>'s script, or none.</summary>
	public Es1TtmPlayer? Script(int handle) => handle >= 0 && handle < _handles.Length ? _handles[handle] : null;

	/// <summary>
	/// <c>Ttm_Configure</c> (<c>000464b0</c>)(<paramref name="backgroundPage"/>, <paramref name="defaultContext"/>,
	/// <paramref name="count"/>): the count, the default render context, the shell's display (this manager's already) and
	/// the page <i>a</i>, and the prefix cleared with <c>Ttm_SetPathPrefix</c>(0).
	/// </summary>
	public void Configure(int backgroundPage, Func<Es1PanelCanvas>? defaultContext, byte count) {
		Count = count;
		DefaultContext = defaultContext;
		BackgroundPage = backgroundPage;
		SetPathPrefix(null);
	}

	/// <summary>
	/// <c>Ttm_AllocHandles</c> (<c>ES1DBSIM 00069780</c>, <c>ES1GO 0004670e</c>)(<paramref name="timerCount"/>): the table
	/// of <see cref="Count"/> empty handles, and <c>Ttm_TimerTick</c> registered on the PC timer at the count
	/// (docs/es1/simulation/timer.md#callbacks). The timer it would build when GL slot 0 is empty is
	/// <paramref name="clock"/>: <c>Sim_StartMission</c>'s in the simulator, <c>Shell_Timer</c> in the shell. Answers 1.
	/// </summary>
	public bool AllocHandles(int timerCount, Es1SimClock clock) {
		_handles = new Es1TtmPlayer?[Count];
		TimerSlot = clock.AddCallback(timerCount, TimerTick);
		return true;
	}

	/// <summary>
	/// <c>Ttm_TimerTick</c> (<c>ES1DBSIM 000696f4</c>, <c>ES1GO 00046681</c>): nothing while <see cref="FadeActive"/> is
	/// set (the shell's test; the simulator's has none and never sets the flag); otherwise the flag set and the count
	/// raised by 1, as a word.
	/// </summary>
	internal void TimerTick() {
		if (FadeActive) {
			return;
		}

		Ticked = true;
		PendingTicks++;
	}

	/// <summary><c>Ttm_Open</c>(manager, <paramref name="mode"/>, <paramref name="loop"/>, <paramref name="name"/>), the simulator's, which takes no render context.</summary>
	public byte Open(int mode, int loop, string name) => Open(mode, loop, null, name);

	/// <summary>
	/// <c>Ttm_Open</c> (<c>ES1DBSIM 00069848</c>, <c>ES1GO 0004680a</c>)(<paramref name="mode"/>, <paramref name="loop"/>,
	/// <paramref name="context"/>, <paramref name="name"/>): the first empty handle takes a new script loaded with
	/// <see cref="Es1TtmPlayer.Load"/>; <see cref="NoHandle"/> when the count is 0, every handle holds a script, or the
	/// load answers anything but 0, the script then destroyed and the handle emptied. The context is the shell's
	/// <i>c</i>, the script's <c>+0x2c</c>; the simulator's load takes none.
	/// </summary>
	public byte Open(int mode, int loop, Func<Es1PanelCanvas>? context, string name) {
		int handle = Array.IndexOf(_handles, null);
		if (Count == 0 || handle < 0) {
			return NoHandle;
		}

		var script = new Es1TtmPlayer(this);
		_handles[handle] = script;
		if (script.Load(mode, loop, context, name) != 0) {
			_handles[handle] = null;
			return NoHandle;
		}

		return (byte)handle;
	}

	/// <summary>
	/// <c>Ttm_Close</c> (<c>000469da</c>)(<paramref name="handle"/>): the script destroyed and the handle emptied, 1; 0
	/// for an empty handle, whose message retail formats into a buffer it does not use. A handle past the table, which
	/// retail reads beyond it, answers 0 here, an engine guard.
	/// </summary>
	public bool Close(byte handle) {
		if (Script(handle) == null) {
			return false;
		}

		_handles[handle] = null;
		return true;
	}

	/// <summary><c>Ttm_CloseAll</c> (<c>00046a3f</c>): every handle's script destroyed and the handle emptied.</summary>
	public void CloseAll() => Array.Clear(_handles);

	/// <summary>
	/// <c>Ttm_Shutdown</c> (<c>000464e8</c>): the tick's slot passed to <c>Timer_RemoveCallback</c> (<c>000565b7</c>),
	/// <see cref="CloseAll"/>, and the table deleted. The PC timer is not run again after the shell's shutdown, so the
	/// slot is left as it is here.
	/// </summary>
	public void Shutdown() {
		CloseAll();
		_handles = [];
	}

	/// <summary>
	/// <c>Ttm_SetPage</c>(<paramref name="page"/>, <paramref name="handle"/>): the shell's (<c>000468fc</c>) answers
	/// <c>TtmScript_SetPage</c> (<c>000470e7</c>) for the handle's script, which below the page count moves to the page,
	/// reloads the wait, clears the once, sound and page-ran flags and sets the mode to playing; the simulator's
	/// (<c>ES1DBSIM 00069904</c>) only stores the page below the count. 0 for an empty handle.
	/// </summary>
	public bool SetPage(short page, byte handle) => Script(handle) is { } script && script.SetPage(page);

	/// <summary><c>Ttm_SetMode</c> (<c>00046948</c>)(<paramref name="mode"/>, <paramref name="handle"/>): the script's mode set, 1; 0 for an empty handle.</summary>
	public bool SetMode(int mode, byte handle) {
		if (Script(handle) is not { } script) {
			return false;
		}

		script.Mode = mode;
		return true;
	}

	/// <summary><c>Ttm_SetLoop</c> (<c>00046991</c>)(<paramref name="loop"/>, <paramref name="handle"/>): the script's loop flag set, 1; 0 for an empty handle.</summary>
	public bool SetLoop(int loop, byte handle) {
		if (Script(handle) is not { } script) {
			return false;
		}

		script.Loop = loop;
		return true;
	}

	/// <summary><c>Ttm_GetPage</c> (<c>0004669f</c>)(<paramref name="handle"/>): the script's page, or <c>0xffff</c> for an empty handle.</summary>
	public ushort GetPage(byte handle) => Script(handle) is { } script ? (ushort)script.Page : (ushort)0xffff;

	/// <summary>
	/// <c>Ttm_GetMode</c> (<c>000466cc</c>)(<paramref name="handle"/>): the script's mode. Retail reads an empty handle's
	/// slot as a script; here an empty handle answers <see cref="Es1TtmPlayer.Hidden"/>, an engine guard, so a script that
	/// did not open counts as ended.
	/// </summary>
	public int GetMode(byte handle) => Script(handle)?.Mode ?? Es1TtmPlayer.Hidden;

	/// <summary><c>Ttm_GetLoop</c> (<c>000466ed</c>)(<paramref name="handle"/>): the script's loop flag; 0 for an empty handle here, where retail reads the empty slot.</summary>
	public int GetLoop(byte handle) => Script(handle)?.Loop ?? 0;

	/// <summary><c>Ttm_SetPathPrefix</c> (<c>0004692d</c>)(<paramref name="prefix"/>): the prefix stored, the one before returned.</summary>
	public string? SetPathPrefix(string? prefix) {
		string? before = PathPrefix;
		PathPrefix = prefix;
		return before;
	}

	/// <summary><c>Ttm_SetDefaultContext</c> (<c>000467ef</c>)(<paramref name="context"/>): <see cref="DefaultContext"/> stored, the one before returned.</summary>
	public Func<Es1PanelCanvas>? SetDefaultContext(Func<Es1PanelCanvas>? context) {
		var before = DefaultContext;
		DefaultContext = context;
		return before;
	}

	/// <summary>
	/// <c>Ttm_RunScript</c> (<c>000465da</c>)(<paramref name="handle"/>), the shell's: nothing for <see cref="NoHandle"/>;
	/// otherwise <see cref="Ticked"/> cleared, the handle's script run with <c>TtmScript_Run</c>(script, the ticks pending),
	/// and that many taken off the count. It does not wait for a tick. The waits are the script's fades'
	/// (<see cref="Es1TtmPlayer.RunWaiting"/>).
	/// </summary>
	public IEnumerable<Es1ShellWait> RunScript(byte handle) {
		if (handle == NoHandle) {
			yield break;
		}

		short ticks = PendingTicks;
		Ticked = false;
		if (Script(handle) is { } script) {
			foreach (var retrace in script.RunWaiting(ticks)) {
				yield return Es1ShellWait.For(retrace);
			}
		}

		PendingTicks -= ticks;
	}

	/// <summary>
	/// <c>Ttm_RunAll</c> (<c>ES1GO 0004652e</c>), the shell's (docs/es1/formats/ttm-scripts.md#the-timer): a wait until a
	/// tick has set <see cref="Ticked"/>, then the flag cleared, each handle's script run with <c>TtmScript_Run</c>(script,
	/// the ticks pending as they are after the wait) and that many taken off the count. The waits are the tick's and the
	/// scripts' fades'.
	/// </summary>
	public IEnumerable<Es1ShellWait> RunAllAfterTick() {
		while (!Ticked) {
			yield return Es1ShellWait.For(() => Ticked);
		}

		short ticks = PendingTicks;
		Ticked = false;
		foreach (var script in _handles) {
			if (script == null) {
				continue;
			}

			foreach (var retrace in script.RunWaiting(ticks)) {
				yield return Es1ShellWait.For(retrace);
			}
		}

		PendingTicks -= ticks;
	}

	/// <summary>
	/// <c>Ttm_RunAll</c> (<c>ES1DBSIM 000695e0</c>), the simulator's (docs/es1/formats/ttm-scripts.md#each-frame): false at
	/// once while <see cref="Ticked"/> is clear; otherwise the flag cleared, each handle's script run with
	/// <c>TtmScript_Run</c>(script, the ticks pending), and that many taken off the count. Retail installs the default
	/// render context around the runs and puts the installed one back; see the class summary.
	/// </summary>
	public bool RunAll() {
		if (!Ticked) {
			return false;
		}

		short ticks = PendingTicks;
		Ticked = false;
		foreach (var script in _handles) {
			script?.Run(ticks);
		}

		PendingTicks -= ticks;
		return true;
	}

	/// <summary>
	/// <c>TtmScript_BuildPath</c>(script, <paramref name="name"/>), the prefix put before the name with its first
	/// <c>.SCR</c>, <c>.BMP</c> and <c>.PAL</c> rewritten to <c>.DBM</c>, <c>.DBA</c> and <c>.DPL</c>: the shell's
	/// (<c>00047138</c>) upper-cases the name first (and rewrites <c>.RAW</c> to itself); the simulator's
	/// (<c>ES1DBSIM 00079b54</c>) matches the name as written.
	/// </summary>
	internal string BuildPath(string name) {
		if (Mode == Es1TtmMode.Shell) {
			name = name.ToUpperInvariant();
		}

		name = ReplaceFirst(ReplaceFirst(ReplaceFirst(name, ".SCR", ".DBM"), ".BMP", ".DBA"), ".PAL", ".DPL");
		return (PathPrefix ?? "") + name;

		static string ReplaceFirst(string text, string from, string to) {
			int at = text.IndexOf(from, StringComparison.Ordinal);
			return at < 0 ? text : string.Concat(text.AsSpan(0, at), to, text.AsSpan(at + from.Length));
		}
	}

	/// <summary><c>VolRStream_CtorFromPath</c>'s open of <paramref name="path"/>, its folder before the last backslash: the bytes, or null when it fails.</summary>
	internal byte[]? Read(string path) {
		int slash = path.LastIndexOf('\\');
		return _read?.Invoke(slash < 0 ? "" : path[..slash], path[(slash + 1)..]);
	}

	/// <summary>
	/// <c>rand</c> (<c>ES1DBSIM 0007cd28</c>), Symantec's: the state <c>000a0164</c> times <c>0x41c64e6d</c> plus
	/// <c>0x3039</c>, and bits 16 to 30 of it.
	/// </summary>
	internal int Rand() {
		_randomState = unchecked(_randomState * 0x41c64e6d + 0x3039);
		return (int)((_randomState >> 16) & 0x7fff);
	}
}
