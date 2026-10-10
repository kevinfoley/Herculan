using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>The replay dialog's two buttons.</summary>
public enum ShellReplayButton {
	Yes,
	No,
}

/// <summary>
/// <c>REPLAY MISSION?</c>, which the debrief puts up when the campaign ends for want of a pilot or of a war to
/// fight (<see cref="ShellDebrief.CampaignOverState"/> and <see cref="ShellDebrief.ShellState"/>). Built once at
/// startup by <c>ReplayDialog_Build</c> (<c>0044c71c</c>): a full-display picture of the shell's backdrop with a
/// titled panel over it, two centred lines and <c>Yes</c> and <c>No</c>. Filled and put up by
/// <c>ReplayDialog_Show(state)</c> (<c>0044ca57</c>). <c>Yes</c> (<c>ReplayDialog_OnYes</c>, <c>0044cb44</c>) takes it down,
/// loads slot 10 again and flies the mission from it; <c>No</c> (<c>ReplayDialog_OnNo</c>, <c>0044cbbd</c>) saves
/// slot 10, takes it down and shows the main menu. Both are the host's to do. See
/// docs/retail/shell/campaign-loop.md#where-the-debrief-goes-next.
/// </summary>
public sealed class ShellReplayDialog {
	/// <summary>The titled panel, in the canvas: the backdrop picture it sits on is the whole display.</summary>
	public static readonly ShellRect PanelRect = new(0xce, 199, 0x1b2, 0x145);

	private static readonly ShellRect FirstLineRect = new(0x14, 0x2d, 0xdc, 0x3a);
	private static readonly ShellRect SecondLineRect = new(0x14, 0x3c, 0xdc, 0x49);
	private static readonly ShellRect YesRect = new(0x76, 0x66, 0xd8, 0x75);
	private static readonly ShellRect NoRect = new(10, 0x66, 0x6c, 0x75);

	/// <summary>Whether the dialog is up.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>
	/// The two lines' <c>estext.bin</c> entries. The builder leaves both on the empty entry 0, and a state
	/// <see cref="Open"/> has no lines for keeps whatever the last one wrote.
	/// </summary>
	public (int First, int Second) LineTexts { get; private set; }

	/// <summary>
	/// <c>ReplayDialog_Show(state)</c>: the lines from the game state — 0 <c>The war is lost. Do you</c> / <c>want to
	/// replay the mission?</c>, 3 <c>You were killed. Do you</c> / the same — then the backdrop and the panel up.
	/// </summary>
	public void Open(int gameState) {
		LineTexts = gameState switch {
			ShellDebrief.ShellState => (WarLostText, WarLostText + 1),
			ShellDebrief.CampaignOverState => (KilledText, KilledText + 1),
			_ => LineTexts,
		};
		IsOpen = true;
	}

	/// <summary><c>ReplayDialog_Hide</c> (<c>0044cb29</c>): the backdrop and the panel down.</summary>
	public void Close() => IsOpen = false;

	/// <summary>A button's rect, in the canvas.</summary>
	public static ShellRect ButtonRect(ShellReplayButton button) => Inside(PanelRect, button == ShellReplayButton.Yes ? YesRect : NoRect);

	/// <summary>
	/// A button under a canvas point, or null. The backdrop picture covers the display and swallows every
	/// other click while the dialog is up.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		foreach (var button in Enum.GetValues<ShellReplayButton>()) {
			var rect = ButtonRect(button);
			if (rect.Contains(canvasX, canvasY)) {
				return ShellHit.Button(new ShellWidget(ShellWidgetKind.ReplayButton, (int)button), rect, canvasX, canvasY);
			}
		}

		return null;
	}

	/// <summary>
	/// Draws the dialog. The picture under it is the shell's own backdrop at the display's origin with no
	/// border, which is what the renderer already draws beneath the content, so it is left unpainted here.
	/// The panel keeps a dithered body (<c>+0x59</c> cleared) over it, so the bay shows through at half
	/// strength; the title and the buttons are as the other dialogs draw theirs. <paramref name="lit"/> is the
	/// widget a press has lit.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, ShellWidget? lit) {
		if (!IsOpen) {
			return;
		}

		var font = sprites?.Font(ShellArt.ScreenFont);
		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, PanelFace, PanelBodyDither, TitleHeight,
			headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: false);
		ShellChrome.PaintText(surface, new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight),
			font, text?.Text(TitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);

		ShellChrome.PaintText(surface, Inside(PanelRect, FirstLineRect), font, text?.Text(LineTexts.First),
			ShellTextAlign.Center, ShellChrome.FontInkColor);
		ShellChrome.PaintText(surface, Inside(PanelRect, SecondLineRect), font, text?.Text(LineTexts.Second),
			ShellTextAlign.Center, ShellChrome.FontInkColor);

		foreach (var button in Enum.GetValues<ShellReplayButton>()) {
			ShellChrome.PaintButton(surface, ButtonRect(button), ButtonBorder, font,
				text?.Text(button == ShellReplayButton.Yes ? YesText : NoText), ShellChrome.FontInkColor,
				pressed: lit == new ShellWidget(ShellWidgetKind.ReplayButton, (int)button));
		}
	}

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary><c>ESTitle_Ctor</c>'s border and header height, and the face, dither and plate the builder writes.</summary>
	private const byte PanelBorder = 0x27;
	private const int TitleHeight = 0x13;
	private const byte PanelFace = 0x25;
	private const byte PanelBodyDither = 0x10;
	private const int TitlePlateFirst = 0x37;
	private const int TitlePlateLast = 0xad;

	private const byte ButtonBorder = 0x22;

	/// <summary><c>estext.bin</c> indices: <c>REPLAY MISSION?</c>, each state's first line (its second follows it), <c>Yes</c> and <c>No</c>.</summary>
	private const int TitleText = 0x134;
	private const int KilledText = 0x135;
	private const int WarLostText = 0x137;
	private const int YesText = 0x139;
	private const int NoText = 0x13a;
}
