using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Settings;
using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Ai;
using HercWorks.Core.Data.File;

namespace Herculan.Engine.Cockpit;

/// <summary>What a click in the command display's map did.</summary>
public enum HddMapClick {
	/// <summary>Nothing.</summary>
	None,

	/// <summary>
	/// Picked the armed order's unit or gridpoint — <c>HddCommandScreen_PickTarget</c> (<c>0044d6b8</c>), which
	/// plays <c>bptslct</c> for it.
	/// </summary>
	Picked,

	/// <summary>Selected the squadmate whose marker it landed on — <c>HddCommandScreen_PickPilot</c> (<c>0044d804</c>).</summary>
	PilotSelected,
}

/// <summary>
/// The command display's own state and the actions its buttons and keys perform —
/// <c>HddCommandScreen</c> (<c>HddCommandScreen_Ctor</c>, <c>0044c264</c>) minus the drawing, which is
/// <see cref="Render.Cockpit.HeadsDownPainter"/>'s and <see cref="Render.Cockpit.HddMapPainter"/>'s.
/// </summary>
/// <remarks>
/// <para>The screen is a small state machine and the manual describes it as one: pick a pilot, pick
/// an order, pick a target on the map if the order wants one, then XMIT or CANCEL. Every transition
/// below is one of the original's — <c>HddCommandScreen_SelectPilot</c> (<c>0044da70</c>) selects a pilot, <c>HddCommandScreen_SelectOrder</c> (<c>0044d9cc</c>) an
/// order, <c>HddCommandScreen_PickTarget</c> (<c>0044d6b8</c>) resolves a map click into a unit or a gridpoint, and
/// <c>HddCommandScreen_CancelTransmission</c> (<c>0044dbe8</c>) tears the whole thing back down.</para>
///
/// <para><b>What a transmitted order does.</b> It reaches the addressed squadmate's AI through
/// <see cref="SquadOrders.SendToSlot"/>, which is where the eight orders turn into standing squad
/// orders; the comm box's OBJECTIVE: line then reports back what that pilot is <i>doing</i>, which
/// can differ from what they were last told. Built without a <see cref="Sim.SimWorld"/> — a screen
/// with no mission under it — the screen still runs its own state machine and simply has nobody to
/// transmit to.</para>
/// </remarks>
public sealed class HddCommandScreen {
	/// <summary>
	/// <c>STRINGS0.STR</c> group holding the four message-row prompts: <c>SELECT PILOT</c>,
	/// <c>SELECT COMMAND</c>, <c>DESIGNATE LOCATION</c>, <c>DESIGNATE TARGET</c>. The screen picks
	/// between them with <c>HddCommandScreen_SetMessageRow</c> (<c>0044dc44</c>)'s own three-way test.
	/// </summary>
	public const int PromptGroup = 32;

	/// <summary>Prompt shown with no pilot selected.</summary>
	public const int SelectPilotPrompt = 0;

	/// <summary>With a pilot selected and no order armed.</summary>
	public const int SelectCommandPrompt = 1;

	/// <summary>With an order armed that wants a gridpoint.</summary>
	public const int DesignateLocationPrompt = 2;

	/// <summary>With one armed that wants a unit.</summary>
	public const int DesignateTargetPrompt = 3;

	/// <summary>
	/// Coarse ticks between blink toggles — <c>HddCommandScreen_Update</c> (<c>0044c960</c>)'s <c>+ 0x1e</c> against
	/// <c>Time_GetCoarseTicks</c>, whose unit is 16 ms.
	/// </summary>
	public const int BlinkTicks = 30;

	/// <summary>
	/// What an empty comm box's OBJECTIVE: line reads: group 40's <c>FORM UP</c>, the state a squad
	/// that spawned on the player's own formation point is in.
	/// </summary>
	public const int DefaultObjective = 3;

	private double _blinkTicks;

	// The order record's point and subject halves, kept across transmissions — see Transmit.
	private Vec3i _recordPoint;
	private SimObject? _recordSubject;

	/// <param name="view">The map camera, sized to the herc's own map viewport.</param>
	/// <param name="raster">The mission's terrain raster, or null when the zone could not supply one.</param>
	/// <param name="squad">
	/// The squadmates the three comm boxes address, in slot order — the original's
	/// <c>g_SquadmateMachines</c> (<c>004d044c</c>). Fewer than three leaves the remaining boxes empty.
	/// </param>
	/// <param name="world">The simulation a transmitted order is delivered into, or null for none.</param>
	public HddCommandScreen(HddMapView view, HddMapRaster? raster, IReadOnlyList<SimObject> squad,
			SimWorld? world = null) {
		View = view ?? throw new ArgumentNullException(nameof(view));
		Raster = raster;
		Squad = squad ?? Array.Empty<SimObject>();
		World = world;
	}

	/// <summary>The simulation transmitted orders are delivered into, or null.</summary>
	public SimWorld? World { get; }

	/// <summary>The map camera.</summary>
	public HddMapView View { get; }

	/// <summary>The terrain raster under the map.</summary>
	public HddMapRaster? Raster { get; }

	/// <summary>The squadmates the three comm boxes address.</summary>
	public IReadOnlyList<SimObject> Squad { get; }

	/// <summary>
	/// The name across a box, by slot — the gauge's own <c>+0x137</c>, which
	/// <c>HddGauge_LoadPilotFrames</c> fills by walking <c>str\PILOTS.STR</c> to the machine's pilot
	/// index. Supply <see cref="SquadCommChannel.Name"/>; without it a box falls back to the machine's
	/// type name.
	/// </summary>
	public Func<int, string>? PilotNameOf { get; set; }

	/// <summary>Which comm box is selected, or -1.</summary>
	public int SelectedPilot { get; private set; } = -1;

	/// <summary>Which order is armed, or null.</summary>
	public HddOrder? SelectedOrder { get; private set; }

	/// <summary>The object an armed order has been pointed at, or null.</summary>
	public SimObject? ChosenUnit { get; private set; }

	/// <summary>The gridpoint an armed order has been pointed at, or null.</summary>
	public Vec3i? ChosenPoint { get; private set; }

	/// <summary>
	/// The unit the last unit pick landed on — screen <c>+0x15c</c>, where [Enter] drops the map cursor and
	/// [Tab] steps from. Unlike <see cref="ChosenUnit"/> it outlives arming another order; only a transmission,
	/// a cancel, or changing pilot with an order armed clears it.
	/// </summary>
	public SimObject? PickedUnit { get; private set; }

	/// <summary>Whether the blink is in its lit half — <c>DAT_0049d6ad</c>.</summary>
	public bool Blink { get; private set; } = true;

	/// <summary>
	/// Whether the armed order still wants something picked on the map before XMIT will take it — the
	/// state the message row's DESIGNATE prompts announce. Any pick will do, which is the original's one
	/// ready flag at <c>+0x104</c>: DEFEND POSITION is ready on a gridpoint as well as on a unit.
	/// </summary>
	public bool AwaitingPick =>
		SelectedOrder is { } order && WantsPick(order) && ChosenUnit == null && ChosenPoint == null;

	/// <summary>
	/// What <paramref name="slot"/>'s pilot is currently doing, as a group-40 index —
	/// <c>Mech_SquadOrderLineIndex</c>, through <see cref="MechObject.SquadOrderLineIndex"/>.
	/// </summary>
	public int Objective(int slot) =>
		slot >= 0 && slot < Squad.Count && Squad[slot] is MechObject mate
			? mate.SquadOrderLineIndex
			: DefaultObjective;

	/// <summary>Advances the blink. Wall time, not simulation time, exactly as the original's is.</summary>
	public void Update(TimeSpan elapsed) {
		_blinkTicks += elapsed.TotalSeconds / Audio.GameAudio.CoarseTickSeconds;
		while (_blinkTicks >= BlinkTicks) {
			_blinkTicks -= BlinkTicks;
			Blink = !Blink;
		}
	}

	/// <summary>
	/// Selects a comm box, or -1 for none — <c>HddDisplay_SelectPilot</c> (<c>0044a720</c>) and the
	/// <c>HddCommandScreen_SelectPilot</c> (<c>0044da70</c>) it ends in. Selecting a different pilot drops
	/// whatever order was armed for the previous one, which is what the second's first branch does before it
	/// moves the selection.
	///
	/// <para>Selecting a pilot, the one already selected included, has them say
	/// <see cref="SquadOrders.StandingByMessage"/> — the first function's post, which
	/// <see cref="SquadOrders.SendToSlot"/> withdraws. See docs/retail/formats/cockpit-messages.md#what-each-id-says.</para>
	/// </summary>
	public void SelectPilot(int slot) {
		if (slot >= 0 && (slot >= Squad.Count || Squad[slot].Neutralised)) {
			return;
		}

		if (slot != SelectedPilot) {
			if (SelectedOrder != null) {
				PickedUnit = null;
			}

			ClearOrder();
		}

		SelectedPilot = slot;
		if (slot >= 0) {
			World?.Sounds?.SquadSay(SquadOrders.StandingByMessage, Squad[slot]);
		}
	}

	/// <summary>
	/// Arms an order, or clears it — <c>HddCommandScreen_SelectOrder</c> (<c>0044d9cc</c>). The four that want something picked on the
	/// map put the screen into its designate state; the other four are ready to transmit at once.
	/// </summary>
	public void SelectOrder(HddOrder? order) {
		if (order != null && SelectedPilot < 0) {
			return;
		}

		SelectedOrder = order;
		ChosenUnit = null;
		ChosenPoint = null;
	}

	/// <summary>
	/// Steps the armed order one place along the list, wrapping — the <c>,&lt;</c> and <c>.&gt;</c>
	/// keys (<c>HddCommandScreen_PreviousOrder</c> (<c>0044ee60</c>) and <c>HddCommandScreen_NextOrder</c> (<c>0044ee20</c>)). Does nothing with no order armed, which is
	/// both functions' own guard.
	/// </summary>
	public void StepOrder(int delta) {
		if (SelectedOrder is not { } order) {
			return;
		}

		int next = ((int)order + delta) % HddLayout.OrderCount;
		SelectOrder((HddOrder)(next < 0 ? next + HddLayout.OrderCount : next));
	}

	/// <summary>
	/// Resolves a click in the map viewport, at <paramref name="artX"/>/<paramref name="artY"/> device
	/// pixels inside it. With an order armed that wants a target this is the pick
	/// (<c>HddCommandScreen_PickTarget</c>, <c>0044d6b8</c>); otherwise it is a pilot selection, since clicking a squadmate's marker
	/// is one of the three ways the manual gives for choosing who to talk to (<c>HddCommandScreen_PickPilot</c>, <c>0044d804</c>).
	/// </summary>
	/// <param name="objects">Everything live, for the unit hit test.</param>
	public HddMapClick ClickMap(float artX, float artY, IEnumerable<SimObject> objects) {
		ArgumentNullException.ThrowIfNull(objects);
		int worldX = View.ToWorldX(artX);
		int worldY = View.ToWorldY(artY);
		var hit = HitTest(worldX, worldY, objects);

		if (SelectedOrder is { } order && WantsPick(order) && SelectedPilot >= 0) {
			// ATTACK ENEMY takes a hostile, DEFEND POSITION a friendly. DEFEND falling on nothing eligible
			// drops through to the gridpoint; ATTACK picks nothing.
			if (HddCommandState.NeedsUnit(order) && hit != null
				&& (hit.Side == Engine.World.MissionSide.Cybrid) == (order == HddOrder.AttackEnemy)) {
				ChosenUnit = hit;
				ChosenPoint = null;
				PickedUnit = hit;
				return HddMapClick.Picked;
			}

			if (order == HddOrder.AttackEnemy) {
				return HddMapClick.None;
			}

			ChosenUnit = null;
			ChosenPoint = new Vec3i(worldX, worldY, 0);
			return HddMapClick.Picked;
		}

		if (hit != null && HddMap.SquadSlotOf(Squad, hit) is var slot && slot >= 0) {
			SelectPilot(slot);
			return HddMapClick.PilotSelected;
		}

		return HddMapClick.None;
	}

	/// <summary>
	/// [Enter] — <c>HddCommandScreen_KeyDispatch</c> (<c>0044cc40</c>)'s <c>0x1c</c> case, which feeds the map a click
	/// through <c>HddCommandScreen_SynthesizeListClick</c> (<c>0044d598</c>). Only with an order armed that wants a
	/// pick. ATTACK ENEMY and DEFEND POSITION click on <see cref="PickedUnit"/> when there is one; otherwise
	/// DEFEND POSITION, PATROL GRIDPOINT and GOTO GRIDPOINT click under the pointer, and ATTACK ENEMY does nothing.
	/// </summary>
	/// <param name="pointerArtX">The pointer, in device pixels inside the map viewport — not clamped to it.</param>
	/// <param name="pointerArtY">The same on y.</param>
	/// <param name="objects">Everything live, for the unit hit test.</param>
	public HddMapClick PickByEnter(float pointerArtX, float pointerArtY, IEnumerable<SimObject> objects) {
		if (SelectedOrder is not { } order || !WantsPick(order)) {
			return HddMapClick.None;
		}

		// The unit's own position, projected onto the map the way its marker is.
		if (HddCommandState.NeedsUnit(order) && PickedUnit is { } unit) {
			return ClickMap(View.ToScreenX(unit.Position.X), View.ToScreenY(unit.Position.Y), objects);
		}

		return order == HddOrder.AttackEnemy || float.IsNaN(pointerArtX) || float.IsNaN(pointerArtY)
			? HddMapClick.None
			: ClickMap(pointerArtX, pointerArtY, objects);
	}

	/// <summary>
	/// [Tab] — <c>HddCommandScreen_KeyDispatch</c> (<c>0044cc40</c>)'s <c>0x0f</c> case: with ATTACK ENEMY or DEFEND
	/// POSITION armed, steps <see cref="PickedUnit"/> to the next unit
	/// <c>HddCommandScreen_CycleUnit</c> (<c>0044ef08</c>) finds and makes it the pick.
	///
	/// <para>Two departures, both in KNOWN_ISSUES.md. With PATROL GRIDPOINT or GOTO GRIDPOINT armed the original
	/// still makes whatever unit <see cref="PickedUnit"/> holds the pick; this picks nothing there and answers
	/// as the original does with no unit held. And the original's step never ends when the held unit has been
	/// deleted from the live list; this starts from the list's head instead.</para>
	/// </summary>
	/// <param name="objects">The live objects, in list order — <see cref="Sim.SimWorld.Objects"/>.</param>
	/// <returns>
	/// Null with no order armed that wants a pick, where the key does nothing; otherwise whether it picked a
	/// unit, which picks the original's found and not-found sounds.
	/// </returns>
	public bool? CycleUnit(IReadOnlyList<SimObject> objects) {
		ArgumentNullException.ThrowIfNull(objects);
		if (SelectedOrder is not { } order || !WantsPick(order)) {
			return null;
		}

		if (!HddCommandState.NeedsUnit(order)) {
			return false;
		}

		var side = order == HddOrder.AttackEnemy ? Engine.World.MissionSide.Cybrid : Engine.World.MissionSide.Human;
		if (NextEligibleUnit(objects, side) is not { } unit) {
			PickedUnit = null;
			return false;
		}

		PickedUnit = unit;
		ChosenUnit = unit;
		ChosenPoint = null;
		return true;
	}

	/// <summary>
	/// Sends the armed order — the XMIT button and [X]. Refuses, as the original does, when there is
	/// no pilot, no order, or the order still wants something picked. Returns whether the order found a
	/// recipient at all, not whether that recipient agreed to it. The original's XMIT path calls no sound
	/// function; the button's own click is the one known sound (docs/retail/formats/audio.md).
	///
	/// <para><b>The point and subject are the mission's one order record</b> (<c>DAT_004d0458</c>),
	/// which nothing clears: <c>HddCommandScreen_FillOrderRecord</c> (<c>0044db24</c>) writes only the
	/// half the pick names. So a DEFEND POSITION on bare ground carries the unit the record last held,
	/// and the receiver guards that unit. With the tweak off, an order with no pick writes neither half
	/// here; the original writes that pilot's last pick back into the record again, which only changes
	/// which unit a later bare-ground DEFEND POSITION inherits. With
	/// <see cref="TweakSettingDefinitions.FixDefendPositionOrder"/> on, each order is built fresh
	/// instead, and a unit pick also fills the point with the unit's position. See
	/// docs/retail/simulation/ai-squadmates.md, "The order record — 22 bytes".</para>
	/// </summary>
	public bool Transmit() {
		if (SelectedPilot < 0 || SelectedOrder is not { } order || AwaitingPick) {
			return false;
		}

		if (TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.FixDefendPositionOrder)) {
			_recordPoint = ChosenPoint ?? ChosenUnit?.Position ?? default;
			_recordSubject = ChosenUnit;
		} else if (ChosenUnit != null) {
			_recordSubject = ChosenUnit;
		} else if (ChosenPoint is { } point) {
			_recordPoint = point;
		}

		// The screen counts its eight orders from zero; the verb is the group-0 entry they name.
		var message = new SquadOrderMessage(
			(SquadCommand)((int)order + HddLayout.FirstCommandOrder),
			Point: _recordPoint,
			Subject: _recordSubject);

		bool reached = World == null || SquadOrders.SendToSlot(World, Squad, SelectedPilot, message);

		ClearOrder();
		SelectPilot(-1);
		PickedUnit = null;
		return reached;
	}

	/// <summary>
	/// Drops the whole transmission — CANCEL and [Backspace], which is <c>HddCommandScreen_CancelTransmission</c> (<c>0044dbe8</c>): the order,
	/// the pick and the pilot selection all go together.
	/// </summary>
	public void Cancel() {
		ClearOrder();
		SelectedPilot = -1;
		PickedUnit = null;
	}

	/// <summary>This frame's snapshot for the renderer.</summary>
	/// <param name="player">The machine the player is flying.</param>
	/// <param name="objects">Everything live, in the order the marker list wants it.</param>
	/// <param name="route">The player squad's route, for the numbered waypoint markers.</param>
	/// <param name="strings">For the message row and the comm boxes' text.</param>
	public HddCommandState Build(SimObject? player, IReadOnlyList<SimObject> objects,
			IReadOnlyList<Vec3i>? route, StringFile? strings) {
		ArgumentNullException.ThrowIfNull(objects);
		if (player != null) {
			View.Follow(player.Position);
		}

		var markers = HddMap.Markers(objects, player, route, Squad);
		int chosen = -1;
		if (ChosenUnit != null) {
			for (int i = 0; i < markers.Count; i++) {
				if (markers[i].WorldX == ChosenUnit.Position.X && markers[i].WorldY == ChosenUnit.Position.Y) {
					chosen = i;
					break;
				}
			}
		}

		var pilots = new HddPilotSlot[HddLayout.PilotSlotCount];
		for (int i = 0; i < pilots.Length; i++) {
			if (i >= Squad.Count) {
				pilots[i] = new HddPilotSlot(false, string.Empty, 0, DefaultObjective);
				continue;
			}

			var mate = Squad[i];
			pilots[i] = new HddPilotSlot(
				Occupied: true,
				Name: PilotName(i, mate),
				ConditionIndex: ConditionOf(mate),
				OrderIndex: Objective(i));
		}

		return new HddCommandState(View, Raster, markers, pilots, SelectedPilot, SelectedOrder,
			chosen, ChosenPoint, strings?.Text(PromptGroup, PromptIndex), Blink);
	}

	/// <summary>
	/// Which of the four prompts the message row shows — <c>HddCommandScreen_SetMessageRow</c> (<c>0044dc44</c>)'s own derivation, which
	/// asks only whether a pilot is selected and, if an order is armed, which of the two picks it
	/// wants.
	/// </summary>
	public int PromptIndex {
		get {
			if (SelectedPilot < 0) {
				return SelectPilotPrompt;
			}

			if (SelectedOrder is not { } order) {
				return SelectCommandPrompt;
			}

			return order == HddOrder.AttackEnemy ? DesignateTargetPrompt
				: HddCommandState.NeedsPoint(order) || order == HddOrder.DefendPosition
					? DesignateLocationPrompt
					: SelectCommandPrompt;
		}
	}

	// HddCommandScreen_CycleUnit's walk: from the unit after PickedUnit when that unit is of the side wanted,
	// from the head otherwise, wrapping, with PickedUnit itself tested last.
	private SimObject? NextEligibleUnit(IReadOnlyList<SimObject> objects, Engine.World.MissionSide side) {
		int start = 0;
		if (PickedUnit is { } held && held.Side == side) {
			for (int i = 0; i < objects.Count; i++) {
				if (ReferenceEquals(objects[i], held)) {
					start = i + 1;
					break;
				}
			}
		}

		for (int n = 0; n < objects.Count; n++) {
			var candidate = objects[(start + n) % objects.Count];
			if (IsEligibleUnit(candidate, side)) {
				return candidate;
			}
		}

		return null;
	}

	// HddCommandScreen_IsEligibleUnit (0044f014): of the side wanted, not destroyed, on the map, and a hostile
	// known to the player's machine. Its +0x98 test passes for every object: each write of it found stores 1.
	// An object deleted from the original's list, or not yet deployed, is not in it.
	private bool IsEligibleUnit(SimObject candidate, Engine.World.MissionSide side) =>
		!candidate.Removed && !candidate.AwaitingDeployment
			&& candidate.Side == side && !candidate.Destroyed
			&& View.OnViewport(candidate.Position)
			&& (side == Engine.World.MissionSide.Human
				|| (World?.PlayerMech is { } player && AiTargeting.Knows(player, candidate)));

	// The four orders that put the screen into its designate state: screen +0x103, which
	// HddCommandScreen_SelectOrder sets for orders 11-14.
	private static bool WantsPick(HddOrder order) =>
		HddCommandState.NeedsUnit(order) || HddCommandState.NeedsPoint(order);

	private void ClearOrder() {
		SelectedOrder = null;
		ChosenUnit = null;
		ChosenPoint = null;
	}

	/// <summary>
	/// The nearest live object within the marker's own click radius of a world point —
	/// <c>HddCommandScreen_HitTestMarker</c> (<c>0044d860</c>), which converts the click to world units and tests each object against a
	/// radius scaled from <c>5 &lt;&lt; XCoordShift</c> device pixels.
	/// </summary>
	private SimObject? HitTest(int worldX, int worldY, IEnumerable<SimObject> objects) {
		int radius = HddMap.UnitMarkerSize / 2 * View.Scale >> HddMapView.ScaleShift;
		SimObject? best = null;
		long bestDistance = long.MaxValue;

		foreach (var subject in objects) {
			if (subject.Removed || subject.AwaitingDeployment || subject.TargetClass == TargetClass.None) {
				continue;
			}

			long dx = subject.Position.X - (long)worldX;
			long dy = subject.Position.Y - (long)worldY;
			long distance = dx * dx + dy * dy;
			if (Math.Abs(dx) <= radius && Math.Abs(dy) <= radius && distance < bestDistance) {
				bestDistance = distance;
				best = subject;
			}
		}

		return best;
	}

	/// <summary>
	/// The name across a comm box. The original stores a pointer per gauge, filled from the pilot
	/// roster the player's save carries — which this engine does not read, since that file is
	/// VSHELL's. The machine's own type name stands in, so the box has something true to draw.
	/// </summary>
	private string PilotName(int slot, SimObject mate) =>
		PilotNameOf?.Invoke(slot) is { Length: > 0 } pilot ? pilot
			: mate is MechObject mech ? mech.Name.ToUpperInvariant() : $"WING {slot + 1}";

	/// <summary>
	/// <c>HddGauge_ConditionIndex</c>: the mean of the machine's structural readings, bucketed into
	/// group 28's five conditions by the same bands the MFD status screen uses.
	/// </summary>
	private static int ConditionOf(SimObject mate) {
		if (mate.Neutralised) {
			return 4;
		}

		if (mate is not MechObject { Damage: { } damage }) {
			return 0;
		}

		int total = 0;
		int count = Math.Min(HddLayout.StructuralRowCount, damage.Count);
		for (int i = 0; i < count; i++) {
			total += damage.DamagePercent(i);
		}

		return MfdStatusSubject.ConditionFromDamage(count == 0 ? 0 : total / count);
	}
}
