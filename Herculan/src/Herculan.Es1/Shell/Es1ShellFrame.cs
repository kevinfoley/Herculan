using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Host;
using Herculan.Es1.Input;
using Herculan.Es1.Shell.Menus;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// Where the shell's frame stands waiting: until a timer callback sets a flag (<see cref="Until"/>), or for the
/// monitor's retrace (<see cref="Retrace"/>).
/// </summary>
public readonly record struct Es1ShellWait(Func<bool>? Until, Es1Retrace? Retrace) {
	public static Es1ShellWait For(Func<bool> until) => new(until, null);

	public static Es1ShellWait For(Es1Retrace retrace) => new(null, retrace);
}

/// <summary>
/// The shell's frame (docs/es1/shell/startup.md#the-frame-loop): <c>Shell_PresentFrame</c>, the input, and the loop's
/// pass, run by the host on host time.
///
/// <para>Retail's frame spins: <c>Shell_PresentFrame</c> until the timer callback <c>Shell_FrameTick</c> sets
/// <c>Shell_FrameDue</c>, <c>Shell_WaitTicks</c> until <c>Shell_WaitTick</c> has counted, and <c>VGA8_ShowPage</c> on
/// the monitor's retrace. Here the shell's work is one piece that stands at each such wait (<see cref="Es1ShellWait"/>)
/// and goes on from there: <see cref="AdvanceHostFrame"/> runs the PC timer an interrupt at a time
/// (<see cref="Sim.Es1SimClock.ToNextInterrupt"/>) until a timer wait is met, or to the retrace a retrace wait names, on
/// the timeline of <see cref="Es1FrameLoop.RetracesPerSecond"/> retraces a second the simulator's frame uses, a retrace's
/// own length taken as nil. The work between waits takes no host time. So the shell shows at most 60 frames a second,
/// one each time the timer's frame flag and the monitor allow, as retail does.</para>
/// </summary>
public sealed class Es1ShellFrame {
	/// <summary>The input block's keyboard-word bits <c>Shell_PollInput</c> clears, Num Lock's and Scroll Lock's.</summary>
	private const ushort LockBits = Es1Keyboard.NumLockBit | Es1Keyboard.ScrollLockBit;

	/// <summary>The keyboard word's bit for a typed character, which the DOS read sets.</summary>
	private const ushort CharacterBit = 0x4000;

	/// <summary>The latched buttons, <c>+0x1d</c>–<c>+0x20</c> of the block.</summary>
	private const int LatchedButtons = 4;

	/// <summary>The codes <c>Shell_HandleInput</c> hands <c>DUIWindow_HandleNavKey</c>: <c>Tab</c>, <c>Enter</c> and <c>Enter</c>'s release.</summary>
	private const ushort NavTab = 0x0f, NavEnter = 0x1c, NavEnterRelease = 0x9c;

	private readonly Es1ShellDevices _devices;
	private readonly Es1ShellBackdrop _backdrop;
	private readonly Es1DirtyList _dirty;
	private readonly Es1ShellScreens _screens;
	private readonly Es1ShellTime _time;
	private readonly bool _debugMode;

	// Input_LatchButton's bytes: the latch at 00083c84 and the latched value at 000a0bd0, one per button.
	private readonly bool[] _latched = new bool[LatchedButtons];
	private readonly byte[] _latchedValue = new byte[LatchedButtons];

	// The byte 0007e87c: the first button sent Enter, whose release follows.
	private bool _enterHeld;

	// The shell's work, standing at its current wait; null once it has run.
	private IEnumerator<Es1ShellWait>? _work;

	// The host time the frame has been advanced by, in TimeSpan ticks, on which the retraces lie.
	private long _hostTime;

	// The host time the current retrace wait ends at.
	private long? _retraceEnds;

	public Es1ShellFrame(Es1ShellDevices devices, Es1ShellBackdrop backdrop, Es1DirtyList dirty, Es1ShellScreens screens,
			Es1ShellTime time, bool debugMode) {
		_devices = devices;
		_backdrop = backdrop;
		_dirty = dirty;
		_screens = screens;
		_time = time;
		_debugMode = debugMode;
	}

	/// <summary><c>Palette_UploadWhole</c>: the palette made the live one, the monitor's (<see cref="Es1Monitor.SetPalette"/>).</summary>
	public Action<DynamixPalette>? UploadPalette { get; set; }

	/// <summary><c>Shell_QuitRequested</c> (<c>0007e808</c>), which the quit keys set.</summary>
	public bool QuitRequested { get; private set; }

	/// <inheritdoc cref="Menus.Es1MenuGlobals.SyncPointer"/>
	public bool SyncPointer => _screens.Globals.SyncPointer;

	/// <inheritdoc cref="Menus.Es1MenuGlobals.Pointer"/>
	public (int X, int Y) Pointer => _screens.Globals.Pointer;

	/// <summary><c>Shell_InputBlock</c> (<c>0007e73c</c>), the block the last pass read.</summary>
	public Es1LiveBlock? InputBlock { get; private set; }

	/// <summary>Whether the shell's work has run to its end, the frame loop left.</summary>
	public bool Ended => _work == null;

	/// <summary>Starts <paramref name="work"/>, which <see cref="AdvanceHostFrame"/> runs on.</summary>
	public void Start(IEnumerable<Es1ShellWait> work) {
		_work = work.GetEnumerator();
		Resume();
	}

	/// <summary>
	/// One host frame of <paramref name="elapsed"/>: while the work stands at a wait, the PC timer runs on an interrupt at a
	/// time until a timer wait is met, or to a retrace wait's retrace, and the work goes on from there; at the host frame's
	/// end the timer runs on to it.
	/// </summary>
	public void AdvanceHostFrame(TimeSpan elapsed) {
		long end = _hostTime + Math.Max(0, elapsed.Ticks);
		var timer = _devices.Timer;
		while (_work != null) {
			var wait = _work.Current;
			if (wait.Until is { } until) {
				if (until()) {
					Resume();
					continue;
				}

				long next = _hostTime + timer.ToNextInterrupt.Ticks;
				if (next > end) {
					break;
				}

				Hold(next);
				continue;
			}

			if (_retraceEnds is not { } retrace || retrace > end) {
				break;
			}

			Hold(retrace);
			Resume();
		}

		Hold(end);
	}

	/// <summary>The host time up to <paramref name="until"/> held: the PC timer runs on.</summary>
	private void Hold(long until) {
		if (until > _hostTime) {
			_devices.Timer.Hold(TimeSpan.FromTicks(until - _hostTime));
			_hostTime = until;
		}
	}

	/// <summary>The work run on to its next wait, a retrace wait's end kept, or to its end.</summary>
	private void Resume() {
		if (_work!.MoveNext()) {
			_retraceEnds = _work.Current.Retrace is { } retrace ? Es1FrameLoop.WaitEnd(_hostTime, retrace) : null;
			return;
		}

		_work.Dispose();
		_work = null;
		_retraceEnds = null;
	}

	/// <summary>
	/// <c>Shell_PresentFrame</c> (<c>00012980</c>) (docs/es1/rendering/screen-pages.md#presenting-a-frame): waits for
	/// <c>Shell_FrameDue</c> and clears it; uploads the palette when one is due; starts the track when one is due, which
	/// <c>ShellMusic_Play</c> does through the sound manager, which plays nothing here; shows <c>+0x88</c>, which <c>VGA8_ShowPage</c>
	/// does after waiting out the retrace it finds and before waiting for the next to start, the display's attribute 9
	/// being 2; draws on <c>+0x84</c> and swaps the two; waits 0.1 s once after a screen drew on backdrop 1000; flushes the
	/// dirty list onto the new draw page from page 2; and with <see cref="SyncPointer"/> set puts the cursor and the mouse
	/// on <see cref="Pointer"/>.
	/// </summary>
	public IEnumerable<Es1ShellWait> PresentFrame() {
		var devices = _devices;
		while (!devices.FrameDue) {
			yield return Es1ShellWait.For(() => devices.FrameDue);
		}

		devices.FrameDue = false;
		if (_backdrop.Palette is { } palette && _backdrop.PaletteDue) {
			UploadPalette?.Invoke(palette);
			_backdrop.PaletteDue = false;
		}

		if (_backdrop.MusicDue) {
			// PLACEHOLDER: ShellMusic_Play(Shell_MusicTrack); ES1's sound is not ported (Es1ShellDevices.Init's ShellSound_Init).
			_backdrop.MusicDue = false;
		}

		var display = devices.Display;
		_screens.PageShown = false;
		yield return Es1ShellWait.For(Es1Retrace.VerticalRetrace);
		display.ShowPage(devices.PairB);
		yield return Es1ShellWait.For(default(Es1Retrace));
		_screens.PageShown = true;

		display.DrawPage = devices.PairA;
		(devices.PairA, devices.PairB) = (devices.PairB, devices.PairA);
		if (_screens.AnmMode == 0 && _screens.WaitAfterDraw && _screens.BackdropId == Es1ShellScreens.MainMenuScreen) {
			foreach (var wait in WaitTicks(6)) {
				yield return wait;
			}

			_screens.WaitAfterDraw = false;
		}

		_dirty.Flush(devices.PairB, Es1ShellBackdrop.BackgroundPage);
		if (SyncPointer) {
			display.Cursor.SyncPosition(Pointer.X, Pointer.Y);
			// 00027bc9, the mouse device's getter, whose answer is dropped.
			devices.Mouse.SetPosition(Pointer.X, Pointer.Y);
		}
	}

	/// <summary>
	/// <c>Shell_WaitTicks</c> (<c>0004bbe9</c>)(<paramref name="count"/>): <c>Shell_WaitCount</c> zeroed, then a wait
	/// until <c>Shell_WaitTick</c> has raised it to the count, two interrupts each.
	/// </summary>
	public IEnumerable<Es1ShellWait> WaitTicks(short count) {
		var devices = _devices;
		devices.WaitCount = 0;
		while (devices.WaitCount < count) {
			yield return Es1ShellWait.For(() => devices.WaitCount >= count);
		}
	}

	/// <summary>
	/// The frame loop's pass (docs/es1/shell/startup.md#the-frame-loop), in retail's order: a frame shown, the input read
	/// and handled, the quit flag set on screen 9999, the pending menu message handled, the scripts run with
	/// <c>Ttm_RunAll</c> when it changed no screen (<see cref="Ttm.Es1TtmManager.RunAllAfterTick"/>, which waits for the
	/// scripts' tick), the screen's work: <c>Shell_TickScreen</c>, the message box's loop (<c>MessageBox_Run</c>
	/// (<c>000382f6</c>)(<c>Shell_MessageBox</c>, <see cref="InputBlock"/>), <see cref="MessageBox.Es1MessageBox.Run"/>) and the
	/// text box drawn (<c>Dialog_Draw</c>(<c>Shell_Dialog</c>)); the quit question, and the frame-time scale. The scripts and
	/// the message box exist from the first screen on, before the first pass; the tests for them are engine guards.
	/// </summary>
	public IEnumerable<Es1ShellWait> Pass() {
		foreach (var wait in PresentFrame()) {
			yield return wait;
		}

		InputBlock = PollInput();
		HandleInput(InputBlock);
		// NoOp_Void.
		if (_screens.CurrentScreen == Es1ShellScreens.LoadingScreen) {
			_screens.QuitFlag = true;
		}

		foreach (var wait in _screens.HandleMessage(_screens.Globals.PendingMessage, PresentFrame)) {
			yield return wait;
		}

		if (!_screens.MessageChangedScreen && _screens.Scripts is { } scripts) {
			foreach (var wait in scripts.RunAllAfterTick()) {
				yield return wait;
			}
		}

		_screens.TickScreen();
		// 0001098e, the widget root's mouse queue (Shell_PollInput's), does nothing until its queue is started.
		if (_screens.MessageBox is { } messageBox) {
			foreach (var wait in messageBox.Run(InputBlock, PresentFrame, PollDeviceBlock)) {
				yield return wait;
			}
		}

		_screens.Dialog?.Draw();
		if (QuitRequested) {
			foreach (var wait in _screens.OpenQuitAlert()) {
				yield return wait;
			}

			QuitRequested = false;
		}

		// X32_TotalFreeBytes into locals Shell_Main reads no further.
		_time.UpdateFrameScale();
	}

	/// <summary>
	/// <c>Shell_PollInput</c> (<c>0004de3a</c>): <c>0001098e</c>, then <c>Input_PollDeviceBlock</c> (<c>000277d8</c>) on
	/// the shell's devices, then each of the four buttons <c>+0x1d</c>–<c>+0x20</c> with its latch set inverted while it
	/// still equals the latched value and the latch dropped once it differs, and the keyboard word's Num Lock and Scroll
	/// Lock bits cleared.
	///
	/// <para><c>0001098e</c> is the shell's copy of the simulator's <c>CockpitMouse_ProcessQueue</c> for a widget root, not
	/// the menus' path: the driver calls the menus' callbacks itself (<see cref="Menus.Es1ShellMouse"/>). It returns at once
	/// until <c>maybe_MouseEventQueue_Start</c> (<c>00010559</c>, which <c>Shell_PrepareMission</c> and screen 1320's case
	/// call) has registered its queue's callback and <c>0001070d</c> has set its root, so on every ported screen it does
	/// nothing.</para>
	/// </summary>
	public Es1LiveBlock PollInput() {
		// PLACEHOLDER: 0001098e's queue, once Shell_PrepareMission's maybe_MouseEventQueue_Start (the mission map's, S5's) or
		// screen 1320's case starts it.
		var block = PollDeviceBlock();
		for (int i = 0; i < LatchedButtons; i++) {
			if (!_latched[i]) {
				continue;
			}

			if (_latchedValue[i] == block.StickButtons[i]) {
				block.StickButtons[i] = (byte)(_latchedValue[i] ^ 1);
			} else {
				_latched[i] = false;
			}
		}

		block.CommandWord = (ushort)(block.CommandWord & ~LockBits);
		return block;
	}

	/// <summary>
	/// <c>Input_PollDeviceBlock</c> (<c>000277d8</c>) on the shell's devices, which <see cref="PollInput"/> calls and
	/// <c>Shell_PlayTtm</c> calls itself: the keyboard's word, the mouse's position and its three buttons at
	/// <c>+0xa</c>–<c>+0xc</c>.
	/// </summary>
	public Es1LiveBlock PollDeviceBlock() {
		var mouse = _devices.Mouse;
		var block = new Es1LiveBlock {
			CommandWord = ReadKeyboardWord(),
			KeyboardEnabled = _devices.Keyboard != null,
			PointerX = mouse.X,
			PointerY = mouse.Y,
			MouseEnabled = true,
		};

		// Input_PollJoysticks and the two joystick devices' records: Shell_InitDevices builds no joystick device.
		block.MouseButtons[0] = (byte)((mouse.Buttons & Es1HostMouse.LeftBit) != 0 ? 1 : 0);
		block.MouseButtons[1] = (byte)((mouse.Buttons & Es1HostMouse.RightBit) != 0 ? 1 : 0);
		block.MouseButtons[2] = (byte)((mouse.Buttons & Es1HostMouse.MiddleBit) != 0 ? 1 : 0);
		return block;
	}

	/// <summary>
	/// <c>Input_ReadDeviceEvent</c> (<c>00025b30</c>) on the keyboard: in mode <c>0x10000</c> the queue's next word or 0;
	/// in mode <c>0x20000</c> the queue's next word while it holds one, and otherwise the DOS key read, a character or'd
	/// with <c>0x4000</c>, which the BIOS stand-in this engine lacks never gives (<see cref="Es1ShellDevices.Init"/>).
	/// </summary>
	private ushort ReadKeyboardWord() {
		if (_devices.Keyboard is not { } keyboard) {
			return 0;
		}

		return keyboard.Queue.HasWords ? keyboard.Queue.Pop() : (ushort)0;
	}

	/// <summary><c>Input_LatchButton</c> (<c>0004de18</c>)(<paramref name="value"/>, <paramref name="button"/>), button 1 to 4.</summary>
	private void LatchButton(byte value, int button) {
		_latched[button - 1] = true;
		_latchedValue[button - 1] = value;
	}

	/// <summary>
	/// <c>Shell_HandleInput</c> (<c>00012be8</c>) on <paramref name="block"/> (docs/es1/shell/startup.md#the-input): for a
	/// keyboard word without <c>0x4000</c>, Num Lock's bit cleared, the 15 words at <c>0007e880</c> and then <c>Q</c>,
	/// <c>X</c> and <c>C</c>; then the first two latched buttons as <c>Enter</c> and <c>Tab</c>; last, unless <c>-X</c>, a
	/// typed character to the menu.
	/// </summary>
	public void HandleInput(Es1LiveBlock? block) {
		if (block == null) {
			return;
		}

		if ((block.CommandWord & CharacterBit) == 0) {
			block.CommandWord = (ushort)(block.CommandWord & ~Es1Keyboard.NumLockBit);
			ushort word = block.CommandWord;
			switch (word) {
				case 0x0000:
				case 0x042e:
					// No key, and Ctrl+C, whose entry goes straight to the buttons.
					break;
				case 0x0039 or 0x0839 or 0x0033 or 0x0833 or 0x0034 or 0x0834 or 0x000c or 0x080c or 0x000d or 0x080d:
					// Space, ',', '.', '-' and '=', with or without Shift: Shell_MissionMap (000809a4)'s functions while it is
					// set and the byte 000809a8 is not 0. PLACEHOLDER: the mission map's, S5's.
					break;
				case 0x0052:
					// Insert (00012cca): Tab to the dialog while prefs.cfg option 10, the simulator's throttle keys, is set.
					if (_devices.Preferences is { } preferences && preferences[Es1Preferences.ThrottleKeysOption] != 0) {
						Es1DUIWindow.HandleNavKey(_screens.Globals, NavTab);
					}

					break;
				case 0x0401:
				case 0x0201:
					// Ctrl+Esc and Alt+Esc.
					QuitRequested = true;
					break;
				default:
					HandleLetterKey(word);
					break;
			}
		}

		var globals = _screens.Globals;
		if (block.StickButtons[0] != 0) {
			Es1DUIWindow.HandleNavKey(globals, NavEnter);
			LatchButton(1, 1);
			_enterHeld = true;
		} else if (_enterHeld) {
			Es1DUIWindow.HandleNavKey(globals, NavEnterRelease);
			_enterHeld = false;
		}

		if (block.StickButtons[1] != 0) {
			Es1DUIWindow.HandleNavKey(globals, NavTab);
			LatchButton(1, 2);
		}

		if (!_debugMode && (block.CommandWord & CharacterBit) != 0) {
			SendCharToMenu(block.CommandWord);
		}
	}

	/// <summary>
	/// <c>DUIWindow_SendChar</c> (<c>0001bb85</c>) with the current menu <c>Shell_CurrentMenu</c> (<c>000817c8</c>)'s
	/// window <c>+6</c>, while there is a current menu.
	/// </summary>
	private void SendCharToMenu(ushort word) {
		_screens.CurrentMenu?.Window?.SendChar(_screens.Globals, word);
	}

	/// <summary>
	/// The word's scan code <c>Q</c>, <c>X</c> or <c>C</c>: with <c>Ctrl</c> or <c>Alt</c> (<c>0x600</c>) and not
	/// <c>C</c>, <see cref="QuitRequested"/>; otherwise the character <c>q</c>, <c>x</c> or <c>c</c>, upper case with
	/// <c>0x800</c>, or'd with <c>0x4000</c>, to the current menu.
	/// </summary>
	private void HandleLetterKey(ushort word) {
		int scan = word & 0xff;
		if (scan is not (0x10 or 0x2d or 0x2e)) {
			return;
		}

		if ((word & (Es1Keyboard.AltBit | Es1Keyboard.CtrlBit)) != 0 && scan != 0x2e) {
			QuitRequested = true;
			return;
		}

		int character = scan switch {
			0x10 => 'q',
			0x2d => 'x',
			_ => 'c',
		};
		if ((word & Es1Keyboard.ShiftBit) != 0) {
			character -= 0x20;
		}

		SendCharToMenu((ushort)(character | CharacterBit));
	}
}
