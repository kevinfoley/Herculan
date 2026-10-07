using Herculan.Engine.Numerics;
using Herculan.Engine.Render;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The eight orders the command display transmits, in the order the screen lists them — which is
/// <c>STRINGS0.STR</c> group 0 entries 10-17 and so also the order the screen's own selection index
/// counts in. The manual's hotkey for each is the character the entry's own attribute byte points at,
/// not a binding in the code; see <see cref="HddLayout.FirstCommandOrder"/>.
/// </summary>
public enum HddOrder {
	/// <summary>[D] — break off contact.</summary>
	Disengage = 0,

	/// <summary>[A] — attack a unit picked on the map.</summary>
	AttackEnemy = 1,

	/// <summary>[F] — travel to and hold a gridpoint or friendly unit picked on the map.</summary>
	DefendPosition = 2,

	/// <summary>[T] — proceed to a picked gridpoint, engaging on the way.</summary>
	PatrolGridpoint = 3,

	/// <summary>[G] — proceed to a picked gridpoint, avoiding contact.</summary>
	GotoGridpoint = 4,

	/// <summary>[O] — form up on the player.</summary>
	JoinOnMe = 5,

	/// <summary>[C] — go to active radar.</summary>
	ScanForHostiles = 6,

	/// <summary>[E] — emission control.</summary>
	Emcon = 7,
}

/// <summary>
/// One of the three squad comm boxes as the display draws it — <c>HddGauge_PaintIdle</c>'s five
/// labels plus the slot number the sixth carries.
/// </summary>
/// <param name="Occupied">
/// Whether a squadmate holds this slot. An empty one is not painted by its gauge at all: the display
/// floods the box rect inset one device pixel with colour id 19 and draws nothing else.
/// </param>
/// <param name="Name">
/// The name across the top of the box, on that slot's own <c>COLORS.DAT</c> colour — which is the
/// manual's "squad members are shown on the map in the same color that highlights their name on the
/// comm screen".
/// </param>
/// <param name="ConditionIndex">
/// Index into <c>STRINGS0.STR</c> group 28 — <c>HddGauge_ConditionIndex</c>'s bucketing of the
/// machine's structural integrity.
/// </param>
/// <param name="OrderIndex">
/// Index into group 40, the OBJECTIVE: line: the order the pilot is currently carrying out.
/// </param>
/// <remarks>
/// A box showing a picture instead of these labels is not described here: what it draws comes from
/// the comm box's own state machine, through <see cref="CockpitHudState.PilotVideos"/>.
/// </remarks>
public readonly record struct HddPilotSlot(bool Occupied, string Name, int ConditionIndex,
	int OrderIndex);

/// <summary>
/// Everything the command display draws that comes from the simulation rather than from the herc's
/// <c>.GAU</c>: the map camera and its markers, the three comm boxes, which pilot and which order
/// are selected, and the message row above the order list.
/// </summary>
/// <param name="View">
/// The map camera. A reference rather than a value because it is state the buttons move and the
/// paint reads — see <see cref="HddMapView"/>. Null before a mission is loaded, which draws the
/// screen's flood and nothing in it.
/// </param>
/// <param name="Raster">
/// The mission's terrain raster, built once when the zone loads — see <see cref="HddMapRaster"/>. It
/// carries the world rect the map quad is drawn between as well as the pixels.
/// </param>
/// <param name="Markers">The icons on the map this frame — <see cref="HddMap.Markers"/>.</param>
/// <param name="Pilots">The three comm boxes.</param>
/// <param name="SelectedPilot">
/// Which comm box is selected, or -1 — the display's <c>+0x517</c>. <b>It gates the whole
/// screen</b>: with no pilot selected every order draws unavailable, because
/// <c>HddDisplay_SelectPilot</c> is the only thing that ever sets the availability bytes and
/// deselecting clears them again.
/// </param>
/// <param name="SelectedOrder">
/// Which order is armed, or null — the screen's <c>+0x5d</c>, held as a group-0 entry index there
/// and as its own enum here.
/// </param>
/// <param name="ChosenUnit">
/// The unit an armed ATTACK ENEMY or DEFEND POSITION has been pointed at, as a marker index into
/// <paramref name="Markers"/>, or -1. The paint outlines it and draws a line from the selected
/// pilot to it, which is the manual's "a colored line will then link the chosen pilot to your
/// selected target".
/// </param>
/// <param name="ChosenPoint">
/// The gridpoint a PATROL or GOTO has been pointed at, or null. Same line, drawn to a point rather
/// than to a unit.
/// </param>
/// <param name="Message">
/// The row above the order list — the prompt the screen writes when it wants something picked
/// (<c>HddCommandScreen_SetMessageRow</c>, <c>0044dc44</c>), or null for a blank row.
/// </param>
/// <param name="Blink">
/// The display's own half-second blink, <c>DAT_0049d6ad</c>, toggled every 30 coarse ticks. The
/// selected pilot's marker uses it.
/// </param>
public readonly record struct HddCommandState(
	HddMapView? View,
	HddMapRaster? Raster,
	IReadOnlyList<HddMapMarker> Markers,
	IReadOnlyList<HddPilotSlot> Pilots,
	int SelectedPilot,
	HddOrder? SelectedOrder,
	int ChosenUnit,
	Vec3i? ChosenPoint,
	string? Message,
	bool Blink) {

	/// <summary>What the screen holds before a mission is loaded.</summary>
	public static HddCommandState Empty { get; } = new(
		View: null,
		Raster: null,
		Markers: Array.Empty<HddMapMarker>(),
		Pilots: Array.Empty<HddPilotSlot>(),
		SelectedPilot: -1,
		SelectedOrder: null,
		ChosenUnit: -1,
		ChosenPoint: null,
		Message: null,
		Blink: false);

	/// <summary><see cref="Markers"/> with a default-constructed record's null standing in for empty.</summary>
	public IReadOnlyList<HddMapMarker> Plotted => Markers ?? Array.Empty<HddMapMarker>();

	/// <summary><see cref="Pilots"/> with the same guard.</summary>
	public IReadOnlyList<HddPilotSlot> PilotBoxes => Pilots ?? Array.Empty<HddPilotSlot>();

	/// <summary>
	/// Whether the orders can be taken at all. <c>HddCommandScreen_EnableOrders</c> (<c>0044edd8</c>) sets all eight availability bytes
	/// the moment a pilot is selected and <c>HddCommandScreen_DisableOrders</c> (<c>0044edfc</c>) clears all eight when none is, so the
	/// eight-byte array the original keeps is exactly this one bit.
	/// </summary>
	public bool OrdersAvailable => SelectedPilot >= 0;

	/// <summary>
	/// Whether <paramref name="order"/> still wants a target picked before XMIT will send it. ATTACK
	/// ENEMY and DEFEND POSITION want a unit; PATROL and GOTO want a gridpoint. The screen's key
	/// dispatch splits on exactly those two ranges.
	/// </summary>
	public static bool NeedsUnit(HddOrder order) =>
		order is HddOrder.AttackEnemy or HddOrder.DefendPosition;

	/// <summary>The other half of that split.</summary>
	public static bool NeedsPoint(HddOrder order) =>
		order is HddOrder.PatrolGridpoint or HddOrder.GotoGridpoint;
}
