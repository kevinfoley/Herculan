using System.Runtime.CompilerServices;
using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.Shell.Menus;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// Screen 1320's case of <c>Shell_HandleMessage</c> (<c>0003e9ae</c>–<c>0003ec5d</c>), OPTIONS > CONTROLS
/// (docs/es1/shell/options.md#the-case): the simulator's controls panel (<see cref="Es1ControlsPanel"/>) on the shell's
/// copy of <c>alert.cpp</c>, run over the backdrop with the keyboard's subscribers changed for it. The case runs on into
/// screen 1300's.
/// </summary>
public sealed class Es1ShellControls {
	/// <summary>The screen, CONTROLS.</summary>
	public const short Screen = 1320;

	/// <summary>The point the case gives the panel, OPTIONS' window's corner.</summary>
	private const int PanelX = 49, PanelY = 50;

	/// <summary><c>Shell_Fonts</c> (<c>000aacc6</c>) entry 5, <c>nm_green</c>, the readouts' font.</summary>
	private const int ReadoutFontSlot = 5;

	/// <summary>The codes given to <c>KeyQueue_Subscriber</c> while the panel runs, the NUL-terminated bytes at <c>000817b8</c>: <c>Esc</c>, <c>Tab</c>, <c>Enter</c>.</summary>
	private static readonly byte[] PanelKeys = [0x01, 0x0f, 0x1c];

	private readonly Es1ShellDevices _devices;
	private readonly Es1ShellScreens _screens;

	// The shell's alert.cpp globals, made at the first CONTROLS: AlertPanel_CursorImage (0007d848) is loaded once.
	private Es1AlertGlobals? _alertGlobals;

	public Es1ShellControls(Es1ShellDevices devices, Es1ShellScreens screens) {
		_devices = devices;
		_screens = screens;
	}

	/// <summary>
	/// The case's steps 2 to 8: the page last drawn shown and the other drawn on, as <c>Shell_PresentFrame</c>'s step 4
	/// does with its retrace waits, and the pair swapped; the mouse queue started and <see cref="Es1ShellMouseQueue.Live"/>
	/// set; <c>ControlsPanel_Ctor</c>(0, (49, 50), 1) with the shell's numbers and readouts
	/// (<see cref="Es1ControlsVariant.Shell"/>); unless <c>Shell_DebugMode</c>, with a keyboard, <c>Shell_NavKeySubscriber</c>
	/// and <c>Shell_ScanKeySubscriber</c> removed, the keyboard's mode kept and set to <c>0x20000</c>, and
	/// <see cref="PanelKeys"/> given to <c>KeyQueue_Subscriber</c>; the panel run through its slot <c>+0xc</c> and
	/// destroyed (<c>ControlsPanel_Dtor</c>(panel, 3)); the mouse queue stopped and <see cref="Es1ShellMouseQueue.Live"/>
	/// cleared; and with the same two conditions <c>Shell_NavKeySubscriber</c> added for its codes and
	/// <c>Shell_ScanKeySubscriber</c> for the ten at <c>0007e7f4</c>, <see cref="PanelKeys"/> taken back, and the mode put
	/// back. So <c>Shell_ScanKeySubscriber</c> stays registered afterwards with no edit box up (options.md's Open). The
	/// panel's loop stands at a next host frame between passes (<see cref="Es1ShellWait.NextHostFrame"/>).
	/// </summary>
	/// <param name="data">The shared data: the font slots, the styles, the corners and <c>CTL_ALRT.BND</c>.</param>
	/// <param name="preferences">The shell's options, <c>Prefs_Options</c>.</param>
	public IEnumerable<Es1ShellWait> Run(Es1ShellData data, Es1Preferences preferences, GameContent content) {
		var devices = _devices;
		var display = devices.Display;
		_screens.PageShown = false;
		yield return Es1ShellWait.For(Es1Retrace.VerticalRetrace);
		display.ShowPage(devices.PairB);
		yield return Es1ShellWait.For(default(Es1Retrace));
		_screens.PageShown = true;
		display.DrawPage = devices.PairA;
		(devices.PairA, devices.PairB) = (devices.PairB, devices.PairA);

		var queue = _screens.MapMouse;
		queue.Start();
		queue.Live = true;
		var globals = _alertGlobals ??= MakeAlertGlobals(data, preferences, content);
		var panel = new Es1ControlsPanel(globals, new Es1Rect(PanelX, PanelY, PanelX, PanelY), true,
			Es1ControlsVariant.Shell(data.CtlAlertBnd, data.Fonts[ReadoutFontSlot]));

		var menus = _screens.Globals;
		var keyboard = menus.DebugMode ? null : menus.Keyboard;
		int keptMode = 0;
		if (keyboard != null) {
			keyboard.RemoveSubscriber(devices.NavKeySubscriber);
			keyboard.RemoveSubscriber(menus.ScanKeySubscriber);
			keptMode = keyboard.Mode;
			keyboard.SetMode(Es1Keyboard.Int15Mode);
			keyboard.AddSubscriberCodes(keyboard.QueueSubscriber, PanelKeys);
		}

		// The panel's loop waits only for the next host frame (Es1Wait.NextHostFrame).
		foreach (var _ in panel.Run(new StrongBox<int>())) {
			yield return Es1ShellWait.NextHostFrame;
		}

		panel.Destroy();
		queue.Stop();
		queue.Live = false;
		if (keyboard != null) {
			keyboard.AddSubscriber(devices.NavKeySubscriber, Es1ShellDevices.NavKeyCodes);
			keyboard.AddSubscriber(menus.ScanKeySubscriber, Es1MTEditBox.ScanKeyCodes);
			keyboard.RemoveSubscriberCodes(keyboard.QueueSubscriber, PanelKeys);
			keyboard.SetMode(keptMode);
		}
	}

	/// <summary>
	/// The shell's <c>alert.cpp</c> globals (docs/es1/shell/options.md#the-panel-base--alertpanel_ctorbase-0000222d): the
	/// display, the shell's mouse queue, the shell's input (<see cref="Es1ShellAlertInput"/>), the widgets'
	/// <c>Shell_Fonts</c> entries 0 to 4, the styles table <c>00082654</c> and the corners <c>000ab4ee</c>; the page on the
	/// screen <c>+0x84</c>, no open count, and the [Ctrl+C] entry. The hooks <c>0007d84c</c> and <c>0007d850</c> are 0 in
	/// the image, and <c>es2_xref.py --decode</c> finds only <c>AlertPanel_Enter</c>'s and <c>AlertPanel_Leave</c>'s reads.
	/// </summary>
	private Es1AlertGlobals MakeAlertGlobals(Es1ShellData data, Es1Preferences preferences, GameContent content) {
		var devices = _devices;
		var widgets = new Es1WidgetResources {
			Styles = data.Styles,
			Title = data.Fonts[0],
			Cpylw = data.Fonts[1],
			Active = data.Fonts[2],
			Pushed = data.Fonts[3],
			Inactive = data.Fonts[4],
			Corners = data.Corners,
		};

		var input = new Es1ShellAlertInput(_screens.PollInput!, _screens.LatchButton!, preferences, devices);
		return new Es1AlertGlobals(devices.Display, _screens.MapMouse.Queue, input, widgets, content) {
			PageOnScreen = () => devices.PairA,
			CountsOpen = false,
			// PLACEHOLDER: 00014ac7(CAPTURE), which builds CAPTURE paths with Screenshot_BuildPath and writes the screen to
			// a .SCR and a .PAL (options.md's Open); the panel takes the key and returns 1 here, and nothing is written.
			Capture = () => { },
		};
	}
}

/// <summary>
/// The shell's input and options for its copy of <c>alert.cpp</c> and the controls panel
/// (docs/es1/shell/options.md#the-panel-base--alertpanel_ctorbase-0000222d): <c>Shell_PollInput</c>, the shell's
/// <c>Input_LatchButton</c> and <c>prefs.cpp</c>, and the shell's devices.
/// </summary>
public sealed class Es1ShellAlertInput : Es1AlertInput {
	private readonly Func<Es1LiveBlock> _poll;
	private readonly Action<byte, int> _latch;
	private readonly Es1Preferences _preferences;
	private readonly Es1ShellDevices _devices;

	/// <param name="poll"><c>Shell_PollInput</c> (<see cref="Es1ShellFrame.PollInput"/>).</param>
	/// <param name="latch"><c>Input_LatchButton</c> (<see cref="Es1ShellFrame.LatchButton"/>).</param>
	public Es1ShellAlertInput(Func<Es1LiveBlock> poll, Action<byte, int> latch, Es1Preferences preferences, Es1ShellDevices devices) {
		_poll = poll;
		_latch = latch;
		_preferences = preferences;
		_devices = devices;
	}

	public override Es1Preferences Preferences => _preferences;

	/// <summary>
	/// <c>Shell_PollInput</c> (<c>0004de3a</c>), the block the shell's panel reads: its command word, pointer and mouse
	/// buttons, and its eight buttons from <c>+0x1d</c>, which <c>AlertPanel_HandleEvent</c> and the controls panel's
	/// JOYSTICK read where the simulator's read <c>+0x15</c>.
	/// </summary>
	public override Es1PlayerInputBlock Build() {
		var live = _poll();
		var block = new Es1PlayerInputBlock {
			CommandWord = live.CommandWord,
			PointerX = live.PointerX,
			PointerY = live.PointerY,
		};

		Array.Copy(live.MouseButtons, block.MouseButtons, block.MouseButtons.Length);
		Array.Copy(live.StickButtons, block.Buttons, block.Buttons.Length);
		return block;
	}

	/// <summary>The shell's <c>Input_LatchButton</c> (<c>0004de18</c>)(1, button).</summary>
	public override void LatchButton(int button) => _latch(1, button);

	/// <summary>
	/// The 0x29 bytes at <c>000a0ba4</c> copied to the panel's <c>+0x2e8</c>, with no button latched. The static
	/// initialiser <c>0004deb0</c> builds that block, and <c>es2_xref.py</c> finds it held there and in
	/// <c>AlertPanel_Enter</c> and <c>AlertPanel_Leave</c> alone, so there is nothing to keep it for here.
	/// </summary>
	public override Es1PlayerInputBlock? Keep() => null;

	/// <summary>The 0x29 bytes copied back to <c>000a0ba4</c> (<see cref="Keep"/>); the mouse is not moved back.</summary>
	public override void PutBack(Es1AlertGlobals globals, Es1PlayerInputBlock? kept) {
	}

	/// <summary>The shell's <c>Prefs_SetOption</c> (<see cref="Es1Preferences.ShellSetOption"/>) with apply 1.</summary>
	public override void SetOption(byte value, int index) => _preferences.ShellSetOption(true, value, index);

	public override byte ToggleOption(int index) => _preferences.ShellToggleOption(true, index);

	public override byte StepOption(int modulus, int index) => _preferences.ShellStepOption(true, modulus, index);

	public override byte StepOptionBack(int modulus, int index) => _preferences.ShellStepOptionBack(true, modulus, index);

	/// <summary>
	/// <c>Prefs_CommitAll</c> (<c>000082b0</c>)(1) (<see cref="Es1Preferences.ShellCommitAll"/>): of options 7 to 11 the
	/// shell registers a handler for 11 alone (<see cref="Es1ShellDevices.ApplyMouseOption"/>).
	/// </summary>
	public override void CommitAll() => _preferences.ShellCommitAll(true);

	/// <summary>
	/// <c>ControlsPanel_ApplyDevices</c>' (<c>00006bed</c>) rebuild: the two joystick devices destroyed
	/// (<c>JoystickDevice_Dtor</c>(device, 3)) and built again as <c>Prefs_Init</c>'s <c>000087ff</c> builds them.
	/// PLACEHOLDER: the shell holds no joystick devices here (<see cref="Es1ShellDevices.Init"/>'s placeholder for
	/// <c>000087ff</c>), so there is none to destroy or build.
	/// </summary>
	public override void RebuildJoysticks() {
	}

	/// <summary>
	/// <c>InputDevice_Enable</c> or <c>InputDevice_Disable</c> of the shell's mouse device's <c>+0x3a</c>, which the shell
	/// always builds (<see cref="Es1ShellDevices.MouseDeviceEnabled"/>).
	/// </summary>
	public override void SetMouseDeviceEnabled(bool enabled) => _devices.MouseDeviceEnabled = enabled;
}
