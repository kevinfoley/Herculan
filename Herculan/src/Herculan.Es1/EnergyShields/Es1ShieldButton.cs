using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.View;

namespace Herculan.Es1.EnergyShields;

/// <summary>
/// A shield display's facing, the button that moves the balance toward its facing
/// (docs/es1/simulation/cockpit-energy-shields.md#the-facings--shieldsgauge_facingctor-000396ac): a picture by its state
/// from <c>dba\pbuttons</c>, whose click goes to the display. Its slots are <c>ShieldsSelectGadgetVtable</c>
/// (<c>00093eae</c>)'s; the value, set-value and drag slots are the button base's.
/// </summary>
public sealed class Es1ShieldButton : Es1ButtonWidget {
	/// <summary>
	/// <c>ShieldsGauge_FacingCtor</c> (<c>ES1DBSIM 000396ac</c>)(0, <paramref name="display"/>, <paramref name="rect"/>):
	/// the widget on the rect, the drag flag 0, the display <c>+0x20</c> and the state 0; and the three pictures
	/// <c>ShieldsGauge_Ctor</c> gives it at <c>+0x30</c>, <c>+0x34</c> and <c>+0x38</c>.
	/// </summary>
	/// <param name="pictures">The pictures for the states 0, 1 and 2; a null entry blits nothing.</param>
	internal Es1ShieldButton(Es1ShieldDisplay display, Es1Rect rect, DynamixBitmap?[] pictures) {
		Display = display;
		Rect = rect;
		Pictures = pictures;
	}

	/// <summary><c>+0x20</c>, the display the click goes to.</summary>
	public Es1ShieldDisplay Display { get; }

	/// <summary><c>+0x30</c>–<c>+0x38</c>, the pictures for the states up, pressed and 2.</summary>
	public IReadOnlyList<DynamixBitmap?> Pictures { get; }

	/// <summary><c>+0x1b</c>, the paint count the invalidate sets to 2.</summary>
	public byte PaintCount { get; private set; }

	/// <summary>
	/// Slot <c>+4</c>, <c>ShieldFacing_Paint</c> (<c>00039784</c>): when <c>CockpitView_RectCornerVisible</c> passes the
	/// rect, the picture for the state blitted at its top-left.
	/// </summary>
	public override void Paint() {
		var cockpit = Display.Cockpit;
		if (!cockpit.RectCornerVisible(Rect)) {
			return;
		}

		// The state indexes the three pictures with no bound; a state past them, which nothing sets, blits nothing here.
		var picture = State >= 0 && State < Pictures.Count ? Pictures[State] : null;
		cockpit.PushCanvasContext().Blit(picture, Rect.X0, Rect.Y0);
	}

	/// <summary>Slot <c>+8</c>, <c>Widget_InvalidateDeferredShieldsGaugeCopy</c> (<c>0003975c</c>): the paint count set to 2.</summary>
	public override void Invalidate() => PaintCount = 2;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>Widget_ForwardClickToOwner</c> (<c>000314ac</c>)(button, <paramref name="value"/>): with bit 0
	/// of the value set, the left button, the display's <see cref="Es1ShieldDisplay.OnChildClick"/>, then the button
	/// invalidated and the mixin's click sound.
	/// </summary>
	public override void OnClick(short value) {
		if ((value & 1) == 0) {
			return;
		}

		Display.OnChildClick(value, this);
		Invalidate();
		// Widget_ClickSound (000314a0).
		Display.Cockpit.Globals.Sound.Play(Sound.Es1Sound.ClickRow);
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>ShieldFacing_FlushDeferredPaint</c> (<c>000397b8</c>), which only the display's update calls:
	/// at a paint count of 2 the paint, at 1 the rect copied from the page on the screen to the page drawn
	/// (<see cref="Es1Cockpit.DrawToCockpit"/> with 0), each lowering it by 1.
	/// </summary>
	public override void Tick() {
		if (PaintCount == 2) {
			Paint();
			PaintCount--;
		} else if (PaintCount == 1) {
			Display.Cockpit.DrawToCockpit(Rect, 0);
			PaintCount--;
		}
	}
}
