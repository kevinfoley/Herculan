using Herculan.Engine.Content;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The live values the cockpit's readouts display. Separate from <see cref="CockpitArt"/>, which is
/// the herc's layout and art and is loaded once: this is what changes frame to frame.
///
/// <para>Some of it is not wired to the simulation yet. <see cref="Default"/> is the state a herc
/// powers up in — full pool, an even shield balance, no weapons fitted, unchained, unlinked —
/// which is also what the retail reference screenshots show, so what the engine draws can be
/// compared against them directly. Fields become live as the sim grows the state behind them.</para>
/// </summary>
/// <param name="Weapons">
/// One entry per <c>.GAU</c> weapon row, in row order — see <see cref="WeaponRowState"/>. A row no
/// mount claims draws its plate and number with no name rather than an invented one.
/// </param>
/// <param name="ShieldFront">
/// Front shield readout, 0-200. This is the shield <i>balance</i>, not the charge — the original
/// prints `balance * 200 >> 10` here and its complement below, so the pair always sums to 200 even
/// on an empty array. Charge is shown by the meter rings instead.
/// </param>
/// <param name="ShieldRear">Rear shield readout — exactly 200 minus <see cref="ShieldFront"/>.</param>
/// <param name="EnergyFraction">
/// The Master Energy Pool's fill, Q10 over 0-1024 — <c>MechObject.EnergyPoolFraction</c>, which is
/// the same <c>(pool &lt;&lt; 10) / 10000</c> <c>Player_PerFrameCockpitUpdate</c> hands the meter
/// widget, and 1024 is the range that widget's LED bar was built with.
/// </param>
/// <param name="SpeedKph">Ground speed in K/H, as the readout under the reticle spells it.</param>
/// <param name="Throttle">
/// The console throttle slider's setting, Q10 over the gauge's own +/-0x400 range, positive
/// forward. It is the same number the piloted machine holds at <c>mech+0x290</c>, kept in step with
/// it by <c>MechObject.ExchangeCockpitThrottle</c> once a frame — see <see cref="ThrottleTrack"/>.
/// </param>
/// <param name="TorsoTwist">
/// The turret's twist angle, <c>mech+0x298</c> — what the front window's Rotation Indicator
/// shows. Binary angle relative to the machine's own heading, positive to the same side
/// <see cref="Herculan.Engine.Sim.MechControls.TorsoTwist"/> positive drives it.
/// </param>
/// <param name="Heading">
/// The angle the gunsight hands the front window's Heading Indicator. That is the machine's own
/// heading, <c>mech+0x10</c>, every frame but the cockpit's power-up wind-up, when it is that ramp
/// instead — see <see cref="HeadingTapeSweep"/>. A binary angle; <see cref="HeadingTape"/> negates
/// and slides the compass strip behind its window by it.
/// </param>
/// <param name="MissionTime">
/// The front window's <c>TIME:</c> readout, already rendered — see <see cref="MissionClock"/>, which
/// carries it as ASCII digits rather than as a duration.
/// </param>
/// <param name="ChainGroup">
/// Which of the three fire chains is selected, 0-2 — <c>WeaponMounts.Group</c>. The chain button is
/// captioned with that many <c>I</c>s from a literal table in the executable, so chain 0 reads "I".
/// </param>
/// <param name="AutoTrack">Whether the TRACK button is latched — <c>WeaponMounts.AutoTrack</c>.</param>
/// <param name="Mfd">Which screen the multi-function display is showing. F1-F6 select it, exactly as DBSIM's own mode buttons do.</param>
/// <param name="Hdd">Which screen the Heads-Down Display is showing. F7 and F8 select it, and either one also pans down to it.</param>
/// <param name="HddDamage">
/// Which component category <see cref="HddPage.DamageDetail"/> is listing. The manual binds [S], [I]
/// and [W] to it; the display's own up/down arrow buttons step through the same three.
/// </param>
/// <param name="HddSubject">
/// Whose herc that screen is inspecting — the player, a squadmate or the target, stepped by the
/// display's left/right arrows. See <see cref="HddDamageSubject"/>.
/// </param>
/// <param name="PressedWidget">
/// The widget currently held down under the pointer, drawn in its lit frame for as long as it is —
/// the original's own <c>Widget_PressedIndex</c> (<c>0049dbdc</c>) plus the state byte it sets (docs/retail/simulation/cockpit-input.md
/// §7). Transient input state rather than simulation state, and it lives here for the same reason the
/// rest does: <see cref="CockpitWidgets"/> folds it into each widget's lit flag, so the one place that
/// decides what a widget looks like stays the one place, and no renderer needs a second parameter
/// threaded through it. Read it through <see cref="ShowsPressed"/>.
/// </param>
/// <param name="Target">
/// The front-window target indicator's resolved state, or null when nothing is selected (or the
/// indicator has never been armed, which is <c>TargetSelection.IndicatorArmed</c>). See
/// <see cref="TargetIndicator"/> and <see cref="TargetBox"/>.
/// </param>
/// <param name="StatusSubject">
/// What F1's status screen showed at its last paint - the squad roster's entry, which SELECT steps
/// (<see cref="MfdStatusRoster"/>), as <see cref="MfdStatusRefresh"/> last parked it.
/// </param>
/// <param name="TargetSubject">
/// And what F5's did: the selection. Same screen class, same record, different subject -
/// see <see cref="MfdStatusSubject"/>.
/// </param>
/// <param name="Command">
/// What the Heads-Down Display's command display draws that the herc's <c>.GAU</c> does not supply —
/// the map camera and its markers, the three comm boxes, and which pilot and order are selected. See
/// <see cref="HddCommandState"/>.
/// </param>
/// <param name="FlashComm">
/// What F2's order list shows — which row the cursor is on and what each row currently names. See
/// <see cref="MfdFlashCommScreen"/>.
/// </param>
/// <param name="Transmission">
/// What a squadmate's video box is showing this frame, or null when nobody is transmitting. Drawn
/// over whichever MFD screen is up, because the original's update draws it before the screen's own
/// paint slot ever runs — see <see cref="SquadCommChannel"/>.
/// </param>
/// <param name="Scanner">
/// What F4's scanner plots — its contacts, its display range and the radar mode it mirrors from the
/// machine. See <see cref="MfdScannerState"/>.
/// </param>
/// <param name="NavMap">
/// Where F3's NAV MAP is centred and which way it is turned — the machine being flown — and the
/// terrain raster it draws. See <see cref="MfdNavMapState"/>.
/// </param>
/// <param name="PilotVideos">
/// What each of the Heads-Down Display's three comm boxes is showing, or a null entry for a box on
/// its idle labels — <see cref="SquadCommChannel.Video"/>.
/// </param>
/// <param name="PilotMessage">
/// The line the pilot and squad channel is showing over the canopy, or null when it is quiet. A
/// separate port from <paramref name="Message"/> with its own box and its own colours — see
/// <see cref="PilotMessageBoxLayout"/>.
/// </param>
/// <param name="RouteWaypoint">
/// Where the next waypoint of the player group's route lies, for the diamond over the compass and
/// the <c>WAYPOINT n: d M.</c> line under it, or null when the route has run out — see
/// <see cref="WaypointIndicator"/>.
/// </param>
/// <param name="NavMarker">
/// The same for the marker the player drops on themselves, which wears its own colour and prints no
/// line. Null while none is down — see <see cref="NavMarker"/>.
/// </param>
/// <param name="TrainingMessage">
/// What the training port is showing in place of <paramref name="PilotMessage"/> — a training
/// mission's instructor, as a wrapped block. Null when it is quiet or the mission is not a training
/// one; see <see cref="TrainingMessageLayout"/>.
/// </param>
/// <param name="MfdPowerUpFrame">
/// While the MFD is powering up on its scanner screen, which <see cref="CockpitPowerUp.MfdBank"/>
/// frame stands over the dish in place of the screen and any transmission; null once it has powered
/// up. See <see cref="CockpitPowerUp.MfdFrame"/>.
/// </param>
/// <param name="Dropout">
/// Which displays the sensor dropout has blanked — see <see cref="CockpitDropouts"/>. The weapon rows
/// carry their own, in <see cref="WeaponRowState"/>.
/// </param>
/// <param name="ChargeBarsDraggable">
/// Whether the energy rows' charge bars take presses —
/// <see cref="Settings.TweakSettingDefinitions.ChargeBarPowerLevel"/>, carried here so the widget list
/// can be built without the settings. See <see cref="CockpitWidgets.VisibleWeaponRows"/>.
/// </param>
/// <param name="MissileCam">What F6's MISSILE CAM screen last painted — see <see cref="MfdMissileCamScreen"/>.</param>
/// <param name="MissileCamHolding">
/// Whether the display switched itself to the missile camera and still holds it
/// (<see cref="MfdMissileCamSwitch.Holding"/>), which keeps a transmission off the screen.
/// </param>
/// <param name="Altitude">
/// What the RAZOR's altitude scale reads this frame, or null when the piloted machine is not a flyer
/// and the scale does not draw — see <see cref="AltitudeScale"/>.
/// </param>
/// <param name="TrackHidden">
/// Whether the TRACK button is hidden, neither drawn nor taking a press: true when the piloted machine
/// is a flyer (docs/retail/simulation/torso-aim.md#automatic-turret-tracking--t).
/// </param>
/// <param name="FlashingWidgets">
/// The buttons a key or another press made for the player is holding down for a moment —
/// <see cref="Input.PressFlashes{TId}.Lit"/>. Null for none. Read it through <see cref="ShowsPressed"/>.
/// </param>
public readonly record struct CockpitHudState(
	IReadOnlyList<WeaponRowState> Weapons,
	int ShieldFront,
	int ShieldRear,
	int EnergyFraction,
	int SpeedKph,
	short Throttle,
	short TorsoTwist,
	short Heading,
	string MissionTime,
	int ChainGroup,
	bool AutoTrack,
	MfdMode Mfd,
	HddPage Hdd,
	HddDamageView HddDamage,
	HddDamageSubject HddSubject,
	CockpitWidgetId? PressedWidget = null,
	TargetIndicator? Target = null,
	MfdStatusSubject StatusSubject = default,
	MfdStatusSubject TargetSubject = default,
	MfdScannerState Scanner = default,
	MessageTicker Message = default,
	HddCommandState Command = default,
	MfdFlashCommState FlashComm = default,
	SquadTransmission? Transmission = null,
	IReadOnlyList<SquadTransmission?>? PilotVideos = null,
	PilotMessageLine? PilotMessage = null,
	WaypointMark? RouteWaypoint = null,
	WaypointMark? NavMarker = null,
	MfdNavMapState NavMap = default,
	TrainingMessageBox? TrainingMessage = null,
	int? MfdPowerUpFrame = null,
	CockpitDropoutState Dropout = default,
	bool ChargeBarsDraggable = false,
	MfdMissileCamState MissileCam = default,
	bool MissileCamHolding = false,
	AltitudeReading? Altitude = null,
	bool TrackHidden = false,
	IReadOnlyList<CockpitWidgetId>? FlashingWidgets = null) {

	/// <summary>
	/// Whether <paramref name="id"/> is in its pressed state, the original's state byte <c>+0x1b</c> at 1: held
	/// under the pointer, or flashing from a press made for the player. Only the buttons whose paint reads that
	/// byte draw it (docs/retail/simulation/cockpit-input.md#the-press-flash).
	/// </summary>
	public bool ShowsPressed(CockpitWidgetId id) =>
		PressedWidget == id || (FlashingWidgets?.Contains(id) ?? false);

	/// <summary>
	/// Power-up state: an even shield balance printing 100/100 the way <c>ShieldsGauge_UpdateReadouts</c>
	/// scales it (a balance readout, not a charge one), no weapon panel, chain at one, stationary clock, and the
	/// MFD on the scanner — which is the screen <c>Gau_MfdPanelWidget</c> boots the display to with
	/// its own <c>SetMode(obj, 3)</c> call.
	///
	/// <para>The Heads-Down Display boots to its command display and structural damage for the same
	/// reason: <c>HddDisplay_Ctor</c> (<c>00448cc8</c>) ends with <c>HddDisplay_SetPage(obj, 0)</c> (<c>0044a5e4</c>) and <c>HddDamageScreen_Ctor</c> (<c>0045079c</c>) with
	/// <c>HddDamageScreen_SetView(obj, 0)</c> (<c>00450b60</c>).</para>
	/// </summary>
	public static CockpitHudState Default { get; } = new(
		Weapons: Array.Empty<WeaponRowState>(),
		ShieldFront: 100,
		ShieldRear: 100,
		EnergyFraction: 1024,
		SpeedKph: 0,
		Throttle: 0,
		TorsoTwist: 0,
		Heading: 0,
		MissionTime: "",
		ChainGroup: 0,
		AutoTrack: false,
		Mfd: MfdMode.Scanner,
		Hdd: HddPage.CommandDisplay,
		HddDamage: HddDamageView.Structural,
		HddSubject: HddDamageSubject.Default,
		PressedWidget: null,
		Target: null,
		StatusSubject: MfdStatusSubject.None,
		TargetSubject: MfdStatusSubject.None,
		Scanner: MfdScannerState.Empty,
		Message: default,
		Command: default,
		FlashComm: MfdFlashCommState.Default,
		Transmission: null,
		PilotVideos: null,
		PilotMessage: null,
		RouteWaypoint: null,
		NavMarker: null);
}

/// <summary>
/// One line on the pilot and squad channel, already composed the way <c>PilotMessagePort_ComposeLine</c> (<c>00435d0c</c>) composes it
/// — the speaker's name, <see cref="PilotMessageBoxLayout.NameSeparator"/>, then their message.
/// </summary>
/// <param name="Text">The composed line.</param>
/// <param name="Slot">
/// Which comm box is speaking, which is what picks the box's colours, or -1 for a message with no
/// squadmate behind it — see <see cref="PilotMessageBoxLayout"/>.
/// </param>
public readonly record struct PilotMessageLine(string Text, int Slot);

/// <summary>
/// The training port's wrapped block, already laid out by <see cref="TrainingMessageLayout.Wrap"/>.
/// </summary>
/// <param name="Lines">The lines, top to bottom.</param>
/// <param name="Widest">Which line sets the box's width, or -1 for none.</param>
public readonly record struct TrainingMessageBox(IReadOnlyList<string> Lines, int Widest);
