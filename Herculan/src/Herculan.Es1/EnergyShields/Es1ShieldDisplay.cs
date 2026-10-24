using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.EnergyShields;

/// <summary>
/// The shield display's 15-byte state block at its <c>+0x7e</c>, which <c>Shield_BalanceInputRead</c> exchanges with the
/// machine's shields (docs/es1/simulation/shields-and-behaviour-states.md#the-shields).
/// </summary>
/// <param name="FrontReading"><c>+0</c>, the front charge as (charge &lt;&lt; 10) / capacity.</param>
/// <param name="RearReading"><c>+4</c>, the rear's.</param>
/// <param name="Balance"><c>+8</c>, the balance, Q10, <c>0x200</c> even.</param>
/// <param name="Byte0C"><c>+0xc</c>, copied whole; what it holds is open (docs/es1/simulation/cockpit-energy-shields.md#open).</param>
/// <param name="FrontPressed"><c>+0xd</c>, set by a click on the front button.</param>
/// <param name="RearPressed"><c>+0xe</c>, set by a click on the rear button.</param>
public record struct Es1ShieldState(int FrontReading, int RearReading, int Balance, byte Byte0C, bool FrontPressed, bool RearPressed);

/// <summary>
/// The shield display, the cockpit's instrument <c>+0x1e5</c>, <c>ShieldsGauge</c>
/// (docs/es1/simulation/cockpit-energy-shields.md#the-shield-display): the front and rear buttons <c>+0x14</c> and
/// <c>+0x18</c>, the balance's two readouts <c>+0x8d</c> and <c>+0x91</c>, the state block <c>+0x7e</c>, and the canopy's
/// rings, lit by rewriting palette entries 80 to 85. Its slots are <c>ShieldsGaugeVtable</c> (<c>00093e12</c>)'s.
/// </summary>
public sealed class Es1ShieldDisplay {
	/// <summary>The facings, front and rear (<c>+0x64</c>).</summary>
	public const int FacingCount = 2;

	/// <summary>The first of <c>pbuttons</c>' three frames for each facing: 58 for the front, 61 for the rear.</summary>
	public const int FirstFrame = 58;

	/// <summary>The readouts' fill color.</summary>
	public const byte ReadoutFill = 0x3f;

	/// <summary>The first palette entry the rings take, and how many: 80 to 85, the front's three first.</summary>
	public const int FirstRingEntry = 0x50, RingEntries = 6;

	/// <summary>
	/// <c>ShieldRingColors</c> (<c>00093e04</c>): the rings' base color, red, green and blue in the palette's six-bit
	/// channels. <c>ShieldsGauge_Ctor</c> also stores it as a palette entry at <c>ShieldRingBaseEntry</c>
	/// (<c>000b13ec</c>), which nothing found reads (docs/es1/simulation/cockpit-energy-shields.md#open), so it is not kept.
	/// </summary>
	public static readonly (int R, int G, int B) RingBase = (25, 59, 23);

	private readonly Es1ShieldButton[] _facings;

	/// <summary>
	/// <c>ShieldsGauge_Ctor</c> (<c>ES1DBSIM 00038f34</c>)(0, <paramref name="cockpit"/>, <paramref name="block"/>): the
	/// gauge base's rect, the block's first 16 bytes; then, with (<i>x</i>, <i>y</i>) the block's first pair, for the front
	/// and then the rear: the button on its rect moved by (<i>x</i>, <i>y</i>), registered on the cockpit's root
	/// (<c>Widget_RegisterClickable</c>), with <c>pbuttons</c>' frames <see cref="FirstFrame"/> + 3<i>i</i> to
	/// <see cref="FirstFrame"/> + 3<i>i</i> + 2; and the readout, a label in <c>cpgreen</c> centered on its rect moved by
	/// (<i>x</i>, <i>y</i>) with the fill <see cref="ReadoutFill"/>. Then the state block cleared and the rings installed
	/// (<see cref="UpdateRingPalette"/>).
	/// </summary>
	/// <param name="fonts"><c>ColorSchemePanels</c>.</param>
	/// <param name="pushButtons"><c>CockpitPButtonsBank</c>, the frames of <c>dba\pbuttons</c>; no picture is blitted when it or a frame is missing.</param>
	internal Es1ShieldDisplay(Es1Cockpit cockpit, Es1GauShieldDisplayBlock block, IReadOnlyList<HudFont?> fonts,
			IReadOnlyList<DynamixBitmap>? pushButtons) {
		Cockpit = cockpit;
		Rect = Es1Rect.From(block.Rect);
		int x = block.Rect.X0, y = block.Rect.Y0;
		var font = Es1CockpitViews.CpGreen < fonts.Count ? fonts[Es1CockpitViews.CpGreen] : null;
		_facings = new Es1ShieldButton[FacingCount];
		var readouts = new Es1Label[FacingCount];
		for (int i = 0; i < FacingCount; i++) {
			var buttonRect = Es1Rect.From(i == 0 ? block.FrontButton : block.RearButton).Offset(x, y);
			int first = FirstFrame + 3 * i;
			_facings[i] = new Es1ShieldButton(this, buttonRect, [Frame(first), Frame(first + 1), Frame(first + 2)]);
			cockpit.Root.RegisterClickable(_facings[i]);
			readouts[i] = new Es1Label(font) { FillColor = ReadoutFill };
			readouts[i].SetRect(Es1Label.Centered, Es1Rect.From(i == 0 ? block.FrontReadout : block.RearReadout).Offset(x, y));
		}

		FrontReadout = readouts[0];
		RearReadout = readouts[1];
		UpdateRingPalette();

		DynamixBitmap? Frame(int index) => pushButtons != null && index < pushButtons.Count ? pushButtons[index] : null;
	}

	/// <summary><c>+0x10</c>, the cockpit the display draws through.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary><c>+0</c>–<c>+0xc</c>, the gauge's rect, whose corners the update tests; (0, 0, 0, 0) in five retail files.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x14</c>, the front button, the first child.</summary>
	public Es1ShieldButton FrontButton => _facings[0];

	/// <summary><c>+0x18</c>, the rear button.</summary>
	public Es1ShieldButton RearButton => _facings[1];

	/// <summary><c>+0x8d</c>, the front readout.</summary>
	public Es1Label FrontReadout { get; }

	/// <summary><c>+0x91</c>, the rear readout.</summary>
	public Es1Label RearReadout { get; }

	/// <summary><c>+0x7e</c>–<c>+0x8c</c>, the state block, all 0 from the constructor.</summary>
	public Es1ShieldState State { get; private set; }

	/// <summary><c>+0x75</c>, the paint count <see cref="SetStateBlock"/> sets to 2.</summary>
	public byte PaintCount { get; private set; }

	/// <summary>
	/// <c>Palette_InstallRange</c> (<c>00027e98</c>)(entries, count, first), through which the rings' colors reach the
	/// live palette: the first entry and retail's palette dwords, red in the low byte, then green, blue and a flag byte of
	/// 1. Null drops them, as before the host wires the monitor; the cockpit is built before it is, so the constructor's
	/// install is dropped and the first view's paint makes it.
	/// </summary>
	public Action<int, IReadOnlyList<uint>>? InstallPaletteRange { get; set; }

	/// <summary><c>ShieldsGauge_GetStateBlock</c> (<c>00039334</c>)(gauge): a copy of the state block.</summary>
	public Es1ShieldState GetStateBlock() => State;

	/// <summary>
	/// <c>ShieldsGauge_SetStateBlock</c> (<c>000392d0</c>)(gauge, <paramref name="block"/>): the block copied over the
	/// state block, and the paint count set to 2 when the readings or the balance changed.
	/// </summary>
	public void SetStateBlock(Es1ShieldState block) {
		bool changed = block.FrontReading != State.FrontReading || block.RearReading != State.RearReading
			|| block.Balance != State.Balance;
		State = block;
		if (changed) {
			PaintCount = 2;
		}
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>ShieldsGauge_OnClick</c> (<c>00039254</c>)(gauge, <paramref name="value"/>,
	/// <paramref name="child"/>), which a button's click calls: for the front button the front press flag set and the rear
	/// cleared, for the rear the reverse, the value not tested; for another child nothing.
	/// </summary>
	public void OnChildClick(short value, Es1Widget child) {
		_ = value;
		// PLACEHOLDER: Shield_BalanceInputRead (0000fbb4), plan step 7's shields, is what takes the flags and moves the
		// balance; until it is ported a click is stored and nothing reads it.
		if (ReferenceEquals(child, _facings[0])) {
			State = State with { FrontPressed = true, RearPressed = false };
		} else if (ReferenceEquals(child, _facings[1])) {
			State = State with { RearPressed = true, FrontPressed = false };
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>ShieldsGauge_Paint</c> (<c>00039178</c>), which <c>CockpitView_InvalidateWidgets</c> runs on a
	/// view change: the rings (<see cref="UpdateRingPalette"/>), the readouts (<see cref="UpdateReadouts"/>), and the
	/// front and then the rear button's paint.
	/// </summary>
	public void Paint() {
		UpdateRingPalette();
		UpdateReadouts();
		_facings[0].Paint();
		_facings[1].Paint();
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>ShieldsGauge_Update</c> (<c>000391ac</c>), which <c>Cockpit_DrawFrame</c> runs once a frame:
	/// when <c>CockpitView_RectCornerVisible</c> passes the gauge's rect, at a paint count of 2 both buttons' flush
	/// (<see cref="Es1ShieldButton.Tick"/>), the rings and the readouts; at 1 both buttons' flush and each readout's rect
	/// copied from the page on the screen to the page drawn (<see cref="Es1Cockpit.DrawToCockpit"/> with 0); each lowering
	/// the count by 1. So a button's press shows only in a frame after the readings or the balance changed.
	/// </summary>
	public void Update() {
		if (!Cockpit.RectCornerVisible(Rect)) {
			return;
		}

		if (PaintCount == 2) {
			_facings[0].Tick();
			_facings[1].Tick();
			UpdateRingPalette();
			UpdateReadouts();
			PaintCount--;
		} else if (PaintCount == 1) {
			_facings[0].Tick();
			_facings[1].Tick();
			Cockpit.DrawToCockpit(FrontReadout.Rect, 0);
			Cockpit.DrawToCockpit(RearReadout.Rect, 0);
			PaintCount--;
		}
	}

	/// <summary>Slot <c>+0x10</c>, <c>PanelGauge_KeyDispatchNoneShieldsGaugeCopy</c> (<c>0003916f</c>)(gauge, code): 0, taking no key.</summary>
	public static short KeyDispatch(ushort code) {
		_ = code;
		return 0;
	}

	/// <summary>
	/// <c>ShieldsGauge_UpdateRingPalette</c> (<c>00039340</c>)(gauge)
	/// (docs/es1/simulation/cockpit-energy-shields.md#the-ring-colors--shieldsgauge_updateringpalette-00039340): for the
	/// front and then the rear reading <i>v</i>, <i>f</i> = (<i>v</i> · 100) &gt;&gt; 9, and three entries of
	/// <see cref="RingBase"/> less a dimming <i>d</i>, each channel at least 0 and kept as a byte: the first
	/// <i>d</i> 0 above 125, 125 − <i>f</i> from 25 and 50 below; the second 0 above 150, (150 − <i>f</i>) &gt;&gt; 1 from
	/// 50 and 50 below; the third (200 − <i>f</i>) &gt;&gt; 2. The six installed from <see cref="FirstRingEntry"/>.
	/// </summary>
	public void UpdateRingPalette() {
		var entries = new uint[RingEntries];
		Rings(State.FrontReading, entries, 0);
		Rings(State.RearReading, entries, 3);
		InstallPaletteRange?.Invoke(FirstRingEntry, entries);

		static void Rings(int reading, uint[] entries, int at) {
			int f = reading * 100 >> 9;
			entries[at] = Ring(f > 125 ? 0 : f < 25 ? 50 : 125 - f);
			entries[at + 1] = Ring(f > 150 ? 0 : f < 50 ? 50 : (150 - f) >> 1);
			entries[at + 2] = Ring((200 - f) >> 2);
		}

		static uint Ring(int d) =>
			(uint)Channel(RingBase.R - d) | (uint)Channel(RingBase.G - d) << 8 | (uint)Channel(RingBase.B - d) << 16 | 0x01000000u;

		static byte Channel(int value) => (byte)Math.Max(value, 0);
	}

	/// <summary>
	/// <c>ShieldsGauge_UpdateReadouts</c> (<c>00039628</c>)(gauge): when <c>CockpitView_RectCornerVisible</c> passes the
	/// front readout's rect, <i>n</i> = (the balance · 200) &gt;&gt; 10 written in base 10 on the front readout and 200 −
	/// <i>n</i> on the rear, each through <see cref="Es1Label.SetText"/>, which fills its rect first.
	/// </summary>
	public void UpdateReadouts() {
		if (!Cockpit.RectCornerVisible(FrontReadout.Rect)) {
			return;
		}

		int n = State.Balance * 200 >> 10;
		var canvas = Cockpit.PushCanvasContext();
		FrontReadout.SetText(canvas, n.ToString(System.Globalization.CultureInfo.InvariantCulture));
		RearReadout.SetText(canvas, (200 - n).ToString(System.Globalization.CultureInfo.InvariantCulture));
	}
}
