using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// What an AI machine may target and which it picks (docs/es1/simulation/sim-object-base.md#what-an-ai-machine-knows-and-may-target,
/// which links ES2's derivation), and when it lets a target go (docs/es1/simulation/mech-ai.md#targets-and-orders).
/// </summary>
public static class Es1Targets {
	/// <summary>The mask bits: only the group's designated target counts as designated; no "it is shooting at me" weight; no crowding divisor; reject the machine's own class; ignore the bearing.</summary>
	public const short DesignatedOnly = 1, NoShootingWeight = 2, NoCrowding = 4, OtherClassOnly = 0x10, IgnoreBearing = 0x20;

	/// <summary>The group order (<c>+4</c>) under which the group's order target counts as designated: 1, <c>search destroy</c>.</summary>
	public const short DesignatingOrder = 1;

	/// <summary>The script.dat header word 3 under which a squad led by the player may not target its order's object until the data link is done.</summary>
	public const short DataLinkSelector = 3;

	/// <summary>The bearing score's scale, Q10, and the range inside which the nearness score can raise it.</summary>
	public const short BearingScale = 100;

	/// <inheritdoc cref="BearingScale"/>
	public const int NearRange = 25000;

	/// <summary>The shift of the range that divides the bearing term inside <see cref="NearRange"/>.</summary>
	public const int NearShift = 11;

	/// <summary>
	/// <c>Ai_SelectTarget</c>'s weight tables (<c>00091620</c>, <c>00091628</c>, <c>00091630</c>), Q10, indexed by
	/// <see cref="Es1CombatRating.Compare"/>: the candidate holds this machine as its target; its state is not committed;
	/// it holds another target.
	/// </summary>
	private static readonly short[] ShootingWeights = [1500, 2000, 2500], IdleWeights = [600, 400, 200],
		TakenWeights = [700, 850, 1000];

	/// <summary><c>Ai_SelectTarget</c>'s class table (<c>00091638</c>), Q10, indexed by the candidate's class <c>+0x162</c>.</summary>
	private static readonly short[] ClassWeights = [2000, 500, 200, 0];

	/// <summary>
	/// <c>Ai_IsTargetable</c> (<c>ES1DBSIM 0000dce8</c>)(<paramref name="machine"/>, <paramref name="mask"/>,
	/// <paramref name="candidate"/>): the candidate's group's side differs from the machine's, it is neither destroyed
	/// (<c>+0x93</c>) nor collapsed (<c>+0xae</c>), <see cref="Es1SensorModel.KnowsObject"/> holds, and with
	/// <see cref="OtherClassOnly"/> its class differs from the machine's; then it is refused when the machine's group's first
	/// member is the player's machine, header word 3 is <see cref="DataLinkSelector"/>, the candidate is the group's order
	/// object and that member's <c>+0x9a</c> is clear; or when the machine's <c>+0x94</c> is set and the candidate is the
	/// cockpit's selection (<c>CockpitViewInstance</c> <c>+0x20c</c>).
	/// </summary>
	public static bool IsTargetable(Es1Mech machine, short mask, IEs1SimObject candidate, Es1SimGlobals globals) {
		// Retail reads both groups unchecked; an object with no group is not targetable here, an engine guard.
		if (candidate.Group is not { } candidateGroup || machine.Group is not { } group) {
			return false;
		}

		if (candidateGroup.Side == group.Side || candidate.Destroyed || candidate.Collapsed
				|| !Es1SensorModel.KnowsObject(machine, candidate)
				|| ((mask & OtherClassOnly) != 0 && candidate.TargetClass == machine.TargetClass)) {
			return false;
		}

		// Retail reads the first member unchecked; a group with none skips this test here.
		if (Es1Navigation.Leader(machine) is { IsPlayer: true } leader && globals.HeaderWord3 == DataLinkSelector
				&& ReferenceEquals(group.OrderTargetObject(), candidate) && !leader.DataLinkComplete) {
			return false;
		}

		// Retail reads the cockpit unchecked; with none built there is no selection here.
		return !machine.IgnorePlayerSelection || !ReferenceEquals(globals.CockpitViewInstance?.Selection, candidate);
	}

	/// <summary>
	/// <c>Ai_TargetStateTier</c> (<c>0000d880</c>)(<paramref name="simObject"/>): 2 when its behaviour descriptor's flag 5
	/// (<c>+0xd</c>) is set, 1 when its flag 4 (<c>+0xc</c>) is, otherwise 0, and 0 with no descriptor.
	/// </summary>
	public static short TargetStateTier(IEs1SimObject simObject) => simObject.BehaviourDescriptor switch {
		{ Flags: var flags } when flags[5] => 2,
		{ Flags: var flags } when flags[4] => 1,
		_ => 0,
	};

	/// <summary>
	/// <c>Ai_SelectTarget</c> (<c>0000ddf4</c>)(<paramref name="machine"/>, <paramref name="mask"/>): over
	/// <c>LiveObjectList</c>, the candidates <see cref="IsTargetable"/> passes, each given a tier, 2 when designated (the
	/// group's order target under <see cref="DesignatingOrder"/>, or any candidate without <see cref="DesignatedOnly"/>) plus
	/// 1 when it is neither disarmed, immobilized nor destroyed. An immobilized candidate is passed over unless it is the
	/// designated target and the machine's side is not 0, as is one below the tier of the best so far, which starts at 0.
	/// Its score: the bearing term <i>b</i> = <c>0x1000</c> − (|bearing error| ≫ 3), the error 0 with
	/// <see cref="IgnoreBearing"/>; Q10(<see cref="BearingScale"/>, <i>b</i>), raised inside <see cref="NearRange"/> to
	/// <i>b</i> / max(range ≫ <see cref="NearShift"/>, 1); multiplied, Q10, by the three weights its relation to the
	/// machine takes and the class weight; and divided by one more than its holders other than this machine, unless
	/// <see cref="NoCrowding"/>. A higher score or a higher tier makes it the best. Every product is ES1's rounding Q10, cut to
	/// 16 bits.
	/// </summary>
	public static IEs1SimObject? SelectTarget(Es1Mech machine, short mask, Es1SimGlobals globals) {
		// Retail reads the group unchecked; a machine with none finds nothing here, an engine guard.
		if (machine.Group is not { } group) {
			return null;
		}

		IEs1SimObject? best = null;
		short bestScore = 0;
		int bar = 0;
		foreach (var candidate in globals.LiveObjects) {
			if (!IsTargetable(machine, mask, candidate, globals)) {
				continue;
			}

			bool designated = group.IsOrderTarget(candidate) && group.Order == DesignatingOrder;
			int counted = designated || (mask & DesignatedOnly) == 0 ? 1 : 0;
			bool down = candidate.Disarmed || candidate.Immobilized || candidate.Destroyed;
			int tier = counted * 2 + (down ? 0 : 1);
			if (!((designated && group.Side != 0) || !candidate.Immobilized) || tier < bar) {
				continue;
			}

			short error = (short)(Es1MissionGroup.HeadingToward(machine.Position, candidate.Position) - machine.Heading);
			short magnitude = (mask & IgnoreBearing) != 0 ? (short)0 : Es1CombatStates.Magnitude(error);
			short bearingTerm = (short)(0x1000 - (magnitude >> 3));
			short score = (short)SimMath.Q10MultiplyRounded(BearingScale, bearingTerm);
			int range = candidate.Position.ApproxDistanceTo(machine.Position);
			if (range < NearRange) {
				short near = (short)(bearingTerm / Math.Max(range >> NearShift, 1));
				if (score < near) {
					score = near;
				}
			}

			short weighted = score;
			int rating = Es1CombatRating.Compare(machine, candidate, globals);
			if ((mask & NoShootingWeight) == 0 && ReferenceEquals(candidate.Target, machine)) {
				weighted = (short)SimMath.Q10MultiplyRounded(score, ShootingWeights[rating]);
			}

			if (candidate.BehaviourDescriptor is { } descriptor && !descriptor.Flags[1]) {
				weighted = (short)SimMath.Q10MultiplyRounded(weighted, IdleWeights[rating]);
			}

			if (candidate.Target != null && !ReferenceEquals(candidate.Target, machine)) {
				weighted = (short)SimMath.Q10MultiplyRounded(weighted, TakenWeights[rating]);
			}

			// Retail indexes the class table unchecked; a class past its four entries weighs 0 here, an engine guard.
			short classWeight = candidate.TargetClass is >= 0 and < 4 ? ClassWeights[candidate.TargetClass] : (short)0;
			weighted = (short)SimMath.Q10MultiplyRounded(weighted, classWeight);
			short holders = (short)(candidate.TargetHolders - (ReferenceEquals(machine.Target, candidate) ? 1 : 0));
			if ((mask & NoCrowding) == 0 && holders != 0) {
				weighted = (short)(weighted / (holders + 1));
			}

			if (bestScore < weighted || bar < tier) {
				best = candidate;
				bestScore = weighted;
				bar = tier;
			}
		}

		return best;
	}

	/// <summary>
	/// <c>Ai_ShouldAbandonTarget</c> (<c>000162b4</c>)(<paramref name="mech"/>): under squad verb 4 with an order target,
	/// whether <see cref="TargetStateTier"/> of the machine's target exceeds the order's threshold <c>+0x1e4</c>, compared
	/// signed; for the group's order target, whether it is destroyed; for any other, whether it is disarmed, immobilized or
	/// destroyed.
	/// </summary>
	public static bool ShouldAbandonTarget(Es1Mech mech) {
		// Retail reads the target unchecked; with none it is let go here, an engine guard.
		if (mech.Target is not { } target) {
			return true;
		}

		if (mech.SquadVerb == Es1NavigationStates.EngageSquadVerb && mech.SquadTarget != null) {
			return TargetStateTier(target) > mech.SquadThreshold;
		}

		// Retail reads the group unchecked; with none the target is not the group's here.
		if (mech.Group?.IsOrderTarget(target) == true) {
			return target.Destroyed;
		}

		return target.Disarmed || target.Immobilized || target.Destroyed;
	}

	/// <summary>
	/// <c>Ai_ClearSquadEngageOrder</c> (<c>00016294</c>)(<paramref name="mech"/>): the squad verb cleared when it is 4,
	/// whatever the order's target.
	/// </summary>
	public static void ClearSquadEngageOrder(Es1Mech mech) {
		if (mech.SquadVerb == Es1NavigationStates.EngageSquadVerb) {
			mech.SquadVerb = 0;
		}
	}
}
