using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The dialog <c>Rock &amp; Roll &gt;</c> refuses through: a <c>WARNING!</c> alert with two centred lines
/// and <c>OKAY</c>, built once at startup by the function ending at <c>0044d27c</c>, filled and put up by
/// <c>LaunchRefusal_Show(code)</c> (<c>0044d27c</c>) and taken down by <c>OKAY</c>'s handler, 
/// <c>LaunchRefusal_OnOkay</c> (<c>0044d404</c>). Like the scrap dialog it is placed in a window the size
/// of the display, so its rect is a canvas rect, and it is treated as modal, a divergence from retail.
/// </summary>
public sealed class ShellLaunchRefusalDialog {
	/// <summary>The alert, an <c>ESAlert</c>, in the canvas.</summary>
	public static readonly ShellRect PanelRect = new(0xb1, 0x67, 0x1d2, 0xc6);

	private static readonly ShellRect FirstLineRect = new(5, 0x1e, 0x117, 0x2b);
	private static readonly ShellRect SecondLineRect = new(5, 0x2c, 0x117, 0x39);
	private static readonly ShellRect OkayRect = new(0x5f, 0x45, 0xc3, 0x54);

	/// <summary>Whether the dialog is up.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>What the dialog is saying, while it is up.</summary>
	public ShellLaunchRefusal Refusal { get; private set; }

	public void Open(ShellLaunchRefusal refusal) {
		Refusal = refusal;
		IsOpen = true;
	}

	public void Close() => IsOpen = false;

	/// <summary><c>OKAY</c>'s rect, in the canvas.</summary>
	public static ShellRect OkayButtonRect => Inside(PanelRect, OkayRect);

	/// <summary><c>OKAY</c> under a canvas point, or null — a click anywhere else is swallowed, as the scrap dialog's is (docs/retail/shell/build-and-armory.md, "The scrap dialog").</summary>
	public ShellHit? HitAt(float canvasX, float canvasY) =>
		OkayButtonRect.Contains(canvasX, canvasY)
			? ShellHit.Button(new ShellWidget(ShellWidgetKind.LaunchRefusalOkay, 0), OkayButtonRect, canvasX, canvasY)
			: null;

	/// <summary>
	/// Draws the dialog over whatever <paramref name="surface"/> holds. The builder writes the face and the
	/// title plate and leaves <c>ESTitle_Ctor</c>'s filled body; the two lines are centred in <c>0x29</c>
	/// with no backing. <paramref name="lit"/> is the widget a press has lit.
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

		int first = FirstLineText + 2 * (int)Refusal;
		ShellChrome.PaintText(surface, Inside(PanelRect, FirstLineRect), font, text?.Text(first), ShellTextAlign.Center,
			ShellChrome.FontInkColor);
		ShellChrome.PaintText(surface, Inside(PanelRect, SecondLineRect), font, text?.Text(first + 1),
			ShellTextAlign.Center, ShellChrome.FontInkColor);

		ShellChrome.PaintButton(surface, OkayButtonRect, ButtonBorder, font, text?.Text(OkayText), ShellChrome.FontInkColor,
			pressed: lit == new ShellWidget(ShellWidgetKind.LaunchRefusalOkay, 0));
	}

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary><c>ESAlert_Ctor</c>'s border argument and its header height, and the face and plate the builder writes.</summary>
	private const byte PanelBorder = 0x15;
	private const int TitleHeight = 0x14;
	private const byte PanelFace = 0x25;
	private const int TitlePlateFirst = 100;
	private const int TitlePlateLast = 0xbd;

	private const byte ButtonBorder = 0x22;

	/// <summary><c>estext.bin</c> indices: <c>WARNING!</c>, the first of the eight refusal lines, and <c>OKAY</c>.</summary>
	private const int TitleText = 0x13b;
	private const int FirstLineText = 0x13c;
	private const int OkayText = 0x144;
}
