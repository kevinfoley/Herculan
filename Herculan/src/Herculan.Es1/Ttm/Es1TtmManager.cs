using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Ttm;

/// <summary>
/// The simulator's <c>.TTM</c> manager, <c>Ttm_ManagerCtor</c> (<c>ES1DBSIM 00069540</c>)'s <c>0xb</c> bytes and the
/// module's globals (docs/es1/formats/ttm-scripts.md#handles): the handle count <c>+4</c> and table <c>+5</c>, the timer
/// slot <c>+9</c>, the path prefix <c>0009f254</c>, the page <c>0009f250</c> a fixed rect is copied to, and the tick
/// flag <c>0009b404</c> and count <c>0009b406</c> that <c>Ttm_TimerTick</c> raises
/// (docs/es1/formats/ttm-scripts.md#each-frame). <c>Cockpit_LoadPanel</c> makes the one a mission has.
///
/// <para><c>Ttm_ManagerCtor</c>'s <i>b</i> and <c>Ttm_SetDefaultContext</c>'s store, the default render context
/// <c>0009f248</c>, are not kept: <c>TtmScript_Run</c> draws through it only for a script whose own context
/// <c>+0x1bd</c> is 0, and the one script a mission opens, the clock's, always has its own. A script without one draws
/// nothing here. <c>Ttm_ManagerDtor</c>, <c>Ttm_GetPage</c>, <c>Ttm_GetMode</c>, <c>Ttm_GetLoop</c>,
/// <c>Ttm_SetPage</c>, <c>Ttm_SetMode</c>, <c>Ttm_SetLoop</c> and <c>Ttm_Close</c> have no caller <c>es2_xref.py</c>
/// finds (docs/es1/formats/ttm-scripts.md#open) and are not ported.</para>
/// </summary>
public sealed class Es1TtmManager {
	/// <summary><c>Ttm_Open</c>'s answer when no handle takes the script.</summary>
	public const byte NoHandle = 0xff;

	private readonly Func<string, string, byte[]?>? _read;
	private Es1TtmPlayer?[] _handles = [];
	private uint _randomState;

	/// <summary>
	/// <c>Ttm_ManagerCtor</c>(0, <paramref name="backgroundPage"/>, <i>b</i>, <paramref name="count"/>): the count, the
	/// page <i>a</i>, the display <c>000bdd28</c>, the prefix cleared, and <c>rand</c>'s state seeded with
	/// <c>srand(clock())</c>. <c>clock()</c> counts the program's run in ticks; this engine seeds with the host's
	/// millisecond count, which changes only which draws a random wait makes.
	/// </summary>
	/// <param name="read">The resource reader, (folder, name) to the file's bytes or null; with none every load fails.</param>
	public Es1TtmManager(Es1VideoMemory display, int backgroundPage, byte count, Func<string, string, byte[]?>? read,
			uint? seed = null) {
		Display = display;
		BackgroundPage = backgroundPage;
		Count = count;
		_read = read;
		_randomState = seed ?? (uint)Environment.TickCount;
	}

	/// <summary><c>0009f24c</c>, the display the scripts draw on.</summary>
	public Es1VideoMemory Display { get; }

	/// <summary><c>0009f250</c>, <c>Ttm_ManagerCtor</c>'s <i>a</i>: the page <c>TtmScript_FixRect</c> copies a rect onto and the fades copy the screen to.</summary>
	public int BackgroundPage { get; }

	/// <summary><c>+4</c>, the handles the table holds.</summary>
	public byte Count { get; }

	/// <summary><c>0009f254</c>, the prefix <c>TtmScript_BuildPath</c> puts before a name; none from the constructor.</summary>
	public string? PathPrefix { get; set; }

	/// <summary><c>+9</c>, the timer slot + 1 that <see cref="AllocHandles"/> registered the tick in.</summary>
	public int TimerSlot { get; private set; }

	/// <summary><c>0009b404</c>, set by each tick and cleared by <see cref="RunAll"/>.</summary>
	public bool Ticked { get; private set; }

	/// <summary><c>0009b406</c>, the ticks <see cref="RunAll"/> has still to run.</summary>
	public short PendingTicks { get; private set; }

	/// <summary>
	/// <c>Palette_UploadWhole</c>: the palette sent to the VGA's colour registers, 256 colours of red, green and blue in
	/// the low three bytes; none drops it.
	/// </summary>
	public Action<DynamixPalette>? UploadPalette { get; set; }

	/// <summary>Handle <paramref name="handle"/>'s script, or none.</summary>
	public Es1TtmPlayer? Script(int handle) => handle >= 0 && handle < _handles.Length ? _handles[handle] : null;

	/// <summary>
	/// <c>Ttm_AllocHandles</c> (<c>00069780</c>)(manager, <paramref name="timerCount"/>): the table of <see cref="Count"/>
	/// empty handles, and <c>Ttm_TimerTick</c> (<c>000696f4</c>) registered on the PC timer at the count
	/// (docs/es1/simulation/timer.md#callbacks). The timer it would build when GL slot 0 is empty is
	/// <c>Sim_StartMission</c>'s by then, <paramref name="clock"/>. Answers 1.
	/// </summary>
	public bool AllocHandles(int timerCount, Es1SimClock clock) {
		_handles = new Es1TtmPlayer?[Count];
		TimerSlot = clock.AddCallback(timerCount, TimerTick);
		return true;
	}

	/// <summary><c>Ttm_TimerTick</c>: the flag set and the count raised by 1, as a word.</summary>
	internal void TimerTick() {
		Ticked = true;
		PendingTicks++;
	}

	/// <summary>
	/// <c>Ttm_Open</c> (<c>00069848</c>)(manager, <paramref name="mode"/>, <paramref name="loop"/>, <paramref name="name"/>):
	/// the first empty handle takes a new script loaded with <see cref="Es1TtmPlayer.Load"/>; <see cref="NoHandle"/> when
	/// the count is 0, every handle holds a script, or the load answers anything but 0, the script then destroyed and the
	/// handle emptied.
	/// </summary>
	public byte Open(int mode, int loop, string name) {
		int handle = Array.IndexOf(_handles, null);
		if (Count == 0 || handle < 0) {
			return NoHandle;
		}

		var script = new Es1TtmPlayer(this);
		_handles[handle] = script;
		if (script.Load(mode, loop, name) != 0) {
			_handles[handle] = null;
			return NoHandle;
		}

		return (byte)handle;
	}

	/// <summary>
	/// <c>Ttm_RunAll</c> (<c>000695e0</c>): false at once while <see cref="Ticked"/> is clear; otherwise the flag cleared,
	/// each handle's script run with <c>TtmScript_Run</c>(script, the ticks pending), and that many taken off the count.
	/// Retail installs the default render context around the runs and puts the installed one back; see the class summary.
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
	/// <c>TtmScript_BuildPath</c> (<c>00079b54</c>)(script, <paramref name="name"/>): the first <c>.SCR</c>,
	/// <c>.BMP</c> and <c>.PAL</c> rewritten to <c>.DBM</c>, <c>.DBA</c> and <c>.DPL</c>, matched as written, and the
	/// prefix put before it.
	/// </summary>
	internal string BuildPath(string name) {
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
	/// <c>rand</c> (<c>0007cd28</c>), Symantec's: the state <c>000a0164</c> times <c>0x41c64e6d</c> plus <c>0x3039</c>,
	/// and bits 16 to 30 of it.
	/// </summary>
	internal int Rand() {
		_randomState = unchecked(_randomState * 0x41c64e6d + 0x3039);
		return (int)((_randomState >> 16) & 0x7fff);
	}
}
