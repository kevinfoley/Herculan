using HercWorks.Core.Data.File.Gau;

namespace Herculan.Es1.View;

/// <summary>A rect of the simulator's UI library, {<i>x0</i>, <i>y0</i>, <i>x1</i>, <i>y1</i>}, both corners inside it.</summary>
public readonly record struct Es1Rect(int X0, int Y0, int X1, int Y1) {
	/// <summary>A <c>.GAU</c> rect as the file holds it.</summary>
	public static Es1Rect From(Es1GauRect rect) => new(rect.X0, rect.Y0, rect.X1, rect.Y1);

	/// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>) lies in the rect, edges included.</summary>
	public bool Contains(int x, int y) => X0 <= x && x <= X1 && Y0 <= y && y <= Y1;

	/// <summary><c>Widget_OffsetRect</c> (<c>ES1DBSIM 0004387c</c>): <paramref name="dx"/> added to <i>x0</i> and <i>x1</i>, <paramref name="dy"/> to <i>y0</i> and <i>y1</i>.</summary>
	public Es1Rect Offset(int dx, int dy) => new(X0 + dx, Y0 + dy, X1 + dx, Y1 + dy);
}

/// <summary>
/// The leaf widget of the simulator's UI library (docs/es1/simulation/cockpit-mouse.md#hit-testing): its rect
/// <c>+0</c>, the hit-shape byte <c>+0x10</c> with the diamond's centre and radius, the state <c>+0x17</c> and the drag
/// flag <c>+0x19</c>, and the slots of its table at <c>+0x1c</c> that a press, a release and a drag call, as virtual
/// members. A subclass is a widget class's table.
/// </summary>
public abstract class Es1Widget {
	/// <summary>The states <see cref="State"/> takes: up, pressed, and the one <c>Widget_HitTestChildren</c> passes over.</summary>
	public const short Up = 0, Pressed = 1, Hidden = 2;

	/// <summary><c>+0</c>, in the root's canvas.</summary>
	public Es1Rect Rect { get; set; }

	/// <summary><c>+0x10</c> non-zero: the widget is the diamond about (<see cref="CenterX"/>, <see cref="CenterY"/>) of radius <see cref="Radius"/>, not its rect.</summary>
	public bool IsDiamond { get; init; }

	/// <summary><c>+0x11</c>, <c>+0x13</c>, <c>+0x15</c>.</summary>
	public short CenterX { get; init; }

	/// <inheritdoc cref="CenterX"/>
	public short CenterY { get; init; }

	/// <inheritdoc cref="CenterX"/>
	public short Radius { get; init; }

	/// <summary><c>+0x17</c>.</summary>
	public short State { get; set; }

	/// <summary><c>+0x19</c>, 1 when a press captures the drag.</summary>
	public short DragFlag { get; set; }

	/// <summary>
	/// <c>Widget_HitTest</c> (<c>000437d0</c>): with the hit-shape byte 0, whether the point lies in the rect, edges
	/// included; otherwise whether |<i>x</i> − <i>cx</i>| + |<i>y</i> − <i>cy</i>| ≤ <i>r</i>.
	/// </summary>
	public bool HitTest(int x, int y) =>
		!IsDiamond ? Rect.Contains(x, y) : Math.Abs(x - CenterX) + Math.Abs(y - CenterY) <= Radius;

	/// <summary>Slot <c>+8</c>, invalidate, which <c>Widget_Repaint</c> calls.</summary>
	public abstract void Invalidate();

	/// <summary>Slot <c>+0xc</c>, the click, with what <see cref="GetValue"/> answered.</summary>
	public abstract void OnClick(short value);

	/// <summary>Slot <c>+0x14</c>, the value a click is handed.</summary>
	public abstract short GetValue(Es1WidgetGlobals globals);

	/// <summary>Slot <c>+0x1c</c>, the drag, with the point in the root's canvas.</summary>
	public abstract void DragTo(int x, int y);
}

/// <summary>
/// A widget on the button base's table, the library's <c>00094b8a</c>, of which
/// <c>CTLButtonControlVtablePanelCopy</c> (<c>0009301e</c>) is <c>panel.cpp</c>'s copy
/// (docs/es1/simulation/cockpit-panel.md#the-screen-edges-and-the-cursors): every slot does nothing but the value, which
/// answers <c>Widget_ClickButtons</c>, so a click hands the button the release's mouse buttons.
/// </summary>
public class Es1ButtonWidget : Es1Widget {
	/// <inheritdoc/>
	public override void Invalidate() {
	}

	/// <inheritdoc/>
	public override void OnClick(short value) {
	}

	/// <summary><c>Widget_GetButtonValuePanelCopy</c> (<c>0002daef</c>): <c>Widget_ClickButtons</c> (<c>00094b84</c>).</summary>
	public override short GetValue(Es1WidgetGlobals globals) => (short)globals.ClickButtons;

	/// <inheritdoc/>
	public override void DragTo(int x, int y) {
	}
}

/// <summary>
/// The UI library's globals that a press, a release and the frame's mouse events share
/// (docs/es1/simulation/cockpit-mouse.md#globals).
/// </summary>
public sealed class Es1WidgetGlobals {
	/// <summary><c>Widget_ClickButtons</c> (<c>00094b84</c>): the mouse buttons of the record being handled, bit 0 the left and bit 1 the right.</summary>
	public ushort ClickButtons { get; set; }

	/// <summary><c>Widget_ActiveRoot</c> (<c>00094c54</c>), the root the frame's mouse events go to.</summary>
	public Es1WidgetRoot? ActiveRoot { get; set; }

	/// <summary><c>Widget_PressedIndex</c> (<c>00094c58</c>): the pressed widget's index in the root's list, −1 for none.</summary>
	public short PressedIndex { get; set; }

	/// <summary><c>Widget_DragCapture</c> (<c>00094c5a</c>): the pressed widget has the drag.</summary>
	public bool DragCapture { get; set; }

	/// <summary><c>Widget_ClearPressed</c> (<c>00044310</c>)(root): <paramref name="root"/> the active root, nothing pressed.</summary>
	public void ClearPressed(Es1WidgetRoot root) {
		ActiveRoot = root;
		PressedIndex = -1;
	}
}
