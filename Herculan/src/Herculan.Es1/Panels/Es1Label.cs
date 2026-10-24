using Herculan.Engine.Content;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// The render context a label writes into, the one installed when <c>Label_SetText</c> runs: a panel's or the
/// cockpit's canvas context (<see cref="Es1PanelCanvas"/>), or the scene's (<see cref="Es1SceneCanvas"/>).
/// </summary>
public interface IEs1LabelSurface {
	/// <summary><c>Raster_FillRect</c> under the brush {0, <paramref name="color"/>}: the inclusive rect filled.</summary>
	void FillRect(int x0, int y0, int x1, int y1, byte color);

	/// <summary><c>HudFont_DrawString</c> (<c>00069e38</c>) from the pen (<paramref name="x"/>, <paramref name="y"/>).</summary>
	void DrawString(HudFont? font, string? text, int x, int y);
}

/// <summary>
/// The simulator's label, <c>Label_Ctor</c> (<c>ES1DBSIM 00030f44</c>), placed by <c>Label_SetRect</c>
/// (<c>00031080</c>) and drawn by <c>Label_SetText</c> (<c>0003114c</c>) (docs/es1/simulation/cockpit-widgets.md#labels):
/// its font <c>+0</c>, rect <c>+4</c>, anchor <c>+0x14</c>/<c>+0x16</c>, flags <c>+0x18</c> and fill color
/// <c>+0x1c</c>, 0 from the constructor. The flags' bit 1 (2) centers the text on the anchor and bit 2 (4) ends it
/// there; any other value starts it there.
/// </summary>
public sealed class Es1Label {
	/// <summary>The flags that center and right-align.</summary>
	public const int Centered = 2, RightAligned = 4;

	/// <summary><c>Label_Ctor</c>(0, <paramref name="font"/>).</summary>
	public Es1Label(HudFont? font) {
		Font = font;
	}

	/// <summary><c>+0</c>, which a panel button changes with its state.</summary>
	public HudFont? Font { get; set; }

	/// <summary><c>+4</c>–<c>+0x10</c>, in the render context's coordinates.</summary>
	public Es1Rect Rect { get; private set; }

	/// <summary><c>+0x14</c>, <c>+0x16</c>, the anchor, <c>int16</c> each.</summary>
	public (short X, short Y) Anchor { get; private set; }

	/// <summary><c>+0x18</c>.</summary>
	public int Flags { get; private set; }

	/// <summary><c>+0x1c</c>, the fill color; 0 fills nothing.</summary>
	public int FillColor { get; set; }

	/// <summary>
	/// <c>+0x20</c>, the hotkey font of a <c>Label_CtorWithAltFont</c> (<c>00031250</c>) label, which
	/// <see cref="SetTextWithHotkey"/> draws its one character in; a plain label has none.
	/// </summary>
	public HudFont? HotkeyFont { get; set; }

	/// <summary>
	/// <c>Label_SetRect</c>(label, <paramref name="flags"/>, <paramref name="rect"/>, margin): the rect kept; the
	/// anchor's x at the rect's middle, ((<i>x1</i> − <i>x0</i>) &gt;&gt; 1) + <i>x0</i> + <paramref name="marginX"/>,
	/// for flags 2, at <i>x1</i> − <paramref name="marginX"/> + 1 for flags 4, and at <i>x0</i> +
	/// <paramref name="marginX"/> otherwise; its y at ((<i>y1</i> − <i>y0</i> + the font's cell height) &gt;&gt; 1) +
	/// <i>y0</i> + <paramref name="marginY"/>; the flags kept. The margin is a point, of which the function reads the
	/// low words of x and y; the font's cell height is <c>HudFont_CellHeight</c>(font, <c>'0'</c>).
	/// </summary>
	public void SetRect(int flags, Es1Rect rect, int marginX = 0, int marginY = 0) {
		Rect = rect;
		short x = (flags & Centered) != 0
			? (short)(((rect.X1 - rect.X0) >> 1) + rect.X0 + marginX)
			: (flags & RightAligned) != 0
				? (short)(rect.X1 - marginX + 1)
				: (short)(rect.X0 + marginX);
		int cellHeight = Font?.CellHeight ?? 0;
		short y = (short)((short)((rect.Y1 - rect.Y0 + cellHeight) >> 1) + rect.Y0 + marginY);
		Anchor = (x, y);
		Flags = flags;
	}

	/// <summary>
	/// <c>Label_SetText</c>(label, <paramref name="text"/>) on <paramref name="canvas"/>: the pen at the anchor, moved
	/// left by (<i>w</i> − 1) &gt;&gt; 1 for flags 2 and by <i>w</i> − 1 for flags 4, <i>w</i> the text's width
	/// (<c>HudFont_MeasureString</c>); the rect filled in the fill color when it is not 0; then the text drawn from the
	/// pen. With no font nothing is drawn.
	/// </summary>
	public void SetText(IEs1LabelSurface canvas, string? text) {
		if (Font == null || text == null) {
			return;
		}

		if (FillColor != 0) {
			canvas.FillRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, (byte)FillColor);
		}

		canvas.DrawString(Font, text, Pen(Font, text), Anchor.Y);
	}

	/// <summary>
	/// <c>Label_SetTextWithHotkey</c> (<c>00031294</c>)(label, <paramref name="index"/>, <paramref name="text"/>)
	/// (docs/es1/simulation/cockpit-widgets.md#labels): <see cref="SetText"/>, then character <paramref name="index"/>
	/// drawn again in <see cref="HotkeyFont"/> with the pen moved right by the width of the characters before it. For an
	/// index at the text's length retail draws the terminating NUL, which no font has a glyph for, and past it a character
	/// and width from two stack locals it never wrote; nothing is drawn here for either, an engine guard.
	/// </summary>
	public void SetTextWithHotkey(IEs1LabelSurface canvas, int index, string? text) {
		SetText(canvas, text);
		if (Font == null || text == null || index >= text.Length) {
			return;
		}

		canvas.DrawString(HotkeyFont, text[index].ToString(), Pen(Font, text) + Font.Measure(text[..index]), Anchor.Y);
	}

	/// <summary>The pen's x <see cref="SetText"/> starts the text at.</summary>
	private int Pen(HudFont font, string text) =>
		(Flags & Centered) != 0
			? Anchor.X - ((font.Measure(text) - 1) >> 1)
			: (Flags & RightAligned) != 0
				? Anchor.X - (font.Measure(text) - 1)
				: Anchor.X;
}
