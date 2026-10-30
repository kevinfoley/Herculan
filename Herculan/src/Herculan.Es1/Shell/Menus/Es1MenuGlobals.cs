using Herculan.Es1.Input;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// A control's message, <c>Shell_PendingMessage</c>'s four words (<c>0007fcb0</c>–<c>0007fcb7</c>)
/// (docs/es1/shell/menu-input.md#a-messages-path): the code, 0 from a control and −1 for none, the control's index in the
/// window, the value its slot <c>+0x2c</c> gives, and the target, the control's id.
/// </summary>
public readonly record struct Es1MenuMessage(short Code, short Index, short Value, short Target) {
	/// <summary>The message <c>Shell_HandleMessage</c> resets the pending one to: (−1, −1, 0, 0), the image's value too.</summary>
	public static Es1MenuMessage None => new(-1, -1, 0, 0);
}

/// <summary>
/// The globals of the shell's window file and the pointer they move (docs/es1/shell/menus.md#globals), each initialised
/// to the value the image holds, with the pending message the releases post and the two of <c>main.cpp</c>'s the active
/// window's activation reads (the keyboard and <c>-X</c>). Retail keeps them as statics; here they are one object the
/// shell host owns and hands to the devices, the screens, the windows, the mouse's callbacks and the frame, so the input
/// shares them. The object is this engine's invention; every member is retail's.
/// </summary>
public sealed class Es1MenuGlobals {
	public Es1MenuGlobals() {
		KeySubscriber = code => Es1DUIWindow.KeySubscriber(this, code);
	}

	/// <summary>
	/// <c>DUIWindow_KeySubscriber</c> (<c>0001ba73</c>) as the one delegate <c>DUIWindow_Activate</c> registers and
	/// <c>DUIWindow_Deactivate</c> removes (<see cref="Es1Keyboard.RemoveSubscriber"/> finds it by equality).
	/// </summary>
	public Es1KeySubscriber KeySubscriber { get; }

	/// <summary>
	/// The keyboard library, <c>Shell_KeyboardDevice</c> (<c>0008275c</c>)'s, which <c>DUIWindow_Activate</c> registers the
	/// window's key subscriber with; set by <see cref="Es1ShellDevices.Init"/>, none before.
	/// </summary>
	public Es1Keyboard? Keyboard { get; set; }

	/// <summary><c>Shell_DebugMode</c> (<c>0007fd08</c>), <c>-X</c>: the window's key subscriber is not registered.</summary>
	public bool DebugMode { get; set; }

	/// <summary>
	/// <c>Shell_PendingMessage</c> (<c>0007fcb0</c>), which a release posts and the frame loop's step 4 hands to
	/// <see cref="Es1ShellScreens.HandleMessage"/>; <see cref="Es1MenuMessage.None"/> in the image.
	/// </summary>
	public Es1MenuMessage PendingMessage { get; set; } = Es1MenuMessage.None;

	/// <summary><c>DUIWindow_Active</c> (<c>0007f0d0</c>), the window that takes the input; none in the image.</summary>
	public Es1DUIWindow? Active { get; set; }

	/// <summary><c>DUIWindow_PressedIndex</c> (<c>0007f0d4</c>), the pressed control's index; −1 in the image.</summary>
	public short PressedIndex { get; set; } = -1;

	/// <summary><c>DUIWindow_HeldIndex</c> (<c>0007f0d6</c>), the control holding the input; −1 in the image.</summary>
	public short HeldIndex { get; set; } = -1;

	/// <summary><c>DUIWindow_FocusIndex</c> (<c>0007f0d8</c>), the focused control's index; −1 in the image.</summary>
	public short FocusIndex { get; set; } = -1;

	/// <summary><c>DUIWindow_Tracking</c> (<c>0007f0da</c>): the pressed control follows the pointer.</summary>
	public bool Tracking { get; set; }

	/// <summary><c>DUIWindow_Holding</c> (<c>0007f0db</c>): a control holds the input.</summary>
	public bool Holding { get; set; }

	/// <summary>
	/// <c>DUIWindow_InputReady</c> (<c>0007f0dc</c>), set in the image: the mouse's move callback runs only while it is
	/// set, and the drawing and the mouse's callbacks clear it for their run.
	/// </summary>
	public bool InputReady { get; set; } = true;

	/// <summary><c>DUIWindow_KeysEnabled</c> (<c>0007f0dd</c>), set in the image: the window's key subscriber acts.</summary>
	public bool KeysEnabled { get; set; } = true;

	/// <summary><c>DUIWindow_LastKey</c> (<c>0007f0c4</c>), the last code the window's key subscriber took; 0 in the image.</summary>
	public short LastKey { get; set; }

	/// <summary>
	/// <c>Shell_SyncPointer</c> (<c>0007fd07</c>), which <see cref="Es1Menu.Open"/> sets and no write found clears: every frame
	/// from then on puts the cursor and the mouse on <see cref="Pointer"/>.
	/// </summary>
	public bool SyncPointer { get; set; }

	/// <summary>
	/// <c>Shell_PointerX</c> and <c>Shell_PointerY</c> (<c>0009aff0</c>, <c>0009aff4</c>), which
	/// <see cref="Es1DUIWindow.PointAtControl"/> and the mouse's move callback store.
	/// </summary>
	public (int X, int Y) Pointer { get; set; }

	/// <summary>
	/// The message box's "up" byte, <c>Shell_MessageBox</c> (<c>0009b010</c>)'s <c>+3</c> (<c>0009b013</c>): while it is
	/// set the screen's window is neither drawn nor updated, and the mouse's press and release do nothing. The message
	/// box is S2's (docs/es1/shell/main-menu.md#instant-actions-path-to-the-mission); nothing here sets it.
	/// </summary>
	public bool MessageBoxUp { get; set; }
}
