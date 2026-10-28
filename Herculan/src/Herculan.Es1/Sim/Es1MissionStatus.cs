using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// <c>Mission_Box</c> (<c>ES1DBSIM 000af7a4</c>), four <c>int32</c>: the least X, least Y, greatest X and greatest Y
/// (docs/es1/simulation/mission-status.md#the-mission-box).
/// </summary>
public readonly record struct Es1MissionBox(int MinX, int MinY, int MaxX, int MaxY) {
	/// <summary>
	/// The box <c>DBSim_LoadScriptDat</c> (<c>0001d850</c>) leaves from block 1's <paramref name="points"/>, in their order:
	/// each point's X and Y widen it where they lie past it (<c>0001dc99</c>–<c>0001dcda</c>). The box lies past the image's
	/// file bytes, so it starts at {0, 0, 0, 0}, and the load's four stores are the only ones a search of the disassembly
	/// by the box's addresses finds, so the box always holds the origin.
	/// </summary>
	public static Es1MissionBox Of(IEnumerable<Vec3i> points) {
		int minX = 0, minY = 0, maxX = 0, maxY = 0;
		foreach (var point in points) {
			if (point.X < minX) {
				minX = point.X;
			}

			if (point.Y < minY) {
				minY = point.Y;
			}

			if (maxX < point.X) {
				maxX = point.X;
			}

			if (maxY < point.Y) {
				maxY = point.Y;
			}
		}

		return new Es1MissionBox(minX, minY, maxX, maxY);
	}

	/// <summary>
	/// <c>Mission_IsOutsideBox</c> (<c>0000f6b8</c>)(<paramref name="margin"/>, <paramref name="point"/>): false when the
	/// point's X and Y lie strictly inside the box widened by the margin on every side, otherwise true.
	/// </summary>
	public bool IsOutside(int margin, Vec3i point) =>
		!(MinX - margin < point.X && point.X < MaxX + margin && MinY - margin < point.Y && point.Y < MaxY + margin);
}

/// <summary>
/// The mission status and its poll (docs/es1/simulation/mission-status.md#the-status--mission_status-0000f4e8,
/// docs/es1/simulation/mission-status.md#the-poll--mission_pollstatus-0000f454): <c>Mission_Status</c>
/// (<c>ES1DBSIM 0000f4e8</c>), <c>Mission_StatusForAlert</c> (<c>0000f43c</c>), <c>Mission_PollStatus</c>
/// (<c>0000f454</c>) and the two box tests, on the globals they read and write.
/// </summary>
public static class Es1MissionStatus {
	/// <summary>The statuses for the machine destroyed, immobilized, and immobilized and then destroyed by going out of the fight.</summary>
	public const short Destroyed = 2, Immobilized = 3, Captured = 0x12;

	/// <summary>The statuses the box tests give: 60000 past the box, 170000 past it.</summary>
	public const short NearingEdge = 7, AreaLeft = 8;

	/// <summary>The statuses for not clear in the group-word arm or with the test unmet, clear in the group-word arm, the test met clear, and the test met not clear.</summary>
	public const short AtRisk = 4, Failed = 6, Complete = 9, CompleteAtRisk = 10;

	/// <summary>The group order (<c>+4</c>) that brings in the group-word arm, <c>guarding</c>.</summary>
	public const short GuardOrder = 6;

	/// <summary>The condition from which the group-word arm decides: the squad's target badly damaged.</summary>
	public const short BadlyDamaged = 3;

	/// <summary><c>SimObject_IsFarOutsideMissionBox</c> (<c>0000f704</c>)'s and <c>SimObject_IsOutsideMissionBox</c> (<c>0000f724</c>)'s margins.</summary>
	public const int FarMargin = 170000, NearMargin = 60000;

	/// <summary>What the poll loads the poll interval's and the alert delay's counters with.</summary>
	public const short PollInterval = 10000, AlertDelay = 10000;

	/// <summary>
	/// <c>Mission_StatusBySelector</c> (<c>00091728</c>), 10 <c>int16</c>: the status for a selector whose test is unmet
	/// with the machine clear of threats.
	/// </summary>
	public static readonly short[] StatusBySelector = [12, 11, 11, 15, 13, 14, 16, 14, 16, 16];

	/// <summary>
	/// <c>Mission_StatusWorthRaising</c> (<c>00091700</c>), 20 <c>int16</c>: non-zero for the statuses the poll passes on,
	/// 2, 3, 6, 7, 8, 9 and <c>0x12</c>.
	/// </summary>
	public static readonly short[] StatusWorthRaising = [0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0];

	/// <summary>
	/// <c>Mission_Status</c>(<paramref name="machine"/>, <paramref name="quiet"/>, <paramref name="selector"/>): 2 when
	/// the machine is destroyed; 3 when immobilized; when its group's order is <see cref="GuardOrder"/> and the group's
	/// target's condition (<see cref="Es1MissionGroup.TargetCondition"/>) is <see cref="BadlyDamaged"/> or worse,
	/// <see cref="Failed"/> when the machine is clear of threats (<see cref="Es1MechDamage.IsClearOfThreats"/>) and
	/// <see cref="AtRisk"/> otherwise; unless quiet, <see cref="AreaLeft"/> past <see cref="FarMargin"/> and
	/// <see cref="NearingEdge"/> past <see cref="NearMargin"/>; otherwise by the selector's test (<see cref="SelectorHolds"/>),
	/// <see cref="Complete"/> or <see cref="CompleteAtRisk"/> when it holds, and when it does not
	/// <see cref="StatusBySelector"/>[selector] when clear and <see cref="AtRisk"/> otherwise.
	/// <para>A selector outside 0–9 holds nothing, and retail reads its clear status past the table's ten words, from the
	/// jump table after it; no retail mission carries one (docs/es1/simulation/mission-status.md#the-status--mission_status-0000f4e8),
	/// and here it answers −1, no status, an engine guard. Retail reads the machine's group unchecked; with none, the
	/// group-word arm and the selector tests that read the group do not hold here, an engine guard.</para>
	/// </summary>
	public static short Status(Es1Mech machine, bool quiet, short selector, Es1SimGlobals globals) {
		if (machine.Destroyed) {
			return Destroyed;
		}

		if (machine.Immobilized) {
			return Immobilized;
		}

		var group = machine.Group;
		if (group is { Order: GuardOrder } && group.TargetCondition() >= BadlyDamaged) {
			return Es1MechDamage.IsClearOfThreats(machine, globals) ? Failed : AtRisk;
		}

		if (!quiet && globals.MissionBox.IsOutside(FarMargin, machine.Position)) {
			return AreaLeft;
		}

		if (!quiet && globals.MissionBox.IsOutside(NearMargin, machine.Position)) {
			return NearingEdge;
		}

		if (SelectorHolds(machine, group, selector, globals)) {
			return Es1MechDamage.IsClearOfThreats(machine, globals) ? Complete : CompleteAtRisk;
		}

		if (!Es1MechDamage.IsClearOfThreats(machine, globals)) {
			return AtRisk;
		}

		return (uint)selector < (uint)StatusBySelector.Length ? StatusBySelector[selector] : (short)-1;
	}

	/// <summary>
	/// The selector's test, the jump table <c>0009173c</c> behind an unsigned bound against 9: 0, the machine's
	/// <c>+0x99</c> (<see cref="Es1Mech.ObjectiveReached"/>); 1 and 2, no waypoint after the group's route cursor; 3, its
	/// <c>+0x9a</c> (<see cref="Es1Mech.DataLinkComplete"/>); 4 and 5, <see cref="Es1MissionGroup.TargetArrivedAndClear"/>;
	/// 6, 8 and 9, a target condition of 4; 7, <see cref="Es1MissionGroup.TargetRouteExhausted"/> and then
	/// <see cref="Es1MissionGroup.TargetArrivedAndClear"/>; any other selector, nothing.
	/// </summary>
	private static bool SelectorHolds(Es1Mech machine, Es1MissionGroup? group, short selector, Es1SimGlobals globals) =>
		selector switch {
			0 => machine.ObjectiveReached,
			1 or 2 => group != null && group.WaypointAt(group.RouteCursor + 1) == null,
			3 => machine.DataLinkComplete,
			4 or 5 => group != null && group.TargetArrivedAndClear(globals),
			6 or 8 or 9 => group != null && group.TargetCondition() == Es1MissionGroup.ConditionLost,
			7 => group != null && group.TargetRouteExhausted() && group.TargetArrivedAndClear(globals),
			_ => false,
		};

	/// <summary>
	/// <c>Mission_StatusForAlert</c>(<paramref name="quiet"/>, <paramref name="machine"/>): <see cref="Status"/> with header
	/// word 3 (<see cref="Es1SimGlobals.HeaderWord3"/>) as the selector.
	/// </summary>
	public static short StatusForAlert(bool quiet, Es1Mech machine, Es1SimGlobals globals) =>
		Status(machine, quiet, globals.HeaderWord3, globals);

	/// <summary>Whether <see cref="StatusWorthRaising"/> marks <paramref name="status"/>; retail indexes it unchecked, and a status outside it is not here.</summary>
	public static bool WorthRaising(short status) =>
		(uint)status < (uint)StatusWorthRaising.Length && StatusWorthRaising[status] != 0;

	/// <summary>
	/// <c>Mission_PollStatus</c>(<paramref name="machine"/>): <c>Math_CountdownTimerTick</c> on the alert delay's counter
	/// (<see cref="Es1SimGlobals.MissionAlertCountdown"/>) and then on the poll interval's
	/// (<see cref="Es1SimGlobals.MissionPollCountdown"/>); unless the interval has run out, −1. Otherwise the status
	/// <see cref="StatusForAlert"/>(false, machine): when it is worth raising (<see cref="WorthRaising"/>) and is not
	/// <see cref="Es1SimGlobals.MissionStatusRaised"/>, the alert delay armed when <see cref="Es1SimGlobals.MissionAlertArmed"/>
	/// is clear (the flag set and the delay's counter loaded with <see cref="AlertDelay"/>), and the status returned and
	/// recorded as raised when the delay's counter is 0 or the status is <see cref="Destroyed"/>. Otherwise the interval's
	/// counter is loaded with <see cref="PollInterval"/> and −1 returned. A status returned leaves the interval run out, so
	/// the next tick asks again.
	/// </summary>
	public static short Poll(Es1Mech machine, Es1SimGlobals globals) {
		var clock = globals.Clock;
		clock.CountdownTimerTick(ref globals.MissionAlertCountdown);
		if (clock.CountdownTimerTick(ref globals.MissionPollCountdown) != 0) {
			return -1;
		}

		short status = StatusForAlert(false, machine, globals);
		if (WorthRaising(status) && status != globals.MissionStatusRaised) {
			if (!globals.MissionAlertArmed) {
				globals.MissionAlertArmed = true;
				globals.MissionAlertCountdown = AlertDelay;
			}

			if (globals.MissionAlertCountdown == 0 || status == Destroyed) {
				globals.MissionStatusRaised = status;
				return status;
			}
		}

		globals.MissionPollCountdown = PollInterval;
		return -1;
	}
}
