using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.MessageBox;

/// <summary>
/// <c>dialog.cpp</c>'s <c>DialogBox</c> (vtable <c>0007ee88</c>), <c>0x2d</c> bytes: the shell's text box, which cuts a text
/// into lines that fit its rect and shows one page of them (docs/es1/shell/message-box.md#the-box). The subclasses differ
/// only in their constructors and destructors: <see cref="Es1DefaultDialogBox"/>, <see cref="Es1DigitalDialogBox"/> (a
/// message), and the <c>.ENG</c> block's <c>ConditionalDialogBox</c>, which the war map's briefing and the debriefing load.
///
/// <para>Retail keeps the text in a buffer it cuts in place, each line's start a pointer into it ended by a NUL the wrap
/// writes; here the buffer is a <see cref="char"/> array and the line slots indexes into it, −1 for a slot never set.</para>
/// </summary>
public abstract class Es1DialogBox {
	/// <summary>The fill color of a box outside the top strip, <c>Raster_FillFrameRectColours</c>(<c>0x1f</c>, <c>0x1f</c>, rect).</summary>
	private const byte FillColor = 0x1f;

	/// <summary>Where <c>DialogBox_Draw</c> draws the top strip's frame bitmap.</summary>
	private const int FrameX = 3, FrameY = 4;

	/// <summary><c>Shell_RadioOption</c>'s <c>VOICE ONLY</c>, which hides the frame and a message's text.</summary>
	public const short VoiceOnly = 1;

	/// <summary><c>DialogBox_TopRect</c> (<c>0009ae64</c>), the top strip's text, from <c>Dialog_StaticInit</c> (<c>00019c2a</c>).</summary>
	public static readonly Es1Rect TopRect = new(8, 7, 284, 29);

	/// <summary><c>DialogBox_TopFrameRect</c> (<c>0009ae74</c>), the top strip's frame.</summary>
	public static readonly Es1Rect TopFrameRect = new(3, 4, 287, 32);

	/// <summary><c>DialogBox_Backdrop6000Rect</c> (<c>0009ae84</c>), the war map's box.</summary>
	public static readonly Es1Rect Backdrop6000Rect = new(69, 150, 267, 184);

	/// <summary><c>DialogBox_Backdrop5000Rect</c> (<c>0009ae94</c>), the debriefing's box.</summary>
	public static readonly Es1Rect Backdrop5000Rect = new(17, 31, 143, 85);

	/// <summary><c>DialogBox_Backdrop7000Rect</c> (<c>0009aea4</c>), backdrop 7000's box.</summary>
	public static readonly Es1Rect Backdrop7000Rect = new(108, 160, 299, 195);

	/// <summary>The dialog record the box belongs to, which holds what every box shares.</summary>
	protected Es1ShellDialog Dialog { get; }

	/// <summary><c>DialogBox_Init</c> (<c>00019b25</c>), which each constructor runs first: <see cref="TopStrip"/> set and the rest cleared; the rect is left as it is.</summary>
	protected Es1DialogBox(Es1ShellDialog dialog) {
		Dialog = dialog;
		Init();
	}

	/// <summary><c>+0x00</c>: the box stands in the framed strip at the top and draws its frame.</summary>
	public bool TopStrip { get; protected set; }

	/// <summary><c>+0x01</c>, the font: <c>000aacda</c>, the <c>nm_green</c> font, for every class.</summary>
	public HudFont? Font { get; protected set; }

	/// <summary><c>+0x05</c>–<c>+0x11</c>, the box: left, top, right, bottom.</summary>
	public Es1Rect Rect { get; protected set; }

	/// <summary><c>+0x15</c>, the text, a NUL after it, cut into lines in place by <see cref="Wrap"/>; none while the box has none.</summary>
	private char[]? _text;

	/// <summary><c>+0x19</c>, the count of line slots.</summary>
	public short SlotCount { get; private set; }

	/// <summary><c>+0x1b</c>, the line slots: each line's start in the text, −1 for a slot not set; none while the box has none.</summary>
	private int[]? _slots;

	/// <summary><c>+0x1f</c>, one point per line of a page: where <see cref="Draw"/> puts the line's pen.</summary>
	private (int X, int Y)[] _points = [];

	/// <summary><c>+0x23</c>, the count of pages.</summary>
	public short PageCount { get; private set; }

	/// <summary><c>+0x25</c>, the lines a page holds.</summary>
	public short LinesPerPage { get; private set; }

	/// <summary><c>+0x27</c>, the page shown.</summary>
	public short Page { get; private set; }

	/// <summary><c>DialogBox_Init</c> (<c>00019b25</c>): <see cref="TopStrip"/> set; the font, the text, the slots, the points and the four words cleared.</summary>
	private void Init() {
		TopStrip = true;
		Font = null;
		_text = null;
		_slots = null;
		_points = [];
		SlotCount = PageCount = LinesPerPage = Page = 0;
	}

	/// <summary>The box's text as its constructor copies it, a NUL after it.</summary>
	protected void SetText(string text) => _text = (text + '\0').ToCharArray();

	/// <summary>
	/// <c>DialogBox_Layout</c> (<c>000198a6</c>) (docs/es1/shell/message-box.md#laying-out-the-text): with <i>h</i> the font's
	/// cell height less 2 (<c>HudFont_CellHeight2</c>), the slot count the text's width over the box's width plus 2, the
	/// lines a page holds the box's height over <i>h</i>, the page count the slot count over that plus 1 when the two
	/// differ, the page 0; the slots cleared; line <i>i</i>'s point (left, top + (<i>i</i> + 1)<i>h</i>); and the text cut
	/// into lines (<see cref="Wrap"/>), its count dropped. A box with no font, a width or line height of 0 or less, or no
	/// line to a page, where retail reads a null font or divides by 0, is laid out with no slots here, an engine guard.
	/// </summary>
	protected void Layout() {
		var font = Font;
		var rect = Rect;
		int width = rect.X1 - rect.X0;
		if (font == null || _text == null) {
			return;
		}

		short lineHeight = (short)(font.CellHeight - 2);
		if (width <= 0 || lineHeight <= 0 || (rect.Y1 - rect.Y0) / lineHeight is not (> 0 and var perPage)) {
			return;
		}

		SlotCount = (short)(font.Measure(Line(0)) / width + 2);
		LinesPerPage = (short)perPage;
		PageCount = (short)(SlotCount / LinesPerPage + (SlotCount != LinesPerPage ? 1 : 0));
		Page = 0;
		_slots = new int[SlotCount];
		Array.Fill(_slots, -1);
		_points = new (int X, int Y)[LinesPerPage];
		for (int i = 0; i < _points.Length; i++) {
			_points[i] = (rect.X0, i * lineHeight + rect.Y0 + lineHeight);
		}

		Wrap((short)width);
	}

	/// <summary>The text from <paramref name="start"/> to its next NUL.</summary>
	private string Line(int start) {
		var text = _text!;
		int end = Array.IndexOf(text, '\0', start);
		return new string(text, start, (end < 0 ? text.Length : end) - start);
	}

	/// <summary>
	/// <c>DialogBox_Wrap</c> (<c>00019a1d</c>)(box, <paramref name="width"/>, text) (docs/es1/shell/message-box.md#laying-out-the-text):
	/// the text walked in place, slot 0 its start; a newline ends a line there, the break moved to it; at a space the line
	/// so far measured, and when it is wider than <paramref name="width"/> the line cut at the last break, a space there
	/// made a NUL, the next slot the break's next character, the break left where it was, otherwise the break moved to the
	/// space; after the last character the last line measured and, when too wide, cut at the break once more. Returns the
	/// count of cuts and newlines. Retail writes the starts past the slots' allocation for a text of more lines than
	/// <see cref="SlotCount"/>; here those starts are kept for the wrap's own reads and dropped from the slots, which
	/// <see cref="Draw"/> reads only below the count, an engine guard.
	/// </summary>
	private short Wrap(short width) {
		var text = _text!;
		var font = Font!;
		var starts = new List<int> { 0 };
		int brk = 0;
		for (int at = 0; text[at] != '\0'; at++) {
			if (text[at] == '\n') {
				text[at] = '\0';
				brk = at;
				starts.Add(at + 1);
			} else if (text[at] == ' ') {
				text[at] = '\0';
				if (font.Measure(Line(starts[^1])) > width) {
					if (text[brk] == ' ') {
						text[brk] = '\0';
					}

					starts.Add(brk + 1);
				} else {
					brk = at;
				}

				text[at] = ' ';
			}
		}

		if (font.Measure(Line(starts[^1])) > width) {
			text[brk] = '\0';
			starts.Add(brk + 1);
		}

		var slots = _slots!;
		for (int i = 0; i < starts.Count && i < slots.Length; i++) {
			slots[i] = starts[i];
		}

		return (short)(starts.Count - 1);
	}

	/// <summary>
	/// <c>DialogBox_Draw</c> (<c>00018edd</c>)(box, <paramref name="mode"/>), <paramref name="mode"/> the dialog's version
	/// word (docs/es1/shell/message-box.md#drawing-a-page), on the display's draw page: with <see cref="TopStrip"/> set,
	/// unless <c>Shell_RadioOption</c> is <see cref="VoiceOnly"/>, <c>res32\textbox.dba</c>'s first bitmap at (3, 4), the
	/// array loaded on first use; with it clear, the rect filled in colour <c>0x1f</c>. Then, with a font and slots: unless
	/// <paramref name="mode"/> is 2 and the option is <see cref="VoiceOnly"/>, the page's lines, each slot below the count
	/// that is set drawn in the font on the next line point; and the message's line started
	/// (<see cref="Es1LipSyncPlayer.StartSound"/>).
	/// </summary>
	public void Draw(short mode) {
		var dialog = Dialog;
		// PLACEHOLDER: retail draws through the render context installed when it runs, whose alignment +0x231 decides where a
		// line's pen goes (message-box.md's Open); here a full-screen context draws from the pen, alignment 0.
		var canvas = new Es1PanelCanvas(dialog.Display, Es1ShellScreens.FullScreen);
		short radio = dialog.RadioOption();
		if (!TopStrip) {
			// Raster_FillFrameRectColours (0004b8d8): the box filled under the brush {0, fill}, then its edges under {4, frame}.
			canvas.FillRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, FillColor);
			canvas.OutlineRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, FillColor);
		} else if (radio != VoiceOnly) {
			canvas.Blit(dialog.FrameBitmaps() is { Count: > 0 } frames ? frames[0] : null, FrameX, FrameY);
		}

		if (Font == null || _slots is not { } slots) {
			return;
		}

		if (mode != Es1ShellDialog.MessageVersion || radio != VoiceOnly) {
			int point = 0;
			int first = Page * LinesPerPage;
			for (int i = first; i < first + LinesPerPage; i++) {
				if (i < SlotCount && slots[i] >= 0) {
					var (x, y) = _points[point++];
					canvas.DrawString(Font, Line(slots[i]), x, y);
				}
			}
		}

		dialog.Player.StartSound();
	}

	/// <summary>
	/// <c>DialogBox_MarkDirty</c> (<c>0001907e</c>): <see cref="TopFrameRect"/> when <see cref="TopStrip"/> is set and the
	/// box's rect otherwise added as a dirty box with delay 0 and again with delay 1.
	/// </summary>
	public void MarkDirty() {
		var rect = TopStrip ? TopFrameRect : Rect;
		Dialog.Dirty.AddRect(rect.X0, rect.Y0, rect.X1, rect.Y1, 0);
		Dialog.Dirty.AddRect(rect.X0, rect.Y0, rect.X1, rect.Y1, 1);
	}

	/// <summary><c>DialogBox_PrevPage</c> (<c>000190fd</c>): while the page is above 0, one page back and <see cref="TopStrip"/> cleared.</summary>
	public void PrevPage() {
		if (Page > 0) {
			Page--;
			TopStrip = false;
		}
	}

	/// <summary><c>DialogBox_NextPage</c> (<c>0001911f</c>): while the page is below the page count less 1, one page on and <see cref="TopStrip"/> cleared.</summary>
	public void NextPage() {
		if (Page < PageCount - 1) {
			Page++;
			TopStrip = false;
		}
	}

	/// <summary>
	/// The destructor, the vtable's slot <c>+4</c>, which <c>Dialog_DeleteBox</c> calls with 3: the class's own work, then
	/// <see cref="DtorBase"/>. The waits are those of the scripts a subclass's destructor runs.
	/// </summary>
	public virtual IEnumerable<Es1ShellWait> Dtor() {
		DtorBase();
		yield break;
	}

	/// <summary>
	/// <c>DialogBox_Dtor</c> (<c>00019b54</c>)(box, 0): the text, the slots and the points freed and the box cleared
	/// (<c>DialogBox_Init</c>), the frame bitmaps destroyed when loaded, and the top strip's frame rect added as a dirty
	/// box with delay 0, the rect <see cref="TopStrip"/> picks having just been set.
	/// </summary>
	protected void DtorBase() {
		Init();
		Dialog.ReleaseFrameBitmaps();
		var rect = TopStrip ? TopFrameRect : Rect;
		Dialog.Dirty.AddRect(rect.X0, rect.Y0, rect.X1, rect.Y1, 0);
	}
}

/// <summary>
/// <c>DefaultDialogBox</c> (vtable <c>0007eea8</c>), which <c>Dialog_LoadFromFile</c> builds for a version word of 0
/// (docs/es1/formats/bin-string-tables.md#the-load): <c>DefaultDialogBox_Ctor</c> (<c>00019149</c>) takes the
/// <c>nm_green</c> font, <see cref="Es1DialogBox.TopRect"/> and its own text, and lays it out. Its destructor,
/// <c>DefaultDialogBox_Dtor</c> (<c>00019244</c>), is the base's.
/// </summary>
public sealed class Es1DefaultDialogBox : Es1DialogBox {
	/// <summary>The text at <c>0007eed2</c>.</summary>
	private const string DefaultText = "This is a Default Dialog Box.";

	public Es1DefaultDialogBox(Es1ShellDialog dialog) : base(dialog) {
		Font = dialog.Font();
		Rect = TopRect;
		SetText(DefaultText);
		Layout();
	}
}

/// <summary>
/// <c>DigitalDialogBox</c> (vtable <c>0007ef04</c>), a message's box, which <c>Dialog_LoadFromFile</c> builds for a version
/// word of 2 (docs/es1/formats/bin-string-tables.md#the-load).
/// </summary>
public sealed class Es1DigitalDialogBox : Es1DialogBox {
	/// <summary>
	/// <c>DigitalDialogBox_Ctor</c> (<c>00019279</c>)(box, <i>a</i>, <i>b</i>, dialog) as far as the talking head: the
	/// <c>nm_green</c> font, the rect by the backdrop (<see cref="SetRect"/>), line <paramref name="line"/> of
	/// <paramref name="lines"/> copied as the text, and the text laid out. <c>Text_GetString</c> reads the line with no
	/// bounds test; a line past the file's is an empty text here, an engine guard. The talking head is
	/// <see cref="LoadTalkingHead"/>'s, which the constructor calls next.
	/// </summary>
	public Es1DigitalDialogBox(Es1ShellDialog dialog, IReadOnlyList<string> lines, short line) : base(dialog) {
		Font = dialog.Font();
		SetRect(dialog.BackdropId());
		SetText(line >= 0 && line < lines.Count ? lines[line] : "");
		Layout();
	}

	/// <summary>
	/// <c>DigitalDialogBox_SetRect</c> (<c>000193ee</c>) (docs/es1/shell/message-box.md#where-the-box-stands): the backdrop
	/// <paramref name="backdropId"/> rounded down to a thousand, and looked up in the four groups at <c>0007ef10</c>: 3000,
	/// 4000 and 5000 put the box in the top strip, 7000 in <see cref="Es1DialogBox.Backdrop7000Rect"/> with
	/// <see cref="Es1DialogBox.TopStrip"/> clear, and any other group changes nothing, so the box keeps the rect it was
	/// allocated with, all 0 here.
	/// </summary>
	private void SetRect(short backdropId) {
		switch (backdropId / 1000 * 1000) {
			case 3000:
			case 4000:
			case 5000:
				Rect = TopRect;
				TopStrip = true;
				break;
			case 7000:
				Rect = Backdrop7000Rect;
				TopStrip = false;
				break;
		}
	}

	/// <summary>
	/// The rest of <c>DigitalDialogBox_Ctor</c>: <c>maybe_SncList_PlayEntry</c> (<c>000453be</c>)(<c>Shell_LipSyncPlayer</c>,
	/// <paramref name="a"/>, <paramref name="b"/>), the face and the line's lip-sync script loaded
	/// (docs/es1/shell/message-box.md#loading-a-line).
	/// </summary>
	public IEnumerable<Es1ShellWait> LoadTalkingHead(short a, byte b) => Dialog.Player.PlayEntry(a, b, Dialog.Line());

	/// <summary>
	/// <c>DigitalDialogBox_Dtor</c> (<c>000193a8</c>): the message's line stopped (<see cref="Es1LipSyncPlayer.Stop"/>), then
	/// the base's.
	/// </summary>
	public override IEnumerable<Es1ShellWait> Dtor() {
		foreach (var wait in Dialog.Player.Stop()) {
			yield return wait;
		}

		DtorBase();
	}
}
