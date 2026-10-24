using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.WeaponRows;

/// <summary>
/// A row's number button, <c>WeaponSelectGadget</c> (docs/es1/simulation/cockpit-weapon-gauges.md#the-plain-button):
/// a button the width of the digit drawn from <c>dba\pbuttons</c> in its press state, which passes every click to the
/// row's gauge. The pod row uses it as it is; the energy and ammunition rows use <see cref="Es1ChainedWeaponSelectButton"/>.
/// Its slots are <c>WeaponSelectGadgetVtable</c> (<c>00093bbe</c>)'s.
/// </summary>
public class Es1WeaponSelectButton : Es1ButtonWidget {
	/// <summary>The columns the digit's label spans from the given rect's <i>x0</i>, and the columns the button keeps.</summary>
	private const int DigitWidth = 9, ButtonWidth = 8;

	private readonly IReadOnlyList<HudFont?> _fonts;

	/// <summary>
	/// <c>WeaponSelectGadget_Ctor</c> (<c>000380fc</c>)(0, <paramref name="row"/>, <paramref name="gauge"/>,
	/// <paramref name="rect"/>): the state 0; the digit's label in <c>cpon</c> centered over {<i>x0</i>, <i>y0</i>,
	/// <i>x0</i> + 9, <i>y1</i>}; the digit the row + <c>'1'</c>; and last the rect cut to {<i>x0</i>, <i>y0</i>,
	/// <i>x0</i> + 8, <i>y0</i> + the rows of <c>pbuttons</c>' frame 0}.
	/// </summary>
	/// <param name="fonts"><c>ColorSchemePanels</c>.</param>
	/// <param name="pushButtons"><c>CockpitPButtonsBank</c>, the frames of <c>dba\pbuttons</c>; none when null.</param>
	internal Es1WeaponSelectButton(Es1WeaponGauge gauge, short row, Es1Rect rect, IReadOnlyList<HudFont?> fonts,
			IReadOnlyList<DynamixBitmap>? pushButtons) {
		Gauge = gauge;
		Row = row;
		_fonts = fonts;
		PushButtons = pushButtons;
		DigitLabel = new Es1Label(Es1WeaponGauge.Font(fonts, Es1CockpitViews.CpOn));
		DigitLabel.SetRect(Es1Label.Centered, new Es1Rect(rect.X0, rect.Y0, rect.X0 + DigitWidth, rect.Y1));
		Digit = ((char)(row + '1')).ToString();
		// The rows of frame 0, its int16 +4; with no frame, which a missing dba\pbuttons leaves, 0, an engine guard.
		Rect = new Es1Rect(rect.X0, rect.Y0, rect.X0 + ButtonWidth, rect.Y0 + (Frame(0)?.Rows ?? 0));
	}

	/// <summary><c>+0x20</c>, the owning gauge.</summary>
	public Es1WeaponGauge Gauge { get; }

	/// <summary><c>+0x28</c>, the row.</summary>
	public short Row { get; }

	/// <summary><c>+0x3c</c>, the digit's label.</summary>
	public Es1Label DigitLabel { get; }

	/// <summary><c>+0x40</c>, the digit.</summary>
	public string Digit { get; }

	/// <summary><c>+0x1b</c>, the paint counter the invalidate sets to 2.</summary>
	public byte PaintCounter { get; set; }

	/// <summary>The frames of <c>dba\pbuttons</c>.</summary>
	protected IReadOnlyList<DynamixBitmap>? PushButtons { get; }

	/// <summary>Slot <c>+4</c>, <c>WeaponSelectGadget_PaintUnderlay</c>.</summary>
	public override void Paint() => PaintUnderlay();

	/// <summary>Slot <c>+8</c>, <c>Widget_InvalidateDeferredWeaponGaugeCopy</c> (<c>00038234</c>): the paint counter set to 2.</summary>
	public override void Invalidate() => PaintCounter = 2;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>WeaponSelectGadget_OnClick</c> (<c>00038304</c>)(gadget, <paramref name="value"/>): whatever
	/// the mouse buttons, the gauge's <see cref="Es1WeaponGauge.OnChildClick"/>, then the button invalidated and the
	/// mixin's click sound.
	/// </summary>
	public override void OnClick(short value) {
		Gauge.OnChildClick(value, this);
		Invalidate();
		// PLACEHOLDER: Widget_ClickSound, Sound_Play(0x11); no plan step ports ES1's sound yet.
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>WeaponSelectGadget_FlushDeferredPaint</c> (<c>000382cc</c>): at a paint counter of 2 the
	/// paint, at 1 the rect copied from the page on the screen to the page drawn (<see cref="Es1Cockpit.DrawToCockpit"/>
	/// with 0), each lowering it by 1.
	/// </summary>
	public override void Tick() {
		if (PaintCounter == 2) {
			Paint();
			PaintCounter--;
		} else if (PaintCounter == 1) {
			Gauge.Cockpit.DrawToCockpit(Rect, 0);
			PaintCounter--;
		}
	}

	/// <summary>
	/// <c>WeaponSelectGadget_PaintUnderlay</c> (<c>0003825c</c>): when <c>CockpitView_RectCornerVisible</c> passes the
	/// rect, <c>pbuttons</c>' frame <see cref="Es1Widget.State"/> blitted at its top-left and the digit written in
	/// <c>ColorSchemePanels</c>[4 + state], <c>cpon</c> up, <c>cppress</c> pressed and <c>cpoff</c> destroyed.
	/// </summary>
	protected void PaintUnderlay() {
		var cockpit = Gauge.Cockpit;
		if (!cockpit.RectCornerVisible(Rect)) {
			return;
		}

		var canvas = cockpit.PushCanvasContext();
		canvas.Blit(Frame(State), Rect.X0, Rect.Y0);
		DigitLabel.Font = Es1WeaponGauge.Font(_fonts, Es1CockpitViews.CpOn + State);
		DigitLabel.SetText(canvas, Digit);
	}

	/// <summary>Frame <paramref name="index"/> of <c>dba\pbuttons</c>; null when the bank or the frame is missing.</summary>
	protected DynamixBitmap? Frame(int index) =>
		PushButtons != null && index >= 0 && index < PushButtons.Count ? PushButtons[index] : null;
}

/// <summary>
/// The energy and ammunition rows' number button, <c>ChainedWeaponSelectGadget</c>
/// (docs/es1/simulation/cockpit-weapon-gauges.md#the-chained-button): the plain button with the weapon's name beside it
/// and a state box that shows whether the weapon is armed or in the chain and ready. Its slots are
/// <c>ChainedWeaponSelectGadgetVtable</c> (<c>00093bfe</c>)'s.
/// </summary>
public sealed class Es1ChainedWeaponSelectButton : Es1WeaponSelectButton {
	/// <summary><c>pbuttons</c>' frames for the state box: ready, not ready, and destroyed.</summary>
	public const int ReadyFrame = 55, NotReadyFrame = 56, DestroyedFrame = 57;

	/// <summary>The name's plate while the row is armed.</summary>
	public const byte ArmedPlate = 0x35;

	/// <summary>The empty state box's size beyond its top-left: 2 columns and 7 rows.</summary>
	private const int BoxWidth = 2, BoxHeight = 7;

	private readonly Es1WeaponRowGauge _row;
	private readonly IReadOnlyList<HudFont?> _fonts;
	private readonly string? _offline;

	/// <summary>
	/// <c>ChainedWeaponSelectGadget_Ctor</c> (<c>00038338</c>)(0, <paramref name="row"/>, <paramref name="gauge"/>,
	/// <paramref name="rect"/>): the plain button, then, with {<i>x0</i>, <i>y0</i>, <i>x1</i>, <i>y1</i>} the rect given,
	/// the state box's top-left at the digit label's <i>x1</i> + 2 and <i>y0</i>; the name's label in <c>cpylw</c>
	/// placed left-aligned one column in over {<i>x0</i> + 15, <i>y0</i>, <i>x1</i>, <i>y1</i>}; and the second rect
	/// {<i>x0</i> + 11, <i>y0</i>, <i>x1</i>, <i>y1</i>}.
	/// </summary>
	/// <param name="offline"><c>STRINGS0.STR</c> group 2's <c>OFFLINE</c> (<c>000bd2c4</c>).</param>
	internal Es1ChainedWeaponSelectButton(Es1WeaponRowGauge gauge, short row, Es1Rect rect, IReadOnlyList<HudFont?> fonts,
			IReadOnlyList<DynamixBitmap>? pushButtons, string? offline)
		: base(gauge, row, rect, fonts, pushButtons) {
		_row = gauge;
		_fonts = fonts;
		_offline = offline;
		Box = (DigitLabel.Rect.X1 + 2, rect.Y0);
		NameLabel = new Es1Label(Es1WeaponGauge.Font(fonts, Es1CockpitViews.CpYellow));
		NameLabel.SetRect(1, new Es1Rect(rect.X0 + 15, rect.Y0, rect.X1, rect.Y1), 1);
		SecondRect = new Es1Rect(rect.X0 + 11, rect.Y0, rect.X1, rect.Y1);
	}

	/// <summary><c>+0x2c</c>, the name's label.</summary>
	public Es1Label NameLabel { get; }

	/// <summary><c>+0x42</c>, <c>+0x46</c>, the state box's top-left.</summary>
	public (int X, int Y) Box { get; }

	/// <summary><c>+0x4a</c>–<c>+0x56</c>, the rect from the box to the name's end, which the flush copies with the button's.</summary>
	public Es1Rect SecondRect { get; }

	/// <summary>
	/// Slot <c>+4</c>, <c>WeaponSelectGadget_Paint</c> (<c>00038464</c>): when <c>CockpitView_RectCornerVisible</c> passes
	/// the rect, the digit (<see cref="Es1WeaponSelectButton.PaintUnderlay"/>); then, with the row's destroyed byte clear,
	/// its name in <c>cpylw</c> on a plate of <see cref="ArmedPlate"/> while armed and in <c>cpgreen</c> on
	/// <see cref="Es1WeaponGauge.DarkPlate"/> while not, and the state box: while armed or in the chain,
	/// <see cref="ReadyFrame"/> when ready and <see cref="NotReadyFrame"/> when not, blitted at the box's top-left,
	/// otherwise the box filled in <see cref="Es1WeaponGauge.DarkPlate"/>. With the destroyed byte set, the button's rect
	/// widened to the row's <i>x1</i> and <i>y1</i> and left so, the label centered over it showing <c>OFFLINE</c> in
	/// <c>cpylw</c> on <see cref="Es1WeaponGauge.DarkPlate"/>, and the box <see cref="DestroyedFrame"/>.
	/// </summary>
	public override void Paint() {
		var cockpit = Gauge.Cockpit;
		if (!cockpit.RectCornerVisible(Rect)) {
			return;
		}

		PaintUnderlay();
		var canvas = cockpit.PushCanvasContext();
		var state = _row.State;
		DynamixBitmap? box;
		HudFont? font;
		byte plate;
		string? text;
		if (!state.Destroyed) {
			box = !state.Armed && !state.InChain ? null : Frame(state.Ready ? ReadyFrame : NotReadyFrame);
			(font, plate) = state.Armed
				? (Es1WeaponGauge.Font(_fonts, Es1CockpitViews.CpYellow), ArmedPlate)
				: (Es1WeaponGauge.Font(_fonts, Es1CockpitViews.CpGreen), Es1WeaponGauge.DarkPlate);
			text = _row.Name;
		} else {
			font = Es1WeaponGauge.Font(_fonts, Es1CockpitViews.CpYellow);
			box = Frame(DestroyedFrame);
			Rect = Rect with { X1 = _row.Rect.X1, Y1 = _row.Rect.Y1 };
			NameLabel.SetRect(Es1Label.Centered, Rect);
			plate = Es1WeaponGauge.DarkPlate;
			text = _offline;
		}

		NameLabel.FillColor = plate;
		NameLabel.Font = font;
		NameLabel.SetText(canvas, text);
		// Retail fills the box when it has no frame to blit; a frame a missing dba\pbuttons leaves out fills it too.
		if (box == null) {
			canvas.FillRect(Box.X, Box.Y, Box.X + BoxWidth, Box.Y + BoxHeight, Es1WeaponGauge.DarkPlate);
		} else {
			canvas.Blit(box, Box.X, Box.Y);
		}
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>ChainedWeaponSelectGadget_FlushDeferredPaint</c> (<c>0003861c</c>): at a paint counter of 2
	/// the digit and the paint, at 1 the rect and <see cref="SecondRect"/> copied from the page on the screen to the page
	/// drawn, each lowering it by 1.
	/// </summary>
	public override void Tick() {
		if (PaintCounter == 2) {
			PaintUnderlay();
			Paint();
			PaintCounter--;
		} else if (PaintCounter == 1) {
			Gauge.Cockpit.DrawToCockpit(Rect, 0);
			Gauge.Cockpit.DrawToCockpit(SecondRect, 0);
			PaintCounter--;
		}
	}
}
