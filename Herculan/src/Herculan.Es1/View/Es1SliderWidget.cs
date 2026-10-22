namespace Herculan.Es1.View;

/// <summary>
/// The slider base of the simulator's UI library (docs/es1/simulation/cockpit-mouse.md#the-slider-base): a widget whose
/// knob runs along its rect, with the scale <c>+0x20</c>, the range <c>+0x24</c>/<c>+0x26</c>, the knob's position
/// <c>+0x28</c>, its previous position <c>+0x2a</c>, its length <c>+0x2c</c> and its rect <c>+0x2e</c>–<c>+0x3d</c>; and
/// the paint counter <c>+0x1b</c> its subclasses' invalidate sets. The value, set-value, drag and recompute slots
/// (<c>+0x14</c>–<c>+0x20</c>) are <c>SliderWidget_*H</c> for a horizontal track and <c>SliderWidget_*V</c> for a
/// vertical one, by the table the subclass installs; a subclass supplies the paint, invalidate, click and per-frame
/// slots.
/// </summary>
public abstract class Es1SliderWidget : Es1Widget {
	/// <summary>
	/// <c>SliderWidget_CtorBase</c> (<c>ES1DBSIM 000439d4</c>)(slider, <paramref name="knobLength"/>,
	/// <paramref name="position"/>, <paramref name="maximum"/>, <paramref name="minimum"/>, <paramref name="rect"/>): the
	/// widget on the rect, the scale 0, the range, the position and the previous position, the knob's length, and the
	/// drag flag 1. <paramref name="horizontal"/> is the table the subclass installs.
	/// </summary>
	protected Es1SliderWidget(bool horizontal, short knobLength, short position, short maximum, short minimum, Es1Rect rect) {
		Horizontal = horizontal;
		Rect = rect;
		Minimum = minimum;
		Maximum = maximum;
		Position = position;
		PreviousPosition = position;
		KnobLength = knobLength;
		DragFlag = 1;
	}

	/// <summary>Whether the slots are <c>SliderWidget_*H</c>'s, for a knob that runs across, or <c>SliderWidget_*V</c>'s.</summary>
	public bool Horizontal { get; }

	/// <summary><c>+0x1b</c>, the paint counter: the invalidate sets it to 2 and the per-frame paint acts while it is 2.</summary>
	public byte PaintCounter { get; set; }

	/// <summary><c>+0x20</c>, the scale: Q16 pixels a unit of value.</summary>
	public int Scale { get; protected set; }

	/// <summary><c>+0x24</c>, the range's minimum.</summary>
	public short Minimum { get; protected set; }

	/// <summary><c>+0x26</c>, the range's maximum.</summary>
	public short Maximum { get; protected set; }

	/// <summary>
	/// <c>+0x28</c>, the knob's position: its left column on a horizontal track, its bottom row on a vertical one. The
	/// knob's rect follows it only when the subclass paints.
	/// </summary>
	public short Position { get; protected set; }

	/// <summary><c>+0x2a</c>, the position before the last drag.</summary>
	public short PreviousPosition { get; protected set; }

	/// <summary><c>+0x2c</c>, the knob's length along the track.</summary>
	public short KnobLength { get; protected set; }

	/// <summary><c>+0x2e</c>–<c>+0x3d</c>, the knob's rect as last painted, which the value slot reads.</summary>
	public Es1Rect KnobRect { get; protected set; }

	/// <summary>
	/// Slot <c>+0x1c</c>, <c>SliderWidget_DragToPointH</c> (<c>00043a68</c>) or <c>SliderWidget_DragToPointV</c>
	/// (<c>00043b80</c>), (slider, point): the position kept as the previous one, then put at the point's <i>x</i>,
	/// held to <i>x0</i> … <i>x1</i> − length, or its <i>y</i>, held to <i>y0</i> + length … <i>y1</i>, each as an
	/// <c>int16</c>.
	/// </summary>
	public override void DragTo(int x, int y) {
		PreviousPosition = Position;
		if (Horizontal) {
			Position = x < Rect.X0 ? (short)Rect.X0 : Rect.X1 - KnobLength < x ? (short)(Rect.X1 - KnobLength) : (short)x;
		} else {
			Position = y < Rect.Y0 + KnobLength ? (short)(Rect.Y0 + KnobLength) : Rect.Y1 < y ? (short)Rect.Y1 : (short)y;
		}
	}

	/// <summary>
	/// Slot <c>+0x14</c>, <c>SliderWidget_GetValueH</c> (<c>00043ac8</c>) or <c>SliderWidget_GetValueV</c>
	/// (<c>00043be8</c>), (slider): ((the knob rect's <i>x0</i> − <i>x0</i>) ≪ 16) / scale, or the same from the
	/// <i>y0</i>s, plus the minimum, as an <c>int16</c>: the release sign-extends the answer's low 16 bits (<c>CWDE</c>)
	/// before it reaches the click (docs/es1/simulation/cockpit-throttle-gauge.md#dragging-the-knob).
	/// </summary>
	public override short GetValue(Es1WidgetGlobals globals) {
		int along = Horizontal ? KnobRect.X0 - Rect.X0 : KnobRect.Y0 - Rect.Y0;
		return (short)((along << 16) / Scale + Minimum);
	}

	/// <summary>
	/// Slot <c>+0x18</c>, <c>SliderWidget_SetValueH</c> (<c>00043ae4</c>) or <c>SliderWidget_SetValueV</c>
	/// (<c>00043c08</c>), (slider, <paramref name="value"/>): the value held to the range; the position at <i>x0</i> +
	/// ((value − minimum) × scale ≫ 16), or <i>y1</i> less that, the product shifted arithmetically (<c>SAR</c>); and the
	/// click slot with the held value.
	/// </summary>
	public void SetValue(short value) {
		short held = value < Minimum ? Minimum : value > Maximum ? Maximum : value;
		short along = (short)(((held - Minimum) * Scale) >> 16);
		Position = Horizontal ? (short)(along + Rect.X0) : (short)(Rect.Y1 - along);
		OnClick(held);
	}

	/// <summary>
	/// Slot <c>+0x20</c>, <c>SliderWidget_RecomputeScaleH</c> (<c>00043b4c</c>) or <c>SliderWidget_RecomputeScaleV</c>
	/// (<c>00043c74</c>), (slider): the scale ((<i>x1</i> − the knob rect's width − <i>x0</i>) ≪ 16) / (maximum −
	/// minimum), or the same over the heights.
	/// </summary>
	public void RecomputeScale() {
		int travel = Horizontal
			? Rect.X1 - (KnobRect.X1 - KnobRect.X0) - Rect.X0
			: Rect.Y1 - (KnobRect.Y1 - KnobRect.Y0) - Rect.Y0;
		Scale = (travel << 16) / (Maximum - Minimum);
	}
}
