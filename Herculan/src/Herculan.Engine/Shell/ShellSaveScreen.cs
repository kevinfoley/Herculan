using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The five buttons on the save screen, in the order its builder constructs them. The first two sit
/// inside the slot list and the last three in a column beside it.
/// </summary>
public enum ShellSaveButton {
	/// <summary>Abandons a slot rename. Live only while a row is being typed into.</summary>
	Cancel = 0,

	/// <summary>Commits a slot rename, writing the save under the typed label. Live only while a row is being typed into.</summary>
	Accept = 1,

	/// <summary>
	/// Starts a rename of the selected slot; ACCEPT is what writes the save. See
	/// docs/retail/shell/main-menu.md, "Saving is a rename".
	/// </summary>
	Save = 2,

	/// <summary>Loads the selected slot. Live only for a slot that holds a save.</summary>
	Restore = 3,

	/// <summary>Leaves the screen — back to the tab strip, or to the main menu.</summary>
	Exit = 4,
}

/// <summary>
/// Where EXIT goes — <c>SaveScreen_ExitTarget</c> (<c>0048d344</c>), written by whichever handler brought the screen up just
/// before it enters it. The values are the original's.
/// </summary>
public enum ShellSaveExitTarget {
	/// <summary>Back to the main menu. Written by <c>00431498</c>, the handler of a button <c>0043094c</c> builds.</summary>
	MainMenu = 0,

	/// <summary>Back to the bare frame and the tab strip. Written by tab 1's handler.</summary>
	TabStrip = 8,
}

/// <summary>
/// Tab 1, <c>SAVED GAMES</c> — the slot list, the five buttons and the summary of whichever slot is
/// selected. Built by <c>SaveScreen_BuildScreen</c> (<c>004385b0</c>), entered by <c>SaveScreen_Enter</c> (<c>00439b0c</c>), its selection moved by
/// <c>SaveScreen_SelectSlot</c> (<c>0043795f</c>) and its summary panel refilled by <c>SaveScreen_RefreshDetail</c> (<c>0043712c</c>).
///
/// <para><b>Every rect here is a literal in the executable</b>, written as four immediates onto the
/// builder's own stack, and they are kept parent-relative exactly as the builder writes them — the
/// panel is placed in the canvas, the list and the button column in the panel, and the two inner
/// buttons in the list. Composing them the same way the widget tree does is what makes each number
/// checkable against the decompilation instead of a pre-added absolute nobody can trace back. See
/// docs/retail/shell/main-menu.md.</para>
///
/// <para><b>The screen names itself.</b> Its title, all five button captions, every field label and
/// the words for skill, rank and sector are <c>estext.bin</c> entries fetched by index, so nothing
/// here hardcodes an English string. The three that are formatted rather than looked up — the kill
/// counts, the salvage figure and the mission number — are formatted the way the original's
/// <c>sprintf</c> calls are.</para>
///
/// <para><b>Ten rows, twelve slots.</b> Every loop on this screen bounds at ten: slots 10 and 11 are
/// the campaign and training autosaves, which are one slot from the caller's side and are reached by
/// the resume path rather than by a row.</para>
///
/// <para><b>Each row is an edit field</b>, and keeps what the original's does: its string, its caret
/// enable <c>+0xbf</c> and its blink phase <c>+0xb3</c>. Its focus <c>+0xa7</c> is the pointer's
/// (<see cref="ShellPointer.Focused"/>). SAVE starts a rename and ACCEPT writes the save; the host runs
/// <c>Game_SaveSlot</c>. See docs/retail/shell/main-menu.md#saving-is-a-rename.</para>
/// </summary>
public sealed class ShellSaveScreen {
	/// <summary>How many slots the list shows.</summary>
	public const int RowCount = 10;

	/// <summary>
	/// The content panel, in the canvas: <c>{0x8e, 0x7f, 0x1f2, 0x1d4}</c>. Horizontally centred, and
	/// low enough to leave the backdrop showing between it and the tab strip.
	/// </summary>
	public static readonly ShellRect PanelRect = new(0x8e, 0x7f, 0x1f2, 0x1d4);

	private static readonly ShellRect ListRect = new(9, 0x1c, 0x15b, 0xc6);
	private static readonly ShellRect SummaryRect = new(0x74, 0xcc, 0x15b, 0x150);
	private static readonly ShellRect CancelRect = new(0x43, 0x93, 0xa5, 0xa2);
	private static readonly ShellRect AcceptRect = new(0xb0, 0x93, 0x112, 0xa2);
	private static readonly ShellRect SaveRect = new(9, 0xe5, 0x6b, 0xf4);
	private static readonly ShellRect RestoreRect = new(9, 0xfb, 0x6b, 0x10a);
	private static readonly ShellRect ExitRect = new(9, 0x111, 0x6b, 0x120);

	/// <summary>
	/// A sixth panel the builder constructs over the summary panel's top two thirds,
	/// <c>{0x74, 0xcc, 0x15b, 0x136}</c>, which <c>SaveRegistration_BuildPanel</c> (<c>0043b260</c>) fills with a second registration panel
	/// (docs/retail/shell/main-menu.md#the-second-registration-panel). It is not painted here: the entry routine
	/// shows the summary panel and hides this one, and what shows it is open.
	/// </summary>
	public static readonly ShellRect StubPanelRect = new(0x74, 0xcc, 0x15b, 0x136);

	private readonly List<ShellSaveSlot> _slots = new();
	private int _selected;

	/// <summary>Each row's string, its <c>+0x45</c>.</summary>
	private readonly string[] _rowText = new string[RowCount];

	/// <summary>
	/// Each row's <c>+0xbf</c>, which lets it take a character and a command. The builder clears it and
	/// only a rename sets it, on that slot's row; neither ACCEPT, CANCEL nor SaveScreen_Enter clears it again.
	/// </summary>
	private readonly bool[] _caretEnabled = new bool[RowCount];

	/// <summary>Each row's <c>+0xb3</c>, the caret's blink phase.</summary>
	private readonly bool[] _caretOn = new bool[RowCount];

	public ShellSaveScreen(IReadOnlyList<ShellSaveSlot>? slots = null, bool canSave = true) {
		if (slots != null) {
			_slots.AddRange(slots);
		}

		CanSave = canSave;
		Enter();
	}

	/// <summary>
	/// Whether a rename is live — <c>maybe_SaveScreen_EditMode</c> (<c>00474f40</c>) at 2. SAVE's handler holds it at 1 only while it
	/// runs, and nothing reads it there, so the two states are all there is to keep.
	/// </summary>
	public bool Renaming { get; private set; }

	/// <summary>A row's string: the slot's label, or what a rename has typed over it.</summary>
	public string RowText(int row) => _rowText[row];

	/// <summary>Whether a row takes characters and commands, its <c>+0xbf</c>.</summary>
	public bool CaretEnabled(int row) => row >= 0 && row < RowCount && _caretEnabled[row];

	/// <summary>Whether a row's caret blink phase, its <c>+0xb3</c>, is on.</summary>
	public bool CaretOn(int row) => row >= 0 && row < RowCount && _caretOn[row];

	/// <summary>
	/// <c>SaveScreen_Enter</c> (<c>00439b0c</c>)'s rows: every row's string back to its slot's label. The
	/// gates it writes are <see cref="IsEnabled"/>'s.
	/// </summary>
	public void Enter() {
		for (int row = 0; row < RowCount; row++) {
			_rowText[row] = SlotAt(row)?.Label ?? string.Empty;
		}
	}

	/// <summary>A slot's directory entry and summary, as a save has just rewritten them.</summary>
	public void SetSlot(int slot, ShellSaveSlot entry) {
		if (slot >= 0 && slot < _slots.Count) {
			_slots[slot] = entry;
		}
	}

	/// <summary>
	/// SAVE (<c>SaveScreen_OnSave</c>, <c>00437bd3</c>) and the rename it starts
	/// (<c>SaveScreen_BeginRename</c>, <c>004377d2</c>): the selected row's caret flags set and its string
	/// rewritten as <c>"%2d. "</c>, the slot number with the name gone. Taking the pointer onto the row is
	/// the host's, through <see cref="ShellPointer.Grab"/>.
	/// </summary>
	public void BeginRename() {
		_caretEnabled[_selected] = true;
		_caretOn[_selected] = true;
		_rowText[_selected] = $"{_selected + 1,2}. ";
		Renaming = true;
	}

	/// <summary>
	/// ACCEPT (<c>SaveScreen_OnAccept</c>, <c>00437ffa</c>) once the host has saved under
	/// <see cref="RowText"/> and restaged the slot's summary: the rename is over and the selection stays
	/// on the slot just written.
	/// </summary>
	public void EndRename() => Renaming = false;

	/// <summary>
	/// CANCEL (<c>SaveScreen_OnCancel</c>, <c>00437e1a</c>): the row's label back, the rename over, and the
	/// selection moved to slot 10, which deselects the row.
	/// </summary>
	public void CancelRename() {
		if (_selected < RowCount) {
			_rowText[_selected] = SlotAt(_selected)?.Label ?? string.Empty;
		}

		Renaming = false;
		SelectSlot(RowCount);
	}

	/// <summary>
	/// A keystroke reaching a row, as <c>ESDialog_HandleEvent</c> (<c>0040beaf</c>) takes it: a
	/// character is added while the row's <c>+0xbf</c> is set (<c>ESDialog_TypeChar</c> (<c>0040bdd2</c>)), and Backspace or the
	/// left arrow takes the last one off while <c>+0xbf</c> and the focus are both set
	/// (<c>ESDialog_Erase</c> (<c>0040be56</c>)). Enter's release of the pointer is the host's. Returns whether the string
	/// changed. The handler then runs the row's click handler whatever the key was, which the host does.
	/// </summary>
	public bool Key(int row, ShellKey key, bool focused, HudFont? font) {
		if (!CaretEnabled(row)) {
			return false;
		}

		if (key.Character is { } c) {
			return Type(row, c, font);
		}

		return focused && key.Command is ShellKey.Backspace or ShellKey.Left && Erase(row);
	}

	/// <summary>
	/// <c>ESDialog_TypeChar</c> (<c>0040bdd2</c>): a character the row's set permits goes on the end, while the string stays
	/// under 89 characters and the glyph, the string and six pixels more fit inside the row.
	/// </summary>
	private bool Type(int row, char c, HudFont? font) {
		string text = _rowText[row];
		if (!PermittedCharacters.Contains(c) || text.Length + 1 >= MaxLength) {
			return false;
		}

		var rect = RowRect(row);
		if ((font?.Width(c) ?? 0) + (font?.Measure(text) ?? 0) + CaretWidth >= rect.X1 - rect.X0) {
			return false;
		}

		_rowText[row] = text + c;
		return true;
	}

	/// <summary><c>ESDialog_Erase</c> (<c>0040be56</c>): the last character off, never into the first <see cref="KeptPrefix"/>.</summary>
	private bool Erase(int row) {
		string text = _rowText[row];
		if (text.Length <= KeptPrefix) {
			return false;
		}

		_rowText[row] = text[..^1];
		return true;
	}

	/// <summary>
	/// One tick of a focused row's blink alarm, every 500 ms (event <c>0x200</c>): the phase flips while
	/// <c>+0xbf</c> is set and is put out otherwise.
	/// </summary>
	public void CaretTick(int row) {
		if (row >= 0 && row < RowCount) {
			_caretOn[row] = _caretEnabled[row] && !_caretOn[row];
		}
	}

	/// <summary>The blink alarm's period, <c>WinTimer_InstallAlarm</c>'s 500 and 500.</summary>
	public const int CaretBlinkMilliseconds = 500;

	/// <summary>
	/// The rows' permitted-character set, <c>004757cd</c>, which the builder writes over the class's
	/// upper-case alphabet. Every letter reaches the row upper-cased, so the lower-case run never matches.
	/// </summary>
	private const string PermittedCharacters = "^0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ ";

	/// <summary>The string's limit, <c>0x5a</c>: a character goes on only while the new length stays below it.</summary>
	private const int MaxLength = 0x5a;

	/// <summary>The caret block's width, which the fit test leaves room for.</summary>
	private const int CaretWidth = 6;

	/// <summary>The builder's <c>+0xb7 = 4</c>, which erasing never goes below: the <c>"%2d. "</c> prefix stays.</summary>
	private const int KeptPrefix = 4;

	/// <summary>
	/// Which slot the list has selected. The original parks it at 10 — the autosave, past every row —
	/// on leaving the screen, so an out-of-range value is a normal state and not an error: it means no
	/// row is highlighted and both SAVE and RESTORE are dead.
	/// </summary>
	public int SelectedSlot => _selected;

	/// <summary>
	/// Whether there is a game in progress to write out — <c>maybe_HasGameInProgress</c> (<c>0048260a</c>), which SAVE is gated on
	/// alongside the selection being a real row.
	/// </summary>
	public bool CanSave { get; set; }

	/// <summary>The slots, as <c>sav\GAMEFILE.STR</c> lists them. Empty when there is no directory file.</summary>
	public IReadOnlyList<ShellSaveSlot> Slots => _slots;

	/// <summary>Where EXIT returns to. Set by whoever brings the screen up.</summary>
	public ShellSaveExitTarget ExitTarget { get; set; } = ShellSaveExitTarget.TabStrip;

	/// <summary>
	/// The teardown, <c>SaveScreen_Teardown</c> (<c>00439d66</c>), which EXIT and RESTORE both run first: it parks the selection on
	/// slot 10, past every row, so the screen comes back up with nothing selected and SAVE and RESTORE
	/// both dead, and then hides the screen's widgets. The hiding is the host's — this screen is simply
	/// no longer painted once the tab is not up.
	/// </summary>
	public void Leave() => SelectSlot(RowCount);

	/// <summary>
	/// The slot the row at a canvas point belongs to, or null when the point is on no row. Rows overlap by
	/// their border line, which goes to the lower row because it was built later
	/// (docs/retail/shell/widgets.md#which-widget-a-click-reaches).
	/// </summary>
	public int? RowAt(float canvasX, float canvasY) {
		for (int slot = RowCount - 1; slot >= 0; slot--) {
			if (RowRect(slot).Contains(canvasX, canvasY)) {
				return slot;
			}
		}

		return null;
	}

	/// <summary>The button at a canvas point, or null. A disabled button does not answer.</summary>
	public ShellSaveButton? ButtonAt(float canvasX, float canvasY) {
		foreach (var button in Enum.GetValues<ShellSaveButton>()) {
			if (IsEnabled(button) && ButtonRect(button).Contains(canvasX, canvasY)) {
				return button;
			}
		}

		return null;
	}

	/// <summary>
	/// What the pointer hits: a row, which is an edit field and acts on the left press, or a live
	/// button. A disabled button swallows a click, which is the same as hitting nothing here.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		if (RowAt(canvasX, canvasY) is { } slot) {
			return new ShellHit(new ShellWidget(ShellWidgetKind.SaveRow, slot), ShellHandler.EditField);
		}

		return ButtonAt(canvasX, canvasY) is { } button
			? ShellHit.Button(new ShellWidget(ShellWidgetKind.SaveButton, (int)button), ButtonRect(button),
				canvasX, canvasY)
			: null;
	}

	/// <summary>
	/// Moves the selection, as <c>SaveScreen_SelectSlot</c> (<c>0043795f</c>) does, and returns whether it
	/// moved. A click on the row already selected is a no-op, the same early return the tab handlers
	/// make; so is every move while a rename is live, which keeps the selection on the row being typed
	/// into. A row past what the directory lists still takes the selection — the original tests only the
	/// bound of ten.
	/// </summary>
	public bool SelectSlot(int slot) {
		if (slot == _selected || Renaming) {
			return false;
		}

		_selected = slot;
		return true;
	}

	/// <summary>
	/// Whether a button answers a click, which is the same test that greys its caption.
	///
	/// <para>While a rename is live, CANCEL and ACCEPT are the only live buttons. Otherwise SAVE needs a
	/// real row selected and a game to save, RESTORE needs a real row that holds one, and EXIT is live.
	/// The original writes each gate at the handler that changes it rather than testing it here, and
	/// ACCEPT lights SAVE and RESTORE without their tests — which they pass then anyway, the slot having
	/// just been saved.</para>
	/// </summary>
	public bool IsEnabled(ShellSaveButton button) => button switch {
		ShellSaveButton.Cancel or ShellSaveButton.Accept => Renaming,
		_ when Renaming => false,
		ShellSaveButton.Save => _selected < RowCount && CanSave,
		ShellSaveButton.Restore => _selected < RowCount && SlotAt(_selected)?.InUse == true,
		_ => true,
	};

	/// <summary>One slot row's rect, in the canvas.</summary>
	public ShellRect RowRect(int slot) {
		var list = Inside(PanelRect, ListRect);

		// The rows are 13 tall on a 12-pixel pitch, so each overlaps its neighbour's border row, and the
		// first and last are inset two pixels further from the left edge than the eight between them.
		int y0 = slot * RowPitch + FirstRowY;
		int left = slot == 0 || slot == RowCount - 1 ? EndRowInset : RowInset;
		return new ShellRect(list.X0 + left, list.Y0 + y0,
			list.X0 + list.Width - 2, list.Y0 + y0 + RowHeight - 1);
	}

	private const int RowPitch = 12;
	private const int RowHeight = 13;
	private const int FirstRowY = 0x13;
	private const int RowInset = 10;
	private const int EndRowInset = 12;

	/// <summary>One button's rect, in the canvas. Two are placed in the list panel, three in the content panel.</summary>
	public ShellRect ButtonRect(ShellSaveButton button) => button switch {
		ShellSaveButton.Cancel => Inside(Inside(PanelRect, ListRect), CancelRect),
		ShellSaveButton.Accept => Inside(Inside(PanelRect, ListRect), AcceptRect),
		ShellSaveButton.Save => Inside(PanelRect, SaveRect),
		ShellSaveButton.Restore => Inside(PanelRect, RestoreRect),
		_ => Inside(PanelRect, ExitRect),
	};

	/// <summary>
	/// Draws the whole screen into <paramref name="surface"/>. The caller clears it first; what this
	/// leaves untouched is what the shell's backdrop shows through, which the content panel's dithered
	/// body relies on. <paramref name="focusedRow"/> is the row that has the pointer's focus, whose caret
	/// shows while its blink phase is on, and <paramref name="lit"/> the widget a press has lit.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, int? focusedRow = null,
			ShellWidget? lit = null) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, PanelFace, PanelBodyDither,
			TitleHeight, headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: false);
		ShellChrome.PaintText(surface, TitleRect(), font, text?.Text(TitleText),
			ShellTextAlign.Center, ShellChrome.FontInkColor);

		var list = Inside(PanelRect, ListRect);
		ShellChrome.PaintFramedPanel(surface, list, FrameBorder, FrameFace, fill: true);
		ShellChrome.PaintFramedPanel(surface, Inside(PanelRect, SummaryRect), FrameBorder, FrameFace,
			fill: true);

		for (int slot = 0; slot < RowCount; slot++) {
			ShellChrome.PaintEditField(surface, RowRect(slot), font, _rowText[slot],
				slot == _selected ? SelectedRowColor : RowColor, caret: slot == focusedRow && _caretOn[slot]);
		}

		foreach (var button in Enum.GetValues<ShellSaveButton>()) {
			PaintButton(surface, font, text, button, lit);
		}

		PaintSummary(surface, font, text);
	}

	/// <summary>
	/// One button: a filled box with a doubled chamfered border and its caption centred on it. Both the border and
	/// the caption grey together on the enable test, which is the trio the original writes at every
	/// gated button — the border colour, the caption colour and the enable flag itself.
	/// </summary>
	private void PaintButton(ShellSurface surface, HudFont? font, ShellText? text,
			ShellSaveButton button, ShellWidget? lit) {
		bool enabled = IsEnabled(button);
		ShellChrome.PaintButton(surface, ButtonRect(button), enabled ? ButtonBorder : DisabledColor, font,
			text?.Text(CaptionText(button)), enabled ? ShellChrome.FontInkColor : DisabledColor,
			pressed: enabled && lit == new ShellWidget(ShellWidgetKind.SaveButton, (int)button));
	}

	/// <summary>
	/// The summary panel's labels and values — <c>SaveScreen_RefreshDetail</c> (<c>0043712c</c>). Every value field clears its own
	/// rect before drawing, so a refresh overwrites the last slot's figures rather than layering over
	/// them; the labels do not, and draw straight onto the panel.
	///
	/// <para>A slot with no save reads its values from string-table entry 0, which is empty — so the
	/// labels stay and the figures go blank rather than showing zeroes.</para>
	/// </summary>
	private void PaintSummary(ShellSurface surface, HudFont? font, ShellText? text) {
		var panel = Inside(PanelRect, SummaryRect);
		var summary = _selected < RowCount ? SlotAt(_selected)?.Summary : null;

		void Label(ShellRect rect, int index, ShellTextAlign align = ShellTextAlign.Right) =>
			ShellChrome.PaintText(surface, Inside(panel, rect), font, text?.Text(index), align,
				LabelColor);

		void Value(ShellRect rect, string? value, ShellTextAlign align) =>
			ShellChrome.PaintText(surface, Inside(panel, rect), font, value, align,
				ShellChrome.FontInkColor, ShellChrome.InteriorColor);

		Label(new ShellRect(0x0e, 0x06, 0x36, 0x12), NameLabel);
		Value(new ShellRect(0x3b, 0x06, 0xd8, 0x12), summary?.PilotName, ShellTextAlign.Left);

		Label(new ShellRect(0x0e, 0x12, 0x36, 0x1e), SkillLabel);
		Value(new ShellRect(0x3b, 0x12, 0x71, 0x1e),
			summary == null ? null : text?.Text(FirstSkillWord + summary.Skill), ShellTextAlign.Left);

		Label(new ShellRect(0x76, 0x12, 0x9a, 0x1e), RankLabel);
		Value(new ShellRect(0x9f, 0x12, 0xe5, 0x1e),
			summary == null ? null : text?.Text(FirstRankWord + summary.Rank), ShellTextAlign.Left);

		// The two column headers over the kill grid, both centred over their own column of numbers.
		Label(new ShellRect(0x5a, 0x24, 0x8e, 0x30), CurrentColumnLabel, ShellTextAlign.Center);
		Label(new ShellRect(0x91, 0x24, 0xc5, 0x30), TotalColumnLabel, ShellTextAlign.Center);

		PaintKillRow(0x30, HercKillsLabel, summary?.HercKills, summary?.TotalHercKills);
		PaintKillRow(0x3c, FlyerKillsLabel, summary?.FlyerKills, summary?.TotalFlyerKills);
		PaintKillRow(0x48, BaseKillsLabel, summary?.BaseKills, summary?.TotalBaseKills);

		// Salvage is stored in kilograms and quoted in tons, which is the shell's split throughout —
		// integer division, so a part-ton is dropped rather than rounded. See docs/retail/shell/armory.md.
		Label(new ShellRect(0x0e, 0x5a, 0x54, 0x66), SalvageLabel);
		Value(new ShellRect(0x5a, 0x5a, 0xc5, 0x66),
			summary == null ? null
				: $"{summary.SalvageKilograms / KilogramsPerTon} {text?.Text(TonsWord)}",
			ShellTextAlign.Left);

		Label(new ShellRect(0x0e, 0x66, 0x54, 0x72), SectorLabel);
		Value(new ShellRect(0x5a, 0x66, 0xc5, 0x72),
			summary == null ? null : text?.Text(FirstSectorWord + summary.Sector),
			ShellTextAlign.Left);

		// The mission counter is stored from zero and printed from one.
		Label(new ShellRect(0x0e, 0x72, 0x54, 0x7e), MissionLabel);
		Value(new ShellRect(0x5a, 0x72, 0xc5, 0x7e),
			summary == null ? null : (summary.Mission + 1).ToString(), ShellTextAlign.Left);

		void PaintKillRow(int y, int label, int? current, int? total) {
			Label(new ShellRect(0x0e, y, 0x54, y + 0x0c), label);
			Value(new ShellRect(0x5a, y, 0x8e, y + 0x0c), current?.ToString(), ShellTextAlign.Center);
			Value(new ShellRect(0x91, y, 0xc5, y + 0x0c), total?.ToString(), ShellTextAlign.Center);
		}
	}

	/// <summary>
	/// The title's own rect: the panel's full width by its header height, which is what
	/// <c>ESTitle_Ctor</c> builds for the caption it is handed rather than anything the screen
	/// chooses.
	/// </summary>
	private static ShellRect TitleRect() =>
		new(PanelRect.X0, PanelRect.Y0, PanelRect.X0 + PanelRect.Width - 1,
			PanelRect.Y0 + TitleHeight);

	private ShellSaveSlot? SlotAt(int slot) => slot >= 0 && slot < _slots.Count ? _slots[slot] : null;

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0,
			parent.X0 + child.X1, parent.Y0 + child.Y1);

	private static int CaptionText(ShellSaveButton button) => button switch {
		ShellSaveButton.Cancel => 0x33,
		ShellSaveButton.Accept => 0x34,
		ShellSaveButton.Save => 0x1d,
		ShellSaveButton.Restore => 0x1e,
		_ => 0x1f,
	};

	/// <summary>The content panel's colour fields, written by the builder over the class defaults.</summary>
	private const byte PanelBorder = 0x27;
	private const byte PanelFace = 0x25;
	private const byte PanelBodyDither = 0x10;
	private const int TitleHeight = 0x13;
	private const int TitlePlateFirst = 0x70;
	private const int TitlePlateLast = 0xf3;

	/// <summary>The three framed sub-panels', which the builder flattens to the interior colour.</summary>
	private const byte FrameBorder = 0x15;
	private const byte FrameFace = ShellChrome.InteriorColor;

	private const byte ButtonBorder = 0x22;

	/// <summary>
	/// The colour a gated control greys to — the caption and the border alike, and the same value at
	/// every gated button in the shell.
	/// </summary>
	private const byte DisabledColor = 0x26;

	private const byte RowColor = 0x27;
	private const byte SelectedRowColor = 0x29;
	private const byte LabelColor = 0x1a;

	/// <summary><c>estext.bin</c> indices the screen prints, in the order they appear on it.</summary>
	private const int TitleText = 0x1b;
	private const int NameLabel = 0x24;
	private const int SkillLabel = 0x25;
	private const int RankLabel = 0x26;
	private const int HercKillsLabel = 0x27;
	private const int FlyerKillsLabel = 0x28;
	private const int BaseKillsLabel = 0x29;
	private const int CurrentColumnLabel = 0x2a;
	private const int TotalColumnLabel = 0x2b;
	private const int SalvageLabel = 0x2c;
	private const int MissionLabel = 0x2d;
	private const int SectorLabel = 0x2e;
	private const int TonsWord = 0x2f;

	/// <summary>
	/// Three runs of consecutive words the screen indexes into, each by a field's own value.
	///
	/// <para>The sector run is reached as <c>sector + 0x76</c> and <c>0x76</c> is <c>Razor</c>, a
	/// chassis name — the run of five sector names starts at <c>0x77</c>. That is not an off-by-one:
	/// stage 0 holds the practice missions and the campaign's stages are 1-5, so stage 1 lands on the first name.</para>
	/// </summary>
	private const int FirstSkillWord = 0x35;
	private const int FirstRankWord = 0x39;
	private const int FirstSectorWord = 0x76;

	/// <summary>The shell's kilograms-to-tons divisor, in the salvage figure's own <c>sprintf</c>.</summary>
	private const int KilogramsPerTon = 1000;
}
