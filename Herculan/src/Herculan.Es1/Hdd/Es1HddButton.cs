using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Hdd;

/// <summary>
/// One of the Heads-Down Display's fifteen buttons (docs/es1/simulation/heads-down-display.md#the-buttons): a picture of
/// <c>dba\pbuttons</c> for the page buttons, the arrows and the magnifiers, a filled box for the comm boxes, a caption
/// on a fill for <c>XMIT</c> and <c>CANCEL</c>; a click goes to the display and changes nothing on the button. Its slots
/// are <c>HDDSelectGadgetVtable</c> (<c>00094776</c>)'s; the value, set-value and drag slots are the button base's.
/// </summary>
public sealed class Es1HddButton : Es1ButtonWidget {
	/// <summary>The fill of a comm box in highlight mode 0, with its lamp lit and with it out.</summary>
	public const byte BoxLit = 0x38, BoxUnlit = 0x3f;

	/// <summary>The fill of the selected box's marker and of the previous one's in highlight mode 1, and of a held <c>XMIT</c> or <c>CANCEL</c>.</summary>
	public const byte MarkerLit = 0x3b, MarkerUnlit = 0x39, CaptionHeld = 0x3b;

	/// <summary>
	/// <c>HddButton_Ctor</c> (<c>ES1DBSIM 0003ea5c</c>)(0, <paramref name="index"/>, <paramref name="display"/>,
	/// <paramref name="rect"/>): the widget on the rect, the drag flag 0, the display <c>+0x20</c>, the index
	/// <c>+0x28</c>, the state 0 and the lamp out; with the pictures and the caption <c>HddDisplay_Ctor</c> gives it after.
	/// </summary>
	/// <param name="unlit"><c>+0x30</c>, the frame at state 0; null blits nothing.</param>
	/// <param name="lit"><c>+0x34</c>, the frame at any other state.</param>
	internal Es1HddButton(int index, Es1HddDisplay display, Es1Rect rect, DynamixBitmap? unlit, DynamixBitmap? lit) {
		Index = index;
		Display = display;
		Rect = rect;
		UnlitFrame = unlit;
		LitFrame = lit;
	}

	/// <summary><c>+0x20</c>, the display the click goes to.</summary>
	public Es1HddDisplay Display { get; }

	/// <summary><c>+0x28</c>, the index, 0 to 14.</summary>
	public int Index { get; }

	/// <summary><c>+0x2c</c>, the caption label, which <c>HddDisplay_Ctor</c> builds for <c>XMIT</c> and <c>CANCEL</c> alone.</summary>
	public Es1Label? Caption { get; internal set; }

	/// <summary><c>+0x30</c>, the unlit frame, shown at state 0.</summary>
	public DynamixBitmap? UnlitFrame { get; }

	/// <summary><c>+0x34</c>, the lit frame, shown at any other state, held and hidden alike.</summary>
	public DynamixBitmap? LitFrame { get; }

	/// <summary><c>+0x3c</c>, the lamp, which <see cref="Es1HddDisplay.SelectPilot"/> alone lights and puts out.</summary>
	public bool Lamp { get; set; }

	/// <summary><c>+0x1b</c>, the paint counter the invalidate sets to 2.</summary>
	public byte PaintCounter { get; set; }

	/// <summary>
	/// Slot <c>+4</c>, <c>HddButton_Paint</c> (<c>0003eb3c</c>), by the index with no test of the state
	/// (docs/es1/simulation/heads-down-display.md#painting-a-button): 0 to 7 <see cref="LitFrame"/> at a state other than 0
	/// and <see cref="UnlitFrame"/> at 0, blitted at the top-left; 10 to 12 by the display's highlight mode: 0 the box's rect
	/// framed (brush type 4) in <see cref="BoxLit"/> with the lamp lit and <see cref="BoxUnlit"/> with it out, 1 with the
	/// lamp lit the selected box's marker rect filled <see cref="MarkerLit"/> and with it out the previous box's
	/// <see cref="MarkerUnlit"/>, none when there is no previous box; 13 and 14 the caption's fill
	/// <see cref="CaptionHeld"/> while held (for <c>XMIT</c> also only with the command screen's
	/// <see cref="Es1HddCommandScreen.ReadyToSend"/> set), else <see cref="Es1HddDisplay.LabelFill"/>, and <c>XMIT</c> or
	/// <c>CANCEL</c> written; 8 and 9 nothing.
	/// </summary>
	public override void Paint() {
		var canvas = Display.Cockpit.PushCanvasContext();
		switch (Index) {
			case <= Es1HddDisplay.ZoomInButton:
				canvas.Blit(State != Up ? LitFrame : UnlitFrame, Rect.X0, Rect.Y0);
				break;
			case >= Es1HddDisplay.FirstCommBoxButton and < Es1HddDisplay.XmitButton:
				if (Display.HighlightMode == 0) {
					canvas.OutlineRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, Lamp ? BoxLit : BoxUnlit);
				} else if (Display.HighlightMode == 1) {
					// With the lamp lit a box is selected; a lit lamp with none, which nothing sets, fills nothing here.
					int slot = Lamp ? Display.SelectedSlot : Display.PreviousSlot;
					if (slot != -1) {
						var marker = Display.CommBoxes[slot].MarkerRect;
						canvas.FillRect(marker.X0, marker.Y0, marker.X1, marker.Y1, Lamp ? MarkerLit : MarkerUnlit);
					}
				}

				break;
			case Es1HddDisplay.XmitButton or Es1HddDisplay.CancelButton:
				if (Caption == null) {
					break;
				}

				bool lit = State == Pressed && (Index == Es1HddDisplay.CancelButton || Display.CommandScreen.ReadyToSend);
				Caption.FillColor = lit ? CaptionHeld : Es1HddDisplay.LabelFill;
				Caption.SetText(canvas, Display.ButtonCaption(Index - Es1HddDisplay.XmitButton));
				break;
		}
	}

	/// <summary>Slot <c>+8</c>, <c>Widget_InvalidateDeferredHddCopy</c> (<c>0003eb14</c>): the paint counter set to 2.</summary>
	public override void Invalidate() => PaintCounter = 2;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>HddButton_OnClick</c> (<c>0003ed2c</c>)(button, <paramref name="value"/>): with no test of
	/// the value, the display's <see cref="Es1HddDisplay.OnChildClick"/>, then the button invalidated and the mixin's
	/// click sound.
	/// </summary>
	public override void OnClick(short value) {
		_ = value;
		Display.OnChildClick(this);
		Invalidate();
		// PLACEHOLDER: Widget_ClickSound, Sound_Play(0x11); no plan step ports ES1's sound yet.
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>HddButton_FlushDeferredPaint</c> (<c>0003ecc8</c>), which the display's update calls: while
	/// <c>CockpitView_RectCornerVisible</c> passes the rect, at a paint counter of 2 the paint, at 1 the paint again for a
	/// comm box and the rect copied from the page on the screen to the page drawn
	/// (<see cref="Es1Cockpit.DrawToCockpit"/> with 0) for any other, each lowering it by 1.
	/// </summary>
	public override void Tick() {
		if (!Display.Cockpit.RectCornerVisible(Rect)) {
			return;
		}

		if (PaintCounter == 2) {
			Paint();
			PaintCounter--;
		} else if (PaintCounter == 1) {
			if (Index is >= Es1HddDisplay.FirstCommBoxButton and < Es1HddDisplay.XmitButton) {
				Paint();
			} else {
				Display.Cockpit.DrawToCockpit(Rect, 0);
			}

			PaintCounter--;
		}
	}
}
