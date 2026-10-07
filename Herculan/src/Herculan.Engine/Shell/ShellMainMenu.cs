using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The main menu's ten buttons, in the order <c>0043094c</c> constructs them. Each value's caption is
/// its own <c>estext.bin</c> entry; see <see cref="ShellMainMenu.CaptionText"/>.
/// </summary>
public enum ShellMainMenuButton {
	/// <summary><c>INSTANT ACTION</c>, <c>004312a6</c>.</summary>
	InstantAction = 0,

	/// <summary><c>START NEW GAME</c>, <c>00431379</c>.</summary>
	StartNewGame = 1,

	/// <summary><c>CONTINUE GAME</c>, <c>004313e4</c>. Live only while slot 10 is in use.</summary>
	ContinueGame = 2,

	/// <summary><c>SAVE/RESTORE</c>, <c>00431498</c> — the save screen, with EXIT set to come back here.</summary>
	SaveRestore = 3,

	/// <summary><c>QUIT</c>, <c>00431727</c>.</summary>
	Quit = 4,

	/// <summary><c>ONLINE MANUAL</c>, <c>0043178c</c>.</summary>
	OnlineManual = 5,

	/// <summary><c>PRACTICE MISSIONS</c>, <c>004318ab</c>.</summary>
	PracticeMissions = 6,

	/// <summary><c>PREFERENCES</c>, <c>0043150c</c>.</summary>
	Preferences = 7,

	/// <summary><c>VIEW DEMO</c>, <c>0043156f</c>.</summary>
	ViewDemo = 8,

	/// <summary><c>CREDITS</c>, <c>004315ec</c>.</summary>
	Credits = 9,
}

/// <summary>
/// Tab 0, <c>MAIN MENU</c> — one titled panel of ten buttons in two columns over the shell's backdrop.
/// Built once by <c>0043094c</c> (<c>wmain.cpp</c>), put up by <c>MainMenu_Show</c> (<c>004310a0</c>)
/// and hidden by <c>MainMenu_Hide</c> (<c>0043114b</c>). See docs/retail/shell/screen-layout.md#the-main-menu.
///
/// <para>Every rect is a literal in the executable, kept parent-relative as the builder writes it: the
/// panel in the canvas, the buttons in the panel.</para>
/// </summary>
public sealed class ShellMainMenu {
	/// <summary>The content panel, in the canvas: <c>{0x90, 0xad, 0x1e2, 0x138}</c>.</summary>
	public static readonly ShellRect PanelRect = new(0x90, 0xad, 0x1e2, 0x138);

	/// <summary>
	/// Whether slot 10, the current-game autosave, is in use — <c>00482a19</c>, the in-use byte of that
	/// slot's <c>GAMEFILE.STR</c> entry, which is what CONTINUE GAME is gated on.
	/// </summary>
	public bool CanContinue { get; set; }

	public ShellMainMenu(bool canContinue = false) => CanContinue = canContinue;

	/// <summary>
	/// Whether a button answers a click. <c>MainMenu_Show</c> writes the greying trio at CONTINUE GAME
	/// alone; the other nine keep the constructor's enable flag.
	/// </summary>
	public bool IsEnabled(ShellMainMenuButton button) =>
		button != ShellMainMenuButton.ContinueGame || CanContinue;

	/// <summary>The button at a canvas point, or null. A disabled button does not answer.</summary>
	public ShellMainMenuButton? ButtonAt(float canvasX, float canvasY) {
		foreach (var button in Enum.GetValues<ShellMainMenuButton>()) {
			if (IsEnabled(button) && ButtonRect(button).Contains(canvasX, canvasY)) {
				return button;
			}
		}

		return null;
	}

	/// <summary>What the pointer hits: a live button, or nothing.</summary>
	public ShellHit? HitAt(float canvasX, float canvasY) =>
		ButtonAt(canvasX, canvasY) is { } button
			? ShellHit.Button(new ShellWidget(ShellWidgetKind.MainMenuButton, (int)button), ButtonRect(button),
				canvasX, canvasY)
			: null;

	/// <summary>One button's rect, in the canvas.</summary>
	public static ShellRect ButtonRect(ShellMainMenuButton button) {
		var (column, row) = button switch {
			ShellMainMenuButton.InstantAction => (0, 0),
			ShellMainMenuButton.StartNewGame => (0, 1),
			ShellMainMenuButton.ContinueGame => (0, 2),
			ShellMainMenuButton.SaveRestore => (0, 3),
			ShellMainMenuButton.Quit => (1, 4),
			ShellMainMenuButton.OnlineManual => (0, 4),
			ShellMainMenuButton.PracticeMissions => (1, 0),
			ShellMainMenuButton.Preferences => (1, 1),
			ShellMainMenuButton.ViewDemo => (1, 2),
			_ => (1, 3),
		};

		int x0 = column == 0 ? LeftColumnX0 : RightColumnX0;
		int y0 = FirstRowY + row * RowPitch;
		return new ShellRect(PanelRect.X0 + x0, PanelRect.Y0 + y0,
			PanelRect.X0 + x0 + ButtonWidth, PanelRect.Y0 + y0 + ButtonHeight);
	}

	/// <summary>
	/// Draws the screen into <paramref name="surface"/>. The caller clears it first; the panel's dithered
	/// body leaves every other pixel to the backdrop.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, PanelFace, PanelBodyDither,
			TitleHeight, headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: false);
		ShellChrome.PaintText(surface,
			new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight), font,
			text?.Text(TitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);

		foreach (var button in Enum.GetValues<ShellMainMenuButton>()) {
			bool enabled = IsEnabled(button);
			var rect = ButtonRect(button);
			ShellChrome.PaintButton(surface, rect, enabled ? ButtonBorder : DisabledColor);
			ShellChrome.PaintText(surface, rect, font, text?.Text(CaptionText(button)),
				ShellTextAlign.Center, enabled ? ShellChrome.FontInkColor : DisabledColor);
		}
	}

	/// <summary>
	/// Each button's <c>estext.bin</c> caption. The builder reaches ten of the run <c>3</c>-<c>0xf</c> and
	/// skips <c>8</c> <c>CONTROLS</c>, <c>9</c> <c>VEHICLE PREVIEW</c> and <c>0xc</c> <c>SERVICE BAY</c>.
	/// </summary>
	public static int CaptionText(ShellMainMenuButton button) => button switch {
		ShellMainMenuButton.InstantAction => 3,
		ShellMainMenuButton.StartNewGame => 4,
		ShellMainMenuButton.ContinueGame => 5,
		ShellMainMenuButton.SaveRestore => 6,
		ShellMainMenuButton.Quit => 0xe,
		ShellMainMenuButton.OnlineManual => 0xf,
		ShellMainMenuButton.PracticeMissions => 7,
		ShellMainMenuButton.Preferences => 0xa,
		ShellMainMenuButton.ViewDemo => 0xb,
		_ => 0xd,
	};

	/// <summary>
	/// The button grid. Every button is <c>{x, y, x + 0x99, y + 0xf}</c> in the panel, on a
	/// <c>0x16</c>-pixel pitch from row <c>0x1c</c>, in columns at <c>0xb</c> and <c>0xad</c>.
	/// </summary>
	private const int LeftColumnX0 = 0xb;
	private const int RightColumnX0 = 0xad;
	private const int FirstRowY = 0x1c;
	private const int RowPitch = 0x16;
	private const int ButtonWidth = 0x99;
	private const int ButtonHeight = 0xf;

	/// <summary>The content panel's colour fields, as the constructor and the builder leave them.</summary>
	private const byte PanelBorder = 0x15;
	private const byte PanelFace = 0x25;
	private const byte PanelBodyDither = 0x10;
	private const int TitleHeight = 0x13;
	private const int TitlePlateFirst = 0x6e;
	private const int TitlePlateLast = 0xe4;

	private const byte ButtonBorder = 0x22;
	private const byte DisabledColor = 0x26;

	private const int TitleText = 2;
}
