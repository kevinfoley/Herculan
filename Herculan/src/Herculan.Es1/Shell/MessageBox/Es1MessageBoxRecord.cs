namespace Herculan.Es1.Shell.MessageBox;

/// <summary>
/// The message box's record <c>Shell_MessageBox</c> (<c>0009b010</c>) (docs/es1/shell/message-box.md#the-record): the kind,
/// <i>to show</i> and <i>up</i>, kind −1 from the static initializer <c>00020b68</c>. The shell's window globals hold it
/// (<see cref="Menus.Es1MenuGlobals.MessageBox"/>), so the mouse's callbacks, the screens and the lip-sync player share it;
/// what the message box does is <see cref="Es1MessageBox"/>'s.
/// </summary>
public sealed class Es1MessageBoxRecord {
	/// <summary>The kind of no message.</summary>
	public const short NoKind = -1;

	/// <summary><c>+0</c>, the kind, an <c>int16</c>.</summary>
	public short Kind { get; private set; } = NoKind;

	/// <summary><c>+2</c>, <i>to show</i>: <c>DigitalDialog_ShowMessage</c> loads the kind's line and <c>MessageBox_Run</c> runs its loop.</summary>
	public bool ToShow { get; private set; }

	/// <summary>
	/// <c>+3</c> (<c>0009b013</c>), <i>up</i>: while it is set <c>MessageBox_Run</c>'s loop runs, the screen's window is neither
	/// drawn nor updated, and the mouse's press and release do nothing. <c>maybe_SncList_Stop</c> clears it when the face's
	/// line ends.
	/// </summary>
	public bool Up { get; set; }

	/// <summary>
	/// <c>MessageBox_SetKind</c> (<c>00037eac</c>)(box, <paramref name="kind"/>): the kind stored, and <see cref="ToShow"/>
	/// and <see cref="Up"/> both cleared for −1 and both set for any other.
	/// </summary>
	public void SetKind(short kind) {
		Kind = kind;
		ToShow = Up = kind != NoKind;
	}
}
