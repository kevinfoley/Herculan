using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The dialog <c>CONTINUE GAME</c> puts up over the menu when the game it loaded has ended: an
/// <c>END OF GAME</c> alert saying why, <c>Restore or start a new game.</c>, and <c>OKAY</c>. Built once at
/// startup by <c>EndOfGame_Build</c> (<c>0044cc2c</c>), filled and put up by <c>EndOfGame_Show(state)</c> (<c>0044cecf</c>) and taken down by
/// <c>OKAY</c>'s handler, <c>EndOfGame_OnOkay</c> (<c>0044cf7b</c>). The launch refusal's sibling, in a window the size of the
/// display, so its rect is a canvas rect; while it is up this engine hit-tests nothing but <c>OKAY</c>,
/// as it does for that dialog. See docs/retail/shell/main-menu.md#the-main-menu.
/// </summary>
public sealed class ShellEndOfGameDialog {
	/// <summary>The alert, an <c>ESAlert</c>, in the canvas.</summary>
	public static readonly ShellRect PanelRect = new(0xcf, 0xcb, 0x1b4, 0x12a);

	private static readonly ShellRect FirstLineRect = new(0xc, 0x1e, 0xd6, 0x2b);
	private static readonly ShellRect SecondLineRect = new(0xc, 0x2c, 0xd6, 0x39);
	private static readonly ShellRect OkayRect = new(0x41, 0x45, 0xa5, 0x54);

	/// <summary>Whether the dialog is up.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>
	/// The first line's <c>estext.bin</c> entry. The builder leaves it on the empty entry 0, and a game state
	/// <see cref="Open"/> has no line for keeps whatever the last one wrote.
	/// </summary>
	public int FirstLineText { get; private set; }

	/// <summary>
	/// <c>EndOfGame_Show(state)</c> (<c>0044cecf</c>): the first line from the game state, <c>0048260e</c>, then the alert up.
	/// State 0 is <c>The war is lost.</c>, 1 <c>The cybrids were defeated.</c> and 3 <c>You have been
	/// killed.</c>; the caller never passes 2, the state a game goes on from.
	/// </summary>
	public void Open(int gameState) {
		FirstLineText = gameState switch {
			0 => WarLostText,
			1 => CybridsDefeatedText,
			3 => KilledText,
			_ => FirstLineText,
		};
		IsOpen = true;
	}

	/// <summary><c>EndOfGame_OnOkay</c> (<c>0044cf7b</c>): the alert down. The menu under it stays up.</summary>
	public void Close() => IsOpen = false;

	/// <summary><c>OKAY</c>'s rect, in the canvas.</summary>
	public static ShellRect OkayButtonRect => Inside(PanelRect, OkayRect);

	/// <summary><c>OKAY</c> under a canvas point, or null.</summary>
	public ShellHit? HitAt(float canvasX, float canvasY) =>
		OkayButtonRect.Contains(canvasX, canvasY)
			? ShellHit.Button(new ShellWidget(ShellWidgetKind.EndOfGameOkay, 0), OkayButtonRect, canvasX, canvasY)
			: null;

	/// <summary>
	/// Draws the dialog over whatever <paramref name="surface"/> holds: <c>ESTitle_Ctor</c>'s filled body
	/// under the face and plate the builder writes, and the two lines centred in <c>0x29</c> with no backing.
	/// <paramref name="lit"/> is the widget a press has lit.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, ShellWidget? lit) {
		if (!IsOpen) {
			return;
		}

		var font = sprites?.Font(ShellArt.ScreenFont);
		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, PanelFace, ShellChrome.InteriorColor, TitleHeight,
			headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: true);
		ShellChrome.PaintText(surface, new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight),
			font, text?.Text(TitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);

		ShellChrome.PaintText(surface, Inside(PanelRect, FirstLineRect), font, text?.Text(FirstLineText),
			ShellTextAlign.Center, ShellChrome.FontInkColor);
		ShellChrome.PaintText(surface, Inside(PanelRect, SecondLineRect), font, text?.Text(SecondLineText),
			ShellTextAlign.Center, ShellChrome.FontInkColor);

		ShellChrome.PaintButton(surface, OkayButtonRect, ButtonBorder, font, text?.Text(OkayText), ShellChrome.FontInkColor,
			pressed: lit == new ShellWidget(ShellWidgetKind.EndOfGameOkay, 0));
	}

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary><c>ESAlert_Ctor</c>'s border argument and its header height, and the face and plate the builder writes.</summary>
	private const byte PanelBorder = 0x15;
	private const int TitleHeight = 0x14;
	private const byte PanelFace = 0x25;
	private const int TitlePlateFirst = 0x31;
	private const int TitlePlateLast = 0xb3;

	private const byte ButtonBorder = 0x22;

	/// <summary><c>estext.bin</c> indices: <c>END OF GAME</c>, the three reasons, the second line and <c>OKAY</c>.</summary>
	private const int TitleText = 0x12e;
	private const int KilledText = 0x12f;
	private const int WarLostText = 0x130;
	private const int CybridsDefeatedText = 0x131;
	private const int SecondLineText = 0x132;
	private const int OkayText = 0x133;
}
