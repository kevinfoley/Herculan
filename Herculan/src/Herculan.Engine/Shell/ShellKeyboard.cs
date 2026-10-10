using Silk.NET.Input;

namespace Herculan.Engine.Shell;

/// <summary>
/// One keystroke as VSHELL's widgets see it: a character (event <c>0x40</c>) or a command (event
/// <c>0x100</c>), each delivered to the pointer's target (docs/retail/shell/main-menu.md#typing-into-a-row).
/// </summary>
public readonly record struct ShellKey(char? Character, int? Command) {
	/// <summary>Backspace's command.</summary>
	public const int Backspace = 1;

	/// <summary>The left arrow's command.</summary>
	public const int Left = 4;

	/// <summary>Enter's command.</summary>
	public const int Enter = 0x0a;
}

/// <summary>
/// VSHELL's keyboard: the three tables that turn a key into the events its widgets receive. See
/// docs/retail/shell/main-menu.md#typing-into-a-row.
///
/// <para><c>MainWndProc</c> (<c>00404a2c</c>) turns a <c>WM_KEYDOWN</c>/<c>WM_KEYUP</c>'s virtual-key
/// code into its position in the table at <c>0046d384</c>, a set-1 scancode, and drops a key that is
/// not in it. A key in the command filter at <c>0046e450</c> becomes a command through
/// <c>0046e471</c>, on the press and on the release alike; any other key's press becomes a character
/// through <c>0046e571</c>, or <c>0046e5c5</c> with Shift down, upper-cased (<c>Keyboard_PostEvents</c> (<c>00408f95</c>)).
/// Only the keys that produce an event are listed here: the digits, the letters, Space and the
/// command keys. Punctuation's virtual-key codes are not in <c>0046d384</c>, and the keypad's digits
/// with Num Lock on are not either.</para>
/// </summary>
public static class ShellKeyboard {
	/// <summary>
	/// The key's scancode position in <c>0046d384</c>, or null for a key VSHELL drops. Silk reports the
	/// arrow and editing cluster separately from the keypad, and both Enter keys are <c>VK_RETURN</c>.
	/// </summary>
	public static int? Index(Key key) => key switch {
		>= Key.Number1 and <= Key.Number9 => 0x02 + (key - Key.Number1),
		Key.Number0 => 0x0b,
		Key.Escape => 0x01,
		Key.Backspace => 0x0e,
		Key.Tab => 0x0f,
		Key.Q => 0x10,
		Key.W => 0x11,
		Key.E => 0x12,
		Key.R => 0x13,
		Key.T => 0x14,
		Key.Y => 0x15,
		Key.U => 0x16,
		Key.I => 0x17,
		Key.O => 0x18,
		Key.P => 0x19,
		Key.Enter or Key.KeypadEnter => 0x1c,
		Key.A => 0x1e,
		Key.S => 0x1f,
		Key.D => 0x20,
		Key.F => 0x21,
		Key.G => 0x22,
		Key.H => 0x23,
		Key.J => 0x24,
		Key.K => 0x25,
		Key.L => 0x26,
		Key.Z => 0x2c,
		Key.X => 0x2d,
		Key.C => 0x2e,
		Key.V => 0x2f,
		Key.B => 0x30,
		Key.N => 0x31,
		Key.M => 0x32,
		Key.Space => 0x39,
		Key.Home => 0x47,
		Key.Up => 0x48,
		Key.PageUp => 0x49,
		Key.Left => 0x4b,
		Key.Right => 0x4d,
		Key.End => 0x4f,
		Key.Down => 0x50,
		Key.PageDown => 0x51,
		Key.Insert => 0x52,
		Key.Delete => 0x53,
		_ => null,
	};

	/// <summary>
	/// The event a key sends, or null for none. A press sends a command or a character; a release sends
	/// a command, and only for a command key, whose release has its own entry at <c>index | 0x80</c>.
	///
	/// <para>With Shift, Ctrl or Alt down, retail indexes the command table with the modifier bits still
	/// in the code, past the table's end, where no byte in the image is an editing command; this engine
	/// sends no command for those (docs/retail/shell/main-menu.md#open). Ctrl+Q posts an event of its own
	/// rather than a character, which no widget ported here takes, so it sends nothing.</para>
	/// </summary>
	public static ShellKey? Event(int index, bool released, bool shift, bool ctrl, bool alt) {
		if (IsCommandKey(index)) {
			if (shift || ctrl || alt) {
				return null;
			}

			int command = CommandTable(released ? index | ReleaseBit : index);
			return command == NoCommand ? null : new ShellKey(null, command);
		}

		if (released || (ctrl && index == CtrlQIndex)) {
			return null;
		}

		return CharacterTable(index, shift) is { } c ? new ShellKey(char.ToUpperInvariant(c), null) : null;
	}

	/// <summary>The filter at <c>0046e450</c>, less its release codes: the keys that are commands.</summary>
	private static bool IsCommandKey(int index) =>
		index is 0x01 or 0x0c or 0x0e or 0x0f or 0x1c or 0x47 or 0x48 or 0x49 or 0x4b or 0x4c or 0x4d or 0x4f
			or 0x50 or 0x51 or 0x52 or 0x53;

	/// <summary><c>0046e471</c> at the filter's own entries; <c>0xff</c> sends nothing.</summary>
	private static int CommandTable(int code) => code switch {
		0x01 => 0x00,
		0x0e => ShellKey.Backspace,
		0x0f => 0x12,
		0x1c => ShellKey.Enter,
		0x47 => 0x0b,
		0x48 => 0x05,
		0x49 => 0x08,
		0x4b => ShellKey.Left,
		0x4d => 0x06,
		0x4f => 0x0c,
		0x50 => 0x07,
		0x51 => 0x09,
		0x52 => 0x02,
		0x53 => 0x03,
		0x81 => 0x13,
		0x8e => 0x14,
		0x8f => 0x25,
		0x9c => 0x1d,
		0xc7 => 0x1e,
		0xc8 => 0x18,
		0xc9 => 0x1b,
		0xcb => 0x17,
		0xcd => 0x19,
		0xcf => 0x1f,
		0xd0 => 0x1a,
		0xd1 => 0x1c,
		0xd2 => 0x15,
		0xd3 => 0x16,
		_ => NoCommand,
	};

	/// <summary>
	/// <c>0046e571</c>, or <c>0046e5c5</c> with Shift, at the keys that reach it. The letters are
	/// upper case in both, and only the digit row differs.
	/// </summary>
	private static char? CharacterTable(int index, bool shift) => index switch {
		>= 0x02 and <= 0x0b => (shift ? ShiftedDigitRow : DigitRow)[index - 0x02],
		0x39 => ' ',
		_ => Letters.IndexOf(index) is >= 0 and var i ? (char)('A' + i) : null,
	};

	private const string DigitRow = "1234567890";
	private const string ShiftedDigitRow = "!@#$%^&*()";

	/// <summary>The scancode of each letter, A to Z.</summary>
	private static readonly List<int> Letters = [
		0x1e, 0x30, 0x2e, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32,
		0x31, 0x18, 0x19, 0x10, 0x13, 0x1f, 0x14, 0x16, 0x2f, 0x11, 0x2d, 0x15, 0x2c,
	];

	private const int ReleaseBit = 0x80;
	private const int NoCommand = 0xff;
	private const int CtrlQIndex = 0x10;
}
