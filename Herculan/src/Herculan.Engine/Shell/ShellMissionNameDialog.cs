using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>The DEBUG dialog's two buttons, in the order <c>MissionNameDialog_Build</c> constructs them.</summary>
public enum ShellMissionNameButton {
	/// <summary><c>Use Default</c>, <c>MissionNameDialog_OnUseDefault</c> (<c>0044d55a</c>): the career position's mission.</summary>
	UseDefault = 0,

	/// <summary><c>estext.bin</c> <c>0x34</c>, <c>ACCEPT</c>, <c>MissionNameDialog_OnLoad</c> (<c>0044d5bd</c>): the typed mission.</summary>
	Load = 1,
}

/// <summary>
/// The developer's mission-name dialog, titled <c>DEBUG</c>, which stands between a career's position and its
/// mission load: <c>Use Default</c> loads the position's mission and the other button a typed one. Built once at
/// startup by <c>MissionNameDialog_Build</c> (<c>0044d6a8</c>), put up by <c>MissionNameDialog_Show</c>
/// (<c>0044db25</c>) and hidden by <c>MissionNameDialog_Hide</c> (<c>0044db87</c>). Without <c>-@</c> the shell clicks
/// its <c>Use Default</c> itself before it is ever drawn. See docs/retail/shell/main-menu.md#the-mission-name-dialog.
///
/// <para>Every rect is a literal in the executable, kept parent-relative as the builder writes it: the panel in
/// the canvas, everything else in the panel, and the name field in the name box. Its root is an image panel of the
/// shared backdrop over the whole display, with no chrome, so nothing of the screen beneath shows and a click
/// anywhere but the field and the two buttons is swallowed.</para>
/// </summary>
public sealed class ShellMissionNameDialog {
	/// <summary>The content panel, in the canvas.</summary>
	public static readonly ShellRect PanelRect = new(0xce, 199, 0x1b2, 0x145);

	private static readonly ShellRect PromptRect = new(0x26, 0x1d, 0xb9, 0x26);
	private static readonly ShellRect NameBoxRect = new(10, 0x2b, 0xda, 0x3d);
	private static readonly ShellRect DefaultLabelRect = new(10, 0x46, 0x42, 0x58);
	private static readonly ShellRect DefaultNameRect = new(0x46, 0x46, 0xda, 0x58);
	private static readonly ShellRect UseDefaultRect = new(0x76, 0x66, 0xd8, 0x75);
	private static readonly ShellRect LoadRect = new(10, 0x66, 0x6c, 0x75);

	/// <summary>The name field, <c>{1, 1, W - 1, H - 1}</c> in the name box: one pixel inside it, over its inner border.</summary>
	private static readonly ShellRect FieldRect =
		new(1, 1, NameBoxRect.X1 - NameBoxRect.X0 - 1, NameBoxRect.Y1 - NameBoxRect.Y0 - 1);

	private readonly ShellEditField _name = new(PermittedCharacters, FieldRect.X1 - FieldRect.X0);

	/// <summary>Whether the dialog is up.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>The name <c>Default:</c> reads, <c>MissionNameDialog_DefaultName</c>'s string.</summary>
	public string? DefaultName { get; private set; }

	/// <summary>The name typed so far, which nothing clears, so the next show comes back to it.</summary>
	public string Name => _name.Text;

	/// <inheritdoc cref="ShellEditField.CaretOn"/>
	public bool CaretOn => _name.CaretOn;

	/// <summary>
	/// <c>MissionNameDialog_OnLoad</c>'s path: <c>"msn\%s.msn"</c> of the typed name with its first <c>^</c> made
	/// <c>_</c>. No key types an underscore, and the campaign's mission names (<c>C1_03</c>) carry one.
	/// </summary>
	public string LoadPath {
		get {
			string path = $"msn\\{Name}.msn";
			int caret = path.IndexOf('^');
			return caret < 0 ? path : string.Concat(path.AsSpan(0, caret), "_", path.AsSpan(caret + 1));
		}
	}

	/// <summary>The name field as the pointer targets it.</summary>
	public static ShellHit FieldHit => new(new ShellWidget(ShellWidgetKind.MissionNameField, 0), ShellHandler.EditField);

	/// <summary>
	/// <c>MissionNameDialog_Show</c>: <paramref name="defaultName"/>, the career position's mission or null past the
	/// end of its stage, written as the default, and the dialog shown.
	/// </summary>
	public void Open(string? defaultName) {
		DefaultName = defaultName;
		IsOpen = true;
	}

	/// <summary><c>MissionNameDialog_Hide</c>, which both buttons' handlers open with.</summary>
	public void Close() => IsOpen = false;

	/// <summary>
	/// A keystroke reaching the name field (<see cref="ShellEditField.Key"/>). The builder gives the field no
	/// handler. Returns whether the name changed.
	/// </summary>
	public bool Key(ShellKey key, bool focused, HudFont? font) => _name.Key(key, focused, font);

	/// <inheritdoc cref="ShellEditField.CaretTick"/>
	public void CaretTick() => _name.CaretTick();

	/// <summary>
	/// What the pointer hits while the dialog is up: the name field, which acts on the left press, or a button.
	/// Neither button is ever greyed; everything else, the disabled name box included, swallows a click.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		if (Inside(NameBox, FieldRect).Contains(canvasX, canvasY)) {
			return FieldHit;
		}

		foreach (var button in Enum.GetValues<ShellMissionNameButton>()) {
			var rect = ButtonRect(button);
			if (rect.Contains(canvasX, canvasY)) {
				return ShellHit.Button(new ShellWidget(ShellWidgetKind.MissionNameButton, (int)button), rect, canvasX, canvasY);
			}
		}

		return null;
	}

	/// <summary>One button's rect, in the canvas.</summary>
	public static ShellRect ButtonRect(ShellMissionNameButton button) =>
		Inside(PanelRect, button == ShellMissionNameButton.UseDefault ? UseDefaultRect : LoadRect);

	private static ShellRect NameBox => Inside(PanelRect, NameBoxRect);

	/// <summary>
	/// Draws the dialog, in the builder's order. The root's backdrop is what the renderer draws beneath an empty
	/// content, so the caller clears the surface and paints nothing else. <paramref name="focused"/> is whether the
	/// field has the pointer's focus, whose caret shows while the blink phase is on, and <paramref name="lit"/> the
	/// widget a press has lit.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, bool focused, ShellWidget? lit) {
		if (!IsOpen) {
			return;
		}

		var font = sprites?.Font(ShellArt.ScreenFont);
		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, PanelFace, PanelBodyDither, TitleHeight,
			headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: false);
		ShellChrome.PaintText(surface, new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight),
			font, Title, ShellTextAlign.Center, ShellChrome.FontInkColor);

		ShellChrome.PaintText(surface, Inside(PanelRect, PromptRect), font, Prompt, ShellTextAlign.Center,
			ShellChrome.FontInkColor);

		// The name box's caption is a single space, which draws nothing; the field covers its inner border.
		ShellChrome.PaintButton(surface, NameBox, ButtonBorder);
		ShellChrome.PaintEditField(surface, Inside(NameBox, FieldRect), font, Name, ShellChrome.FontInkColor,
			caret: focused && CaretOn);

		ShellChrome.PaintText(surface, Inside(PanelRect, DefaultLabelRect), font, DefaultLabel, ShellTextAlign.Center,
			ShellChrome.FontInkColor);
		ShellChrome.PaintText(surface, Inside(PanelRect, DefaultNameRect), font, DefaultName, ShellTextAlign.Center,
			ShellChrome.FontInkColor);

		foreach (var button in Enum.GetValues<ShellMissionNameButton>()) {
			ShellChrome.PaintButton(surface, ButtonRect(button), ButtonBorder, font,
				button == ShellMissionNameButton.UseDefault ? UseDefaultCaption : text?.Text(LoadText), ShellChrome.FontInkColor,
				pressed: lit == new ShellWidget(ShellWidgetKind.MissionNameButton, (int)button));
		}
	}

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary>The literals the builder captions with, all but one of them strings of its own rather than <c>estext.bin</c> entries.</summary>
	private const string Title = "DEBUG";
	private const string Prompt = "Enter Mission File Name";
	private const string DefaultLabel = "Default:";
	private const string UseDefaultCaption = "Use Default";

	/// <summary><c>estext.bin</c> <c>0x34</c>, <c>ACCEPT</c>.</summary>
	private const int LoadText = 0x34;

	/// <summary>
	/// The field's permitted-character set, <c>0047a445</c>: the registration name's with <c>^</c> ahead of it, which
	/// <see cref="LoadPath"/> turns into an underscore.
	/// </summary>
	private const string PermittedCharacters = "^0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ ";

	/// <summary>The panel's fields, as <c>ESTitle_Ctor</c> and the builder leave them.</summary>
	private const byte PanelBorder = 0x27;
	private const byte PanelFace = 0x25;
	private const byte PanelBodyDither = 0x10;
	private const int TitleHeight = 0x13;
	private const int TitlePlateFirst = 0x3f;
	private const int TitlePlateLast = 0xa6;

	private const byte ButtonBorder = 0x22;
}
