namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - one of ES1's 39 <c>dlg\MENU&lt;n&gt;.DLG</c> in <c>SHELL0.VOL</c>: the window and controls of the shell's menu
/// <c>n</c>, which the shell builds one record at a time (docs/es1/formats/dlg-menu-dialogs.md). Read by
/// <see cref="Io.Transform.Shell.Es1MenuDialogTransformer"/>.
/// </summary>
public class Es1MenuDialog {
	/// <summary>
	/// The records in file order, the menu's window first. A record's position is also the line of <c>eng\MENU&lt;n&gt;.BIN</c>
	/// that is its text (docs/es1/formats/dlg-menu-dialogs.md#the-text).
	/// </summary>
	public Es1MenuControl[] Controls { get; set; } = [];
}

/// <summary>One record; the file holds its fields in this order (docs/es1/formats/dlg-menu-dialogs.md#layout).</summary>
public class Es1MenuControl {
	/// <summary>The record's length in the file.</summary>
	public const int Length = 13;

	/// <summary>The rect's left edge; with the three after it, the rect the control is built with.</summary>
	public short Left { get; set; }

	public short Top { get; set; }

	public short Right { get; set; }

	public short Bottom { get; set; }

	/// <summary>What the record builds (docs/es1/formats/dlg-menu-dialogs.md#the-kinds); 0 is the menu's window.</summary>
	public byte Kind { get; set; }

	/// <summary>A variant within the kind: for a window and a button, which class is built.</summary>
	public byte Style { get; set; }

	/// <summary>A button's key, a keyboard scan code; read for kind 3 only.</summary>
	public byte Key { get; set; }

	/// <summary>
	/// The control's id, which the menu's code finds it by; the window's picks its bitmap array, and is not always the menu's
	/// number (docs/es1/formats/dlg-menu-dialogs.md#layout).
	/// </summary>
	public short Id { get; set; }
}
