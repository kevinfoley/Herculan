using Herculan.Engine.Content;
using System.Buffers.Binary;
using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.File;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// Which of the Heads-Down Display's two screens is showing. The order is DBSIM's own page numbering,
/// which is also the key order: <c>HddDisplay_SetPage</c> (<c>0044a5e4</c>) takes this value directly, and buttons 0 and 1 of
/// the display's own two-button column are captioned "F7" and "F8" from a shared <c>"Fx"</c> literal
/// whose second character is <c>'7' + index</c>.
/// </summary>
public enum HddPage {
	/// <summary>[F7] — the real-time tactical map, the squad comm boxes, and the order list you
	/// transmit from.</summary>
	CommandDisplay = 0,

	/// <summary>[F8] — a component-by-component damage analysis of one herc.</summary>
	DamageDetail = 1,
}

/// <summary>
/// Which category of components <see cref="HddPage.DamageDetail"/> is listing. <c>HddDamageScreen_SetView</c> (<c>00450b60</c>)
/// takes this value and sets the row count from it; the manual binds [S], [I] and [W] to it and puts
/// the same three under the up/down arrow buttons.
/// </summary>
public enum HddDamageView {
	/// <summary>[S] — armour by body section, 19 rows.</summary>
	Structural = 0,

	/// <summary>[I] — internal systems, 12 rows.</summary>
	Internal = 1,

	/// <summary>[W] — the fitted weapons, one row each.</summary>
	Weapons = 2,
}

/// <summary>
/// The Heads-Down Display's geometry, decoded from <c>HddDisplay_Ctor</c> (<c>00448cc8</c>, the <c>HDDisplay</c>
/// constructor at <c>.GAU</c> offset 1212) and the two page constructors it builds —
/// <c>HddCommandScreen_Ctor</c> (<c>0044c264</c>) for the command display and <c>HddDamageScreen_Ctor</c> (<c>0045079c</c>) for the damage detail. See
/// docs/retail/simulation/cockpit-views.md for the pan that reaches this view and
/// <see cref="MfdLayout"/> for the closest precedent.
/// </summary>
/// <remarks>
/// <para><b>Where the numbers live.</b> Unlike the MFD, whose per-herc <c>.GAU</c> supplies one panel
/// rect and nothing else, the whole HDD is authored per herc: the block at <c>.GAU</c> offset 1212
/// carries an origin, four region rects, fifteen widget rects, three marker rects and two mode
/// values. Only the sub-layouts <i>inside</i> those regions — the map's inset, the nine order rows,
/// the thirteen damage rows — are hardcoded in DBSIM. That is why the retail hercs differ so much
/// here: TOMAHAWK puts its comm boxes above the map, OUTLAW and RAZOR stack their two page buttons
/// vertically, and APOCA, COLOSSUS and MAVERICK put the whole button strip below the map instead of
/// above it.</para>
///
/// <para><b>Coordinates are device pixels relative to the <c>.HB1</c> art's own top-left.</b> The
/// file's values are authored in the 320-wide space, and <c>HddGau_ApplyCoordShift</c> (<c>0044bed0</c>) shifts every one of them
/// by <c>VideoMode_X/YCoordShift</c> before the constructor adds the block's origin — which it first
/// biases by <c>+0x28</c> on the y axis (<c>param_1[1] = param_1[1] + 0x28</c>, the function's first
/// statement). Every retail file authors that origin as <c>(0, 197)</c>, so the bias makes it
/// <c>(0, 237)</c>: exactly the canvas origin the herc's own <c>.VUE</c> gives view 1, i.e. the row
/// the <c>.HB1</c> art is blitted at. Subtracting that origin back off is what turns a canvas
/// coordinate into an art-local one, and it is the check that confirms the whole block — a stray
/// <c>+0x28</c> reading as anything else would not land the art exactly.</para>
///
/// <para><b>What confirms the widget mapping.</b> Every widget that has art of its own was checked
/// against that art's own frame size in <c>hba\HDD.HBA</c>, across all nine retail <c>.GAU</c> files
/// — the same method the MFD's button table passed. Six of the ten sizes match to the pixel in every
/// file: both arrow sets and both magnifiers, 54 exact matches. The two that do not are the two the
/// original itself does not match: the page buttons' plate is two rows taller than their rect, the
/// same overhang the weapon-row plates have, and XMIT and CANCEL have a 54x20 plate inside a 70x18
/// click rect that overlaps its neighbour by two authored units — which is why their captions
/// centre on <see cref="TransmitCaptionBox"/> rather than on the widget. <see cref="Screen"/> is 459x201 device
/// pixels in all nine files with only its position varying, exactly as the MFD's panel rect is.</para>
///
/// <para><b>What this type is and is not.</b> It is the geometry and the string-table indices, both
/// of which are entirely in the data files. Everything that moves — the map camera and its markers,
/// which pilot and order are selected, the comm boxes' text — is <see cref="HddCommandState"/>, and
/// the actions behind them are <see cref="HddCommandScreen"/>.</para>
/// </remarks>
public sealed class HddLayout {
	/// <summary>Sprite bank the display's buttons are drawn from — <c>HddDisplay_Ctor</c> (<c>00448cc8</c>)'s second load.</summary>
	public const string Bank = "HDD";

	/// <summary>
	/// The <c>.GAU</c> byte offset the <c>HDDisplay</c> constructor is handed, i.e. the block's start.
	/// <c>Gau_PilotRosterWidget</c> (<c>00432634</c>) passes <c>param_2 + 0x4bc</c>.
	/// </summary>
	public const int GauBlockOffset = 1212;

	/// <summary>
	/// Added to the block origin's y before anything else, by <c>HddGau_ApplyCoordShift</c> (<c>0044bed0</c>). See the remarks on
	/// this class for why 197 + 40 landing on the <c>.VUE</c>'s own 237 is the confirmation.
	/// </summary>
	public const int OriginYBias = 0x28;

	/// <summary>Widgets the constructor builds, in its own index order.</summary>
	public const int WidgetCount = 15;

	/// <summary>Squad comm boxes, and therefore squadmates the display can address.</summary>
	public const int PilotSlotCount = 3;

	/// <summary>Orders the command display lists — <c>HddCommandScreen_Ctor</c> (<c>0044c264</c>)'s loop bound.</summary>
	public const int OrderCount = 8;

	/// <summary>
	/// Rows the order column is divided into: the eight orders plus a message row above them. The
	/// constructor divides the column's height by 9 and places the message label in the first slot
	/// before stepping down for the orders.
	/// </summary>
	public const int OrderRowSlots = OrderCount + 1;

	/// <summary>
	/// Structural components a walker has — group 13's own count, and the number of readings
	/// <c>HddGauge_ConditionIndex</c> averages into a comm box's condition line.
	/// </summary>
	public const int StructuralRowCount = 19;

	/// <summary>Damage rows the screen has labels for — <c>HddDamageScreen_Ctor</c>'s loop bound, and
	/// the most a view ever labels.</summary>
	public const int DamageRowCount = 13;

	/// <summary>
	/// <c>STRINGS0.STR</c> group 0 index of the first command-display order. <c>HddCommandScreen_RefreshOrders</c> (<c>0044ddec</c>)
	/// refreshes row <c>i</c> from <c>DAT_004d132c[i + 10]</c> — the group's first six entries are the
	/// MFD's FLASH COMM page, and 10-17 are these eight. Each entry's single attribute byte is the
	/// index of its hotkey character within its own text, which is what draws the D of DISENGAGE, the
	/// F of DEFEND POSITION and the C of SCAN FOR HOSTILES in a different colour.
	/// </summary>
	public const int FirstCommandOrder = 10;

	/// <summary>
	/// The group those orders live in — the same 18-entry table the MFD's FLASH COMM page reads its
	/// own first six from.
	/// </summary>
	public const int OrderGroup = MfdLayout.OrderGroup;

	/// <summary>Group holding "XMIT", "CANCEL", "EXIT" — the two transmit buttons' captions.</summary>
	public const int ButtonCaptionGroup = 9;

	/// <summary>Group holding "MAP" and "DAMAGE" — <c>HddDisplay_SetTitle</c> (<c>0044a6dc</c>)'s page-0 title.</summary>
	public const int PageTitleGroup = 11;

	/// <summary>
	/// Group holding " STRUCT DAMAGE", " INTERN DAMAGE", " WEAPON DAMAGE" — the damage screen's title,
	/// indexed by <see cref="HddDamageView"/> rather than by page.
	/// </summary>
	public const int DamageTitleGroup = 12;

	/// <summary>Group of 19 structural component names for a walker.</summary>
	public const int StructuralComponentGroup = 13;

	/// <summary>
	/// Its flyer counterpart, chosen by the subject type's own flyer flag — "L NACELLE ARMOR" where
	/// the walker table says "L SHOULDER". The RAZOR is the one retail chassis that takes it.
	/// </summary>
	public const int StructuralFlyerComponentGroup = 14;

	/// <summary>Group of 12 internal system names for a walker.</summary>
	public const int InternalComponentGroup = 15;

	/// <inheritdoc cref="StructuralFlyerComponentGroup"/>
	public const int InternalFlyerComponentGroup = 16;

	/// <summary>
	/// The value column's reserved width is the measured width of this literal (<c>DAT_0049da9d</c>),
	/// which is also what an undamaged component reads.
	/// </summary>
	public const string DamageValueReservation = "100";

	/// <summary>Fonts, all <c>ColorSchemePanels</c> entries — in this format the font is the colour.</summary>
	public const string OrderFont = "CPGREEN";

	/// <summary>The alternate the hotkey character is drawn in — <c>ColorSchemePanels[2]</c>.</summary>
	public const string OrderHotkeyFont = "CPRED";

	/// <summary>Damage rows use the same green as the orders; <c>HddDamageScreen_Ctor</c> (<c>0045079c</c>) builds every one of
	/// its 26 labels with <c>ColorSchemePanels[1]</c>.</summary>
	public const string DamageRowFont = "CPGREEN";

	/// <summary>Unlit caption font for XMIT and CANCEL — <c>ColorSchemePanels[4]</c>.</summary>
	public const string TransmitButtonFont = "CPON";

	/// <summary>Lit caption font for the same pair — <c>ColorSchemePanels[5]</c>.</summary>
	public const string TransmitButtonLitFont = "CPPRESS";

	/// <summary>The page title's font — <c>ColorSchemePanels[10]</c>, the same white the MFD titles use.</summary>
	public const string TitleFont = "WHITE";

	/// <summary>
	/// A comm box's pilot name, condition and current order — <c>ColorSchemePanels[2]</c>, the same
	/// red the order list draws its hotkey characters in.
	/// </summary>
	public const string PilotNameFont = OrderHotkeyFont;

	/// <summary>Its two fixed captions, <c>STATUS:</c> and <c>OBJECTIVE:</c> — <c>ColorSchemePanels[0]</c>.</summary>
	public const string PilotCaptionFont = "CPBLUE";

	/// <summary>An unavailable order's font — also <c>[0]</c>, which is what greys the whole list out
	/// until a pilot is selected.</summary>
	public const string OrderUnavailableFont = PilotCaptionFont;

	/// <summary>The selected order's font — <c>ColorSchemePanels[3]</c>.</summary>
	public const string OrderSelectedFont = SubjectFont;

	/// <summary>Group holding those two captions.</summary>
	public const int PilotCaptionGroup = 33;

	/// <summary>
	/// Group holding the eight OBJECTIVE: lines — <c>ATTACK</c>, <c>TRAVEL</c>, <c>PATROL</c>,
	/// <c>FORM UP</c>, <c>GUARD</c>, <c>FLEE</c>, <c>DEAD</c>, <c>IMMOBILE</c>. These are what the
	/// pilot <i>is doing</i>, not what the order list offers, and the two lists do not correspond.
	/// </summary>
	public const int PilotOrderGroup = 40;

	/// <summary>Colour id the tick beside an unselected comm box takes — palette 102.</summary>
	public const int PilotMarkerColorId = 13;

	/// <summary>And the selected one — palette 13, the same yellow the title indicator uses.</summary>
	public const int PilotMarkerSelectedColorId = 15;

	/// <summary>
	/// Background the incoming-message row above the order list sits on — the constructor writes
	/// <c>DAT_004d3c1c</c> into that one label's own background field.
	/// </summary>
	public const int MessageRowColorId = 14;

	/// <summary>
	/// Colour id the bar marking the selected order row is filled with — the 2px-wide strip
	/// <c>HddCommandScreen_Repaint</c> draws at the order column's left edge.
	/// </summary>
	public const int SelectedOrderBarColorId = 15;

	/// <summary>That bar's width in device pixels — the repaint's own <c>x0 + 1 .. x0 + 3</c>.</summary>
	public const int SelectedOrderBarWidth = 2;

	/// <summary>
	/// <c>hba\HDD.HBA</c> frame of the 116x18 plate drawn behind the order the pointer has armed.
	/// <c>HddCommandScreen_DrawOrderHighlight</c> (<c>0044dd4c</c>) blits this plus the state it is handed, or
	/// frame 4 for an order that cannot be taken. <c>HddButton_Paint</c> (<c>0044bb38</c>) hands it XMIT's press
	/// byte through <c>HddCommandScreen_RedrawOrderHighlight</c> (<c>0044ddc8</c>), so the plate is frame 3
	/// while XMIT shows pressed; the order list's own refresh hands it 0.
	/// </summary>
	public const int OrderHighlightFrame = 2;

	/// <summary>The frame that same call substitutes for an unavailable order.</summary>
	public const int OrderHighlightUnavailableFrame = 4;

	/// <summary>
	/// The damage screen's subject caption font while the subject is the player —
	/// <c>ColorSchemePanels[3]</c>. See <see cref="HddDamageSubject.CaptionFont"/>.
	/// </summary>
	public const string SubjectFont = "CPYLW";

	/// <summary>
	/// The font the damage screen prints <see cref="HddDamageSubject.NoData"/> in — <c>ColorSchemePanels[2]</c>,
	/// on <see cref="HddDamageSubject.TargetPlateColorId"/>.
	/// </summary>
	public const string NoSubjectFont = PilotNameFont;

	/// <summary>A widget's index within the constructor's own fifteen, named by what it does.</summary>
	public enum Widget {
		/// <summary>Selects <see cref="HddPage.CommandDisplay"/>; captioned "F7".</summary>
		PageButton0 = 0,

		/// <summary>Selects <see cref="HddPage.DamageDetail"/>; captioned "F8".</summary>
		PageButton1 = 1,

		/// <summary>Scrolls the map up, or steps the damage view to the next category.</summary>
		ArrowUp = 2,

		/// <summary>Scrolls the map down, or steps the damage view to the previous category.</summary>
		ArrowDown = 3,

		/// <summary>Scrolls the map left, or selects the previous herc to inspect.</summary>
		ArrowLeft = 4,

		/// <summary>Scrolls the map right, or selects the next herc.</summary>
		ArrowRight = 5,

		/// <summary>Lower magnifier — zooms the map out. [-] does the same.</summary>
		ZoomOut = 6,

		/// <summary>Upper magnifier — zooms the map in. [+] does the same.</summary>
		ZoomIn = 7,

		/// <summary>A degenerate zero rect in every retail file, with no paint case of its own — the
		/// same dead slot the MFD's button 6 is.</summary>
		Unused = 8,

		/// <summary>Holds the title label's rect. It has no paint case either: the display draws the
		/// title itself, straight into this rect.</summary>
		TitleBox = 9,

		/// <summary>First squad comm box. Its rect is what the gauge behind it paints into.</summary>
		PilotBox0 = 10,

		/// <summary>Second squad comm box.</summary>
		PilotBox1 = 11,

		/// <summary>Third squad comm box.</summary>
		PilotBox2 = 12,

		/// <summary>Transmits the selected order to the selected pilot; captioned XMIT.</summary>
		Transmit = 13,

		/// <summary>Cancels it; captioned CANCEL.</summary>
		Cancel = 14,
	}

	/// <summary>An inclusive rect in device pixels, relative to the <c>.HB1</c> art's top-left.</summary>
	public readonly record struct Rect(int X0, int Y0, int X1, int Y1) {
		/// <summary>Inclusive width.</summary>
		public int Width => X1 - X0 + 1;

		/// <summary>Inclusive height.</summary>
		public int Height => Y1 - Y0 + 1;

		/// <summary>This rect inset by <paramref name="dx"/> and <paramref name="dy"/> on every edge.</summary>
		public Rect Inset(int dx, int dy) => new(X0 + dx, Y0 + dy, X1 - dx, Y1 - dy);
	}

	private readonly Rect[] _widgets;
	private readonly int _arrowFrameSet;

	private HddLayout(Rect screen, Rect orderColumn, Rect damageColumn, Rect indicator,
			Rect[] widgets, Rect[] pilotMarkers, int arrowFrameSet, int pilotHighlightMode) {
		Screen = screen;
		OrderColumn = orderColumn;
		DamageColumn = damageColumn;
		Indicator = indicator;
		_widgets = widgets;
		PilotMarkers = pilotMarkers;
		_arrowFrameSet = arrowFrameSet;
		PilotHighlightMode = pilotHighlightMode;
	}

	/// <summary>
	/// The screen area both pages draw into — the constructor's <c>+0xc1</c> rect, and the first
	/// argument it passes to each page constructor. Its paint floods this whole rect black before
	/// drawing anything (<c>HddCommandScreen_Repaint</c> (<c>0044c894</c>) with colour id 19, <c>HddDamageScreen_Update</c> (<c>00450c54</c>) with id 3; both
	/// resolve to palette 16).
	/// </summary>
	public Rect Screen { get; }

	/// <summary>
	/// The command display's order column — the constructor's <c>+0xe1</c> rect. Its left edge is also
	/// where the map viewport stops, which is what <see cref="MapViewport"/> uses.
	/// </summary>
	public Rect OrderColumn { get; }

	/// <summary>The damage detail's component list — the constructor's <c>+0xd1</c> rect.</summary>
	public Rect DamageColumn { get; }

	/// <summary>
	/// A small block beside the title that the display floods every frame — colour id 13 normally and
	/// id 15 (palette 13, yellow) while its <c>+0x51f</c> flag is set, which the constructor
	/// initialises to 1. Retail's is the yellow tab to the right of the MAP caption.
	/// </summary>
	public Rect Indicator { get; }

	/// <summary>
	/// The three marker rects at block offset <c>0x50</c> — a 2x4 device tick beside each comm box.
	/// The display copies them into the comm gauges, whose paint fills the selected pilot's yellow and
	/// repaints the previous one in colour id 13.
	/// </summary>
	public IReadOnlyList<Rect> PilotMarkers { get; }

	/// <summary>
	/// Block offset <c>0x5e</c> — which of two comm-box highlight behaviours the herc uses. Every
	/// retail file says 1, the branch that highlights the marker beside the box rather than the box
	/// itself; the 0 branch is never exercised by retail data.
	/// </summary>
	public int PilotHighlightMode { get; }

	/// <summary>Widget <paramref name="widget"/>'s rect.</summary>
	public Rect this[Widget widget] => _widgets[(int)widget];

	/// <summary>
	/// Whether <paramref name="widget"/> is latching — lit by being the chosen page and staying chosen
	/// — rather than momentary, lighting only while held.
	///
	/// <para>Unlike the MFD, which splits this across two classes, the Heads-Down Display's paint
	/// (<c>HddButton_Paint</c>, <c>0044bb38</c>) switches on the widget index inside one: cases 0-1, the two page buttons,
	/// take their frame from the object's selection flag <c>+0x40</c> and re-font their caption from
	/// it; cases 2-7, the arrows and the two magnifiers, take their frame from the shared press byte
	/// <c>+0x1b</c>. Cases 13-14, XMIT and CANCEL, are momentary too and additionally re-font from
	/// that same press byte.</para>
	/// </summary>
	public static bool IsLatching(Widget widget) =>
		widget is Widget.PageButton0 or Widget.PageButton1;

	/// <summary>
	/// Whether <paramref name="page"/> shows <paramref name="widget"/>, from the 2x15 byte table at
	/// <c>0049d24c</c> that <c>HddDisplay_SetPage</c> (<c>0044a5e4</c>) walks as <c>table[page][widget]</c>, setting a hidden
	/// widget's state to 2 — the value its paint (<c>HddButton_Paint</c>, <c>0044bb38</c>) refuses to draw at.
	///
	/// <para>Both rows hide the three comm boxes, which is not a contradiction: those widgets paint
	/// only the selection highlight, and <c>HddGauge_LoadPilotFrames</c> clears the state back to 0
	/// for each slot a squadmate actually occupies. The boxes themselves are drawn by the display, not
	/// by the widgets.</para>
	/// </summary>
	public static bool WidgetVisible(HddPage page, Widget widget) {
		int index = (int)widget;
		if (index < 0 || index >= WidgetCount) {
			return false;
		}

		return Visibility[(int)page, index] != 0;
	}

	private static readonly byte[,] Visibility = {
		// F7 F8 up dn  lt rt out in  --  ttl p0 p1 p2  XM CN
		{ 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1 },  // Command display
		{ 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 },  // Damage detail
	};

	/// <summary>
	/// The unlit frame <paramref name="widget"/> draws, or null when it has no art of its own. The lit
	/// frame is always the one after, which is the constructor's own <c>local_60</c> / <c>local_60+1</c>
	/// pairing throughout.
	///
	/// <para>The four arrow buttons come in two sets — frames 5-12 and 13-20 — selected by the block's
	/// own <c>0x5c</c> value. Set 0 is the wide-and-tall pair (58x18 for up/down, 18x58 for
	/// left/right); set 1 is four identical 22x28 plates, used by OUTLAW, RAZOR and TOMAHAWK. Every
	/// one of those eight <c>hba\HDD.HBA</c> frames matches its herc's own widget rect to the pixel at
	/// 2x, which is what confirms the mapping — the same check the MFD's button table passed.</para>
	/// </summary>
	public int? UnlitFrame(Widget widget) => widget switch {
		Widget.PageButton0 or Widget.PageButton1 => 25,
		Widget.ArrowUp or Widget.ArrowDown or Widget.ArrowLeft or Widget.ArrowRight =>
			_arrowFrameSet * 8 + 5 + ((int)widget - (int)Widget.ArrowUp) * 2,
		Widget.ZoomOut => 21,
		Widget.ZoomIn => 23,
		Widget.Transmit or Widget.Cancel => 0,
		_ => null,
	};

	/// <summary>
	/// The map's own viewport inside <see cref="Screen"/>: inset 8 device pixels on x and 4 on y, and
	/// stopping 8 short of <see cref="OrderColumn"/>'s left edge. <c>HddCommandScreen_Ctor</c> (<c>0044c264</c>)'s own
	/// <c>4 &lt;&lt; XCoordShift</c> and <c>2 &lt;&lt; YCoordShift</c> literals, with the right edge
	/// taken from the order column rather than from the screen.
	/// </summary>
	public Rect MapViewport => new(Screen.X0 + 8, Screen.Y0 + 4, OrderColumn.X0 - 8, Screen.Y1 - 4);

	/// <summary>
	/// Row <paramref name="index"/> of the order column, 0 being the message row above the eight
	/// orders. The constructor divides the column's height by <see cref="OrderRowSlots"/> for the
	/// pitch and gives each row a fixed 14 device-pixel height regardless.
	/// </summary>
	public Rect OrderRow(int index) {
		// The constructor's own (y1 - y0) / 9 — exclusive of the bottom edge, unlike Height.
		int pitch = (OrderColumn.Y1 - OrderColumn.Y0) / OrderRowSlots;
		int top = OrderColumn.Y0 + index * pitch;
		return new Rect(OrderColumn.X0, top, OrderColumn.X1, top + OrderRowHeight);
	}

	/// <summary>Each order row's height — <c>7 &lt;&lt; YCoordShift</c>.</summary>
	public const int OrderRowHeight = 14;

	/// <summary>
	/// Device pixels an order's text is indented inside its row. The constructor hands the label
	/// positioner a margin array whose first short is a bare <c>5</c> — unshifted, unlike the MFD's
	/// FLASH COMM rows, which pass <c>2 &lt;&lt; XCoordShift</c>.
	/// </summary>
	public const int OrderTextMargin = 5;

	/// <summary>
	/// Damage row <paramref name="index"/>'s full extent. Rows start at the column's top and step 14
	/// device pixels; each is 8 tall and starts 60 pixels in from the column's left edge, which is the
	/// constructor's <c>0x1e &lt;&lt; XCoordShift</c>.
	///
	/// <para>The row is really two labels, and the split needs font metrics: the constructor measures
	/// <see cref="DamageValueReservation"/> and gives the value column exactly that width off the
	/// right edge, leaving the rest to the component name. Callers with a font in hand do that split;
	/// this reports the whole row.</para>
	/// </summary>
	public Rect DamageRow(int index) {
		int top = DamageColumn.Y0 + index * DamageRowHeight;
		return new Rect(DamageColumn.X0 + 60, top, DamageColumn.X1, top + 8);
	}

	/// <summary>The pitch between damage rows — <c>7 &lt;&lt; YCoordShift</c>.</summary>
	public const int DamageRowHeight = 14;

	/// <summary>
	/// The damage screen's footer caption, centred in an 81x15 device box 56 pixels in from
	/// <see cref="Screen"/>'s left edge and 4 up from its bottom.
	/// </summary>
	public Rect DamageFooter => new(Screen.X0 + 56, Screen.Y1 - 18, Screen.X0 + 136, Screen.Y1 - 4);

	/// <summary>
	/// Where the damage screen prints <see cref="HddDamageSubject.NoData"/>: 160x20 device pixels from
	/// <see cref="Screen"/>'s centre, pulled 40 pixels left. <c>HddDamageScreen_Ctor</c> (<c>0045079c</c>)
	/// takes the 40 off unshifted and the width shifted, so the box, and the text centred in it, sits
	/// right of the screen's centre.
	/// </summary>
	public Rect DamageNoSubject {
		get {
			int x0 = Screen.X0 + ((Screen.X1 - Screen.X0) >> 1) - 40;
			int y0 = Screen.Y0 + ((Screen.Y1 - Screen.Y0) >> 1);
			return new Rect(x0, y0, x0 + 160, y0 + 20);
		}
	}

	/// <summary>
	/// The caption rect for XMIT or CANCEL: the button's top-left plus the plate's own 54x20 size,
	/// which is the constructor's <c>0x1b &lt;&lt; XCoordShift</c> by <c>10 &lt;&lt; YCoordShift</c>.
	/// The widget rect itself is wider (70 device pixels, and the two overlap by two authored units,
	/// three device columns) and is the click region, not the art's extent — so the caption centres
	/// on the plate. XMIT is registered first, so it takes the shared columns — see
	/// <see cref="CockpitWidgets.HitTest"/>.
	/// </summary>
	public Rect TransmitCaptionBox(Widget widget) {
		var rect = this[widget];
		return new Rect(rect.X0, rect.Y0, rect.X0 + 54, rect.Y0 + 20);
	}

	/// <summary>
	/// Reads the block out of <paramref name="gau"/>'s undecoded remainder and re-bases it onto the
	/// <c>.HB1</c> art. <paramref name="headsDownCanvasOriginY"/> is the herc's own <c>.VUE</c> view-1
	/// canvas origin in device pixels (<see cref="CockpitViewGeometry.CanvasOriginY"/>) — the row the
	/// art is blitted at, and therefore what has to come back off a canvas coordinate.
	///
	/// <para>Returns null when the remainder is too short to hold the block, which no retail file is.
	/// The block is read out of <see cref="GAUFile.Remainder"/> rather than modelled as typed fields
	/// for the same reason the gunsight's anchor points are: it is layout the renderer consumes, and
	/// leaving it in the raw span keeps the file's byte-exact round-trip untouched.</para>
	/// </summary>
	public static HddLayout? Load(GAUFile gau, int headsDownCanvasOriginY) {
		// GAUFile.Remainder starts at .GAU offset 1144, immediately past the reticle point.
		const int RemainderStart = 1144;
		const int BlockInts = 95;

		if (gau.Remainder is not { } remainder) {
			return null;
		}

		int at = GauBlockOffset - RemainderStart;
		if (at < 0 || at + BlockInts * 4 > remainder.Length) {
			return null;
		}

		int Value(int index) =>
			BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(at + index * 4));

		// Every value in the block is authored 320-wide and shifted by VideoMode_X/YCoordShift before
		// the origin — itself already shifted — is added. Herculan is always in a 640-wide mode, so
		// the shift is CockpitViewGeometry.CoordShift throughout.
		const int Shift = CockpitViewGeometry.CoordShift;
		int originX = Value(0) << Shift;
		int originY = (Value(1) + OriginYBias) << Shift;

		// ...and the art-local origin is the canvas origin taken back off. In every retail file the
		// two are the same value and this cancels exactly; doing the subtraction is what makes that a
		// finding rather than an assumption.
		int baseX = originX;
		int baseY = originY - headsDownCanvasOriginY;

		Rect ReadRect(int index) => new(
			(Value(index) << Shift) + baseX,
			(Value(index + 1) << Shift) + baseY,
			(Value(index + 2) << Shift) + baseX,
			(Value(index + 3) << Shift) + baseY);

		var widgets = new Rect[WidgetCount];
		for (int i = 0; i < WidgetCount; i++) {
			widgets[i] = ReadRect(0x14 + i * 4);
		}

		var markers = new Rect[PilotSlotCount];
		for (int i = 0; i < PilotSlotCount; i++) {
			markers[i] = ReadRect(0x50 + i * 4);
		}

		return new HddLayout(
			screen: ReadRect(4),
			orderColumn: ReadRect(8),
			damageColumn: ReadRect(0xc),
			indicator: ReadRect(0x10),
			widgets: widgets,
			pilotMarkers: markers,
			arrowFrameSet: Value(0x5c),
			pilotHighlightMode: Value(0x5e));
	}

	/// <summary>
	/// The title for <paramref name="page"/> — "MAP", or the damage view's own " STRUCT DAMAGE" /
	/// " INTERN DAMAGE" / " WEAPON DAMAGE". <c>HddDisplay_SetTitle</c> (<c>0044a6dc</c>) picks between the two groups on the
	/// page and indexes the second by the damage screen's current category, not by page.
	/// </summary>
	public static string? Title(StringFile? strings, HddPage page, HddDamageView view) =>
		page == HddPage.CommandDisplay
			? strings?.Text(PageTitleGroup, 0)
			: strings?.Text(DamageTitleGroup, (int)view);

	/// <summary>
	/// The component names <paramref name="view"/> lists, in the group's own order, or an empty list
	/// when the string table is absent or the view is <see cref="HddDamageView.Weapons"/> —
	/// whose rows are the mech's own fitted weapon names rather than a fixed table.
	///
	/// <para>A row's name is <b>not</b> this list read top to bottom: rows follow the <c>.PDG</c>
	/// view's own region order and each region's id indexes here, which is why a pilotable chassis's internal page
	/// lists its systems 0,1,2,5,6,7,8,3,4,9 (other chassis differ — docs/retail/simulation/heads-down-display.md#damage-detail--page-1).</para>
	/// </summary>
	public static IReadOnlyList<StringFile.Entry> ComponentNames(StringFile? strings, HddDamageView view,
			bool flyer = false) =>
		view switch {
			HddDamageView.Structural => strings?.Group(
				flyer ? StructuralFlyerComponentGroup : StructuralComponentGroup)
				?? Array.Empty<StringFile.Entry>(),
			HddDamageView.Internal => strings?.Group(
				flyer ? InternalFlyerComponentGroup : InternalComponentGroup)
				?? Array.Empty<StringFile.Entry>(),
			_ => Array.Empty<StringFile.Entry>(),
		};

	/// <summary>
	/// Which of the three views in a herc's <c>.PDG</c> the damage screen draws beside its list.
	/// <c>HddDamageScreen_Update</c> (<c>00450c54</c>) sets it with the category, so structural gets the
	/// front doll and internal the rear one; the weapons view reuses the front one and recolours it
	/// blue.
	/// </summary>
	public static int PaperDollView(HddDamageView view) => view == HddDamageView.Internal ? 1 : 0;
}
