using HercWorks.Core.Data.File;
﻿using HercWorks.Core.Data.File.Gau;

namespace Herculan.Engine.Content;

/// <summary>Which screen the multi-function display is showing. The order is DBSIM's own mode
/// numbering, which is also the F-key order: <c>MfdDisplay_SetMode</c> (<c>00446e38</c>) takes
/// this value directly, and button <c>i</c> of the F-key column dispatches
/// <c>SetMode(i)</c>.</summary>
public enum MfdMode {
	/// <summary>F1 — the player herc's own damage wireframe and status readouts.</summary>
	Status = 0,

	/// <summary>F2 — the squadmate order list, and the talking-head clip that plays over it.</summary>
	FlashComm = 1,

	/// <summary>F3 — the overhead terrain map.</summary>
	NavMap = 2,

	/// <summary>F4 — the contact scanner. What a cockpit powers up showing.</summary>
	Scanner = 3,

	/// <summary>F5 — the same screen as <see cref="Status"/>, pointed at the current target.</summary>
	TargetStatus = 4,

	/// <summary>F6 — the steerable-missile camera.</summary>
	MissileCam = 5,
}

/// <summary>
/// The multi-function display's geometry, decoded from <c>MfdDisplay_Ctor</c> (<c>00445218</c>) and
/// the per-screen constructors it builds. See docs/formats/cockpit-hud-widgets.md for the surrounding
/// cockpit; this type covers the MFD sub-widgets that document lists as undecoded.
/// </summary>
/// 
/// <remarks>
/// <para><b>Where the numbers live.</b> Only one rect comes from the herc's <c>.GAU</c> — the panel
/// bounding box at offset 952 (<see cref="GAUFile.MfdPanel"/>). Everything inside it is hardcoded in
/// DBSIM: the constructor reads its 13 button rects out of four parallel <c>int16</c> tables in
/// <c>.rdata</c> (<c>0049cacc</c> x0, <c>0049cae6</c> y0, <c>0049cb00</c> x1, <c>0049cb1a</c> y1) and
/// its label rects from immediates. That is why the MFD looks the same in every cockpit while its
/// position moves: the <c>.GAU</c> supplies the position and nothing else. The <c>.GAU</c> does carry
/// 13 rect-shaped slots at 744-951, which <c>MfdGau_ApplyCoordShift</c> (<c>00447650</c>) even coordinate-shifts, but they are
/// zero in all nine retail files and the constructor never reads them.</para>
///
/// <para><b>The 18-unit inset.</b> The constructor immediately does
/// <c>x0 += 0x12 &lt;&lt; XCoordShift</c> and keeps <c>x1</c>, <c>y0</c>, <c>y1</c> as they came, then
/// works relative to that inset origin for the rest of its life. The strip to the left of the inset
/// is where the F-key column goes — its table x values are negative for exactly that reason. The
/// inset region is <c>98 x 61</c> GAU inclusive, which is <c>196 x 122</c> device pixels, which is
/// exactly the size of <c>MFD</c> bank frames 0-2. Every one of the other rect sizes likewise matches
/// a frame in that bank to the pixel (15x8 the F-keys, 46x10 SELECT/XMIT, 27x10 the four scanner
/// buttons), which is what confirms the whole layout.</para>
///
/// <para>All coordinates here are GAU (320-wide) units, as authored; multiply by
/// <see cref="CockpitArt.GauToPixelScale"/> for the 640-wide space the sprites and fonts live in.
/// Rects are inclusive on all four edges, matching the tables.</para>
/// </remarks>
public static class MfdLayout {
	/// <summary>
	/// GAU units the screen area is inset from the panel's left edge, leaving room for the F-key
	/// column. The constructor's own <c>0x12</c> literal.
	/// </summary>
	public const int ScreenInsetX = 18;

	/// <summary>Sprite bank the whole display is drawn from — <c>MfdDisplay_Ctor</c>'s first load.</summary>
	public const string Bank = "MFD";

	/// <summary>
	/// The screen background frame for <paramref name="mode"/>, or null when that screen draws no
	/// background at all.
	///
	/// <para>Frames 0-2 are three pieces of screen chrome: frame 0 is split into two boxes by a
	/// central divider, frame 1 is one box spanning the whole content area, and frame 2 is a single
	/// small box in the top-left corner with the rest left open. The MFD's repaint
	/// (<c>MfdDisplay_Repaint</c>, <c>00446138</c>) picks between them with a bare switch on the current mode, reaching the
	/// bank's frame-pointer array at <c>+4</c> and <c>+8</c> — elements 1 and 2. <b>Frame 0 is never
	/// used as a background</b>; the display leaves it to the screens that draw their own dividers.</para>
	///
	/// <para>The nav map and missile cam get literal 0 and the repaint skips the blit entirely, which
	/// is correct rather than an omission: both fill the whole screen with their own image, and the
	/// map's paint floods its rect before rasterizing into it.</para>
	///
	/// <para>Corroborated against the retail reference crops: the status, flash-comm and target
	/// screens show a border only at the content area's outer edges, while the scanner shows an extra
	/// vertical border at panel x 100-101 — exactly frame 2's box edge at frame-local x 64-65 plus the
	/// <see cref="ScreenInsetX"/> offset.</para>
	/// </summary>
	public static int? BackgroundFrame(MfdMode mode) => mode switch {
		MfdMode.Status or MfdMode.FlashComm or MfdMode.TargetStatus => 1,
		MfdMode.Scanner => 2,
		_ => null,
	};

	/// <summary>
	/// The <c>MFD</c> frame that is the screen chrome for a mode that has any, and the frame whose
	/// 196x122 size <i>is</i> the inset region — which is what a full-screen flood measures against.
	/// </summary>
	public const int ScreenFrame = 1;

	/// <summary>How many screens the display has, and how many mode buttons therefore exist.</summary>
	public const int ModeCount = 6;

	/// <summary>
	/// The aux button FLASH COMM shows: XMIT, index 10. It shares a rect with 7 SELECT — one
	/// top-right button under two names — and it is its press byte the selected row's plate follows.
	/// </summary>
	public const int TransmitButton = 10;

	/// <summary>Every button the constructor builds, mode selectors included.</summary>
	public const int ButtonCount = 13;

	/// <summary>
	/// <c>STRINGS0.STR</c> group holding the 13 button captions — the sixth registration in
	/// <c>SimStrings_LoadAll</c> (<c>00437598</c>), landing in <c>MfdCaptionTable</c> (<c>004d13e0</c>). Entries 0-5 are the six screen
	/// <i>titles</i> and 6-12 the aux button captions: <c>MfdButton_SetCaption</c> (<c>00447358</c>) composes "F1".."F6" from
	/// its own <c>"Fx"</c> literal for the mode buttons and only reaches this table for index >= 6.
	/// </summary>
	public const int CaptionGroup = 5;

	/// <summary>Group holding "ID:" / "TARGET:" / "DIST:  " — the status screen's first label.</summary>
	public const int IdentLabelGroup = 20;

	/// <summary>Group holding the single string "YOU", the player's own name on the status screen.</summary>
	public const int SelfNameGroup = 17;

	/// <summary>Group holding "STATUS:", the status screen's third label.</summary>
	public const int StatusLabelGroup = 21;

	/// <summary>
	/// Group holding the 31 structure type names, indexed by
	/// <see cref="Herculan.Engine.World.BaseType.SilhouetteIndex"/> - the same index that picks the
	/// <c>BASES</c> silhouette frame.
	/// </summary>
	public const int StructureNameGroup = 23;

	/// <summary>And the four vehicle type names, for a type whose <see cref="Herculan.Engine.World.BaseType.IsVehicle"/> is set.</summary>
	public const int VehicleNameGroup = 24;

	/// <summary>Group holding the single string "NONE" - what the target screen names an empty selection.</summary>
	public const int NoTargetNameGroup = 26;

	/// <summary>Group holding the single string "UNKNOWN" - a subject the screen's class switch does not recognise.</summary>
	public const int UnknownNameGroup = 27;

	/// <summary>
	/// Which entry of <see cref="IdentLabelGroup"/> heads the screen: 0 <c>ID:</c> for the player's own
	/// machine and its squad, 1 <c>TARGET:</c> for anything else, and 2 <c>DIST:</c>, which the fifth
	/// label prefixes a hostile's range with.
	/// </summary>
	public const int IdentSelfEntry = 0;

	/// <inheritdoc cref="IdentSelfEntry"/>
	public const int IdentTargetEntry = 1;

	/// <inheritdoc cref="IdentSelfEntry"/>
	public const int IdentDistanceEntry = 2;

	/// <summary>
	/// Fonts the paint re-installs on the subject-name label from the subject's own side, overriding
	/// the <c>RED</c> the constructor gives it: <c>ColorSchemePanels[1]</c> <c>CPGREEN</c> for one of
	/// ours and <c>[2]</c> <c>CPRED</c> for a Cybrid. It is <c>MfdStatusScreen_Paint</c> (<c>0043a5a0</c>)'s own
	/// <c>DAT_0049b0b0</c>/<c>DAT_0049b0b4</c> pair, read from the group record's side byte.
	/// </summary>
	public const string FriendlyNameFont = "CPGREEN";

	/// <inheritdoc cref="FriendlyNameFont"/>
	public const string HostileNameFont = "CPRED";

	/// <summary>
	/// And the font the screen falls back to with no subject or an unidentified one -
	/// <c>ColorSchemePanels[0]</c>, <c>CPBLUE</c>, which the paint writes straight into the label's
	/// font slot on both of those paths.
	/// </summary>
	public const string UnknownNameFont = "CPBLUE";

	/// <summary>
	/// Bank names the status screen's three silhouette loads use, in
	/// <c>MfdStatusScreen_Ctor</c>'s own order. A structure and a vehicle index their bank by
	/// <see cref="Herculan.Engine.World.BaseType.SilhouetteIndex"/>; a flyer always takes frame 0 of
	/// its own, the paint reaching the bank's frame array with no index at all.
	/// </summary>
	public const string StructureBank = "BASES";

	/// <inheritdoc cref="StructureBank"/>
	public const string VehicleBank = "VEHICLES";

	/// <inheritdoc cref="StructureBank"/>
	public const string FlyerBank = "FLYERS";

	/// <inheritdoc cref="StructureBank"/>
	public const int FlyerFrame = 0;

	/// <summary>
	/// The dependent-component damage a HERC has to be carrying on <i>all</i> twelve of its internals
	/// before the screen calls it CRITICAL rather than INT DAMAGE - the paint's own <c>0x81</c> against
	/// the Q8 readings <c>Component_FillDamageReadouts</c> fills, i.e. every internal more than half gone.
	/// </summary>
	public const int CriticalDependentDamage = 0x81;

	/// <summary>How many dependent slots that scan covers - the paint's own twelve.</summary>
	public const int ScannedDependents = 12;

	/// <summary>
	/// Group holding the status screen's condition strings — "OK", "SHIELDS DN", "INT DAMAGE",
	/// "CRITICAL", "DESTROYED". <c>MfdStatusScreen_SetCondition</c> (<c>0043b260</c>) indexes it as
	/// <c>MfdStatusConditionStrings[state]</c> (<c>004d1698</c>) into the fourth label.
	///
	/// <para>Group 10 holds a near-identical five-string set ("OK", "INT DMG", "SHLD DWN",
	/// "CRITICAL", "WASTED") and is the obvious wrong answer here: it is <b>dead data</b>, with
	/// <c>SimStrings_LoadAll</c> as the only reference to its array anywhere in the image.</para>
	/// </summary>
	public const int ConditionGroup = 28;

	/// <summary>Group holding the 18 squadmate orders; the first six are the FLASH COMM page.</summary>
	public const int OrderGroup = 0;

	/// <summary>A button's rect and the pair of bank frames it draws unlit and lit.</summary>
	/// <param name="X0">Left edge, GAU, relative to the inset screen origin — negative for the F-key column.</param>
	/// <param name="Caption">Index into <see cref="CaptionGroup"/>, or -1 for the mode buttons, whose caption is "F"+(index+1).</param>
	public readonly record struct Button(int X0, int Y0, int X1, int Y1, int UnlitFrame, int Caption) {
		/// <summary>Inclusive width, GAU.</summary>
		public int Width => X1 - X0 + 1;

		/// <summary>Inclusive height, GAU.</summary>
		public int Height => Y1 - Y0 + 1;

		/// <summary>The frame drawn when the button is lit — always the one after its unlit frame.</summary>
		public int LitFrame => UnlitFrame + 1;
	}

	/// <summary>
	/// The 13 buttons, in the constructor's own index order, transcribed from the four <c>.rdata</c>
	/// tables. Indices 0-5 are the F-key mode column (10-unit pitch down the left strip); 6 is a
	/// degenerate zero rect that no screen ever shows — its caption slot says "MODE" — and 7-12 are
	/// the per-screen aux buttons. 7 and 10 share a rect because they are the same top-right button
	/// under two names, SELECT on the status screens and XMIT on FLASH COMM.
	/// </summary>
	public static readonly Button[] Buttons = {
		new(-16, 1, -2, 8, 3, -1),      // F1
		new(-16, 11, -2, 18, 3, -1),    // F2
		new(-16, 21, -2, 28, 3, -1),    // F3
		new(-16, 31, -2, 38, 3, -1),    // F4
		new(-16, 41, -2, 48, 3, -1),    // F5
		new(-16, 51, -2, 58, 3, -1),    // F6
		new(0, 0, 0, 0, 3, 6),          // "MODE" — never visible
		new(50, 2, 95, 11, 5, 7),       // SELECT
		new(4, 36, 30, 45, 7, 8),       // RANGE
		new(4, 47, 30, 56, 7, 9),       // TARGET
		new(50, 2, 95, 11, 5, 10),      // XMIT
		new(4, 14, 30, 23, 9, 11),      // PASS
		new(4, 25, 30, 34, 9, 12),      // ACTIVE
	};

	/// <summary>
	/// Which aux buttons each mode shows, from the 6x13 byte table at <c>0049cbd8</c> that
	/// <c>MfdDisplay_SetMode</c> walks for indices 6-12 (the decompiler folds the <c>+6</c> into the
	/// symbol, which is why it appears as <c>DAT_0049cbde + mode * 13</c>). The six mode buttons are 1
	/// in every row, so only 6-12 vary.
	/// </summary>
	private static readonly byte[,] AuxVisibility = {
		{ 0, 1, 0, 0, 0, 0, 0 },  // Status:      SELECT
		{ 0, 0, 0, 0, 1, 0, 0 },  // FlashComm:   XMIT
		{ 0, 0, 0, 0, 0, 0, 0 },  // NavMap:      none
		{ 0, 0, 1, 1, 0, 1, 1 },  // Scanner:     RANGE, TARGET, PASS, ACTIVE
		{ 0, 1, 0, 0, 0, 0, 0 },  // TargetStatus: SELECT
		{ 0, 0, 0, 0, 0, 0, 0 },  // MissileCam:  none
	};

	/// <summary>
	/// Whether button <paramref name="index"/> is a latching button — lit by being chosen and staying
	/// chosen — rather than a momentary one that lights only while it is held down.
	///
	/// <para><b>They are two different C++ classes.</b> <c>MfdDisplay_Ctor</c> switches on the button
	/// index and constructs indices 0-5 and 11-12 through <c>MFDStateGadget_Ctor</c> (<c>0044741c</c>), whose repaint
	/// (<c>MfdButton_Repaint</c>) picks its frame with the object's own selection flag <c>+0x40</c>,
	/// and indices 7-10 through <c>MFDSelectGadget_Ctor</c> (<c>004472e4</c>), whose repaint (<c>MfdButton_SetCaption</c>) picks
	/// its frame with the shared widget state byte <c>+0x1b</c> — the byte a press sets. So the F-key
	/// column and the two scanner toggles never show a pressed state at all, while SELECT, RANGE,
	/// TARGET and XMIT light only while held.</para>
	///
	/// <para>This also explains <c>MfdButton_Repaint</c>'s caption re-font test,
	/// <c>index &lt; 6 || index - 0xb &lt; 2</c>: that set is exactly the latching class's indices, so
	/// the test is a class invariant restated rather than a rule of its own. Only latching buttons ever
	/// re-font, which is why holding SELECT does not darken its caption.</para>
	/// </summary>
	public static bool IsLatching(int index) => index < ModeCount || index is 11 or 12;

	/// <summary>Whether <paramref name="mode"/> shows button <paramref name="button"/>.</summary>
	public static bool ButtonVisible(MfdMode mode, int button) {
		if (button < 0 || button >= ButtonCount) {
			return false;
		}

		if (button < ModeCount) {
			return true;
		}

		int row = (int)mode;
		return row >= 0 && row < ModeCount && AuxVisibility[row, button - ModeCount] != 0;
	}

	/// <summary>
	/// The screen title's rect, GAU, relative to the inset origin — the constructor's
	/// <c>(+4, +0)</c>-<c>(+0x28, +9)</c>. Drawn in <c>WHITE</c> (<c>ColorSchemePanels[10]</c>), which
	/// is why retail's title reads as near-white against the green screen.
	/// </summary>
	public static readonly (int X0, int Y0, int X1, int Y1) TitleRect = (4, 0, 40, 9);

	/// <summary>
	/// The secondary caption rect at <c>(+0x16, +0x2e)</c>-<c>(+0x4a, +0x34)</c>, in <c>DARK</c>
	/// (<c>ColorSchemePanels[12]</c>). <c>MfdDisplay_Update</c> (<c>00446328</c>) fills it from the incoming-message object
	/// that also drives FLASH COMM's talking-head frames, so it is blank outside a transmission.
	/// </summary>
	public static readonly (int X0, int Y0, int X1, int Y1) MessageRect = (22, 46, 74, 52);

	/// <summary>
	/// The font that caption is written in — <c>ColorSchemePanels[12]</c>, <c>DARK</c>. It is the
	/// text colour only; the plate behind it is the transmitting comm box's own
	/// <c>COLORS.DAT</c> id, which the box publishes alongside the name.
	/// </summary>
	public const string MessageFont = "DARK";

	/// <summary>
	/// The status screens' damage-wireframe viewport, GAU, relative to the inset origin —
	/// <c>(+0x2d, +0xd)</c>-<c>(+0x5f, +0x3a)</c> in the shared constructor at <c>0043a2e0</c>. Its
	/// left edge is also the five labels' right edge, so text and diagram tile the screen exactly.
	/// </summary>
	public static readonly (int X0, int Y0, int X1, int Y1) WireframeRect = (45, 13, 95, 58);

	/// <summary>
	/// Device-pixel offset the wireframe art is blitted at inside <see cref="WireframeRect"/>, on top
	/// of the herc's own <c>.PDG</c> view origin. The status screen's paint computes the blit position
	/// as <c>pdgView.origin + viewportTopLeft + (0x11, 2)</c> — the paper doll goes down whole first,
	/// and the per-region damage tints are then drawn over it at the same origin.
	/// </summary>
	public static readonly (int X, int Y) WireframeArtOffset = (17, 2);

	/// <summary>
	/// Which of the three views in a herc's <c>.PDG</c> the status screen draws. The paint reaches one
	/// view record through the mech type and does not compute the index, but only this one fits: views
	/// 0 and 1 are the full-size front and rear dolls at 96x162 device pixels, against a viewport
	/// 102x92, while view 2 is the compact doll at roughly 48x82.
	/// </summary>
	public const int WireframeViewIndex = 2;

	/// <summary>
	/// <c>COLORS.DAT</c> id the Targeting Pod's component highlight is filled with, and the last thing
	/// the status screen paints: a flat rect over the <c>.PDG</c> region holding the component the pod
	/// has singled out, the region's own rect grown one device pixel on every side. It goes down after
	/// the damage tints, so the highlight blots the region out rather than outlining it.
	///
	/// <para>Drawn only for a hostile subject on F5 with the pod's component-present flag set
	/// (<c>CockpitView+0x27c</c>), so the player's own machine on F1 never shows one. See
	/// <see cref="Sim.TargetingPodLock"/>.</para>
	/// </summary>
	public const int ComponentHighlightColorId = 16;

	/// <summary>
	/// The region a component is highlighted in, when no region states that component id itself. The
	/// compact view merges each three-deep limb stack into one region, so the outer two components of
	/// a stack have to be mapped onto the region the innermost one names.
	///
	/// <para><b>The two rear stacks are mapped wrong in the original</b>: 15 and 17 go to region 13
	/// and 16 and 18 go to region 13 as well, where the tint pass reads region 14 as the mean of 14,
	/// 16 and 18. It cannot be observed — the pod's rotation
	/// (<see cref="Sim.TargetingPodLock.ComponentRotation"/>) only ever produces 0, 4, 5, 7, 8, 9 and
	/// 10 — and it is transcribed rather than corrected.</para>
	/// </summary>
	public static int ComponentHighlightRegion(int componentId) => componentId switch {
		1 => 0,
		9 or 11 => 7,
		10 or 12 => 8,
		15 or 17 => 13,
		16 or 18 => 13,
		_ => componentId,
	};

	/// <summary>
	/// The status screens' five text labels, GAU, relative to the inset origin. x0 is 6 for all five
	/// (<c>0049bd84</c>), y0 comes from <c>0049bd8e</c>, and each is 6 units tall and runs right to
	/// <see cref="WireframeRect"/>'s left edge.
	/// </summary>
	public static readonly int[] StatusLabelY = { 16, 23, 32, 40, 49 };

	/// <summary>Left edge shared by all five status labels.</summary>
	public const int StatusLabelX = 6;

	/// <summary>Height of a status label, GAU, inclusive.</summary>
	public const int StatusLabelHeight = 6;

	/// <summary>
	/// Font per status label, from the selector table at <c>0049bd98</c> (<c>0,1,0,1,1</c>) indexing
	/// <c>{ColorSchemePanels[10] WHITE, [14] RED}</c>. These are the fonts the constructor installs,
	/// and the ones labels 0, 2, 3 and 4 keep.
	///
	/// <para>Label 1, the subject's name, is re-fonted at paint time from the subject's side — see
	/// <see cref="FriendlyNameFont"/>, which is that override — so entry 1 here is never used.</para>
	/// </summary>
	public static readonly string[] StatusLabelFonts = { "WHITE", "RED", "WHITE", "RED", "RED" };

	/// <summary>
	/// The status screen's range readout, which replaces the integrity one for a hostile subject: the
	/// <c>DIST:</c> caption from <see cref="IdentLabelGroup"/> with the range in world units appended,
	/// exactly as <c>MfdStatusScreen_Paint</c> (<c>0043a5a0</c>) builds it (<c>strcpy</c> the caption, <c>itoa</c> onto the end).
	/// The caption's own trailing spaces are what separate the two.
	/// </summary>
	public static string DistanceReadout(StringFile? strings, int distance) =>
		(strings?.Text(IdentLabelGroup, IdentDistanceEntry) ?? "DIST:  ") + distance;

	/// <summary>
	/// The status screen's structural-integrity readout — its fifth label.
	/// <c>MfdStatusScreen_SetCondition</c> builds it as the literal <c>"[ "</c>, then
	/// <c>itoa((0x100 - damage) * 100 &gt;&gt; 8)</c>, then the literal <c>"% ]"</c>, from a subject's
	/// 0-255 damage byte. Undamaged reads <c>[ 100% ]</c>.
	///
	/// <para>When the subject is unreadable the same function writes <c>"XXXXXX"</c> to the condition
	/// label and <c>"XXX"</c> here instead.</para>
	/// </summary>
	public static string IntegrityReadout(int damage) => $"[ {IntegrityPercent(damage)}% ]";

	/// <summary>
	/// <c>(0x100 - damage) * 100 &gt;&gt; 8</c> — a Q8 damage reading as the whole-number percentage
	/// every damage display prints and every condition ladder bands. The original writes it out at
	/// each site; it is one expression and it is here.
	/// </summary>
	public static int IntegrityPercent(int damage) => (0x100 - Math.Clamp(damage, 0, 0x100)) * 100 >> 8;

	/// <summary>How many order rows FLASH COMM lists.</summary>
	public const int FlashCommRowCount = 6;

	/// <summary>
	/// FLASH COMM's row block, device pixels relative to the inset origin, from the constructor at
	/// <c>0043f5d8</c>: it builds the rect as <c>(+2, +0xd)</c>-<c>(+0x60, +0x3a)</c> GAU, nudges the
	/// top-left in by <c>1 &lt;&lt; XCoordShift</c> and the bottom-right out by the same, and steps
	/// each of six rows down by <c>7 &lt;&lt; YCoordShift</c>. Both nudges use <c>XCoordShift</c> even
	/// on the y axis — a quirk of the original with no effect in any retail video mode, all of which
	/// shift both axes equally.
	/// </summary>
	public static readonly (int X0, int Y0, int X1, int RowHeight) FlashCommRows = (6, 28, 190, 14);

	/// <summary>
	/// Device pixels the order text is indented inside its row rect. The constructor passes the label
	/// positioner a margin array whose first short is <c>2 &lt;&lt; XCoordShift</c>; every other MFD
	/// label passes an all-zero one.
	/// </summary>
	public const int FlashCommTextMarginX = 4;

	/// <summary>
	/// Font FLASH COMM lists its orders in — <c>ColorSchemePanels[1]</c>, i.e. <c>CPGREEN</c>.
	/// </summary>
	public const string FlashCommFont = "CPGREEN";

	/// <summary>
	/// The font the selected row is re-written in — <c>ColorSchemePanels[3]</c>, <c>CPYLW</c>. The
	/// page paint (<c>MfdFlashCommScreen_Paint</c> (<c>0043f7a4</c>)) fonts every row as it fills it, so the selected row differs
	/// from its neighbours by colour as well as by the plate laid over it.
	/// </summary>
	public const string FlashCommSelectedFont = "CPYLW";

	/// <summary>
	/// The font a row that cannot currently be taken is written in — <c>ColorSchemePanels[6]</c>,
	/// <c>CPOFF</c>. No retail row ever reaches it: the two functions that set and clear the
	/// unavailable bit (<c>FUN_0043f9f4</c>, <c>FUN_0043fa14</c>) have no caller in the image.
	/// </summary>
	public const string FlashCommUnavailableFont = "CPOFF";

	/// <summary>
	/// The <i>alternate</i> font each row carries at <c>+0x21</c> — <c>ColorSchemePanels[2]</c>,
	/// <c>CPRED</c>. It is not an unavailable state: <c>Label_SetTextWithHotkey</c> (<c>00438aac</c>) redraws exactly one
	/// character of the row in it, at the index the order's own attribute byte names, which is how
	/// the hotkey letter is picked out. The [F7] order list uses the same mechanism.
	/// </summary>
	public const string FlashCommHotkeyFont = "CPRED";

	/// <summary>
	/// The plate laid over the selected row — <c>MFD</c> frame 11, a 91x8 GAU hollow rounded rect,
	/// blitted by <c>MfdFlashCommScreen_DrawRowPlate</c> (<c>0043fa34</c>) <b>after</b> the row's text so it frames rather than covers it.
	/// The frame index is <c>0xb +</c> the XMIT button's own press byte, so holding XMIT swaps it for
	/// <see cref="FlashCommRowPlatePressedFrame"/>.
	/// </summary>
	public const int FlashCommRowPlateFrame = 11;

	/// <inheritdoc cref="FlashCommRowPlateFrame"/>
	public const int FlashCommRowPlatePressedFrame = 12;

	/// <summary>
	/// Frame 13, the plain plate the same function lays over a row that has just <i>stopped</i> being
	/// selected — the erase half of the two-row repaint the page does when the cursor moves.
	/// </summary>
	public const int FlashCommRowPlateClearFrame = 13;

	/// <summary>
	/// The colour an MFD screen floods its own area with before painting — <c>MfdDisplay_Update</c>'s
	/// and the page paint's <c>0x11</c>.
	///
	/// <para><b>A raw palette index, not a <c>COLORS.DAT</c> id</b>, by the same rule
	/// <see cref="MfdScanner.BackgroundPaletteIndex"/> follows: the paint states it as an immediate.
	/// It resolves to (12,12,12), the same near-black the screen chrome's own interior is. Read as an
	/// id instead it would land on a mid grey and fill the whole screen with it.</para>
	/// </summary>
	public const int ScreenFillPaletteIndex = 0x11;

	/// <summary>
	/// Where a transmission's video is drawn, GAU, relative to the inset origin —
	/// <c>MfdDisplay_Update</c>'s <c>inset + (0x14 &lt;&lt; XCoordShift, 0 &lt;&lt; YCoordShift)</c>.
	/// The frame's own <c>.OFS</c> offset is added to this <b>unshifted</b>, so it stays in the bank's
	/// 320-wide space while the frame itself is blitted doubled.
	/// </summary>
	public static readonly (int X, int Y) TransmissionOrigin = (0x14, 0);

	/// <summary>
	/// The panel's outer rect in GAU units, or null when the herc's <c>.GAU</c> had no MFD block.
	/// Everything else here is relative to <see cref="InsetOrigin"/>, which is this rect's top-left
	/// plus <see cref="ScreenInsetX"/>.
	/// </summary>
	public static (int X, int Y)? PanelOrigin(GAUFile gau) =>
		gau.MfdPanel is { } panel ? (panel.Origin.X, panel.Origin.Y) : null;

	/// <summary>
	/// The origin every rect in this type is measured from: the panel's top-left shifted right by
	/// <see cref="ScreenInsetX"/>. This is also where <see cref="ScreenFrame"/> is blitted.
	/// </summary>
	public static (int X, int Y)? InsetOrigin(GAUFile gau) =>
		PanelOrigin(gau) is { } panel ? (panel.X + ScreenInsetX, panel.Y) : null;

	/// <summary>
	/// The origin of the offscreen context the NAV MAP and MISSILE CAM constructors each build over
	/// the inset, device pixels from the inset origin: <c>-((x1 - x0) &gt;&gt; 1)</c>,
	/// <c>-((y1 - y0) &gt;&gt; 1)</c> of the shifted inset rect, which is the GAU span itself — 97, 60
	/// on every retail herc. The map centres on it and the missile camera projects about it.
	/// </summary>
	public static (int X, int Y)? ScreenCentre(GAUFile gau) =>
		gau.MfdPanel is { } panel
			? (panel.Size.Width - ScreenInsetX, panel.Size.Height)
			: null;

	/// <summary>
	/// The title for <paramref name="mode"/> — "STATUS", "FLASH COMM", "NAV MAP", "SCANNER",
	/// "TARGET", "MISSILE CAM" — or null when the string table is absent. Modes index
	/// <see cref="CaptionGroup"/> directly, which is what <c>MfdDisplay_Update</c> (<c>00446328</c>) does.
	/// </summary>
	public static string? Title(StringFile? strings, MfdMode mode) =>
		strings?.Text(CaptionGroup, (int)mode);

	/// <summary>
	/// Button <paramref name="index"/>'s caption: "F1".."F6" for the mode column, otherwise the
	/// caption table entry. Null when the table is absent.
	/// </summary>
	public static string? Caption(StringFile? strings, int index) {
		if (index < 0 || index >= ButtonCount) {
			return null;
		}

		return index < ModeCount
			? "F" + (index + 1)
			: strings?.Text(CaptionGroup, Buttons[index].Caption);
	}
}
