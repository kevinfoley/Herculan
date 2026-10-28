using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// <c>Mech_AiSelectBehaviour</c> (<c>ES1DBSIM 0001892c</c>), the reassess of <c>deciding</c> and of the states a group's
/// order installs (docs/es1/simulation/mech-ai.md#choosing-a-state--mech_aiselectbehaviour-0001892c).
/// </summary>
public static class Es1StateChoice {
	/// <summary>The twist limit, the type record's <c>+0x22</c>, above which <c>travelling</c>'s order installs <c>bulldog travel</c>.</summary>
	public const short BulldogTwistLimit = 0x7d00;

	/// <summary>
	/// The reassess: <c>player</c> for the player's machine (<c>+0x9d</c>); any other takes the state its group's order
	/// (<c>+4</c>) names (<see cref="StateForOrder"/>), installed through <c>Behaviour_SetState</c>. Then the target
	/// released (<see cref="Es1Mech.ReleaseTarget"/>); it returns 1. The second pass sets <c>+0x9d</c> after the
	/// constructor has installed <c>deciding</c> (docs/es1/formats/script-dat.md#the-second-pass), so this is how the
	/// player's machine reaches <c>player</c>.
	/// </summary>
	public static short SelectBehaviour(Es1Mech mech, Es1SimGlobals globals) {
		if (mech.IsPlayer) {
			mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Player], globals.DwellJitter);
		} else {
			// Retail reads the group through +0x3f unchecked; a machine with no group is taken as one whose order has no
			// case, an engine guard.
			int? state = mech.Group is { } group ? StateForOrder(group.Order, mech.Type.Chassis.TorsoTwistLimit) : null;
			if (state is { } index) {
				mech.Behaviour.SetState(Es1MechBehaviour.States[index], globals.DwellJitter);
			}

			if (state is null or Es1MechBehaviour.Ramming) {
				// PLACEHOLDER: an order with no case installs whatever EDI held at the call, which no retail mission's load
				// leaves on a HERC group (docs/es1/simulation/mech-ai.md#choosing-a-state--mech_aiselectbehaviour-0001892c);
				// here the machine stays in deciding, which has no move. And ramming's move, Mech_BehaviourRamTick, is plan
				// step 8e's. Neither runs Mech_PlaceLegsOnGround, so the shadows are placed here in its place, or they would
				// draw at the world's origin from their build depth.
				Es1Footfalls.PlaceLegsOnGround(mech);
			}
		}

		mech.ReleaseTarget();
		return 1;
	}

	/// <summary>
	/// The jump table <c>Mech_AiSelectBehaviourVerbTable</c> (<c>00091d14</c>) over the orders 0–11: 1 <c>search
	/// destroy</c>, 3 <c>ramming</c>, 6 <c>guarding</c>, 7 and 8 <c>patrolling</c>, 10 <c>sleeping</c>, 11
	/// <c>travelling</c>, or <c>bulldog travel</c> when <paramref name="twistLimit"/> is above
	/// <see cref="BulldogTwistLimit"/>; null for an order with no case.
	/// </summary>
	public static int? StateForOrder(short order, short twistLimit) => order switch {
		1 => Es1MechBehaviour.SearchDestroy,
		3 => Es1MechBehaviour.Ramming,
		6 => Es1MechBehaviour.Guarding,
		7 or 8 => Es1MechBehaviour.Patrolling,
		10 => Es1MechBehaviour.Sleeping,
		11 => twistLimit > BulldogTwistLimit ? Es1MechBehaviour.BulldogTravel : Es1MechBehaviour.Travelling,
		_ => null,
	};
}
