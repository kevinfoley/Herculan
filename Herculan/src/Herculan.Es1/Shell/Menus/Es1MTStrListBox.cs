using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The shell's list box, class <c>MTStrListBox</c> (vtable <c>0007f474</c>, <c>0x5a</c> bytes) over <c>DUIVSlider</c>
/// (<c>0007f412</c>) and <c>DUISliderControl</c> (<c>0007f2f8</c>) (docs/es1/shell/menus.md#the-list-box): a framed
/// column of text lines, one of them selected. The two base classes' fields are kept here; their slots are the list's.
/// </summary>
public sealed class Es1MTStrListBox : Es1DUIControl {
	/// <summary>The font slots of <c>nm_green</c> and <c>nm_red1</c>, <c>000aacda</c> and <c>000aacde</c>.</summary>
	private const int LineFont = 5, SelectedFont = 6;

	/// <summary>The <c>STYLES.DAT</c> record the list is framed with: <c>00082654</c> + <c>0xc0</c>, the table's fourth.</summary>
	private const int FrameStyle = 3;

	/// <summary>The selected line's box colors, <c>Raster_FillFrameRectColours</c>(5, 4, box).</summary>
	private const byte SelectionFrameColor = 5, SelectionFillColor = 4;

	private readonly Es1ShellData _data;

	/// <summary>
	/// <c>MTStrListBox_Ctor</c> (<c>0001e11a</c>)(list, <paramref name="max"/>, <paramref name="min"/>,
	/// <paramref name="alignment"/>, <paramref name="selected"/>, <paramref name="style"/>, <paramref name="id"/>,
	/// <paramref name="lines"/>, <paramref name="rect"/>): <c>DUIControl_Ctor</c>(list, 0, id, rect), so <c>Tab</c>
	/// passes over it; the slider's range and selection with tracking 1; the lines, counted into <see cref="Count"/> over
	/// the slider's max; the frame record, the two fonts and the alignment; the line height, the font's cell height less 1,
	/// or the rect's (<c>int16</c>) height less 1 when the height is below that; the lines shown, the height over the line
	/// height, 1 when that is 0, at most the count; the selected line; the first line shown, 0 or so that the selected
	/// line is the last shown; and <see cref="Es1DUIControl.Changed"/> cleared.
	/// </summary>
	public Es1MTStrListBox(Es1ShellData data, short max, short min, int alignment, short selected, short style, short id,
			string?[] lines, Es1Rect rect) : base(false, 0, rect) {
		_data = data;
		Id = id;
		Min = min;
		Count = max;
		TrackY = selected;
		SelectedAtBuild = selected;
		Tracking = 1;
		Lines = lines;
		Style = style;
		Count = CountLines(lines);
		Frame = data.Styles is { } styles && FrameStyle < styles.Styles.Length ? styles.Styles[FrameStyle] : null;
		Fonts = (data.Fonts[LineFont], data.Fonts[SelectedFont]);
		Alignment = alignment;

		short height = (short)(Rect.Y1 - Rect.Y0);
		short lineHeight = (short)((Fonts.Line?.CellHeight ?? 0) - 1);
		if (height < lineHeight) {
			lineHeight = (short)(height - 1);
		}

		LineHeight = lineHeight;
		// A line height of 0 divides by zero in retail; here the quotient is taken as 0, so one line is shown, an engine guard.
		short shown = lineHeight != 0 ? (short)(height / lineHeight) : (short)0;
		if (shown == 0) {
			shown = 1;
		}

		if (Count < shown) {
			shown = Count;
		}

		Shown = shown;
		Selected = selected;
		FirstShown = selected < Shown ? (short)0 : (short)(selected - Shown + 1);
		Changed = false;
	}

	/// <summary><c>+0x24</c>, the slider's minimum.</summary>
	public short Min { get; }

	/// <summary><c>+0x26</c>, the slider's maximum, which the constructor and <see cref="SetLines"/> set to the line count.</summary>
	public short Count { get; private set; }

	/// <summary><c>+0x28</c>, the tracked point's y, relative to the window; the constructor stores the selection there.</summary>
	public short TrackY { get; private set; }

	/// <summary><c>+0x2a</c>, the selection the constructor was given.</summary>
	public short SelectedAtBuild { get; }

	/// <summary><c>+0x2c</c>, a line's height.</summary>
	public short LineHeight { get; }

	/// <summary><c>+0x3e</c>: 0 shows and tracks a selection, 1 shows none and ignores the pointer.</summary>
	public short Style { get; }

	/// <summary><c>+0x40</c>, <c>+0x44</c>: the lines' font, <c>nm_green</c>, and the selected line's in style 0, <c>nm_red1</c>.</summary>
	public (HudFont? Line, HudFont? Selected) Fonts { get; }

	/// <summary><c>+0x48</c>, the alignment the lines are drawn with.</summary>
	public int Alignment { get; }

	/// <summary><c>+0x4c</c>, the zero-ended lines; a null entry ends them.</summary>
	public string?[] Lines { get; private set; }

	/// <summary><c>+0x50</c>, the frame's <c>STYLES.DAT</c> record; none when the table lacks it.</summary>
	public Es1WidgetStyle? Frame { get; }

	/// <summary><c>+0x54</c>, the selected line.</summary>
	public short Selected { get; private set; }

	/// <summary><c>+0x56</c>, the first line shown.</summary>
	public short FirstShown { get; private set; }

	/// <summary><c>+0x58</c>, how many lines are shown.</summary>
	public short Shown { get; private set; }

	/// <summary><c>MTStrListBox_CountLines</c> (<c>0001eac5</c>)(list, <paramref name="lines"/>): the entries before the first null one.</summary>
	public static short CountLines(string?[] lines) {
		short count = 0;
		while (count < lines.Length && lines[count] != null) {
			count++;
		}

		return count;
	}

	/// <summary>The line <paramref name="index"/>, none past the array, where retail reads past it.</summary>
	private string? Line(int index) => index >= 0 && index < Lines.Length ? Lines[index] : null;

	/// <summary>
	/// Slot <c>+4</c>, <c>MTStrListBox_Dtor</c> (<c>0001e41e</c>): the lines freed (for id 1002 first its one line and the
	/// name buffer pointer <c>0007ebc0</c>, which <c>CareerLog_CurrentNameArray</c> makes again on its next call), the
	/// three base vtables stored in turn and the list freed; nothing the collector does not free.
	/// </summary>
	public override void Dtor() {
		Lines = [];
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MTStrListBox_Draw</c> (<c>0001e584</c>): <c>CornerFrame_Draw</c> with <see cref="Frame"/> over
	/// the rect, <see cref="DrawLines"/>, and <see cref="Es1DUIControl.Changed"/> cleared.
	/// </summary>
	public override void Draw(Es1ShellRenderContext context) {
		Es1CornerFrame.Draw(context.Canvas, Frame, Rect, _data.Corners);
		DrawLines(context);
		Changed = false;
	}

	/// <summary>
	/// <c>MTStrListBox_DrawLines</c> (<c>0001e5b7</c>): the context's alignment set to <see cref="Alignment"/> and its
	/// field width to the (<c>int16</c>) width less 2; for each row <i>r</i> from 0 while <i>r</i> is below
	/// <see cref="Shown"/> and line <see cref="FirstShown"/> + <i>r</i> exists, with <i>h</i> the line height: in style 0
	/// the selected line on a box (<i>x0</i> + 1, <i>y0</i> + <i>r</i><i>h</i> + 1)–(<i>x1</i> − 1, <i>y0</i> +
	/// <i>r</i><i>h</i> + <i>h</i>) filled in color 4 and framed in 5, in the selected font; every other line in the line
	/// font; the text from (<i>x0</i> + 3, <i>y0</i> + <i>r</i><i>h</i> + <i>h</i> − 1).
	/// </summary>
	private void DrawLines(Es1ShellRenderContext context) {
		context.Alignment = Alignment;
		context.FieldWidth = (short)(Rect.X1 - Rect.X0) - 2;
		var canvas = context.Canvas;
		short line = FirstShown;
		for (short row = 0; row < Shown && Line(line) is { } text; row++, line++) {
			int top = Rect.Y0 + row * LineHeight;
			if (Style == 0 && line == Selected) {
				// Raster_FillFrameRectColours (0004b8d8): the box filled under the brush {0, fill}, then its edges under {4, frame}.
				canvas.FillRect(Rect.X0 + 1, top + 1, Rect.X1 - 1, top + LineHeight, SelectionFillColor);
				canvas.OutlineRect(Rect.X0 + 1, top + 1, Rect.X1 - 1, top + LineHeight, SelectionFrameColor);
				context.DrawString(Fonts.Selected, text, Rect.X0 + 3, top + LineHeight - 1);
			} else {
				context.DrawString(Fonts.Line, text, Rect.X0 + 3, top + LineHeight - 1);
			}
		}
	}

	/// <summary>
	/// Slot <c>+0xc</c>, called after every state change: <c>MTStrListBox_TrackSelect</c> (<c>0001e96a</c>). Nothing
	/// unless the style is 0; with <see cref="TrackY"/> above the rect's top, <see cref="SelectPrev"/> and the selection
	/// set to the first line shown; below its bottom, <see cref="SelectNext"/> and one more line while that is below the
	/// count less 1; otherwise the first shown row whose band holds the y selected, and <see cref="Es1DUIControl.Changed"/> set.
	/// </summary>
	public override void Invalidate() {
		if (Style != 0) {
			return;
		}

		if (TrackY < Rect.Y0) {
			SelectPrev();
			Selected = FirstShown;
			return;
		}

		if (Rect.Y1 < TrackY) {
			SelectNext();
			if (Selected < Count - 1) {
				Selected++;
			}

			return;
		}

		for (short row = 0; row < Shown; row++) {
			int top = row * LineHeight + Rect.Y0;
			if (top <= TrackY && TrackY < top + LineHeight) {
				Selected = (short)(FirstShown + row);
				Changed = true;
				return;
			}
		}
	}

	/// <summary>Slot <c>+0x10</c>: <c>0001d037</c>, nothing.</summary>
	public override void Highlight() {
	}

	/// <summary>Slot <c>+0x18</c>: <c>0001d04f</c>, nothing.</summary>
	public override void Unhighlight() {
	}

	/// <summary>
	/// Slot <c>+0x1c</c>, <c>MTStrListBox_SelectPrev</c> (<c>0001e8df</c>): above line 0, the selection moved up a line
	/// and the first line shown with it when it falls above; <see cref="Es1DUIControl.Changed"/> set.
	/// </summary>
	public void SelectPrev() {
		if (Selected > 0) {
			Selected--;
			if (Selected < FirstShown) {
				FirstShown--;
			}
		}

		Changed = true;
	}

	/// <summary>
	/// Slot <c>+0x20</c>, <c>MTStrListBox_SelectNext</c> (<c>0001e919</c>): below the count less 1, the selection moved
	/// down a line and the first line shown with it when it reaches the first shown plus the lines shown;
	/// <see cref="Es1DUIControl.Changed"/> set.
	/// </summary>
	public void SelectNext() {
		if (Selected < Count - 1) {
			Selected++;
			if (FirstShown + Shown <= Selected) {
				FirstShown++;
			}
		}

		Changed = true;
	}

	/// <summary>
	/// Slot <c>+0x24</c>, <c>MTStrListBox_SelectLine</c> (<c>0001ea4f</c>)(list, <paramref name="line"/>): a line of 0
	/// or more selected, the last when past the count; the first line shown set to the selection while that is below the
	/// count less the lines shown, else to that difference; <see cref="Es1DUIControl.Changed"/> set.
	/// </summary>
	public void SelectLine(short line) {
		if (line >= 0) {
			Selected = line < Count ? line : (short)(Count - 1);
		}

		FirstShown = Selected < Count - Shown ? Selected : (short)(Count - Shown);
		Changed = true;
	}

	/// <summary>Slot <c>+0x2c</c>, <c>MTStrListBox_GetSelected</c> (<c>0001e3d3</c>): the selected line, the message's value.</summary>
	public override short GetValue() => Selected;

	/// <summary>Slot <c>+0x30</c>, <c>MTStrListBox_SelectedText</c> (<c>0001e3e5</c>): the selected line's text.</summary>
	public string? SelectedText() => Line(Selected);

	/// <summary>Slot <c>+0x38</c>, <c>MTStrListBox_SetTrackPoint</c> (<c>0001e404</c>)(list, point): the point's y kept as <see cref="TrackY"/>.</summary>
	public override void SetTrackPoint(int x, int y) => TrackY = (short)y;

	/// <summary>
	/// Slot <c>+0x44</c>, <c>MTStrListBox_SetLines</c> (<c>0001e50a</c>)(list, <paramref name="lines"/>): the lines taken
	/// and counted, the first line shown 0, and the lines shown the rect's (<c>int16</c>) height over the line height, at
	/// most the count.
	/// </summary>
	public void SetLines(string?[] lines) {
		Lines = lines;
		Count = CountLines(lines);
		FirstShown = 0;
		short shown = LineHeight != 0 ? (short)((short)(Rect.Y1 - Rect.Y0) / LineHeight) : (short)0;
		Shown = Count < shown ? Count : shown;
	}
}
