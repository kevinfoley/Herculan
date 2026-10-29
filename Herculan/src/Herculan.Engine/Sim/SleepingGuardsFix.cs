using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The <see cref="Settings.TweakSettingDefinitions.GuardHoldsUntilDestroyed"/> tweak's reach — <b>this engine's
/// own</b>, a fix for one retail mission. <c>C5_10</c>'s six single-Cybrid groups (162-167) each guard the base and
/// then sleep, and retail moves them on to the sleep the moment they arrive, so they stand still and ignore fire
/// (docs/retail/simulation/ai-goals.md#a-guard-nobody-contests--c5_10s-ring). This marks those six
/// (<see cref="MissionGroup.HoldsGuard"/>), and the tweak holds only a marked group's guard order, so a guard order
/// in any other mission, a custom one included, behaves as retail's whichever way the tweak is set.
///
/// <para><b>The mission is recognised by its data, not its name</b>, as <see cref="StrandedStingrayFix"/> recognises
/// <c>C2_05</c>: one Cybrid mech whose first order guards a group and whose second sleeps on route 52's exact
/// coordinates, with nothing after. All six sleep on that route, its points carry no random table key, and v1.0 and
/// v1.10 carry the same coordinates.</para>
/// </summary>
internal static class SleepingGuardsFix {
	/// <summary>Route 52, the sleep order's: points 15 and 18.</summary>
	private static readonly (int X, int Y)[] SleepRoute = { (1039888, 1498536), (1220653, 1498536) };

	/// <summary>Marks the six groups at load. Marked whatever the tweak says, so turning it on during the mission takes effect.</summary>
	public static void Bind(SimWorld world) {
		foreach (var group in world.Groups) {
			if (IsBaseGuard(group)) {
				group.HoldsGuard = true;
			}
		}
	}

	/// <summary>Read at load, while the group is still on its first order.</summary>
	private static bool IsBaseGuard(MissionGroup group) {
		var orders = group.Orders;
		if (group.Kind != MissionUnitKind.Mech || group.Side != MissionSide.Cybrid || group.Members.Count != 1
				|| group.OrderIndex != 0 || orders.Count < 2
				|| orders[0] is not { Verb: MissionOrder.VerbGuard, SubjectKind: MissionOrderSubject.Group }
				|| orders[1] is not { Verb: MissionOrder.VerbSleep } sleep
				|| !Matches(sleep.Route, SleepRoute)) {
			return false;
		}

		for (int slot = 2; slot < orders.Count; slot++) {
			if (orders[slot] != null) {
				return false;
			}
		}

		return true;
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
