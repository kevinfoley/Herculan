using Herculan.Es1.Input;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The shell's mouse callbacks into the active window, the dialog file's (docs/es1/shell/menu-input.md#the-mouse):
/// <c>ShellMouse_AddCallbacks</c> registers them with the mouse driver at the start-up's step 13 and
/// <c>ShellMouse_RemoveCallbacks</c> takes them off at the shutdown's step 4.
///
/// <para>Retail's driver runs them at interrupt time, between any two instructions of the shell, and
/// <see cref="Es1MenuGlobals.InputReady"/> keeps the move callback out of a window being drawn. Here the host delivers
/// the mouse's events with its other input, between host frames, before <see cref="Es1ShellFrame.AdvanceHostFrame"/>
/// runs the shell on, so they arrive while the shell's work stands at one of its waits (<see cref="Es1ShellWait"/>): in
/// <c>Shell_PresentFrame</c>, before the pass reads its input and handles the message they post, or in the frame
/// <see cref="Es1ShellScreens.HandleMessage"/> presents on leaving backdrop 1000, where no window is active. So none
/// arrives in the middle of a window's drawing, and <see cref="Es1MenuGlobals.InputReady"/> is set at each of them
/// while a window is active.</para>
/// </summary>
public sealed class Es1ShellMouse {
	private readonly Es1MenuGlobals _globals;

	// The callback slots, the words 0007f0c8, 0007f0ca and 0007f0cc.
	private int _moveSlot, _pressSlot, _releaseSlot;

	public Es1ShellMouse(Es1MenuGlobals globals) {
		_globals = globals;
	}

	/// <summary>
	/// <c>ShellMouse_AddCallbacks</c> (<c>0001c460</c>): <c>00027bc9</c>, the mouse device's getter, whose answer is
	/// dropped; then <see cref="OnMove"/>, <see cref="OnLeftPress"/> and <see cref="OnLeftRelease"/> registered for the
	/// pointer's moves and the left button's presses and releases, their slots kept.
	/// </summary>
	public void AddCallbacks(Es1HostMouse mouse) {
		_moveSlot = mouse.AddCallback(OnMove, Es1HostMouse.Moved);
		_pressSlot = mouse.AddCallback(OnLeftPress, Es1HostMouse.LeftPressed);
		_releaseSlot = mouse.AddCallback(OnLeftRelease, Es1HostMouse.LeftReleased);
	}

	/// <summary><c>ShellMouse_RemoveCallbacks</c> (<c>0001c4de</c>): <c>00027bc9</c>, then the three slots removed.</summary>
	public void RemoveCallbacks(Es1HostMouse mouse) {
		mouse.RemoveCallback(_moveSlot);
		mouse.RemoveCallback(_pressSlot);
		mouse.RemoveCallback(_releaseSlot);
	}

	/// <summary>
	/// <c>ShellMouse_OnMove</c> (<c>0001c605</c>)(<paramref name="record"/>): nothing unless
	/// <see cref="Es1MenuGlobals.InputReady"/> is set and a window is active; <see cref="Es1MenuGlobals.InputReady"/>
	/// cleared for the run; the point stored as <see cref="Es1MenuGlobals.Pointer"/>; while
	/// <see cref="Es1MenuGlobals.Tracking"/> is set, the pressed control given the point relative to the window through its
	/// slot <c>+0x38</c> and its slot <c>+0xc</c> called; otherwise, with the point in the window,
	/// <see cref="Es1DUIWindow.OnDrag"/>. A pressed index past the controls skips the slots, an engine guard.
	/// </summary>
	private void OnMove(Es1MouseRecord record) {
		var globals = _globals;
		if (!globals.InputReady || globals.Active is not { } window) {
			return;
		}

		globals.InputReady = false;
		globals.Pointer = (record.X, record.Y);
		if (globals.Tracking) {
			if (window.HasControl(globals.PressedIndex)) {
				var control = window.Controls[globals.PressedIndex];
				control.SetTrackPoint(record.X - window.Rect.X0, record.Y - window.Rect.Y0);
				control.Invalidate();
			}
		} else if (window.Holds(record.X, record.Y)) {
			window.OnDrag(globals, record.X, record.Y);
		}

		globals.InputReady = true;
	}

	/// <summary>
	/// <c>ShellMouse_OnLeftPress</c> (<c>0001c7d8</c>)(<paramref name="record"/>): nothing while the message box is up;
	/// otherwise <see cref="Es1MenuGlobals.InputReady"/> cleared for the run, and with the point in the active window
	/// <see cref="Es1DUIWindow.OnPress"/>; with no active window or the point outside it,
	/// <see cref="Es1MenuGlobals.PressedIndex"/> −1 and the hangar test.
	/// </summary>
	private void OnLeftPress(Es1MouseRecord record) {
		var globals = _globals;
		if (globals.MessageBoxUp) {
			return;
		}

		globals.InputReady = false;
		if (globals.Active is { } window && window.Holds(record.X, record.Y)) {
			window.OnPress(globals, record.X, record.Y);
		} else {
			globals.PressedIndex = -1;
			// PLACEHOLDER: while the dword 0007fce4 is set (screen 2000's case sets it), the point tested against the selected
			// hangar slot's rects and the message (0, -1, the rect's index or -1, 1702) posted, S4's; no ported screen sets it.
		}

		globals.InputReady = true;
	}

	/// <summary>
	/// <c>ShellMouse_OnLeftRelease</c> (<c>0001caaa</c>)(<paramref name="record"/>): nothing while the message box is up;
	/// otherwise <see cref="Es1MenuGlobals.InputReady"/> cleared for the run. With no active window,
	/// <see cref="Es1MenuGlobals.PressedIndex"/> −1. While <see cref="Es1MenuGlobals.Tracking"/> is set, wherever the point
	/// is: the pressed control put up and changed, with no slot called, the tracking cleared, its message posted and
	/// <see cref="Es1MenuGlobals.PressedIndex"/> −1. Otherwise, with the point in the window,
	/// <see cref="Es1DUIWindow.OnRelease"/>, and outside it <see cref="Es1MenuGlobals.PressedIndex"/> −1. A pressed index
	/// past the controls posts nothing, an engine guard.
	/// </summary>
	private void OnLeftRelease(Es1MouseRecord record) {
		var globals = _globals;
		if (globals.MessageBoxUp) {
			return;
		}

		globals.InputReady = false;
		if (globals.Active is not { } window) {
			globals.PressedIndex = -1;
		} else if (globals.Tracking) {
			if (window.HasControl(globals.PressedIndex)) {
				var control = window.Controls[globals.PressedIndex];
				control.State = Es1DUIControl.Up;
				control.Changed = true;
				globals.PendingMessage = new Es1MenuMessage(0, globals.PressedIndex, control.GetValue(), control.Id);
			}

			globals.Tracking = false;
			globals.PressedIndex = -1;
		} else if (window.Holds(record.X, record.Y)) {
			window.OnRelease(globals, record.X, record.Y);
		} else {
			globals.PressedIndex = -1;
		}

		globals.InputReady = true;
	}
}
