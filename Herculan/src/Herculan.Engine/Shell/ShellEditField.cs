using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// An edit field's string and caret phase as <c>ESDialog_Ctor</c> (<c>0040bbf4</c>) leaves them, with <c>+0xbf</c>
/// and <c>+0xb3</c> set and <c>+0xb7</c> at 0: it takes characters from the start and erases down to empty. Its
/// builder writes only the permitted-character set at <c>+0x9f</c>. The registration screen's name and the DEBUG
/// dialog's mission name are built so; the save rows are not (<see cref="ShellSaveScreen"/>). See
/// docs/retail/shell/main-menu.md#typing-into-a-row.
/// </summary>
/// <param name="permittedCharacters">The set the builder writes at <c>+0x9f</c>.</param>
/// <param name="width">The field's <c>+0x2d - +0x25</c>, which a typed character must leave room in.</param>
public sealed class ShellEditField(string permittedCharacters, int width) {
	/// <summary>The string's limit, <c>0x5a</c>: a character goes on only while the new length stays below it.</summary>
	private const int MaxLength = 0x5a;

	/// <summary>The caret block's width, which the fit test leaves room for.</summary>
	private const int CaretWidth = 6;

	/// <summary>The string typed so far — the field's <c>+0x45</c>, empty from the constructor.</summary>
	public string Text { get; private set; } = string.Empty;

	/// <summary>The caret's blink phase, <c>+0xb3</c>, which the constructor leaves on.</summary>
	public bool CaretOn { get; private set; } = true;

	/// <summary>
	/// A keystroke reaching the field, as <c>ESDialog_HandleEvent</c> (<c>0040beaf</c>) takes it: a character is
	/// always tried, and a Backspace or left arrow erases while the field has the focus. Enter's release of the
	/// pointer is the host's. Returns whether the string changed.
	/// </summary>
	public bool Key(ShellKey key, bool focused, HudFont? font) => key.Character is { } c
		? Type(c, font)
		: focused && key.Command is ShellKey.Backspace or ShellKey.Left && Erase();

	/// <summary>One tick of the focused field's blink alarm, every 500 ms: the phase flips, <c>+0xbf</c> being set.</summary>
	public void CaretTick() => CaretOn = !CaretOn;

	/// <summary>
	/// <c>ESDialog_TypeChar</c> (<c>0040bdd2</c>): a character the set permits goes on the end while the
	/// string stays under 89 characters and the glyph, the string and six pixels more fit inside the field.
	/// </summary>
	private bool Type(char c, HudFont? font) {
		if (!permittedCharacters.Contains(c) || Text.Length + 1 >= MaxLength) {
			return false;
		}

		if ((font?.Width(c) ?? 0) + (font?.Measure(Text) ?? 0) + CaretWidth >= width) {
			return false;
		}

		Text += c;
		return true;
	}

	/// <summary><c>ESDialog_Erase</c> (<c>0040be56</c>): the last character off, while there is one.</summary>
	private bool Erase() {
		if (Text.Length == 0) {
			return false;
		}

		Text = Text[..^1];
		return true;
	}
}
