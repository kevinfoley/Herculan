using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// PLACEHOLDER: the AI functions the navigation states call that later parts of plan step 8 port
/// (docs/herculan/plan-es1-first-mission.md#steps): each answers "nothing found" or does nothing, so the part that ports
/// it replaces it here, in one place.
/// </summary>
public static class Es1AiStandIns {
	/// <summary>
	/// PLACEHOLDER: <c>Ai_SelectTarget</c> (<c>0000ddf4</c>)(machine, <paramref name="mask"/>)
	/// (docs/es1/simulation/sim-object-base.md#what-an-ai-machine-knows-and-may-target) finds nothing; plan step 8c.
	/// </summary>
	public static IEs1SimObject? SelectTarget(Es1Mech mech, short mask, Es1SimGlobals globals) => null;

	/// <summary>
	/// PLACEHOLDER: <c>Mech_AiEnterCombat</c> (<c>000174cc</c>), the combat reassess
	/// (docs/es1/simulation/mech-ai.md#the-combat-reassess--mech_aicombatreassess-00016e04), does nothing; plan step 8c.
	/// </summary>
	public static void EnterCombat(Es1Mech mech, Es1SimGlobals globals) {
	}

	/// <summary>
	/// PLACEHOLDER: <c>guarding</c>'s scoring of <c>g_MechPool</c> against the post, from a limit of 980000, or 945000
	/// when the group has a target, finds nothing (docs/es1/simulation/mech-ai.md#navigation); plan step 8c.
	/// </summary>
	public static IEs1SimObject? SelectGuardTarget(Es1Mech mech, Vec3i? post, Es1SimGlobals globals) => null;

	/// <summary>
	/// PLACEHOLDER: <c>guarding</c>'s arm for a target found (docs/es1/simulation/mech-ai.md#navigation) does nothing: the
	/// target installed, <c>fleeing</c> with the aim slot −1 for a machine out of action, otherwise <c>driving off en</c>,
	/// <c>Mech_AiSelectAimComponent</c> (<c>00016cf4</c>) and the radar on unless a squadmate of the player's without
	/// <c>+0xac</c>; plan step 8c.
	/// </summary>
	public static void EngageGuardTarget(Es1Mech mech, IEs1SimObject target, Es1SimGlobals globals) {
	}

	/// <summary>
	/// PLACEHOLDER: <c>Ai_AimAndFire</c> (<c>00018894</c>)(machine, <paramref name="target"/>, <paramref name="aspect"/>)
	/// (docs/es1/simulation/mech-ai.md#weapons) does nothing; plan step 8d.
	/// </summary>
	public static void AimAndFire(Es1Mech mech, IEs1SimObject target, short aspect, Es1SimGlobals globals) {
	}
}
