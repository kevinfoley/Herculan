using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Content;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>Which family a clickable cockpit widget belongs to, and so which table its index means.</summary>
public enum CockpitWidgetKind {
	/// <summary>An index into <see cref="MfdLayout.Buttons"/>.</summary>
	MfdButton = 0,

	/// <summary>A <see cref="HddLayout.Widget"/>.</summary>
	HddWidget = 1,

	/// <summary>The console's throttle slider. One of a kind, so its index is always zero.</summary>
	Throttle = 2,

	/// <summary>A weapon panel row — index is its <c>.GAU</c> weapon slot, the number the row prints minus one.</summary>
	WeaponRow = 3,

	/// <summary>One of the three console buttons — index is a <see cref="ConsoleButton"/>.</summary>
	ConsoleButton = 4,

	/// <summary>
	/// A row of the command display's order column — index is an <see cref="HddOrder"/>. The original
	/// has no widget per row: <c>HddCommandScreen_Ctor</c> registers one clickable over the whole
	/// column and <c>HddCommandScreen_HandleListClick</c> walks the eight label rects to find which
	/// was hit. Splitting it into eight regions here reaches the same row from the same rects, and
	/// lets the shared hit test do the walking.
	/// </summary>
	HddOrderRow = 5,

	/// <summary>
	/// The command display's map viewport, registered over the whole inset region exactly as the
	/// original's second clickable is. What a click does depends on what is armed — see
	/// <see cref="HddCommandState"/>.
	/// </summary>
	HddMapArea = 6,

	/// <summary>One half of the shield-balance rocker — index is a <see cref="ShieldFacing"/>.</summary>
	ShieldFacing = 7,

	/// <summary>
	/// A row of the MFD's FLASH COMM order list. As with <see cref="HddOrderRow"/> the original has no
	/// widget per row — <c>MfdFlashComm_HandleListClick</c> walks the six label rects under the
	/// display's own base panel widget — and splitting it into six regions here reaches the same row
	/// from the same rects.
	/// </summary>
	MfdFlashCommRow = 8,

	/// <summary>
	/// One of DBSIM's screen-edge view strips — a <c>ScrollTrigger</c>, the manual's "change views
	/// with the mouse by clicking on the screen edge leading to the view you want". Index is a
	/// <see cref="ViewEdgeStrip"/>.
	/// </summary>
	ViewEdge = 9,

	/// <summary>
	/// An energy weapon row's charge bar, as the slider retail builds it — index is the row's
	/// <c>.GAU</c> weapon slot. Listed only under
	/// <see cref="Settings.TweakSettingDefinitions.ChargeBarPowerLevel"/>; see <see cref="ChargeBarSlider"/>.
	/// </summary>
	WeaponChargeBar = 10,

	/// <summary>
	/// One of the two system buttons at the screen's top-right corner — index is a
	/// <see cref="SystemButton"/>. A window-space widget; see <see cref="SystemButtons"/>.
	/// </summary>
	SystemButton = 11,
}

/// <summary>
/// Which screen-edge view strip, in <c>CockpitView_BuildScrollTriggers</c> (<c>00433770</c>) order.
/// </summary>
public enum ViewEdgeStrip {
	/// <summary>
	/// The strip that moves between the forward view and the Heads-Down Display — the original's
	/// <c>+0x21a</c>. It is one band of cockpit canvas that shows at the <i>bottom</i> of the forward
	/// view and the <i>top</i> of the heads-down view, not two widgets.
	/// </summary>
	HeadsDown = 0,

	/// <summary>
	/// The left strip, the original's <c>+0x21e</c>: glance to view 3 from the forward view, back to it
	/// from view 2. See <see cref="Render.CockpitScreenLayout.SideViewEdgeAt"/> for where it sits.
	/// </summary>
	Left = 1,

	/// <summary>The right strip, <c>+0x222</c>: glance to view 2, or back from view 3.</summary>
	Right = 2,
}

/// <summary>
/// Which half of the shield-balance rocker, in <c>ShieldsGauge_Ctor</c>'s own child order — the order
/// <c>ShieldsGauge_OnClick</c> (<c>0044380c</c>) switches on, and the order <c>Mech_HandleCommand</c>
/// (<c>004157c8</c>) indexes when a bracket key presses one.
/// </summary>
public enum ShieldFacing {
	/// <summary>The forward facing, child 0 — the <c>]</c> key, and the <c>.GAU</c> rect at 632.</summary>
	Front = 0,

	/// <summary>The rear facing, child 1 — the <c>[</c> key, and the rect at 648.</summary>
	Rear = 1,
}

/// <summary>
/// The three buttons under the weapon panel, in the order <c>ConsoleButtons_Ctor</c> builds them and
/// <c>ConsoleButtons_OnChildClick</c> switches on. The index is the child index, and it is what
/// tells the paint which of them latches.
///
/// <para>The original builds a fourth from the <c>.GAU</c> rect at 532, which is zero in every
/// retail file, so it is a degenerate widget that can never be clicked — the same shape as MFD
/// button 6.</para>
/// </summary>
public enum ConsoleButton {
	/// <summary>The fire-chain selector, captioned with the chain's Roman numeral. Momentary.</summary>
	Chain = 0,

	/// <summary>LINK. Momentary — it lights while held and never latches, because the link state lives on the mounts.</summary>
	Link = 1,

	/// <summary>TRACK, auto turret tracking. The one console button that latches.</summary>
	Track = 2,
}

/// <summary>One clickable widget's identity — the pair a click reports back and an action handler switches on.</summary>
/// <param name="Kind">Which family, and so how to read <paramref name="Index"/>.</param>
/// <param name="Index">The widget's index within its own family's table.</param>
public readonly record struct CockpitWidgetId(CockpitWidgetKind Kind, int Index) {
	/// <summary>Multi-function display button <paramref name="index"/>.</summary>
	public static CockpitWidgetId Mfd(int index) => new(CockpitWidgetKind.MfdButton, index);

	/// <summary>Heads-Down Display widget <paramref name="widget"/>.</summary>
	public static CockpitWidgetId Hdd(HddLayout.Widget widget) => new(CockpitWidgetKind.HddWidget, (int)widget);

	/// <summary>The throttle slider.</summary>
	public static CockpitWidgetId Throttle { get; } = new(CockpitWidgetKind.Throttle, 0);

	/// <summary>Row <paramref name="row"/> of the MFD's FLASH COMM order list.</summary>
	public static CockpitWidgetId MfdFlashCommRow(int row) =>
		new(CockpitWidgetKind.MfdFlashCommRow, row);

	/// <summary>Order row <paramref name="order"/> of the command display's list.</summary>
	public static CockpitWidgetId HddOrder(HddOrder order) =>
		new(CockpitWidgetKind.HddOrderRow, (int)order);

	/// <summary>The command display's map viewport.</summary>
	public static CockpitWidgetId HddMapArea { get; } = new(CockpitWidgetKind.HddMapArea, 0);

	/// <summary>One facing of the shield-balance rocker.</summary>
	public static CockpitWidgetId ShieldFacing(ShieldFacing facing) =>
		new(CockpitWidgetKind.ShieldFacing, (int)facing);

	/// <summary>Weapon panel row <paramref name="gaugeSlot"/>, zero-based.</summary>
	public static CockpitWidgetId Weapon(int gaugeSlot) => new(CockpitWidgetKind.WeaponRow, gaugeSlot);

	/// <summary>The charge bar of weapon panel row <paramref name="gaugeSlot"/>, zero-based.</summary>
	public static CockpitWidgetId ChargeBar(int gaugeSlot) => new(CockpitWidgetKind.WeaponChargeBar, gaugeSlot);

	/// <summary>This id as the row index of a charge bar, or null when it is not one.</summary>
	public int? AsWeaponChargeBar => Kind == CockpitWidgetKind.WeaponChargeBar ? Index : null;

	/// <summary>One of the three console buttons.</summary>
	public static CockpitWidgetId Console(ConsoleButton button) =>
		new(CockpitWidgetKind.ConsoleButton, (int)button);

	/// <summary>This id as a weapon-panel row index, or null when it is not one.</summary>
	public int? AsWeaponRow => Kind == CockpitWidgetKind.WeaponRow ? Index : null;

	/// <summary>This id as a console button, or null when it is not one.</summary>
	public ConsoleButton? AsConsoleButton =>
		Kind == CockpitWidgetKind.ConsoleButton ? (ConsoleButton)Index : null;

	/// <summary>This id as an <see cref="HddLayout.Widget"/>, or null when it is not one.</summary>
	public HddLayout.Widget? AsHddWidget =>
		Kind == CockpitWidgetKind.HddWidget ? (HddLayout.Widget)Index : null;

	/// <summary>This id as an order row, or null when it is not one.</summary>
	public HddOrder? AsHddOrder =>
		Kind == CockpitWidgetKind.HddOrderRow ? (HddOrder)Index : null;

	/// <summary>This id as an MFD button index, or null when it is not one.</summary>
	public int? AsMfdButton => Kind == CockpitWidgetKind.MfdButton ? Index : null;

	/// <summary>This id as a FLASH COMM row index, or null when it is not one.</summary>
	public int? AsMfdFlashCommRow =>
		Kind == CockpitWidgetKind.MfdFlashCommRow ? Index : null;

	/// <summary>One of the screen-edge view strips.</summary>
	public static CockpitWidgetId ViewEdge(ViewEdgeStrip strip) =>
		new(CockpitWidgetKind.ViewEdge, (int)strip);

	/// <summary>This id as a shield facing, or null when it is not one.</summary>
	public ShieldFacing? AsShieldFacing =>
		Kind == CockpitWidgetKind.ShieldFacing ? (ShieldFacing)Index : null;

	/// <summary>This id as a screen-edge view strip, or null when it is not one.</summary>
	public ViewEdgeStrip? AsViewEdge =>
		Kind == CockpitWidgetKind.ViewEdge ? (ViewEdgeStrip)Index : null;

	/// <summary>One of the two system buttons.</summary>
	public static CockpitWidgetId System(SystemButton button) =>
		new(CockpitWidgetKind.SystemButton, (int)button);

	/// <summary>This id as a system button, or null when it is not one.</summary>
	public SystemButton? AsSystemButton =>
		Kind == CockpitWidgetKind.SystemButton ? (SystemButton)Index : null;
}

/// <summary>
/// One clickable widget as it stands this frame: which one it is, which surface it lives on, and the
/// rect it occupies in that surface's art pixels.
/// </summary>
/// <param name="Id">Which widget.</param>
/// <param name="Surface">Which cockpit surface its rect is measured on.</param>
/// <param name="X0">Left edge, art device pixels, inclusive.</param>
/// <param name="Y0">Top edge, inclusive.</param>
/// <param name="X1">Right edge, inclusive — the last pixel the widget covers, not one past it.</param>
/// <param name="Y1">Bottom edge, inclusive.</param>
/// <param name="Lit">
/// Whether the widget draws its lit frame this frame — <b>and which input decides that depends on the
/// button</b>. A latching button (<see cref="MfdLayout.IsLatching"/>,
/// <see cref="HddLayout.IsLatching"/>) is lit by being the chosen one and is unaffected by a press; a
/// momentary one is lit only while it is held. The original does this by having the two kinds read
/// different fields — the selection flag <c>+0x40</c> versus the shared press byte <c>+0x1b</c> — so
/// the F-key columns have no pressed state at all, while SELECT and the arrows have nothing else.
/// </param>
/// <param name="Selected">
/// Whether the widget is the persistently chosen one — the MFD's current screen, the Heads-Down
/// Display's current page. Only latching buttons are ever selected, and only they re-font their
/// caption; a momentary button holds its construction font however long it is held.
/// </param>
/// <param name="Draggable">
/// Whether pressing this widget captures the pointer — the original's per-widget <c>+0x1d</c> flag,
/// which <c>Widget_OnMouseDown</c> (<c>004527a0</c>) tests before latching the global drag capture
/// and dispatching the widget's own drag handler. Every button class leaves it clear; the slider
/// base (<c>004524a8</c>) sets it, which is the whole of what makes the throttle draggable (the energy rows' charge bars carry it too, under their rows' select gadgets).
/// </param>
public readonly record struct CockpitWidget(CockpitWidgetId Id, CockpitSurface Surface,
		int X0, int Y0, int X1, int Y1, bool Lit, bool Selected = false, bool Draggable = false) {

	/// <summary>Inclusive width in art pixels.</summary>
	public int Width => X1 - X0 + 1;

	/// <summary>Inclusive height in art pixels.</summary>
	public int Height => Y1 - Y0 + 1;

	/// <summary>
	/// Whether an art-space point falls on this widget — <c>Widget_HitTest</c>'s rectangular case
	/// (<c>00452388</c>), inclusive on all four edges as the original's is.
	///
	/// <para>The original's other case, a Manhattan-distance circular test selected by a per-widget
	/// flag, is not implemented: every cockpit widget constructor clears that flag, and its setter
	/// (<c>Widget_SetHitShape</c>, <c>0045234c</c>) has no caller <c>es2_xref.py</c> finds. See
	/// docs/retail/simulation/cockpit-input.md §6.</para>
	/// </summary>
	public bool Contains(float artX, float artY) =>
		artX >= X0 && artY >= Y0 && artX <= X1 && artY <= Y1;
}

/// <summary>
/// Every clickable widget in the cockpit, in one flat list, positioned in art pixels.
///
/// <para>This is the shared definition the renderer draws from and the input layer hit-tests against,
/// so a button's click region cannot drift from the art it was drawn over. It pairs with
/// <see cref="CockpitScreenLayout"/>: that type gets a click from the window down to a surface's art
/// pixel, this one says which widget is under it.</para>
///
/// <para><b>Faithful to the original in the part that matters.</b> DBSIM keeps one flat, cockpit-wide
/// clickable list rather than a per-panel tree — every widget anywhere in the cockpit is appended to
/// the same array by <c>Widget_RegisterClickable</c> (<c>00452c44</c>), and
/// <c>Widget_HitTestChildren</c> (<c>00452a00</c>) linear-scans the whole thing. Nothing is removed
/// when a panel stops showing; a hidden widget is skipped because its state byte says so
/// (docs/retail/simulation/cockpit-input.md §5). What is reproduced here is that <i>rule</i> — hidden means not
/// hit — via the layouts' own visibility tables, which are the decoded form of the same state byte.
/// The flat array itself is not reproduced as a mutable registry: widgets are enumerated on demand
/// from the layout tables, since nothing in Herculan needs to register or unregister one at
/// runtime.</para>
///
/// <para><b>Clickable is not the same as drawable.</b> Every widget listed here is hit-testable,
/// including ones with no sprite of their own — the Heads-Down Display's title box and its dead slot
/// are in the original's clickable list too. Renderers keep their own "does this have a frame" check;
/// this type does not filter on it.</para>
/// </summary>
public static class CockpitWidgets {
	/// <summary>
	/// Every widget that is currently visible, and so currently clickable, for the given cockpit and
	/// HUD state. Widgets whose panel is not showing are omitted rather than reported hidden — the
	/// original's hit test skips them just as completely.
	///
	/// <para><b>The order is the original's registration order, and it is load-bearing:</b>
	/// <see cref="HitTest"/> takes the first match, exactly as <c>Widget_HitTestChildren</c> takes the
	/// first entry of the flat list <c>Widget_RegisterClickable</c> appended to. That list is built by
	/// <c>Gau_BuildCockpitWidgets</c> (<c>00431bf8</c>) in the order the seven top-level gauges are
	/// constructed — console buttons (<c>ConsoleButtons_Ctor</c>), energy meter (no clickables),
	/// shield facings, the multi-function display, the throttle, the roving gunsight, then the
	/// Heads-Down Display — followed by the weapon rows, which the function's closing
	/// <c>WeaponMounts_BuildGauges</c> builds one per mount, and finally the screen-edge strips on
	/// the first cockpit frame.</para>
	///
	/// <para>Two of the original's entries are not in this list. The pair of <c>SystemGadget</c>s
	/// <c>SystemButtons_Ctor</c> registers ahead of everything stays on the same screen pixels in every
	/// view rather than on any art, so <see cref="SystemButtons"/> places them and
	/// <see cref="Input.CockpitInput"/> tests them before this list. The roving gunsight's click surface
	/// covers the 3D window and is not implemented; it overlaps no widget that is, so leaving it out
	/// costs nothing but its own behaviour.</para>
	/// </summary>
	public static IEnumerable<CockpitWidget> Visible(CockpitArt art, CockpitHudState state) {
		ArgumentNullException.ThrowIfNull(art);

		foreach (var widget in VisibleConsoleButtons(art, state)) {
			yield return widget;
		}

		foreach (var widget in VisibleShieldFacings(art)) {
			yield return widget;
		}

		foreach (var widget in VisibleMfdButtons(art, state)) {
			yield return widget;
		}

		foreach (var widget in VisibleMfdFlashCommRows(art, state)) {
			yield return widget;
		}

		if (VisibleThrottle(art) is { } throttle) {
			yield return throttle;
		}

		foreach (var widget in VisibleHddWidgets(art, state)) {
			yield return widget;
		}

		foreach (var widget in VisibleWeaponRows(art, state)) {
			yield return widget;
		}

		// The strips go last because the original builds them last: CockpitView_BuildScrollTriggers
		// (00433770) runs from the cockpit tick (004327ac) on the first frame that finds +0x23e
		// clear, after every gauge constructor. Under first-hit-wins that is what lets a console
		// instrument sitting over the bottom band take the click instead of the strip, which is what
		// MAVERICK's [F6] button, RAPTOR2's throttle and RAZOR's TRACK button all do.
		if (VisibleHeadsDownViewEdge(art) is { } viewEdge) {
			yield return viewEdge;
		}
	}

	/// <summary>Band thickness of a horizontal view strip, device pixels — the original's <c>3 &lt;&lt; YCoordShift</c>.</summary>
	public const int ViewEdgeBandRows = 3 << CockpitViewGeometry.CoordShift;

	/// <summary>Band thickness of a side view strip, device pixels — the original's <c>5 &lt;&lt; XCoordShift</c>.</summary>
	public const int ViewEdgeBandColumns = 5 << CockpitViewGeometry.CoordShift;

	/// <summary>
	/// The strip that moves between the forward view and the Heads-Down Display, or null when the herc
	/// has no <c>.HB1</c> to pan to.
	///
	/// <para><b>One widget, two screen edges.</b> <c>CockpitView_BuildScrollTriggers</c>
	/// (<c>00433770</c>) makes this a band across the bottom of the forward view's own window, and the
	/// original's widget rects are in cockpit-canvas space: <c>Widget_OnMouseDown</c> hit-tests
	/// <c>point - root-&gt;rect</c>, and <c>Widget_TranslateRootForView</c> (<c>00452734</c>) moves
	/// that root origin by the view delta on every view change. Because the heads-down view's canvas
	/// origin is only 237 authored rows down a 240-row window, the band it occupies is the bottom of
	/// the forward view and the top of the heads-down view at the same time — which is why retail pans
	/// down on a click at the bottom of the screen and back up on a click at the <i>top</i>.</para>
	///
	/// <para>Herculan reaches the same behaviour without a canvas: the band is the bottom of the
	/// forward surface's art, and <see cref="CockpitScreenLayout.WindowToArt"/> already resolves the
	/// overlap in the forward surface's favour, so once the pan has carried that art to the top of the
	/// screen the same rect answers a click there. See docs/retail/simulation/cockpit-input.md §10.</para>
	///
	/// <para>The two side strips are not art-space widgets here; see
	/// <see cref="Render.CockpitScreenLayout.SideViewEdgeAt"/>.</para>
	/// </summary>
	public static CockpitWidget? VisibleHeadsDownViewEdge(CockpitArt art) {
		ArgumentNullException.ThrowIfNull(art);
		if (art.HeadsDown is null) {
			return null;
		}

		int bottom = art.Front.Height - 1;
		return new CockpitWidget(CockpitWidgetId.ViewEdge(ViewEdgeStrip.HeadsDown),
			CockpitSurface.Forward, 0, bottom - ViewEdgeBandRows, art.Front.Width - 1, bottom, Lit: false);
	}

	/// <summary>
	/// The shield-balance rocker's two facings, front first — <c>ShieldsGauge_Ctor</c>'s two children
	/// over the <c>.GAU</c> rects at 632 and 648, each registered with <c>Widget_RegisterClickable</c>
	/// like any button. They draw nothing of their own (<c>ShieldFacing_Paint</c> only tests
	/// visibility): the rings inside them live in the canopy art's palette slots 66-71, which is why
	/// the rocker looks like part of the bezel and yet takes clicks.
	///
	/// <para>The gauge is always showing — nothing hides it — and it is never lit: a facing has no
	/// pressed frame to draw, so a press is invisible however it arrives.</para>
	///
	/// <para>RAZOR is the one machine where this block is not a shield meter at all but its
	/// altimeter (see <see cref="HShieldDisplay"/>); its facings are still built and still clickable,
	/// exactly as the original's are.</para>
	/// </summary>
	public static IEnumerable<CockpitWidget> VisibleShieldFacings(CockpitArt art) {
		ArgumentNullException.ThrowIfNull(art);
		if (art.Gau.ShieldDisplay is not { } shields) {
			yield break;
		}

		const int scale = (int)CockpitArt.GauToPixelScale;

		CockpitWidget Facing(ShieldFacing facing, PixelPoint origin, PixelSize size) =>
			new(CockpitWidgetId.ShieldFacing(facing), CockpitSurface.Forward,
				X0: origin.X * scale,
				Y0: origin.Y * scale,
				X1: (origin.X + size.Width) * scale + scale - 1,
				Y1: (origin.Y + size.Height) * scale + scale - 1,
				Lit: false);

		yield return Facing(ShieldFacing.Front, shields.FrontBox, shields.FrontBoxSize);
		yield return Facing(ShieldFacing.Rear, shields.RearBox, shields.RearBoxSize);
	}

	/// <summary>
	/// The weapon panel's rows. The clickable region is the <c>.GAU</c> hardpoint rect itself, not the
	/// plate art drawn over it — the row's select gadget is built on that rect
	/// (<c>ChainedWeaponSelectGadget_Ctor</c>) and registered first, so it takes the click even where the
	/// energy row's value-field child overlaps it.
	///
	/// <para>Rows past the herc's own weapon count are not built at all, so they are not listed. A
	/// row whose mount is a pod <i>is</i>: on an ECM or Turbo row the press works the pod's on/off
	/// button, and on the other three it clicks, sounds and changes nothing. Which of those a row
	/// does is decided by its gauge class — see <see cref="WeaponMounts.PressRow"/>.</para>
	///
	/// <para>Under <see cref="CockpitHudState.ChargeBarsDraggable"/> an energy or ELF row's charge bar is
	/// listed just ahead of its row, so it takes presses on its own span — the
	/// <see cref="Settings.TweakSettingDefinitions.ChargeBarPowerLevel"/> tweak, and the one place this
	/// list departs from the original's order. A row the power-up has not reached, or the sensor
	/// dropout is holding back, draws no bar and lists none.</para>
	/// </summary>
	public static IEnumerable<CockpitWidget> VisibleWeaponRows(CockpitArt art, CockpitHudState state) {
		ArgumentNullException.ThrowIfNull(art);
		if (art.Gau.Weapons is not { } rows) {
			yield break;
		}

		const int scale = (int)CockpitArt.GauToPixelScale;
		int slots = Math.Min(art.Gau.WeaponListTotal, rows.Length);

		for (int i = 0; i < slots; i++) {
			var rect = rows[i];
			if (state.ChargeBarsDraggable && i < state.Weapons.Count
				&& state.Weapons[i] is { Kind: WeaponMountKind.Energy or WeaponMountKind.Elf, Powered: true, DropoutHidden: false }
				&& ChargeBarSlider.For(art, i) is { } bar) {
				yield return bar.Widget(i);
			}

			yield return new CockpitWidget(CockpitWidgetId.Weapon(i), CockpitSurface.Forward,
				X0: rect.Origin.X * scale,
				Y0: rect.Origin.Y * scale,
				X1: (rect.Origin.X + rect.Size.Width) * scale + scale - 1,
				Y1: (rect.Origin.Y + rect.Size.Height) * scale + scale - 1,
				Lit: false,
				Selected: i < state.Weapons.Count && state.Weapons[i].Selected);
		}
	}

	/// <summary>
	/// The three console buttons under the weapon panel. <c>ConsoleButton_Paint</c> (<c>00442c88</c>)
	/// picks each one's frame from a different field: CHAIN and LINK read the shared press byte, so
	/// they light only while held, and TRACK reads its own latch flag.
	/// </summary>
	public static IEnumerable<CockpitWidget> VisibleConsoleButtons(CockpitArt art, CockpitHudState state) {
		ArgumentNullException.ThrowIfNull(art);

		const int scale = (int)CockpitArt.GauToPixelScale;

		CockpitWidget? Button(ConsoleButton which, WidgetBase? rect) {
			if (rect == null) {
				return null;
			}

			var id = CockpitWidgetId.Console(which);
			bool latched = which == ConsoleButton.Track && state.AutoTrack;
			return new CockpitWidget(id, CockpitSurface.Forward,
				X0: rect.Origin.X * scale,
				Y0: rect.Origin.Y * scale,
				X1: (rect.Origin.X + rect.Size.Width) * scale + scale - 1,
				Y1: (rect.Origin.Y + rect.Size.Height) * scale + scale - 1,
				Lit: which == ConsoleButton.Track ? latched : state.ShowsPressed(id),
				Selected: latched);
		}

		if (Button(ConsoleButton.Chain, art.Gau.ChainButton) is { } chain) {
			yield return chain;
		}

		if (Button(ConsoleButton.Link, art.Gau.LinkButton) is { } link) {
			yield return link;
		}

		if (Button(ConsoleButton.Track, art.Gau.AutoTrackButton) is { } track) {
			yield return track;
		}
	}

	/// <summary>
	/// The throttle slider's track, or null when this herc's <c>.GAU</c> has no throttle block. It is
	/// the one cockpit control the pointer can drag, and it is always showing: the console it sits on
	/// has no modes.
	/// </summary>
	public static CockpitWidget? VisibleThrottle(CockpitArt art) {
		ArgumentNullException.ThrowIfNull(art);
		if (ThrottleTrack.From(art) is not { } track) {
			return null;
		}

		return new CockpitWidget(CockpitWidgetId.Throttle, CockpitSurface.Forward,
			track.Left, track.Top, track.Right, track.Bottom, Lit: false, Selected: false,
			Draggable: true);
	}

	/// <summary>
	/// The first visible widget under an art-space point on one surface, or null when the point is
	/// over bare art.
	///
	/// <para><b>First hit wins, and so enumeration order is precedence.</b>
	/// <c>Widget_HitTestChildren</c> (<c>00452a00</c>) returns the index of the first entry of the
	/// flat clickable list that the point falls in, so the widget registered earliest takes a
	/// contested pixel. <see cref="Visible"/> enumerates in the original's own registration order,
	/// which is what makes that rule reach the same widget here.</para>
	///
	/// <para>Retail cockpits do contest pixels, in five places — the FLASH COMM rows, whose rects are
	/// built <c>top..top+14</c> and stepped by 14 so each shares its bottom line with the row below;
	/// the Heads-Down Display's XMIT and CANCEL, whose <c>.GAU</c> rects overlap by two authored
	/// units; the same display's arrow plates, which touch at the corners; the view strip under
	/// three hercs' consoles; and every energy row's charge bar, which lies wholly inside its row. The
	/// earlier widget wins every one of them, which leaves the bars unreachable unless
	/// <see cref="CockpitHudState.ChargeBarsDraggable"/> lists them first.</para>
	/// </summary>
	public static CockpitWidget? HitTest(CockpitArt art, CockpitHudState state,
			CockpitSurface surface, float artX, float artY) {
		foreach (var widget in Visible(art, state)) {
			if (widget.Surface == surface && widget.Contains(artX, artY)) {
				return widget;
			}
		}

		return null;
	}

	/// <summary>
	/// The visible multi-function display buttons, in <c>MfdDisplay_Ctor</c>'s own index order.
	///
	/// <para>Rects come from <see cref="MfdLayout.Buttons"/>, which is authored in GAU units relative
	/// to the display's inset origin, so each is offset by that origin and scaled by
	/// <see cref="CockpitArt.GauToPixelScale"/> into the art's device pixels. The inclusive right and
	/// bottom edges land on the last device pixel the button's sprite covers: a GAU rect inclusive of
	/// <c>X1</c> covers <c>(X1 - X0 + 1)</c> GAU units, hence twice that many device pixels.</para>
	/// </summary>
	public static IEnumerable<CockpitWidget> VisibleMfdButtons(CockpitArt art, CockpitHudState state) {
		ArgumentNullException.ThrowIfNull(art);
		if (MfdLayout.InsetOrigin(art.Gau) is not { } inset) {
			yield break;
		}

		const int scale = (int)CockpitArt.GauToPixelScale;

		for (int i = 0; i < MfdLayout.ButtonCount; i++) {
			if (!MfdLayout.ButtonVisible(state.Mfd, i)) {
				continue;
			}

			var button = MfdLayout.Buttons[i];
			var id = CockpitWidgetId.Mfd(i);

			// PASS and ACTIVE are latching too, and what they latch on is not their own click but the
			// machine's radar mode: MfdButton_OnClick writes mech+0x96 and then sets one button's
			// +0x40 flag and clears the other's, and the scanner's update slot re-presses whichever
			// matches whenever the mode changes behind the display's back (the [R] key).
			bool selected = i switch {
				11 => state.Scanner.Passive,
				12 => !state.Scanner.Passive,
				_ => i < MfdLayout.ModeCount && i == (int)state.Mfd,
			};
			yield return new CockpitWidget(id, CockpitSurface.Forward,
				X0: (inset.X + button.X0) * scale,
				Y0: (inset.Y + button.Y0) * scale,
				X1: (inset.X + button.X1) * scale + scale - 1,
				Y1: (inset.Y + button.Y1) * scale + scale - 1,
				Lit: MfdLayout.IsLatching(i) ? selected : state.ShowsPressed(id),
				Selected: selected);
		}
	}

	/// <summary>
	/// FLASH COMM's six order rows, when that is the screen the MFD is showing.
	/// <c>MfdFlashComm_HandleListClick</c> hit-tests each row's own label rect and does two different
	/// things with a hit: a click on the row already selected presses XMIT and transmits, and a click
	/// on any other row selects it. The
	/// rows report <c>Lit</c> for the selected one so a caller can tell which is which without going
	/// back to the layout.
	/// </summary>
	public static IEnumerable<CockpitWidget> VisibleMfdFlashCommRows(CockpitArt art, CockpitHudState state) {
		ArgumentNullException.ThrowIfNull(art);
		if (state.Mfd != MfdMode.FlashComm || MfdLayout.InsetOrigin(art.Gau) is not { } inset) {
			yield break;
		}

		const int scale = (int)CockpitArt.GauToPixelScale;
		var rows = MfdLayout.FlashCommRows;
		int insetX = inset.X * scale;
		int insetY = inset.Y * scale;

		for (int i = 0; i < MfdLayout.FlashCommRowCount; i++) {
			int top = insetY + rows.Y0 + i * rows.RowHeight;
			yield return new CockpitWidget(CockpitWidgetId.MfdFlashCommRow(i), CockpitSurface.Forward,
				X0: insetX + rows.X0,
				Y0: top,
				X1: insetX + rows.X1,
				Y1: top + rows.RowHeight,
				Lit: state.FlashComm.SelectedRow == i,
				Selected: state.FlashComm.SelectedRow == i);
		}
	}

	/// <summary>
	/// The visible Heads-Down Display widgets. Their rects are already device pixels relative to the
	/// <c>.HB1</c> art's top-left — the space <see cref="HddLayout"/> reports in — so they need no
	/// conversion, only the herc's own <c>.GAU</c> block to have loaded.
	///
	/// <para>The command display's two clickables come first because <c>HddDisplay_Ctor</c>
	/// (<c>00448cc8</c>) builds both pages before it builds its own fifteen buttons, and within
	/// <c>HddCommandScreen_Ctor</c> the order column is registered ahead of the map. The damage detail
	/// registers nothing at all — it is labels only.</para>
	/// </summary>
	public static IEnumerable<CockpitWidget> VisibleHddWidgets(CockpitArt art, CockpitHudState state) {
		ArgumentNullException.ThrowIfNull(art);
		if (art.HeadsDownLayout is not { } layout) {
			yield break;
		}

		if (state.Hdd == HddPage.CommandDisplay) {
			for (int i = 0; i < HddLayout.OrderCount; i++) {
				var row = layout.OrderRow(i + 1);
				yield return new CockpitWidget(CockpitWidgetId.HddOrder((HddOrder)i), CockpitSurface.HeadsDown,
					row.X0, row.Y0, row.X1, row.Y1,
					Lit: false,
					Selected: state.Command.SelectedOrder == (HddOrder)i);
			}

			var viewport = layout.MapViewport;
			yield return new CockpitWidget(CockpitWidgetId.HddMapArea, CockpitSurface.HeadsDown,
				viewport.X0, viewport.Y0, viewport.X1, viewport.Y1, Lit: false);
		}

		var litWidget = state.Hdd == HddPage.CommandDisplay
			? HddLayout.Widget.PageButton0
			: HddLayout.Widget.PageButton1;

		var pilots = state.Command.PilotBoxes;

		for (int i = 0; i < HddLayout.WidgetCount; i++) {
			var widget = (HddLayout.Widget)i;

			// A comm box is hidden by both rows of the visibility table and put back by
			// HddGauge_LoadPilotFrames, which clears the state byte for every slot a squadmate
			// actually occupies. An empty slot stays unclickable, which is why selecting a pilot who
			// is not there is impossible rather than merely useless.
			int slot = i - (int)HddLayout.Widget.PilotBox0;
			bool commBox = slot is >= 0 and < HddLayout.PilotSlotCount;
			if (commBox
				? !(slot < pilots.Count && pilots[slot].Occupied)
				: !HddLayout.WidgetVisible(state.Hdd, widget)) {
				continue;
			}

			var rect = layout[widget];
			var id = CockpitWidgetId.Hdd(widget);
			bool selected = commBox ? slot == state.Command.SelectedPilot : widget == litWidget;
			yield return new CockpitWidget(id, CockpitSurface.HeadsDown,
				rect.X0, rect.Y0, rect.X1, rect.Y1,
				Lit: HddLayout.IsLatching(widget) ? selected : state.ShowsPressed(id),
				Selected: selected);
		}
	}
}
