using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Mfd;

/// <summary>
/// One of the MFD's 13 buttons, <c>MFDSelectGadget</c> (docs/es1/simulation/mfd-display.md#the-buttons): a picture
/// from <c>dba\pbuttons</c> by its state, with a caption on buttons 7 to 10, or, for button 6, the title, the screen's
/// name; a click goes to the display. A button the screen does not use is in <see cref="Es1Widget.Hidden"/>, drawn
/// with its third picture and passed over by the mouse and the keys. Its slots are <c>MFDSelectGadgetVtable</c>
/// (<c>00094176</c>)'s; the value, set-value and drag slots are the button base's.
/// </summary>
public sealed class Es1MfdButton : Es1ButtonWidget {
	/// <summary>The title's index.</summary>
	public const int TitleButton = 6;

	/// <summary>The first and last index whose caption is written, <c>MODE</c> to <c>XMIT</c>.</summary>
	public const int FirstCaptioned = 7, LastCaptioned = 10;

	private readonly IReadOnlyList<HudFont?> _fonts;

	/// <summary>
	/// <c>MFDSelectGadget_Ctor</c> (<c>ES1DBSIM 0003ae50</c>)(0, <paramref name="index"/>, <paramref name="display"/>,
	/// <paramref name="rect"/>): the widget on the rect, the drag flag 0, the display <c>+0x20</c>, the index
	/// <c>+0x28</c> and the state 0; with the pictures and the caption label <c>MfdDisplay_Ctor</c> gives it after.
	/// </summary>
	/// <param name="pictures">The pictures <c>+0x30</c>–<c>+0x38</c> for the states 0, 1 and 2; a null entry blits nothing.</param>
	/// <param name="caption">The caption label <c>+0x2c</c>, or none.</param>
	/// <param name="fonts"><c>ColorSchemePanels</c>, whose <c>cpon</c>, <c>cppress</c> and <c>cpoff</c> the caption takes by state.</param>
	internal Es1MfdButton(int index, Es1MfdDisplay display, Es1Rect rect, DynamixBitmap?[] pictures, Es1Label? caption,
			IReadOnlyList<HudFont?> fonts) {
		Index = index;
		Display = display;
		Rect = rect;
		Pictures = pictures;
		Caption = caption;
		_fonts = fonts;
	}

	/// <summary><c>+0x20</c>, the display the click goes to.</summary>
	public Es1MfdDisplay Display { get; }

	/// <summary><c>+0x28</c>, the index, 0 to 12.</summary>
	public int Index { get; }

	/// <summary><c>+0x2c</c>, the caption label: built for buttons 0 to 5 and 7 to 10 and never written on 0 to 5; the title for button 6; none on 11 and 12.</summary>
	public Es1Label? Caption { get; }

	/// <summary><c>+0x30</c>–<c>+0x38</c>, the pictures for the states up, pressed and <see cref="Es1Widget.Hidden"/>.</summary>
	public IReadOnlyList<DynamixBitmap?> Pictures { get; }

	/// <summary><c>+0x1b</c>, the paint counter the invalidate and <see cref="Es1MfdDisplay.SetMode"/> set to 2.</summary>
	public byte PaintCounter { get; set; }

	/// <summary>
	/// Slot <c>+4</c>, <c>MFDSelectGadget_Paint</c> (<c>0003af24</c>), with no visibility test: for the title, group 5's
	/// entry for the display's mode written on its label; for any other, the picture for its state blitted at its
	/// top-left, then, for <see cref="FirstCaptioned"/> to <see cref="LastCaptioned"/>, the caption's font set to
	/// <c>ColorSchemePanels</c>[4 + state] and group 5's entry for its index written.
	/// </summary>
	public override void Paint() {
		var canvas = Display.Cockpit.PushCanvasContext();
		if (Index == TitleButton) {
			Caption?.SetText(canvas, Display.Caption(Display.Mode));
			return;
		}

		// The state indexes the three pictures with no bound; a state past them, which nothing sets, blits nothing here.
		canvas.Blit(State >= 0 && State < Pictures.Count ? Pictures[State] : null, Rect.X0, Rect.Y0);
		if (Index is >= FirstCaptioned and <= LastCaptioned && Caption != null) {
			int font = Es1CockpitViews.CpOn + State;
			Caption.Font = font >= 0 && font < _fonts.Count ? _fonts[font] : null;
			Caption.SetText(canvas, Display.Caption(Index));
		}
	}

	/// <summary>Slot <c>+8</c>, <c>Widget_InvalidateDeferredMfdDisplayCopy</c> (<c>0003aefc</c>): the paint counter set to 2.</summary>
	public override void Invalidate() => PaintCounter = 2;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>Widget_ForwardClickToOwner</c> (<c>000314ac</c>)(button, <paramref name="value"/>): with bit 0
	/// of the value set, the left button, the display's <see cref="Es1MfdDisplay.OnChildClick"/>, then the button
	/// invalidated and the mixin's click sound.
	/// </summary>
	public override void OnClick(short value) {
		if ((value & 1) == 0) {
			return;
		}

		Display.OnChildClick(this);
		Invalidate();
		// PLACEHOLDER: Widget_ClickSound, Sound_Play(0x11); no plan step ports ES1's sound yet.
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>ShieldFacing_FlushDeferredPaintMfdDisplayCopy</c> (<c>0003afa8</c>), which the display's
	/// update calls: at a paint counter of 2 the paint, at 1 the rect copied from the page on the screen to the page
	/// drawn (<see cref="Es1Cockpit.DrawToCockpit"/> with 0), each lowering it by 1.
	/// </summary>
	public override void Tick() {
		if (PaintCounter == 2) {
			Paint();
			PaintCounter--;
		} else if (PaintCounter == 1) {
			Display.Cockpit.DrawToCockpit(Rect, 0);
			PaintCounter--;
		}
	}
}

/// <summary>
/// The MFD's click surface over its screen, <c>MFDListGadget</c> (docs/es1/simulation/mfd-display.md#the-click-surface):
/// it draws nothing and passes a left click to FLASH COMM's row pick. Registered after the buttons, so a button over the
/// screen takes the point. Its slots are <c>MFDListGadgetVtable</c> (<c>000941de</c>)'s.
/// </summary>
public sealed class Es1MfdClickSurface : Es1ButtonWidget {
	/// <summary><c>MFDListGadget_Ctor</c> (<c>0003afe0</c>)(0, <paramref name="display"/>, <paramref name="rect"/>): the widget on the rect, the drag flag 0, the display <c>+0x20</c> and the state 0.</summary>
	internal Es1MfdClickSurface(Es1MfdDisplay display, Es1Rect rect) {
		Display = display;
		Rect = rect;
	}

	/// <summary><c>+0x20</c>, the display.</summary>
	public Es1MfdDisplay Display { get; }

	/// <summary>
	/// Slot <c>+0xc</c>, <c>MFDListGadget_OnClick</c> (<c>0003b088</c>)(gadget, <paramref name="value"/>): with bit 0 set,
	/// <c>MfdFlashComm_HandleListClick</c>(the display, the pointer's canvas point, the gadget), with no sound.
	/// </summary>
	public override void OnClick(short value) {
		if ((value & 1) == 0) {
			return;
		}

		Display.HandleListClick(Display.Cockpit.MouseCanvasPoint, this);
	}
}
