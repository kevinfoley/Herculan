using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.Shell.Menus;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's devices, <c>Shell_InitDevices</c> (<c>ES1GO 00045d24</c>) and <c>Shell_ShutdownDevices</c>
/// (<c>00046094</c>) (docs/es1/shell/startup.md#the-devices): the PC timer and its three callbacks, the display and its
/// four pages, the cursor, the mouse and the keyboard. They are the simulator's libraries, which the shell links too:
/// the PC timer is <see cref="Es1SimClock"/>'s (its interrupts and callback slots; the shell reads none of its simulation
/// countdown), the display <see cref="Es1VideoMemory"/> at <see cref="Es1VideoMemory.ShellWidth"/>, the mouse
/// <see cref="Es1HostMouse"/> and the keyboard <see cref="Es1Keyboard"/> in its <c>INT 15h</c> mode.
/// </summary>
public sealed class Es1ShellDevices {
	/// <summary>The codes <c>KeyQueue_Subscriber</c> is given, the NUL-terminated bytes at <c>00082914</c>: <c>Del</c>, <c>Esc</c>, <c>Q</c>, <c>X</c>, <c>C</c>, <c>Insert</c>, <c>F1</c>–<c>F3</c>.</summary>
	private static readonly byte[] QueueCodes = [0x53, 0x01, 0x10, 0x2d, 0x2e, 0x52, 0x3b, 0x3c, 0x3d];

	/// <summary>The codes <c>Shell_NavKeySubscriber</c> is given, the NUL-terminated bytes at <c>0007e7f0</c>: <c>Tab</c>, <c>Enter</c> and <c>Enter</c>'s release.</summary>
	internal static readonly byte[] NavKeyCodes = [0x0f, 0x1c, 0x9c];

	public Es1ShellDevices() {
		NavKeySubscriber = OnNavKey;
	}

	/// <summary><c>Shell_NavKeySubscriber</c> (<c>00012b4e</c>) as the one delegate the keyboard is given and CONTROLS takes off and gives back.</summary>
	public Es1KeySubscriber NavKeySubscriber { get; }

	/// <summary>
	/// The byte <c>Prefs_MouseOptionApplied</c> (<c>0007e09d</c>), <c>0xff</c> in the image: option 11 as <see cref="ApplyMouseOption"/> last applied it, so
	/// the first call always applies.
	/// </summary>
	public byte MouseOptionApplied { get; private set; } = 0xff;

	/// <summary>
	/// The shell's mouse input device's enabled byte (<c>+0x3a</c>), which CONTROLS' <c>ControlsPanel_ApplyDevices</c>
	/// (<c>00006bed</c>) sets and clears; while it is clear the poll takes no pointer and no buttons, as the simulator's
	/// does (<see cref="Es1DevicePoll.MouseDeviceEnabled"/>).
	/// </summary>
	public bool MouseDeviceEnabled { get; set; } = true;

	/// <summary>The four page origins <c>Shell_CreateDisplay</c> sets at <c>+0x44</c>, one under another.</summary>
	private static readonly (int X, int Y)[] PageOrigins = [(0, 0), (0, 200), (0, 400), (0, 600)];

	// Shell_BlinkTick's counter, 0007e7e8.
	private int _blinkCounter;

	// The menus' globals, whose active window Shell_NavKeySubscriber's dialog key handler acts on; none before Init.
	private Es1MenuGlobals? _globals;

	/// <summary><c>Shell_Timer</c> (<c>00082758</c>), <c>Kind0InputDevice_Ctor</c>(0, <c>0x26d3</c>): the PC timer at the simulator's rate.</summary>
	public Es1SimClock Timer { get; } = new();

	/// <summary><c>Shell_FrameDue</c> (<c>0007fd06</c>), which <see cref="FrameTick"/> sets and <c>Shell_PresentFrame</c> waits on.</summary>
	public bool FrameDue { get; set; }

	/// <summary><c>Shell_WaitCount</c> (<c>000a0a90</c>), which <see cref="WaitTick"/> raises for <c>Shell_WaitTicks</c>.</summary>
	public short WaitCount { get; set; }

	/// <summary>
	/// <c>Shell_WaitTicks</c> (<c>0004bbe9</c>)(<paramref name="count"/>): <see cref="WaitCount"/> zeroed, then a wait
	/// until <see cref="WaitTick"/> has raised it to the count, two interrupts each.
	/// </summary>
	public IEnumerable<Es1ShellWait> WaitTicks(short count) {
		WaitCount = 0;
		while (WaitCount < count) {
			yield return Es1ShellWait.For(() => WaitCount >= count);
		}
	}

	/// <summary><c>Shell_BlinkOn</c> (<c>0007fce0</c>), set for 20 of every 41 <see cref="BlinkTick"/> calls.</summary>
	public bool BlinkOn { get; private set; }

	/// <summary>The display <c>000ab616</c>, which <c>Shell_CreateDisplay</c> (<c>00045b25</c>) makes.</summary>
	public Es1VideoMemory Display { get; } = new(Es1VideoMemory.ShellWidth);

	/// <summary>The display's page pair <c>+0x84</c> and <c>+0x88</c>, which <c>Shell_PresentFrame</c> swaps: 0 and 1.</summary>
	public int PairA { get; set; }

	/// <inheritdoc cref="PairA"/>
	public int PairB { get; set; } = 1;

	/// <summary><c>Shell_MouseDevice</c> (<c>00082760</c>)'s driver, the host's mouse.</summary>
	public Es1HostMouse Mouse { get; } = new();

	/// <summary><c>Shell_KeyboardDevice</c> (<c>0008275c</c>)'s keyboard; none before <see cref="Init"/>.</summary>
	public Es1Keyboard? Keyboard { get; private set; }

	/// <summary>The BIOS's key buffer, which takes the keys <see cref="Keyboard"/> lets the BIOS have and DOS's key read empties.</summary>
	public Es1BiosKeyBuffer BiosKeys { get; } = new();

	/// <summary>The host's keys into <see cref="Keyboard"/>; none before <see cref="Init"/>.</summary>
	public Es1HostKeyboard? HostKeyboard { get; private set; }

	/// <summary>
	/// The dword <c>00082768</c>, the shell's sound manager, which counts as present (the user's choice in P4,
	/// plan-es1-first-mission.md decision 15): <c>ShellSound_Init</c> is not ported (<see cref="Init"/>).
	/// </summary>
	public static bool SoundManagerPresent => true;

	/// <summary>The options <c>Prefs_Options</c> (<c>000aa24e</c>), which <c>000081a4</c> loads; none before <see cref="Init"/>.</summary>
	public Es1Preferences? Preferences { get; private set; }

	/// <summary>
	/// <c>Shell_InitDevices</c>, in its order; <paramref name="debugMode"/> is <c>-X</c>'s <c>Shell_DebugMode</c>, which
	/// with the keyboard is handed to the menus' <paramref name="globals"/>, whose dialog key handler
	/// <c>Shell_NavKeySubscriber</c> calls.
	/// </summary>
	/// <param name="biosShiftFlags">The host's lock and shift state, <see cref="Es1HostKeyboard.ReadBiosShiftFlags"/>.</param>
	public void Init(GameContent content, string installRoot, bool debugMode, byte biosShiftFlags, Es1MenuGlobals globals) {
		_globals = globals;
		globals.DebugMode = debugMode;
		globals.BlinkOn = () => BlinkOn;
		// 0005152c (ClassItem_RegisterBucket) and Light_ResetSystem set up the 3D library, which no ported shell screen
		// draws with; Assert_AddHook(Shell_AssertCleanupHook) adds the fatal assert's hook, and no ported step of the shell
		// asserts.

		// The timer: the three callbacks at a count of 2, every second interrupt, 60 times a second.
		Timer.AddCallback(2, FrameTick);
		Timer.AddCallback(2, WaitTick);
		if (!debugMode) {
			Timer.AddCallback(2, BlinkTick);
		}

		// PLACEHOLDER: ShellSound_Init, the shell's sound manager, plan item 7's module C. The manager counts as
		// present (00082768 set), as on a retail machine with a sound card and as the simulator's does (the user's choice,
		// plan-es1-first-mission.md decision 15): the options' load below leaves options 0-2 as data\prefs.cfg holds them,
		// and what plays through the manager plays nothing.

		CreateDisplay();

		// The cursor: res32\arrow.dci installed, the raster slot 0008662c (VGA_8's slot 31, which takes the cursor off),
		// and Cursor_SyncPosition(1, 1).
		var arrow = content.Read("res32", "ARROW.DCI") is { } bytes
			? Es1CursorImage.From(new DynamixCursorTransformer().Parse(bytes))
			: null;
		if (arrow == null) {
			Console.Error.WriteLine("res32\\arrow.dci is missing or unreadable: no cursor is drawn.");
		}

		Display.Cursor.SetImage(arrow);
		Display.Cursor.Hide();
		Display.Cursor.SyncPosition(1, 1);

		// The mouse: MouseInputDevice_Ctor(0), its bounds (0, 0, 320, 200), which the host's mapping holds the pointer to
		// (Es1HostMouse), Mouse_SetPosition(1, 1), enabled.
		Mouse.SetPosition(1, 1);

		// The keyboard: mode 0x20000, INT 15h, or under -X the simulator's 0x10000; the queue's codes; enabled; and unless
		// -X Shell_NavKeySubscriber.
		// The keys no subscriber claims go to the BIOS's key buffer, which DOS's function 8 reads; the buffer is this
		// engine's stand-in, its characters the host's typed text (Es1BiosKeyBuffer).
		Keyboard = new Es1Keyboard(biosShiftFlags, debugMode ? Es1Keyboard.Int9Mode : Es1Keyboard.Int15Mode) {
			Bios = BiosKeys.Accept,
		};
		globals.Keyboard = Keyboard;
		Keyboard.AddSubscriberCodes(Keyboard.QueueSubscriber, QueueCodes);
		if (!debugMode) {
			Keyboard.AddSubscriber(NavKeySubscriber, NavKeyCodes);
		}

		HostKeyboard = new Es1HostKeyboard(Keyboard) { Pressed = BiosKeys.HostKeyDown, Typed = BiosKeys.HostChar };
		// INT 16h function 2, which Keyboard_SetMode reads when the war map sets the mode again.
		Keyboard.BiosShiftFlags = HostKeyboard.ReadBiosShiftFlags;

		// 000081a4: every option's handler cleared, then Prefs_Init (00008920): option 11's handler
		// Prefs_ApplyMouseOption (0000868c, ApplyMouseOption), Prefs_Load of data\prefs.cfg, which runs each registered handler with its option, so option
		// 11's alone; Speech_ArchiveFound (0007e09c) from Speech_ArchivePresent and, when that is 0, option 2 set to 0 with
		// Prefs_SetOption(1, 0, 2); and Input_CreateJoystickDevices (000087ff).
		string prefsPath = Es1MissionFiles.DataPath(installRoot, "prefs.cfg");
		var preferences = Es1Preferences.FromFile(File.Exists(prefsPath) ? File.ReadAllBytes(prefsPath) : null, prefsPath);
		for (int option = 0; option < Es1Preferences.OptionCount; option++) {
			preferences.SetHandler(option, null);
		}

		preferences.SetHandler(Es1Preferences.MouseOption, ApplyMouseOption);
		ApplyMouseOption(preferences[Es1Preferences.MouseOption]);
		preferences.ShellSpeechArchiveFound = Es1Preferences.SpeechArchiveExists(installRoot);
		if (!preferences.ShellSpeechArchiveFound) {
			preferences.ShellSetOption(true, 0, Es1Preferences.RadioOption);
		}

		// PLACEHOLDER: the shell's Input_CreateJoystickDevices (000087ff): the devices option 7 names, and with option 9 the
		// rudder pedals or option 9 cleared under option 7 = 2, each loaded with Joystick_LoadCalibration (000086d6) from
		// Shell_FileNames lines 1 and 2 through Path_ApplyAltDrive and enabled, as the simulator's Es1JoystickDevices.Create
		// builds them. The shell has no game port here (the simulator's waits for the host's controller,
		// Es1SimulationStart), so it builds no device, Input_PollDeviceBlock reads none (Es1ShellFrame.PollDeviceBlock) and
		// CONTROLS rebuilds none (Es1ShellAlertInput.RebuildJoysticks). No plan step retires it yet.
		// With no sound manager (00082768 0) options 0, 1 and 2 would be set to 0 here; the manager counts as present (above).
		Preferences = preferences;
	}

	/// <summary>
	/// <c>Prefs_ApplyMouseOption</c> (<c>0000868c</c>), option 11's handler, the simulator's statements (<see cref="Es1InputBuild.ApplyMouseOption"/>):
	/// when <paramref name="value"/> differs from <see cref="MouseOptionApplied"/>, it is kept there and, with the mouse
	/// device the shell always builds, the driver turned off for 0 (<c>Mouse_DisableDriver</c>) and on otherwise
	/// (<c>Mouse_EnableDriver</c>). CONTROLS' MOUSE runs it at each click.
	/// </summary>
	public void ApplyMouseOption(byte value) {
		if (value == MouseOptionApplied) {
			return;
		}

		MouseOptionApplied = value;
		if (value == 0) {
			Mouse.DisableDriver();
		} else {
			Mouse.EnableDriver();
		}
	}

	/// <summary>
	/// <c>Shell_CreateDisplay</c> (<c>00045b25</c>): the pair 0 and 1, the draw page 1, the source page <c>+0x88</c> and the
	/// visible page <c>+0x84</c>; the page origins one under another. <c>GLDisplay_SetAttribute</c>(display, 2, 9), the
	/// retrace wait of every page shown, is <c>Shell_PresentFrame</c>'s (<see cref="Es1ShellFrame"/>); the render context
	/// <c>00082754</c> clips nothing a ported step draws.
	/// </summary>
	private void CreateDisplay() {
		PairA = 0;
		PairB = 1;
		Display.DrawPage = 1;
		Display.SourcePage = PairB;
		for (int page = 0; page < PageOrigins.Length; page++) {
			Display.SetPageBase(page, PageOrigins[page].X, PageOrigins[page].Y);
		}

		Display.ShowPage(PairA);
	}

	/// <summary>
	/// <c>Shell_ShutdownDevices</c> (<c>00046094</c>), as far as this engine holds them: the host's keyboard and mouse let
	/// go. The rest returns DOS what the shell took, which the host does not hold: the button sound and the sound
	/// manager (not ported), <c>Shell_NavKeySubscriber</c> and the keyboard and mouse devices, the PC timer's divisor
	/// <c>0x4da6</c>, its callbacks and <c>Pit_RemoveHandler</c>, the render context, and the display, which with
	/// <c>Shell_ExitCode</c> not 0 keeps its mode for the simulator.
	/// </summary>
	public void Shutdown() {
		HostKeyboard?.Detach();
		Mouse.Detach();
	}

	/// <summary><c>Shell_FrameTick</c> (<c>00012974</c>): <see cref="FrameDue"/> set.</summary>
	private void FrameTick() => FrameDue = true;

	/// <summary><c>Shell_WaitTick</c> (<c>0004bbdd</c>): <see cref="WaitCount"/> up by 1.</summary>
	private void WaitTick() => WaitCount++;

	/// <summary>
	/// <c>Shell_BlinkTick</c> (<c>00012933</c>): the counter up to 20 with the flag as it was, then <see cref="BlinkOn"/>
	/// set up to 40, then both cleared.
	/// </summary>
	private void BlinkTick() {
		if (_blinkCounter < 20) {
			_blinkCounter++;
		} else if (_blinkCounter < 40) {
			BlinkOn = true;
			_blinkCounter++;
		} else {
			BlinkOn = false;
			_blinkCounter = 0;
		}
	}

	/// <summary>
	/// <c>Shell_NavKeySubscriber</c> (<c>00012b4e</c>): the code, Num Lock's bit cleared, to the dialog's key handler, and
	/// claimed.
	/// </summary>
	private int OnNavKey(ushort code) {
		if (_globals != null) {
			Es1DUIWindow.HandleNavKey(_globals, (ushort)(code & ~Es1Keyboard.NumLockBit));
		}

		return Es1Keyboard.Claimed;
	}

	/// <summary>
	/// <c>Shell_ScanKeySubscriber</c> (<c>00012b69</c>) (docs/es1/shell/startup.md#the-keyboard-subscribers), which the
	/// name box registers for the ten codes at <c>0007e7f4</c> (<see cref="Es1MTEditBox"/>): <c>Home</c>, <c>Up</c>,
	/// <c>Left</c>, <c>Right</c>, <c>End</c> and <c>Down</c> with Num Lock's bit passed on; the code <c>0x653</c>,
	/// <c>Ctrl+Alt+Del</c>, claimed with nothing done; any other code claimed and handed to
	/// <c>Shell_ForwardKeyToDialog</c> (<c>00012bb7</c>), which gives it to <see cref="Es1DUIWindow.SendKey"/> of the
	/// active window while there is one.
	/// </summary>
	public static int ScanKeySubscriber(Es1MenuGlobals globals, ushort code) {
		if ((code & 0xff) is 0x47 or 0x48 or 0x4b or 0x4d or 0x4f or 0x50 && (code & Es1Keyboard.NumLockBit) != 0) {
			return Es1Keyboard.PassOn;
		}

		if (code != 0x653) {
			globals.Active?.SendKey(globals, code);
		}

		return Es1Keyboard.Claimed;
	}
}
