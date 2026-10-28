using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The <see cref="Settings.TweakSettingDefinitions.StrandedStingrayClosesIn"/> tweak — <b>this engine's own</b>, a
/// fix for one retail mission. <c>C2_05</c>'s lone Cybrid Stingray (group 188) hunts the Raptor II prototype from the
/// start, but walks a closed loop in the map's north-east corner that never runs out, so it never comes near the
/// prototype and counts as a threat to it at any range: the escort objective cannot be met until the player finds
/// and destroys it. See docs/retail/campaign-continuity.md#chapter-2--delta-sector.
///
/// <para>With the tweak on, once the mission's own action 140 fires — the prototype within 100000 units of the point
/// the player's squad starts from, beside the base — the Stingray's leader walks at the prototype instead of along
/// its loop (<see cref="MissionGroup.ClosesInOn"/>). Everything else is retail's: it finds and fights the prototype
/// as any search/destroy machine does, and it counts as a threat until it is out of the fight, so the objective still
/// needs it dealt with.</para>
///
/// <para><b>The mission is recognised by its data, not its name</b>, because <c>script.dat</c> names no mission:
/// the group's two orders and the exact coordinates of their routes, then the action that watches the prototype.
/// None of those points or routes carries a random table key, so every load of the mission places them the same, and v1.0
/// and v1.10 carry the same records; v1.10's one change to the group, its row-16 <c>0x08</c>, is a field this match
/// does not look at. A mission matches only by carrying this group as <c>C2_05</c> does.</para>
/// </summary>
internal static class StrandedStingrayFix {
	/// <summary>Route 78, the loop group 188 walks: points 65, 66 and 65 again.</summary>
	private static readonly (int X, int Y)[] Loop = { (699554, 506270), (678260, 498080), (699554, 506270) };

	/// <summary>Route 79, its unused second order's: point 66 to point 45, the rendezvous.</summary>
	private static readonly (int X, int Y)[] Approach = { (678260, 498080), (678260, 201602) };

	/// <summary>
	/// Finds the group and the action at load and joins them. Bound whatever the tweak says, so turning it on during
	/// the mission takes effect; <see cref="MissionGroup.ClosingInPoint"/> reads the tweak.
	/// </summary>
	public static void Bind(SimWorld world) {
		foreach (var group in world.Groups) {
			if (IsStrandedHunter(group) && HomecomingAction(world, group.OrderSubjectObject!) is { } action) {
				group.ClosesInOn = action;
			}
		}
	}

	/// <summary>
	/// One Cybrid mech hunting a mech on route 78, with a second hunt of the same mech on route 79 and nothing after
	/// it. Read at load, while the group is still on its first order.
	/// </summary>
	private static bool IsStrandedHunter(MissionGroup group) {
		var orders = group.Orders;
		if (group.Kind != MissionUnitKind.Mech || group.Side != MissionSide.Cybrid || group.Members.Count != 1
				|| group.OrderIndex != 0 || group.OrderSubjectObject == null || orders.Count < 2
				|| orders[0] is not { Verb: MissionOrder.VerbSearchDestroy, SubjectKind: MissionOrderSubject.Mech } first
				|| orders[1] is not { Verb: MissionOrder.VerbSearchDestroy, SubjectKind: MissionOrderSubject.Mech } second
				|| second.SubjectRef != first.SubjectRef
				|| !Matches(first.Route, Loop) || !Matches(second.Route, Approach)) {
			return false;
		}

		for (int slot = 2; slot < orders.Count; slot++) {
			if (orders[slot] != null) {
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Action 140: the one type-7 trigger on the hunted mech itself, a single circle. Its centre is not compared: the
	/// mission carries two unconditioned records of its point, and the group has already pinned the mission.
	/// </summary>
	private static MissionActionState? HomecomingAction(SimWorld world, SimObject quarry) {
		MissionActionState? found = null;
		foreach (var action in world.Mission.Actions) {
			if (action.Record.Type == MissionAction.SubjectTargetMech && ReferenceEquals(action.TargetObject, quarry)
					&& action.Record.Areas is [{ Shape: MissionTriggerShape.Circle }]) {
				if (found != null) {
					return null;
				}

				found = action;
			}
		}

		return found;
	}

	private static bool Matches(IReadOnlyList<Vec3i> route, (int X, int Y)[] expected) {
		if (route.Count != expected.Length) {
			return false;
		}

		for (int i = 0; i < expected.Length; i++) {
			if ((route[i].X, route[i].Y) != expected[i]) {
				return false;
			}
		}

		return true;
	}
}
