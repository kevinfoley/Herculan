using HercWorks.Core.Data.File;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Mfd;

/// <summary>
/// The MFD's FLASH COMM, mode 1 (docs/es1/simulation/mfd-flash-comm.md): six squad orders, a row each spread down the
/// screen, the selected one in <c>cpylw</c> and the others in <c>cpgreen</c>, each with its hotkey letter in
/// <c>cpred</c>; rows 4 and 5 switch to their second order once squadmates take the first. Its slots are
/// <c>MFDFlashCommVtable</c> (<c>0009387a</c>)'s.
/// </summary>
public sealed class Es1MfdFlashCommScreen : Es1MfdScreen {
	/// <summary>The rows, <c>+0x10</c>–<c>+0x24</c>.</summary>
	public const int RowCount = 6;

	/// <summary>The palette index the paint fills the rect in, and each row's fill.</summary>
	public const byte Fill = 0x3f;

	/// <summary><c>STRINGS0.STR</c>'s group of the squad orders and their hotkey attribute bytes (docs/es1/formats/str-strings.md#the-groups).</summary>
	public const int OrderGroup = 0;

	/// <summary>A row's state bits: unavailable, drawn in <c>cpoff</c> and skipped by the arrows; showing its second order, the row + 3.</summary>
	public const byte Unavailable = 1, SecondOrder = 2;

	/// <summary>The order a row's <see cref="SecondOrder"/> bit adds to its index.</summary>
	public const int SecondOrderOffset = 3;

	/// <summary>The rows <see cref="ToggleRowVariant"/> acts on.</summary>
	public const int FirstVariantRow = 4, LastVariantRow = 5;

	/// <summary><c>Label_SetRect</c>'s flags 1, which start the text at the anchor, and the rows' margin.</summary>
	private const int LeftAligned = 1, RowMargin = 1;

	private readonly Es1Label[] _rows = new Es1Label[RowCount];
	private readonly byte[] _states = new byte[RowCount];
	private readonly IReadOnlyList<HudFont?> _fonts;
	private readonly StringFile? _strings;

	/// <summary>
	/// <c>MfdFlashCommScreen_Ctor</c> (<c>ES1DBSIM 00035c04</c>)(0, <paramref name="display"/>, <paramref name="rect"/>)
	/// (docs/es1/simulation/mfd-flash-comm.md#the-screen-object): the base and the display; with <i>s</i> = (<i>y1</i> −
	/// <i>y0</i>) / 6, truncated, and <i>h</i> the cell height of <c>cpgreen</c>, row <i>i</i> a label in <c>cpgreen</c>
	/// whose hotkey font is <c>cpred</c> and fill <see cref="Fill"/>, left-aligned with the margin (1, 0) over
	/// (<i>x0</i>, <i>y0</i> + <i>i s</i>)–(<i>x1</i>, <i>y0</i> + <i>i s</i> + <i>h</i>); the state bytes and the force
	/// byte clear, and the previous row −1.
	/// </summary>
	internal Es1MfdFlashCommScreen(Es1Cockpit cockpit, Es1MfdDisplay display, Es1Rect rect, Es1MfdSharedBlock shared,
			IReadOnlyList<HudFont?> fonts, StringFile? strings)
		: base(cockpit, rect, shared) {
		Display = display;
		_fonts = fonts;
		_strings = strings;
		var green = Font(Es1CockpitViews.CpGreen);
		int step = (rect.Y1 - rect.Y0) / RowCount, height = green?.CellHeight ?? 0;
		for (int i = 0; i < RowCount; i++) {
			// Label_CtorWithAltFont, whose hotkey font is the font again until the constructor sets cpred.
			_rows[i] = new Es1Label(green) { HotkeyFont = Font(Es1CockpitViews.CpRed), FillColor = Fill };
			int top = rect.Y0 + i * step;
			_rows[i].SetRect(LeftAligned, new Es1Rect(rect.X0, top, rect.X1, top + height), RowMargin);
		}
	}

	/// <summary><c>+0xc</c>, the display.</summary>
	public Es1MfdDisplay Display { get; }

	/// <summary><c>+0x10</c>–<c>+0x24</c>, the six row labels.</summary>
	public IReadOnlyList<Es1Label> Rows => _rows;

	/// <summary><c>+0x28</c>–<c>+0x2d</c>, the rows' state bytes, <see cref="Unavailable"/> and <see cref="SecondOrder"/>.</summary>
	public IReadOnlyList<byte> States => _states;

	/// <summary><c>+0x2e</c>, the screen's own row, the one <see cref="SelectedVerb"/> resolves.</summary>
	public int ScreenRow { get; private set; }

	/// <summary><c>+0x32</c>, the force byte: the next <see cref="Update"/> rewrites the rows though the selection has not moved.</summary>
	public bool Force { get; private set; }

	/// <summary><c>+0x33</c>, the previous row the next <see cref="Update"/> rewrites, an <c>int16</c>, −1 for none.</summary>
	public short PreviousRow { get; private set; } = -1;

	/// <summary>
	/// Slot <c>+4</c>, <c>MfdFlashCommScreen_Paint</c> (<c>00035d20</c>) (docs/es1/simulation/mfd-flash-comm.md#painting):
	/// the rect filled in <see cref="Fill"/>, the shared block's row copied to <see cref="ScreenRow"/>, and each row
	/// written (<see cref="WriteRow"/>) in <c>cpoff</c> while unavailable, else <c>cpylw</c> for the selected row and
	/// <c>cpgreen</c> for the others.
	/// </summary>
	public override void Paint() {
		var canvas = Cockpit.PushCanvasContext();
		canvas.FillRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, Fill);
		ScreenRow = Shared.FlashCommRow;
		for (int row = 0; row < RowCount; row++) {
			_rows[row].Font = Font((_states[row] & Unavailable) != 0 ? Es1CockpitViews.CpOff
				: row == ScreenRow ? Es1CockpitViews.CpYellow
				: Es1CockpitViews.CpGreen);
			WriteRow(canvas, row);
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MfdFlashCommScreen_Update</c> (<c>00035e14</c>) (docs/es1/simulation/mfd-flash-comm.md#painting):
	/// when the shared block's row differs from <see cref="ScreenRow"/>, <see cref="Force"/> is set or
	/// <see cref="PreviousRow"/> is not −1, the old row (<see cref="PreviousRow"/> when set, else <see cref="ScreenRow"/>)
	/// written in <c>cpgreen</c> whatever its state and the shared block's row, stored in <see cref="ScreenRow"/>, in
	/// <c>cpylw</c>, each then copied from the page drawn to the page on the screen (<see cref="Es1Cockpit.DrawToCockpit"/>
	/// with 1), and <see cref="Force"/> cleared; <see cref="PreviousRow"/> −1 in either case.
	/// </summary>
	public override void Update() {
		if (Shared.FlashCommRow != ScreenRow || Force || PreviousRow != -1) {
			var canvas = Cockpit.PushCanvasContext();
			int old = PreviousRow != -1 ? PreviousRow : ScreenRow;
			_rows[old].Font = Font(Es1CockpitViews.CpGreen);
			WriteRow(canvas, old);
			Cockpit.DrawToCockpit(_rows[old].Rect);
			ScreenRow = Shared.FlashCommRow;
			_rows[ScreenRow].Font = Font(Es1CockpitViews.CpYellow);
			WriteRow(canvas, ScreenRow);
			Cockpit.DrawToCockpit(_rows[ScreenRow].Rect);
			Force = false;
		}

		PreviousRow = -1;
	}

	/// <summary><c>MfdFlashComm_SelectedVerb</c> (<c>00035f4c</c>)(screen): <see cref="ScreenRow"/>'s order, the row + 3 while it shows its second.</summary>
	public int SelectedVerb() => Order(ScreenRow);

	/// <summary>
	/// <c>MfdFlashComm_SetScreenRow</c> (<c>00035f80</c>)(screen, <paramref name="row"/>): <see cref="ScreenRow"/>'s low
	/// word into <see cref="PreviousRow"/> and the row into <see cref="ScreenRow"/>.
	/// </summary>
	public void SetScreenRow(int row) {
		PreviousRow = (short)ScreenRow;
		ScreenRow = row;
	}

	/// <summary>
	/// <c>MfdFlashComm_ToggleRowVariant</c> (<c>00035f9c</c>)(screen): for a <see cref="ScreenRow"/> of 4 or 5 alone, its
	/// <see cref="SecondOrder"/> bit flipped and <see cref="Force"/> set.
	/// </summary>
	public void ToggleRowVariant() {
		if (ScreenRow is FirstVariantRow or LastVariantRow) {
			_states[ScreenRow] ^= SecondOrder;
			Force = true;
		}
	}

	/// <summary>
	/// <c>MfdFlashComm_ResetRow</c> (<c>00035fcc</c>)(screen, <paramref name="row"/>): the state byte cut to its
	/// <see cref="Unavailable"/> bit and the row's hotkey font <c>cpred</c>. Nothing is known to call it
	/// (docs/es1/simulation/mfd-flash-comm.md#open).
	/// </summary>
	public void ResetRow(int row) {
		_states[row] &= Unavailable;
		_rows[row].HotkeyFont = Font(Es1CockpitViews.CpRed);
	}

	/// <summary>
	/// <c>MfdFlashComm_DisableRow</c> (<c>00035ff8</c>)(screen, <paramref name="row"/>): <see cref="Unavailable"/> set and
	/// the row's hotkey font <c>cpoff</c>. Nothing is known to call it (docs/es1/simulation/mfd-flash-comm.md#open).
	/// </summary>
	public void DisableRow(int row) {
		_states[row] |= Unavailable;
		_rows[row].HotkeyFont = Font(Es1CockpitViews.CpOff);
	}

	/// <summary>
	/// <c>Label_SetTextWithHotkey</c>(the row's label, its order's attribute byte, its order's text) on
	/// <paramref name="canvas"/>, the order picked as <see cref="SelectedVerb"/> picks it. An order with no attribute byte,
	/// where retail reads one through its pointer, draws no hotkey here, an engine guard.
	/// </summary>
	private void WriteRow(Es1PanelCanvas canvas, int row) {
		var orders = _strings?.Group(OrderGroup) ?? [];
		int order = Order(row);
		if (order >= orders.Count) {
			return;
		}

		var entry = orders[order];
		_rows[row].SetTextWithHotkey(canvas, entry.Attributes.Length > 0 ? entry.Attributes[0] : int.MaxValue, entry.Text);
	}

	private int Order(int row) => (_states[row] & SecondOrder) != 0 ? row + SecondOrderOffset : row;

	private HudFont? Font(int index) => index < _fonts.Count ? _fonts[index] : null;
}
