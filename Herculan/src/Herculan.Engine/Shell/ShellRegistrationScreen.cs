using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>The registration screen's three buttons, in the order <c>Registration_BuildScreen</c> constructs them.</summary>
public enum ShellRegistrationButton {
	/// <summary><c>SKILL LEVEL</c>, <c>Registration_StepSkill</c> (<c>0043c01d</c>).</summary>
	SkillLevel = 0,

	/// <summary><c>CANCEL</c>, <c>Registration_OnCancel</c> (<c>0043c098</c>).</summary>
	Cancel = 1,

	/// <summary><c>ACCEPT</c>, <c>Registration_OnAccept</c> (<c>0043c0fb</c>). Live once the name has a character.</summary>
	Accept = 2,
}

/// <summary>
/// What <c>START NEW GAME</c> opens: a pilot name and a skill for a new campaign career. Built once at
/// startup by <c>Registration_BuildScreen</c> (<c>0043b69e</c>), put up by <c>Registration_Show</c>
/// (<c>0043bc0a</c>) and hidden by <c>Registration_Hide</c> (<c>0043bcb9</c>). See
/// docs/retail/shell/screen-layout.md#the-registration-screen.
///
/// <para>Every rect is a literal in the executable, kept parent-relative as the builder writes it: the
/// panel in the canvas, the box and the two outer buttons in the panel, the rest in the box, and the
/// name field in the name box. The screen keeps the state the original's widgets keep — the field's
/// string and blink phase, the skill, and <c>ACCEPT</c>'s three greying fields — all of it only ever
/// changed by this screen's own handlers, so it survives a <c>CANCEL</c> as the original's does.</para>
/// </summary>
public sealed class ShellRegistrationScreen {
	/// <summary>The content panel, in the canvas.</summary>
	public static readonly ShellRect PanelRect = new(0xce, 0xc6, 0x1b2, 0x144);

	private static readonly ShellRect BoxRect = new(3, 0x17, 0xe1, 0x5e);
	private static readonly ShellRect PromptRect = new(7, 6, 0xd7, 0xf);
	private static readonly ShellRect NameBoxRect = new(7, 0x14, 0xd7, 0x26);
	private static readonly ShellRect SkillReadoutRect = new(0x72, 0x2b, 0xd8, 0x3d);
	private static readonly ShellRect SkillLevelRect = new(7, 0x2c, 0x69, 0x3b);
	private static readonly ShellRect CancelRect = new(10, 0x66, 0x6c, 0x75);
	private static readonly ShellRect AcceptRect = new(0x76, 0x66, 0xd8, 0x75);

	/// <summary>
	/// The name field, <c>{1, 1, W - 1, H - 1}</c> in the name box, where <c>W</c> and <c>H</c> are the box's
	/// <c>+0x2d - +0x25</c> and <c>+0x31 - +0x29</c>: one pixel inside it, over its inner border.
	/// </summary>
	private static readonly ShellRect FieldRect =
		new(1, 1, NameBoxRect.X1 - NameBoxRect.X0 - 1, NameBoxRect.Y1 - NameBoxRect.Y0 - 1);

	/// <summary>The pilot name typed so far — the field's <c>+0x45</c>, empty from the constructor and cleared by nothing.</summary>
	public string Name { get; private set; } = string.Empty;

	/// <summary>
	/// <c>RegistrationSkillChoice</c> (<c>004761ac</c>), 0-3: the skill the readout shows and
	/// <c>ACCEPT</c> gives the player. 0 in the image, and nothing resets it.
	/// </summary>
	public int Skill { get; private set; }

	/// <summary>The field's <c>+0xb3</c>, the caret's blink phase, which <c>ESDialog_Ctor</c> leaves on.</summary>
	public bool CaretOn { get; private set; } = true;

	/// <summary>
	/// <c>ACCEPT</c>'s greying fields: its caption colour (<c>+0x55</c>'s <c>+0xb5</c>), border colour
	/// (<c>+0x4d</c>) and enable flag (<c>+0x49</c>). The builder greys the caption and clears the flag and
	/// leaves the constructor's border; <see cref="Key"/> writes all three together.
	/// </summary>
	private byte _acceptCaption = DisabledColor;
	private byte _acceptBorder = ButtonBorder;
	private bool _acceptLive;

	/// <summary>Whether a button answers a click. <c>SKILL LEVEL</c> and <c>CANCEL</c> are never disabled.</summary>
	public bool IsEnabled(ShellRegistrationButton button) => button != ShellRegistrationButton.Accept || _acceptLive;

	/// <summary>
	/// The name field as the pointer targets it. <c>Registration_Show</c> posts a left press at it and locks
	/// the pointer on it, which the host does through <see cref="ShellPointer.Grab"/>.
	/// </summary>
	public static ShellHit FieldHit => new(new ShellWidget(ShellWidgetKind.RegistrationField, 0), ShellHandler.EditField);

	/// <summary>
	/// A keystroke reaching the field, as <c>ESDialog_HandleEvent</c> (<c>0040beaf</c>) takes it —
	/// <c>+0xbf</c> is the constructor's 1 here, so a character is always tried and a Backspace or left
	/// arrow erases while the field has the focus, down to empty (<c>+0xb7</c> is 0) — followed by the
	/// field's handler, <c>Registration_OnNameEvent</c> (<c>0043bdee</c>), which on every character and
	/// command writes <c>ACCEPT</c>'s greying trio from the name's first character. Enter's release of the
	/// pointer is the host's. Returns whether anything drawn changed.
	/// </summary>
	public bool Key(ShellKey key, bool focused, HudFont? font) {
		bool changed = key.Character is { } c
			? Type(c, font)
			: focused && key.Command is ShellKey.Backspace or ShellKey.Left && Erase();

		var before = (_acceptLive, _acceptCaption, _acceptBorder);
		_acceptLive = Name.Length > 0;
		_acceptCaption = _acceptLive ? ShellChrome.FontInkColor : DisabledColor;
		_acceptBorder = _acceptLive ? ButtonBorder : DisabledColor;
		return changed || before != (_acceptLive, _acceptCaption, _acceptBorder);
	}

	/// <summary>
	/// <c>ESDialog_TypeChar</c> (<c>0040bdd2</c>): a character the set permits goes on the end while the
	/// string stays under 89 characters and the glyph, the string and six pixels more fit inside the field.
	/// </summary>
	private bool Type(char c, HudFont? font) {
		if (!PermittedCharacters.Contains(c) || Name.Length + 1 >= MaxLength) {
			return false;
		}

		if ((font?.Width(c) ?? 0) + (font?.Measure(Name) ?? 0) + CaretWidth >= FieldRect.X1 - FieldRect.X0) {
			return false;
		}

		Name += c;
		return true;
	}

	/// <summary><c>ESDialog_Erase</c> (<c>0040be56</c>): the last character off, while there is one.</summary>
	private bool Erase() {
		if (Name.Length == 0) {
			return false;
		}

		Name = Name[..^1];
		return true;
	}

	/// <summary>One tick of the focused field's blink alarm, every 500 ms: the phase flips, <c>+0xbf</c> being set.</summary>
	public void CaretTick() => CaretOn = !CaretOn;

	/// <summary>
	/// <c>SKILL LEVEL</c>, <c>Registration_StepSkill</c> (<c>0043c01d</c>): the skill steps modulo 4 and the
	/// readout is rewritten. Either button's release reaches it, and it steps forward for both.
	/// </summary>
	public void StepSkill() => Skill = (Skill + 1) % SkillCount;

	/// <summary>
	/// What the pointer hits: the name field, which is an edit field and acts on the left press, or a live
	/// button. Everything else on the screen — the panel, the box, the prompt, the disabled name box and
	/// the disabled readout — swallows a click, which is the same as hitting nothing here.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		if (Inside(NameBox, FieldRect).Contains(canvasX, canvasY)) {
			return FieldHit;
		}

		foreach (var button in Enum.GetValues<ShellRegistrationButton>()) {
			var rect = ButtonRect(button);
			if (IsEnabled(button) && rect.Contains(canvasX, canvasY)) {
				return ShellHit.Button(new ShellWidget(ShellWidgetKind.RegistrationButton, (int)button), rect, canvasX, canvasY);
			}
		}

		return null;
	}

	/// <summary>One button's rect, in the canvas: <c>SKILL LEVEL</c> in the box, the other two in the panel.</summary>
	public static ShellRect ButtonRect(ShellRegistrationButton button) => button switch {
		ShellRegistrationButton.SkillLevel => Inside(Box, SkillLevelRect),
		ShellRegistrationButton.Cancel => Inside(PanelRect, CancelRect),
		_ => Inside(PanelRect, AcceptRect),
	};

	private static ShellRect Box => Inside(PanelRect, BoxRect);

	private static ShellRect NameBox => Inside(Box, NameBoxRect);

	/// <summary>
	/// Draws the screen into <paramref name="surface"/>, in the builder's order. The caller clears it
	/// first; the panel's dithered body leaves the rest to the backdrop. <paramref name="focused"/> is
	/// whether the field has the pointer's focus, whose caret shows while the blink phase is on.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, bool focused) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, PanelFace, PanelBodyDither, TitleHeight,
			headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: false);
		ShellChrome.PaintText(surface, new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight),
			font, text?.Text(TitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);

		ShellChrome.PaintFramedPanel(surface, Box, BoxBorder, BoxFace, fill: true);
		ShellChrome.PaintText(surface, Inside(Box, PromptRect), font, text?.Text(PromptText), ShellTextAlign.Center,
			ShellChrome.FontInkColor);

		// The name box's caption is a single space, which draws nothing; the field covers its inner border.
		ShellChrome.PaintButton(surface, NameBox, ButtonBorder);
		ShellChrome.PaintEditField(surface, Inside(NameBox, FieldRect), font, Name, ShellChrome.FontInkColor,
			caret: focused && CaretOn);

		var readout = Inside(Box, SkillReadoutRect);
		ShellChrome.PaintButton(surface, readout, ReadoutBorder);
		ShellChrome.PaintText(surface, new ShellRect(readout.X0 + 1, readout.Y0, readout.X1, readout.Y1), font,
			text?.Text(FirstSkillWord + Skill), ShellTextAlign.Center, ReadoutTextColor, ShellChrome.InteriorColor);

		foreach (var button in Enum.GetValues<ShellRegistrationButton>()) {
			var rect = ButtonRect(button);
			bool accept = button == ShellRegistrationButton.Accept;
			ShellChrome.PaintButton(surface, rect, accept ? _acceptBorder : ButtonBorder);
			ShellChrome.PaintText(surface, rect, font, text?.Text(CaptionText(button)), ShellTextAlign.Center,
				accept ? _acceptCaption : ShellChrome.FontInkColor);
		}
	}

	private static int CaptionText(ShellRegistrationButton button) => button switch {
		ShellRegistrationButton.SkillLevel => 0x32,
		ShellRegistrationButton.Cancel => 0x33,
		_ => 0x34,
	};

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary>
	/// The field's permitted-character set, <c>00476169</c>, which the builder writes over the class's
	/// upper-case alphabet. Every letter reaches the field upper-cased, so the lower-case run never matches.
	/// </summary>
	private const string PermittedCharacters = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ ";

	/// <summary>The string's limit, <c>0x5a</c>: a character goes on only while the new length stays below it.</summary>
	private const int MaxLength = 0x5a;

	/// <summary>The caret block's width, which the fit test leaves room for.</summary>
	private const int CaretWidth = 6;

	private const int SkillCount = 4;

	/// <summary>The content panel's colour fields, as the constructor and the builder leave them.</summary>
	private const byte PanelBorder = 0x27;
	private const byte PanelFace = 0x25;
	private const byte PanelBodyDither = 0x10;
	private const int TitleHeight = 0x13;
	private const int TitlePlateFirst = 0x3f;
	private const int TitlePlateLast = 0xa6;

	/// <summary>The box's border from <c>ESRegionFill_Ctor</c> and the face the builder writes, a visible checkerboard.</summary>
	private const byte BoxBorder = 0x15;
	private const byte BoxFace = 0x25;

	private const byte ButtonBorder = 0x22;
	private const byte DisabledColor = 0x26;

	/// <summary>The skill readout: a disabled <c>Button</c> with border <c>0x13</c> and an opaque caption in <c>0x17</c>.</summary>
	private const byte ReadoutBorder = 0x13;
	private const byte ReadoutTextColor = 0x17;

	/// <summary><c>estext.bin</c>: <c>REGISTRATION</c>, <c>ENTER NEW PILOT NAME</c>, and the four skill words from <c>ROOKIE</c>.</summary>
	private const int TitleText = 0x30;
	private const int PromptText = 0x31;
	private const int FirstSkillWord = 0x35;
}
