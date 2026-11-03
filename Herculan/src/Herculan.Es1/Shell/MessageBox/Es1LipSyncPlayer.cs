using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.Ttm;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.MessageBox;

/// <summary>
/// The shell's lip-sync player <c>Shell_LipSyncPlayer</c> (<c>0009afd4</c>), <c>0x1b</c> bytes, which holds one talking head
/// and one line (docs/es1/shell/message-box.md#the-talking-head): the face's <c>.TTM</c> script, the line's lip-sync
/// records and its sound, the lip-sync list's lines, and the line's clock <c>ShellSnc_Clock</c> (<c>00082658</c>) on the
/// shell's timer. Each member starts as <c>ShellData_StaticInit</c> (<c>00020b68</c>) leaves it: the handle <c>0xff</c>, the rest
/// 0.
///
/// <para>The line's voice plays nothing: the shell's sound is not ported (<see cref="Es1ShellDevices.Init"/>'s
/// <c>ShellSound_Init</c>), so <c>ShellSnc_OpenSound</c> opens no sound object and the stop and release have none to stop.
/// The line's timing is the lip-sync script's, so a line still ends on its own as retail's does.</para>
/// </summary>
public sealed class Es1LipSyncPlayer {
	/// <summary>The folder the lip-sync lists and scripts are read from, <c>SNC\</c> (<c>0008265c</c>).</summary>
	public const string Folder = "SNC";

	/// <summary>
	/// The eleven lip-sync lists at <c>0008271c</c>, <c>SNC\&lt;name&gt;</c> (docs/es1/formats/shell-file-lists.md#the-lip-sync-lists).
	/// </summary>
	private static readonly string[] ListNames = [
		"C0_FILES.DAT", "D0_FILES.DAT", "C1_FILES.DAT", "D1_FILES.DAT", "C2_FILES.DAT", "D2_FILES.DAT", "C3_FILES.DAT",
		"D3_FILES.DAT", "T0_FILES.DAT", "T1_FILES.DAT", "G0_FILES.DAT",
	];

	/// <summary><c>maybe_SncList_Init</c>'s timer count: every second interrupt, 60 a second.</summary>
	private const int TimerCount = 2;

	/// <summary>The pages <c>maybe_SncList_Load</c> runs in turn, the shoulders' page between the two.</summary>
	private const short LoadPage = 0, ShouldersPage = 0x19, FirstHeadPage = 1;

	/// <summary>The page <c>maybe_SncList_Stop</c> runs first, or <see cref="FirstHeadPage"/> when the script has none.</summary>
	private const short StopPage = 0x1e;

	private readonly Es1VideoMemory _display;
	private readonly Func<string, string, byte[]?>? _read;
	private readonly Es1MessageBoxRecord _box;

	/// <param name="read">The shell's resource reader, (folder, name) to the file's bytes or null.</param>
	/// <param name="box">The message box whose <i>up</i> byte <see cref="Stop"/> clears.</param>
	public Es1LipSyncPlayer(Es1VideoMemory display, Func<string, string, byte[]?>? read, Es1MessageBoxRecord box) {
		_display = display;
		_read = read;
		_box = box;
	}

	/// <summary>
	/// The shell's scripts, the face's interpreter, which <see cref="Es1ShellScreens.OpenFirstScreen"/> sets up and hands here;
	/// none before. A face loaded without them is not opened, an engine guard.
	/// </summary>
	public Es1TtmManager? Scripts { get; set; }

	/// <summary><c>+0x00</c>, the timer slot <c>Timer_AddCallback</c> gave.</summary>
	public int TimerSlot { get; private set; }

	/// <summary><c>+0x02</c>, the face's script handle, or <see cref="Es1TtmManager.NoHandle"/>.</summary>
	public byte Handle { get; private set; } = Es1TtmManager.NoHandle;

	/// <summary>
	/// <c>+0x07</c> and <c>+0x0b</c>: the line's lip-sync records, each frame with the clock reading it ends at, none while
	/// no line is loaded.
	/// </summary>
	private (short Frame, ushort End)[]? _records;

	/// <summary><c>+0x0d</c>, the current record's index.</summary>
	public short RecordIndex { get; private set; }

	/// <summary><c>+0x0f</c>, the list's line count as an <c>int16</c> whose low byte the list's count byte is read into.</summary>
	public short LineCount { get; private set; }

	/// <summary><c>+0x11</c>, the lines' name lists: the lip-sync script's and the sound's; none before a list loads.</summary>
	private Es1NameList[]? _lines;

	/// <summary>
	/// <c>+0x15</c>, the render context the face's script draws through: <c>GLViewport_Ctor</c>(0) set by
	/// <see cref="Init"/> to the whole screen, (0, 0)–(319, 199), clip mode 1 and the translation clear, made afresh for
	/// each run here.
	/// </summary>
	private Func<Es1PanelCanvas>? _context;

	/// <summary><c>+0x19</c> (<c>0009afed</c>), set while a line plays.</summary>
	public bool Playing { get; private set; }

	/// <summary><c>+0x1a</c> (<c>0009afee</c>), the line's records have run out: a request to stop.</summary>
	public bool StopRequested { get; private set; }

	/// <summary><c>ShellSnc_Clock</c> (<c>00082658</c>), the line's clock, sixtieths of a second since it started.</summary>
	public short Clock { get; private set; }

	/// <summary>
	/// <c>maybe_SncList_Init</c> (<c>00045337</c>), the start-up's step 13 (docs/es1/shell/message-box.md#the-player): the
	/// face's render context made, and <see cref="TimerTick"/> registered on <paramref name="timer"/>, <c>Shell_Timer</c>, at
	/// count 2.
	/// </summary>
	public void Init(Es1SimClock timer) {
		var display = _display;
		_context = () => new Es1PanelCanvas(display, Es1ShellScreens.FullScreen);
		TimerSlot = timer.AddCallback(TimerCount, TimerTick);
	}

	/// <summary><c>maybe_SncList_TimerTick</c> (<c>00045320</c>): the clock raised by 1 while <see cref="Playing"/> is set.</summary>
	private void TimerTick() {
		if (Playing) {
			Clock++;
		}
	}

	/// <summary>
	/// <c>maybe_SncList_PlayEntry</c> (<c>000453be</c>)(<paramref name="a"/>, <paramref name="b"/>), from
	/// <c>DigitalDialogBox_Ctor</c> (docs/es1/shell/message-box.md#loading-a-line): a line still playing stopped; the list
	/// <paramref name="a"/> for <paramref name="b"/> 1, <paramref name="a"/> + 8 for 2, 10 for 3 and 0 otherwise loaded
	/// (<see cref="LoadList"/>); and line <paramref name="line"/>'s first name, its lip-sync script, loaded
	/// (<see cref="LoadScript"/>). Retail reads the lines without a bounds test; a line the list does not hold, or a list
	/// index past the eleven, loads no script here, an engine guard.
	/// </summary>
	/// <param name="line"><c>MessageBox_Line</c> (<c>000aad5c</c>).</param>
	public IEnumerable<Es1ShellWait> PlayEntry(short a, byte b, short line) {
		if (Playing) {
			foreach (var wait in Stop()) {
				yield return wait;
			}
		}

		int list = b switch {
			1 => a,
			2 => a + 8,
			3 => 10,
			_ => 0,
		};
		if (list >= 0 && list < ListNames.Length) {
			foreach (var wait in LoadList(ListNames[list])) {
				yield return wait;
			}
		}

		if (Line(line) is { Names.Length: > 0 } names) {
			LoadScript(names.Names[0]);
		}
	}

	/// <summary>Line <paramref name="line"/>'s name list, <c>StrList_Get</c>'s array entry; none past the lines.</summary>
	private Es1NameList? Line(short line) => _lines != null && line >= 0 && line < _lines.Length ? _lines[line] : null;

	/// <summary>
	/// <c>maybe_SncList_Load</c> (<c>000455dc</c>)(<c>SNC\</c><paramref name="name"/>) (docs/es1/shell/message-box.md#loading-a-line):
	/// the count byte read into <see cref="LineCount"/>'s low byte; when the count is not 0, the name lists replaced, the
	/// face's script name read, the script before it closed, the new one opened playing on <see cref="_context"/>
	/// (<c>Ttm_Open</c>(0, 0, <c>+0x15</c>, name)), its page set to 0, <c>0x19</c> and 1 in turn, run after each page the
	/// script takes (<c>Ttm_RunScript</c>), and set to 1 once more. A list that does not read changes nothing here, as a
	/// count of 0 does, an engine guard: retail reads the count from the failed stream.
	/// </summary>
	private IEnumerable<Es1ShellWait> LoadList(string name) {
		if (_read?.Invoke(Folder, name) is not { } bytes || bytes.Length == 0) {
			yield break;
		}

		LineCount = (short)((LineCount & 0xff00) | bytes[0]);
		if (bytes[0] == 0 || new Es1LipSyncListTransformer().Parse(bytes) is not { Script: { } script } file) {
			yield break;
		}

		_lines = file.Lines;
		if (Scripts is not { } scripts) {
			yield break;
		}

		if (Handle != Es1TtmManager.NoHandle) {
			scripts.Close(Handle);
		}

		Handle = scripts.Open(Es1TtmPlayer.Playing, 0, _context, script);
		foreach (short page in new[] { LoadPage, ShouldersPage, FirstHeadPage }) {
			if (scripts.SetPage(page, Handle)) {
				foreach (var wait in scripts.RunScript(Handle)) {
					yield return wait;
				}
			}
		}

		scripts.SetPage(FirstHeadPage, Handle);
	}

	/// <summary>
	/// <c>ShellSnc_Load</c> (<c>0004589c</c>)(player + 3, <paramref name="name"/>) (docs/es1/shell/message-box.md#loading-a-line):
	/// <c>SNC\</c><paramref name="name"/>'s records, each delta added to the running total from the second record on, so
	/// each holds the clock reading it ends at, as an <c>int16</c> sum read unsigned; and the index set to 0. The records
	/// before are not freed first, as retail's are not. A script that does not read leaves no records here, an engine
	/// guard: retail reads its length from the failed stream.
	/// </summary>
	private void LoadScript(string name) {
		Es1ShellLipSyncScript? script = _read?.Invoke(Folder, name) is { } bytes
			? new Es1ShellLipSyncScriptTransformer().Parse(bytes)
			: null;
		if (script == null) {
			Console.Error.WriteLine($"{Folder}\\{name} is missing or unreadable: the talking head's line has no lip-sync records.");
			_records = null;
			RecordIndex = 0;
			return;
		}

		var records = new (short Frame, ushort End)[script.Records.Length];
		short total = 0;
		for (int i = 0; i < records.Length; i++) {
			total = i == 0 ? script.Records[0].Delta : unchecked((short)(total + script.Records[i].Delta));
			records[i] = (script.Records[i].Frame, (ushort)total);
		}

		_records = records;
		RecordIndex = 0;
	}

	/// <summary>
	/// <c>maybe_SncList_StartSound</c> (<c>0004544a</c>), from each <c>DialogBox_Draw</c>
	/// (docs/es1/shell/message-box.md#loading-a-line): with a face and neither <see cref="Playing"/> nor
	/// <see cref="StopRequested"/> set, <see cref="Playing"/> set, the line's second name, its <c>.RAW</c>, opened with
	/// <c>ShellSnc_OpenSound</c> and started (<c>DigiSound_Play</c>) while <c>Shell_RadioOption</c> is above 0, and the clock
	/// set to 0.
	/// </summary>
	public void StartSound() {
		if (Handle == Es1TtmManager.NoHandle || Playing || StopRequested) {
			return;
		}

		Playing = true;
		// PLACEHOLDER: ShellSnc_OpenSound (00045801) of MessageBox_Line's second name, its .RAW, through the sound manager
		// 00082768, and DigiSound_Play through the sound's slot +0x10 while Shell_RadioOption (0007fcbc) is above 0: the voice.
		// The shell's sound is not ported (Es1ShellDevices.Init's ShellSound_Init), so no sound is made and the face talks
		// without one.
		Clock = 0;
	}

	/// <summary>
	/// <c>maybe_SncList_Tick</c> (<c>000454c4</c>), each pass of <c>MessageBox_Run</c>'s loop
	/// (docs/es1/shell/message-box.md#playing-it): the line stopped when <see cref="StopRequested"/> is set; then, while
	/// <see cref="Playing"/> is set, the line advanced (<see cref="Advance"/>), the face's page set to the current record's
	/// frame, the last record's once the index reaches the count, or −1 with no records, and its mode set to playing.
	/// </summary>
	public IEnumerable<Es1ShellWait> Tick() {
		if (StopRequested) {
			foreach (var wait in Stop()) {
				yield return wait;
			}
		}

		if (!Playing) {
			yield break;
		}

		Advance();
		short page;
		if (_records is not { } records) {
			page = -1;
		} else {
			page = records[RecordIndex == records.Length ? RecordIndex - 1 : RecordIndex].Frame;
		}

		if (Scripts is { } scripts) {
			scripts.SetPage(page, Handle);
			scripts.SetMode(Es1TtmPlayer.Playing, Handle);
		}
	}

	/// <summary>
	/// <c>ShellSnc_Advance</c> (<c>0004597b</c>)(player + 3): nothing with no records or while the clock, signed, is below
	/// the current record's end, unsigned; otherwise the index stepped past every record whose end the clock has reached,
	/// and on reaching the count <see cref="StopRequested"/> set. A count of 0 reads past the records in retail; here it
	/// stops the line, an engine guard.
	/// </summary>
	private void Advance() {
		if (_records is not { } records) {
			return;
		}

		// Retail tests the current record's end once before the loop, which the loop's first pass repeats.
		while (true) {
			if (RecordIndex >= records.Length) {
				StopRequested = true;
				return;
			}

			if (Clock < records[RecordIndex].End) {
				return;
			}

			RecordIndex++;
		}
	}

	/// <summary>
	/// <c>maybe_SncList_Stop</c> (<c>00045556</c>) (docs/es1/shell/message-box.md#playing-it): with a face, its page set to
	/// <c>0x1e</c>, or to 1 when the script has no such page, and run once, then the script closed and the handle emptied;
	/// the line released (<c>ShellSnc_Release</c>: the records freed, and the sound, which is never made here); and
	/// <see cref="Playing"/>, <see cref="StopRequested"/> and the message box's <i>up</i> (<c>0009b013</c>) cleared.
	/// </summary>
	public IEnumerable<Es1ShellWait> Stop() {
		if (Handle != Es1TtmManager.NoHandle) {
			if (Scripts is { } scripts) {
				if (scripts.SetPage(StopPage, Handle) || scripts.SetPage(FirstHeadPage, Handle)) {
					foreach (var wait in scripts.RunScript(Handle)) {
						yield return wait;
					}
				}

				scripts.Close(Handle);
			}

			Handle = Es1TtmManager.NoHandle;
		}

		_records = null;
		Playing = false;
		StopRequested = false;
		_box.Up = false;
	}
}
