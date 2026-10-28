using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// PLACEHOLDER: the AI functions the navigation and combat states call that later parts of plan step 8 port
/// (docs/herculan/plan-es1-first-mission.md#steps): each answers "nothing to do" or does nothing, so the part that ports
/// it replaces it here, in one place.
/// </summary>
public static class Es1AiStandIns {
	/// <summary>
	/// PLACEHOLDER: <c>Ai_AimAndFire</c> (<c>00018894</c>)(machine, <paramref name="target"/>, <paramref name="aspect"/>)
	/// (docs/es1/simulation/mech-ai.md#weapons), a null target meaning the machine's own <c>+0x15e</c>, does nothing; plan
	/// step 8d.
	/// </summary>
	public static void AimAndFire(Es1Mech mech, IEs1SimObject? target, short aspect, Es1SimGlobals globals) {
	}

	/// <summary>
	/// PLACEHOLDER: <c>Ai_BeginSkirtIfBlocked</c> (<c>00017bf8</c>)(machine) (docs/es1/simulation/mech-ai.md#skirting), the
	/// gate at the head of the combat thinks, answers 0, so no machine takes <c>skirting</c>: its flag <c>+0xa7</c> is set
	/// only by the shot sweep's slot <c>+0x68</c>, which is not called yet; plan step 8e.
	/// </summary>
	public static bool BeginSkirtIfBlocked(Es1Mech mech, Es1SimGlobals globals) => false;
}
