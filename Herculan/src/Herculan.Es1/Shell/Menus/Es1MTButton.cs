using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The shell's text button, class <c>MTButton</c> (vtable <c>0007f18c</c>, <c>0x95</c> bytes)
/// (docs/es1/shell/menu-controls.md#buttons): a bevelled frame of the state's <c>STYLES.DAT</c> record, and its text centered
/// in the state's font.
/// </summary>
public class Es1MTButton : Es1DUIButtonControl {
	/// <summary>The color <c>MTButton_Draw</c> frames a highlighted button in.</summary>
	public const byte HighlightColor = 10;

	/// <summary>The font slots of the <c>active</c>, <c>pushed</c> and <c>inactive</c> fonts, <c>000aacce</c>, <c>000aacd2</c>, <c>000aacd6</c>.</summary>
	private const int FirstStateFont = 2;

	private readonly Es1ShellData _data;

	/// <summary>
	/// <c>MTButton_Ctor</c> (<c>0001cde8</c>)(button, <paramref name="id"/>, <paramref name="key"/>,
	/// <paramref name="text"/>, <paramref name="rect"/>): <c>DUIControl_CtorWithKey</c>(button, 1, key, rect), the id,
	/// the <c>STYLES.DAT</c> records 0, 1 and 2 and the <c>active</c>, <c>pushed</c> and <c>inactive</c> fonts, one per
	/// state; the text and its three points (<see cref="PlaceText"/>), or none; <see cref="Highlighted"/> and
	/// <see cref="Es1DUIControl.Changed"/> cleared.
	/// </summary>
	public Es1MTButton(Es1ShellData data, short id, byte key, string? text, Es1Rect rect) : base(key, rect) {
		_data = data;
		Id = id;
		State = Up;
		for (int i = 0; i < 3; i++) {
			Styles[i] = data.Styles is { } styles && i < styles.Styles.Length ? styles.Styles[i] : null;
			Fonts[i] = data.Fonts[FirstStateFont + i];
		}

		if (text != null) {
			Text = text;
			PlaceText();
		}

		Highlighted = false;
		Changed = false;
	}

	/// <summary><c>+0x24</c>, <c>+0x28</c>, <c>+0x2c</c>: the font of each state.</summary>
	public HudFont?[] Fonts { get; } = new HudFont?[3];

	/// <summary><c>+0x30</c>, <c>+0x34</c>, <c>+0x38</c>: the <c>STYLES.DAT</c> record of each state; none when the table lacks it.</summary>
	public Es1WidgetStyle?[] Styles { get; } = new Es1WidgetStyle?[3];

	/// <summary><c>+0x3c</c>, the text; empty for none, where retail stores a 0 byte.</summary>
	public string Text { get; private set; } = "";

	/// <summary><c>+0x7c</c>–<c>+0x93</c>: each state's pen, relative to the window; (0, 0) with no text.</summary>
	public (int X, int Y)[] Points { get; } = new (int X, int Y)[3];

	/// <summary><c>+0x94</c>: <c>MTButton_Draw</c> frames the button in <see cref="HighlightColor"/>.</summary>
	public bool Highlighted { get; private set; }

	/// <summary>
	/// The three pens <c>MTButton_Ctor</c> and <c>MTButton_SetTextFromTable</c> set: x the rect's left, y its bottom less
	/// ((<c>int16</c>) height − the state font's cell height) / 2 less 2, truncating, the up state's in the
	/// <c>active</c> font, the pushed one a row lower, the inactive one's in the <c>inactive</c> font.
	/// </summary>
	private void PlaceText() {
		int height = (short)(Rect.Y1 - Rect.Y0);
		int up = Rect.Y1 - (height - (Fonts[0]?.CellHeight ?? 0)) / 2 - 2;
		Points[0] = (Rect.X0, up);
		Points[1] = (Rect.X0, up + 1);
		Points[2] = (Rect.X0, Rect.Y1 - (height - (Fonts[2]?.CellHeight ?? 0)) / 2 - 2);
	}

	/// <summary>Slot <c>+4</c>, <c>MTButton_Dtor</c> (<c>0001d10d</c>): its, <c>DUIButtonControl</c>'s and <c>DUIControl</c>'s vtables stored and the button freed.</summary>
	public override void Dtor() {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MTButton_Draw</c> (<c>0001d200</c>) (docs/es1/shell/menu-controls.md#drawing-a-control): in state 1
	/// the click (<see cref="Click"/>); in states 0 and 1 <c>CornerFrame_Draw</c> with the state's record over the rect
	/// and, while <see cref="Highlighted"/>, <c>Raster_FrameRectColour</c>(10, rect); in state 2 <c>CornerFrame_Draw</c>
	/// with record 2; any other state no frame. Then the text in the state's font from its pen, centered in the
	/// (<c>int16</c>) width (alignment 2), and <see cref="Es1DUIControl.Changed"/> cleared.
	/// </summary>
	public override void Draw(Es1ShellRenderContext context) {
		if (State == Pushed) {
			Click();
		}

		var canvas = context.Canvas;
		if (State is Up or Pushed) {
			Es1CornerFrame.Draw(canvas, Styles[State], Rect, _data.Corners);
			if (Highlighted) {
				canvas.OutlineRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, HighlightColor);
			}
		} else if (State == Inactive) {
			Es1CornerFrame.Draw(canvas, Styles[State], Rect, _data.Corners);
		}

		DrawText(context, (short)(Rect.X1 - Rect.X0));
		Changed = false;
	}

	/// <summary>The text centered in <paramref name="width"/> in the state's font from the state's pen; a state past 2 draws none, an engine guard.</summary>
	protected void DrawText(Es1ShellRenderContext context, int width) {
		if (State is < Up or > Inactive) {
			return;
		}

		context.Alignment = 2;
		context.FieldWidth = width;
		context.DrawString(Fonts[State], Text, Points[State].X, Points[State].Y);
	}

	/// <summary>
	/// A pushed button's click: while <c>Shell_SoundOption</c> (<see cref="Es1ShellData.SoundOption"/>) and the sound object
	/// <c>0007fd00</c> are set, the object stopped when its slot <c>+0x30</c> answers non-zero (slot <c>+0x14</c>) and
	/// played (slot <c>+0x10</c>).
	/// </summary>
	protected static void Click() {
		// PLACEHOLDER: the sound object 0007fd00, which the shell's sound manager makes; ES1's sound is not ported
		// (Es1ShellDevices.Init's ShellSound_Init), so the press is silent.
	}

	/// <summary>Slot <c>+0xc</c>, <c>MTButton_Invalidate</c> (<c>0001d17b</c>): <see cref="Es1DUIControl.Changed"/> set.</summary>
	public override void Invalidate() => Changed = true;

	/// <summary>Slot <c>+0x10</c>, <c>MTButton_Highlight</c> (<c>0001d1ce</c>): <see cref="Highlighted"/> and <see cref="Es1DUIControl.Changed"/> set.</summary>
	public override void Highlight() {
		Highlighted = true;
		Changed = true;
	}

	/// <summary>Slot <c>+0x18</c>, <c>MTButton_Unhighlight</c> (<c>0001d1e7</c>): <see cref="Highlighted"/> cleared, <see cref="Es1DUIControl.Changed"/> set.</summary>
	public override void Unhighlight() {
		Highlighted = false;
		Changed = true;
	}

	/// <summary>
	/// Slot <c>+0x3c</c>, <c>MTButton_SetTextFromTable</c> (<c>0001d393</c>)(button, <paramref name="value"/>): line
	/// <paramref name="value"/> of <c>eng\watext.bin</c> (<c>0009afa8</c>) copied to the text, the pens placed again and
	/// <see cref="Es1DUIControl.Changed"/> set. <c>Text_GetString</c> has no bounds test; a line the table lacks gives an
	/// empty text here, an engine guard.
	/// </summary>
	public override void SetTextFromTable(short value) {
		Text = _data.Text is { } lines && value >= 0 && value < lines.Length ? lines[value] : "";
		PlaceText();
		Changed = true;
	}
}

/// <summary>
/// The shell's icon button, class <c>MTBmpButton</c> (vtable <c>0007f24a</c>, <c>0xb1</c> bytes)
/// (docs/es1/shell/menu-controls.md#buttons): a text button drawn as a picture of <c>res32\corners.dba</c> chosen by its key.
/// </summary>
public sealed class Es1MTBmpButton : Es1MTButton {
	/// <summary>
	/// <c>MTBmpButton_Ctor</c> (<c>0001d47d</c>)(button, <paramref name="text"/>, <paramref name="id"/>,
	/// <paramref name="key"/>, <paramref name="rect"/>): <c>MTButton_Ctor</c>, the rect's corner at <c>+0x95</c>, and by
	/// the key the frames of <c>res32\corners.dba</c> (<c>000ab4ee</c>): frame <i>a</i> for the up and inactive states,
	/// <i>b</i> for the pushed one; ids 1503 to 1506 also call <see cref="Es1DUIControl.SetHitShape"/>(1). The state and
	/// <see cref="Es1DUIControl.Changed"/> cleared.
	/// </summary>
	public Es1MTBmpButton(Es1ShellData data, string? text, short id, byte key, Es1Rect rect)
			: base(data, id, key, text, rect) {
		Corner = (rect.X0, rect.Y0);
		(int a, int b)? frames = key switch {
			0x48 or 0x49 => id == 1503 ? (36, 37) : (20, 22),
			0x4a => id == 109 ? (34, 35) : (29, 31),
			0x4b => id switch { 1505 => (40, 41), 2003 => (44, 46), _ => (25, 27) },
			0x4d => id switch { 1506 => (42, 43), 2004 => (45, 47), _ => (24, 26) },
			0x4e => id == 108 ? (32, 33) : (28, 30),
			0x50 or 0x51 => id == 1504 ? (38, 39) : (21, 23),
			_ => null,
		};

		if (frames is var (frameA, frameB)) {
			Frames[0] = Es1CornerFrame.Frame(data.Corners, frameA);
			Frames[1] = Es1CornerFrame.Frame(data.Corners, frameB);
			Frames[2] = Frames[0];
			if (id is >= 1503 and <= 1506) {
				SetHitShape(1);
			}
		} else if (id is 1806 or 2003 or 1807 or 2004) {
			// PLACEHOLDER: the four ids at 0007f2c4 jump to code Ghidra left undefined (0001d97a, 0001d9f9), unread; their
			// menus are later modules' (dlg-menu-dialogs.md's Open). The button keeps no frames and draws only its text.
		}

		State = Up;
		Changed = false;
	}

	/// <summary><c>+0x95</c>, <c>+0x99</c>: the rect's corner, relative to the window, where the frame is drawn.</summary>
	public (int X, int Y) Corner { get; }

	/// <summary><c>+0xa5</c>, <c>+0xa9</c>, <c>+0xad</c>: each state's frame; none for a key the table lacks.</summary>
	public DynamixBitmap?[] Frames { get; } = new DynamixBitmap?[3];

	/// <summary>Slot <c>+4</c>, <c>0001da8d</c>: its, <c>MTButton</c>'s, <c>DUIButtonControl</c>'s and <c>DUIControl</c>'s vtables stored and the button freed.</summary>
	public override void Dtor() {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>0001db87</c>: the state's frame blitted at <see cref="Corner"/>; in state 1 the click, as
	/// <c>MTButton_Draw</c>'s; the text centered in the frame's width in the state's font from the state's pen; and
	/// <see cref="Es1DUIControl.Changed"/> cleared.
	/// </summary>
	public override void Draw(Es1ShellRenderContext context) {
		var frame = State is >= Up and <= Inactive ? Frames[State] : null;
		context.Blit(frame, Corner.X, Corner.Y);
		if (State == Pushed) {
			Click();
		}

		DrawText(context, frame?.Cols ?? 0);
		Changed = false;
	}

	/// <summary>Slot <c>+0xc</c>, <c>0001dc9f</c>: <see cref="Es1DUIControl.Changed"/> set.</summary>
	public override void Invalidate() => Changed = true;

	/// <summary>Slot <c>+0x48</c>, <c>0001db1d</c>: the rect from <see cref="Corner"/> to the corner plus the state frame's width and height.</summary>
	public override Es1Rect GetRect() {
		var frame = State is >= Up and <= Inactive ? Frames[State] : null;
		return new Es1Rect(Corner.X, Corner.Y, Corner.X + (frame?.Cols ?? 0), Corner.Y + (frame?.Rows ?? 0));
	}
}
