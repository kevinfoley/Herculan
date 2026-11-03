using System.Text;
using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The name box, class <c>MTEditBox</c> (vtable <c>0007f590</c>, <c>0x90</c> bytes) over <c>0007f356</c>'s and
/// <c>DUISliderControl</c>'s (<c>0007f2f8</c>) vtables (docs/es1/shell/career.md#the-name-box): a framed line of up to 12
/// typed characters after a blinking caret, the <c>.DLG</c>'s kind-2 record, which holds the input (tracking 2).
/// </summary>
public sealed class Es1MTEditBox : Es1DUIControl {
	/// <summary>The text's bytes at <c>+0x46</c>, which the constructor zeroes and <c>Esc</c> clears.</summary>
	public const int TextLength = 13;

	/// <summary>The cursor <see cref="TypeChar"/> stores below: up to 12 characters.</summary>
	private const short MaxCursor = 12;

	/// <summary>
	/// The bytes from <c>+0x46</c> to the cursor word <c>+0x86</c>, which the box's indexes reach: its slots read and write
	/// up to index <c>+0x8a</c>, which typing after a <c>Left</c> raises past the text. Past the 13 the constructor zeroes,
	/// retail reads whatever <c>Mem_New</c> left; they are zero here, and an index past the 64 reads 0 and writes nothing,
	/// engine guards.
	/// </summary>
	private const int BufferLength = 0x86 - 0x46;

	/// <summary>The caret's inset from the rect's left, top and bottom, and the text's from its left and bottom.</summary>
	private const int Inset = 3;

	/// <summary>The caret's color, <c>Raster_DrawLineColour</c>(<c>0x17</c>, line).</summary>
	private const byte CaretColor = 0x17;

	/// <summary>The font slot of <c>000aace2</c>, the second <c>nm_green</c> handle of the 14 at <c>Shell_Fonts</c>.</summary>
	private const int FontSlot = 7;

	/// <summary>The <c>STYLES.DAT</c> record the box is framed with: <c>00082654</c> + <c>0xc0</c>, the table's fourth.</summary>
	private const int FrameStyle = 3;

	/// <summary>The cursor at which <see cref="PlaceCursor"/>'s third path stops moving the first character shown.</summary>
	private const short PlaceCursorBound = 13;

	private readonly Es1ShellData _data;
	private readonly Es1MenuGlobals _globals;
	private readonly byte[] _text = new byte[BufferLength];

	/// <summary>
	/// <c>MTEditBox_Ctor</c> (<c>0001f442</c>)(box, <paramref name="id"/>, <paramref name="rect"/>):
	/// <c>DUIControl_Ctor</c>(box, 0, id, rect), so <c>Tab</c> passes over it; the slider's fields, which leave the press's
	/// <i>x</i> <c>+0x28</c> 0; unless <c>-X</c> and while <see cref="Es1MenuGlobals.ScanKeysRegistered"/> is clear,
	/// <c>Shell_ScanKeySubscriber</c> registered for the ten codes at <c>0007e7f4</c> and the byte set; tracking 2; the
	/// text zeroed; the frame record, the font and the alignment 0; the caret at <i>x</i> left + 3 from top + 3 to
	/// bottom − 3; the cursor and the first character shown 0, <c>+0x8a</c> 1; and <see cref="Es1DUIControl.Changed"/>
	/// cleared.
	/// </summary>
	public Es1MTEditBox(Es1ShellData data, Es1MenuGlobals globals, short id, Es1Rect rect) : base(false, 0, rect) {
		_data = data;
		_globals = globals;
		Id = id;
		if (!globals.DebugMode && !globals.ScanKeysRegistered) {
			// 00027bbf, the keyboard device's getter, whose answer is dropped.
			globals.Keyboard?.AddSubscriber(globals.ScanKeySubscriber, ScanKeyCodes);
			globals.ScanKeysRegistered = true;
		}

		Tracking = 2;
		Frame = data.Styles is { } styles && FrameStyle < styles.Styles.Length ? styles.Styles[FrameStyle] : null;
		Font = data.Fonts[FontSlot];
		Alignment = 0;
		CaretX = Rect.X0 + Inset;
		Cursor = 0;
		FirstShown = 0;
		End = 1;
		Changed = false;
	}

	/// <summary>The codes <c>Shell_ScanKeySubscriber</c> is registered for, the NUL-terminated bytes at <c>0007e7f4</c>.</summary>
	internal static readonly byte[] ScanKeyCodes =[0x39, 0x4b, 0x4d, 0x48, 0x50, 0x0e, 0x47, 0x4f, 0x01, 0x53];

	/// <summary><c>+0x28</c>, <c>int16</c>: the <i>x</i> of the last press, and the caret's <i>x</i> once placed.</summary>
	public short TrackX { get; private set; }

	/// <summary><c>+0x2e</c> and <c>+0x36</c>, the caret line's <i>x</i>; its <i>y</i> run from top + 3 to bottom − 3.</summary>
	public int CaretX { get; private set; }

	/// <summary><c>+0x3e</c>, the font, <c>nm_green</c>.</summary>
	public HudFont? Font { get; }

	/// <summary><c>+0x42</c>, the alignment the draw sets on the render context.</summary>
	public int Alignment { get; }

	/// <summary><c>+0x86</c>, <c>int16</c>: the index the next character goes to.</summary>
	public short Cursor { get; private set; }

	/// <summary><c>+0x88</c>, <c>int16</c>: the first character shown.</summary>
	public short FirstShown { get; private set; }

	/// <summary><c>+0x8a</c>, <c>int16</c>: the text's length + 1, as the slots keep it.</summary>
	public short End { get; private set; }

	/// <summary><c>+0x8c</c>, the frame record.</summary>
	public Es1WidgetStyle? Frame { get; }

	/// <summary>The byte at <paramref name="index"/> of <c>+0x46</c>; 0 outside the bytes kept, an engine guard.</summary>
	private byte At(int index) => index >= 0 && index < _text.Length ? _text[index] : (byte)0;

	private void Set(int index, byte value) {
		if (index >= 0 && index < _text.Length) {
			_text[index] = value;
		}
	}

	/// <summary><c>HudFont_GlyphWidth</c> (<c>00051f80</c>) in <see cref="Font"/>; a byte the font has no glyph for is 0 wide here, where retail reads before its width table.</summary>
	private int GlyphWidth(byte c) => Font?.Width((char)c) ?? 0;

	/// <summary>
	/// Slot <c>+4</c>, <c>MTEditBox_Dtor</c> (<c>0001f66f</c>): while <see cref="Es1MenuGlobals.ScanKeysRegistered"/> is
	/// set, <c>Shell_ScanKeySubscriber</c> removed and the byte cleared; the base vtables stored back and the box freed.
	/// </summary>
	public override void Dtor() {
		if (_globals.ScanKeysRegistered) {
			_globals.Keyboard?.RemoveSubscriber(_globals.ScanKeySubscriber);
			_globals.ScanKeysRegistered = false;
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MTEditBox_Draw</c> (<c>0001f738</c>): the render context's alignment set to <see cref="Alignment"/>;
	/// the frame drawn over the rect (<c>CornerFrame_Draw</c>); while pushed and <c>Shell_BlinkOn</c> is 1, the caret line
	/// in <c>0x17</c>; each non-zero byte from <see cref="FirstShown"/> to <see cref="End"/>, that one included, drawn with
	/// <c>HudFont_DrawGlyph</c> from <i>x</i> left + 3 at <i>y</i> bottom − 3, <i>x</i> advancing by its width; and
	/// <see cref="Es1DUIControl.Changed"/> cleared.
	/// </summary>
	public override void Draw(Es1ShellRenderContext context) {
		context.Alignment = Alignment;
		var canvas = context.Canvas;
		Es1CornerFrame.Draw(canvas, Frame, Rect, _data.Corners);
		if (State == Pushed && _globals.BlinkOn()) {
			canvas.DrawLine(CaretX, Rect.Y1 - Inset, CaretX, Rect.Y0 + Inset, CaretColor);
		}

		int x = Rect.X0 + Inset;
		for (short i = FirstShown; i <= End; i++) {
			byte c = At(i);
			if (c != 0) {
				canvas.DrawString(Font, ((char)c).ToString(), x, Rect.Y1 - Inset);
				x += GlyphWidth(c);
			}
		}

		Changed = false;
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>MTEditBox_PlaceCursor</c> (<c>0001f85f</c>), by <see cref="TrackX"/>: left of left + 3, while
	/// <see cref="FirstShown"/> is above 0, it and the cursor less 1 and the caret at left + 3, <i>changed</i>; up to
	/// right − 3, for each index from <see cref="FirstShown"/> to <see cref="End"/>, a 0 byte 1 wide, the cursor put on the
	/// one under <see cref="TrackX"/> with <see cref="UpdateCaret"/> and <i>changed</i>; right of it, while the caret's
	/// <i>x</i> lies right of right − 3, <see cref="FirstShown"/> up 1 with <see cref="UpdateCaret"/> while the cursor is
	/// below 13, then <i>changed</i>. With the cursor at 13 or more that loop never ends in retail; here it stops, an engine
	/// guard.
	/// </summary>
	public override void Invalidate() {
		if (TrackX < Rect.X0 + Inset) {
			if (FirstShown > 0) {
				FirstShown--;
				Cursor--;
				SetCaret(Rect.X0 + Inset);
				Changed = true;
			}

			return;
		}

		if (TrackX <= Rect.X1 - Inset) {
			int x = Rect.X0 + Inset;
			for (short i = FirstShown; i <= End; i++) {
				byte c = At(i);
				int width = c == 0 ? 1 : GlyphWidth(c);
				x += width;
				if (x - width <= TrackX && TrackX < x) {
					Cursor = i;
					UpdateCaret();
					Changed = true;
				}
			}

			return;
		}

		while (Rect.X1 - Inset < TrackX && Cursor < PlaceCursorBound) {
			FirstShown++;
			UpdateCaret();
		}

		Changed = true;
	}

	/// <summary>The caret's <i>x</i> <c>+0x2e</c> and <c>+0x36</c> and the word <c>+0x28</c> set to <paramref name="x"/>.</summary>
	private void SetCaret(int x) {
		CaretX = x;
		TrackX = (short)x;
	}

	/// <summary>
	/// <c>MTEditBox_UpdateCaret</c> (<c>0001f9a0</c>): the caret at left + 3 plus the widths of the bytes from
	/// <see cref="FirstShown"/> up to the cursor, as an <c>int16</c>.
	/// </summary>
	private void UpdateCaret() {
		short x = (short)(Rect.X0 + Inset);
		for (short i = FirstShown; i < Cursor; i++) {
			x += (short)GlyphWidth(At(i));
		}

		SetCaret(x);
	}

	/// <summary>Slot <c>+0x30</c>, <c>MTEditBox_GetText</c> (<c>0001f647</c>): the text at <c>+0x46</c>, read up to its first 0 byte.</summary>
	public string GetText() {
		int end = Array.IndexOf(_text, (byte)0);
		return Encoding.Latin1.GetString(_text, 0, end < 0 ? _text.Length : end);
	}

	/// <summary>Slot <c>+0x38</c>, <c>MTEditBox_SetTrackPoint</c> (<c>0001f656</c>)(box, point): the point's <i>x</i> kept as <see cref="TrackX"/>.</summary>
	public override void SetTrackPoint(int x, int y) => TrackX = (short)x;

	/// <summary>
	/// Slot <c>+0x3c</c>, <c>MTEditBox_TypeChar</c> (<c>0001fa19</c>)(box, <paramref name="value"/>): a character from
	/// <c>0x20</c> to <c>0x7f</c>, with the cursor below 12, stored at the cursor over what was there, the cursor and
	/// <see cref="End"/> up 1 and <see cref="UpdateCaret"/>; any character sets <i>changed</i>.
	/// </summary>
	public override void SetTextFromTable(short value) => TypeChar((ushort)value);

	/// <inheritdoc cref="SetTextFromTable"/>
	private void TypeChar(ushort value) {
		if (value is > 0x1f and < 0x80 && Cursor < MaxCursor) {
			Set(Cursor, (byte)value);
			Cursor++;
			End++;
			UpdateCaret();
		}

		Changed = true;
	}

	/// <summary>
	/// Slot <c>+0x40</c>, <c>MTEditBox_HandleKey</c> (<c>0001fa7e</c>)(box, <paramref name="code"/>)
	/// (docs/es1/shell/career.md#the-name-box): nothing while inactive; otherwise by the ten codes of the table at
	/// <c>0007f5e0</c> (cases at <c>0007f608</c>): <c>Space</c> types a space; <c>Up</c> and <c>Down</c> nothing;
	/// <c>Left</c>, <c>Right</c>, <c>Backspace</c>, <c>Esc</c>, <c>Home</c>, <c>End</c> and <c>Del</c> edit the text and
	/// the cursor; every code then sets <i>changed</i>.
	/// </summary>
	public override void SendKey(short code) {
		if (State == Inactive) {
			return;
		}

		switch (code) {
			case 0x39:
				TypeChar(0x20);
				break;
			case 0x4b:
				if (Cursor != 0) {
					Cursor--;
				}

				UpdateCaret();
				break;
			case 0x4d:
				if (Cursor < End - 1) {
					Cursor++;
				}

				UpdateCaret();
				break;
			case 0x0e:
				Backspace();
				UpdateCaret();
				break;
			case 0x01:
				Array.Clear(_text, 0, TextLength);
				FirstShown = 0;
				Cursor = 0;
				End = 1;
				SetCaret(Rect.X0 + Inset);
				break;
			case 0x47:
				FirstShown = 0;
				Cursor = 0;
				SetCaret(Rect.X0 + Inset);
				break;
			case 0x4f:
				Cursor = (short)(End - 1);
				UpdateCaret();
				break;
			case 0x53:
				Delete();
				break;
		}

		Changed = true;
	}

	/// <summary>
	/// <c>Backspace</c>: with the cursor at 0 the cursor and <see cref="FirstShown"/> 0 and <see cref="End"/> 1, the text
	/// left as it is; with the cursor at <see cref="End"/> − 1, both less 1 and the byte at the cursor 0; elsewhere the
	/// cursor less 1 and the byte there a space.
	/// </summary>
	private void Backspace() {
		if (Cursor == 0) {
			Cursor = 0;
			FirstShown = 0;
			End = 1;
		} else if (End - 1 == Cursor) {
			End--;
			Cursor--;
			Set(Cursor, 0);
		} else {
			Cursor--;
			Set(Cursor, 0x20);
		}
	}

	/// <summary>
	/// <c>Del</c>: with the cursor before <see cref="End"/> − 1, the bytes after it moved one to the left, the one at
	/// <see cref="End"/> − 1 cleared to 0, and <see cref="End"/> less 1.
	/// </summary>
	private void Delete() {
		if (Cursor >= End - 1) {
			return;
		}

		short i = Cursor;
		for (; i < End - 1; i++) {
			Set(i, At(i + 1));
		}

		Set(i, 0);
		End--;
	}
}
