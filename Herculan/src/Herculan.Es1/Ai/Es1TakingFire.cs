using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// A HERC shot at: its slot <c>+0x54</c>, <c>Mech_AiOnTakingFire</c> (<c>ES1DBSIM 00019338</c>), and the friendly-fire
/// complaint <c>Mech_AiFriendlyFireComplaint</c> (<c>00019300</c>)
/// (docs/es1/simulation/ai-squadmates.md#taking-fire--mech_aiontakingfire-00019338, which links ES2's derivation).
/// </summary>
public static class Es1TakingFire {
	/// <summary>The complaint countdown's reload, so a squadmate complains at most once in it.</summary>
	public const int ComplaintInterval = 40000;

	/// <summary>The squad messages: friendly fire, and taking fire from a HERC.</summary>
	public const short FriendlyFireMessage = 8, UnderFireMessage = 3;

	/// <summary>The under-fire window's reload; when it runs out the systems tick zeroes <see cref="Es1Mech.DamageInPlayerSquad"/>.</summary>
	public const int UnderFireWindow = 30000;

	/// <summary>The damage a machine of the player's group takes before it reacts to fire.</summary>
	public const int SquadDamageThreshold = 8000;

	/// <summary>The retarget countdown's reload, inside which fire brings no new reaction.</summary>
	public const int RetargetInterval = 10000;

	/// <summary>
	/// <c>Mech_AiFriendlyFireComplaint</c>(<paramref name="mech"/>): with the complaint countdown at 0, it is reloaded with
	/// <see cref="ComplaintInterval"/> and <see cref="FriendlyFireMessage"/> posted on the pilot channel
	/// (<see cref="Messages.Es1PilotChannel.PostSquadMessage"/>, not forced).
	/// </summary>
	public static void FriendlyFireComplaint(Es1Mech mech, Es1SimGlobals globals) {
		if (mech.Countdowns.Complaint == 0) {
			mech.Countdowns.Complaint = ComplaintInterval;
			Messages.Es1PilotChannel.PostSquadMessage(globals, mech, false, FriendlyFireMessage);
		}
	}

	/// <summary>
	/// <c>Mech_AiOnTakingFire</c>(<paramref name="mech"/>, <paramref name="damage"/>, <paramref name="attacker"/>), in order:
	/// <list type="number">
	/// <item>the complaint (<see cref="FriendlyFireComplaint"/>) when the attacker is <c>PlayerMech</c> and the machine is in
	/// its group;</item>
	/// <item>nothing more for an attacker of the machine's own side (the groups' <c>+0x14</c>);</item>
	/// <item>the contact shared (<c>Mech_ShareContact</c>, <see cref="Es1SensorModel.ShareContact"/>) and the under-fire
	/// window reloaded with <see cref="UnderFireWindow"/>;</item>
	/// <item>in <c>PlayerMech</c>'s group, the damage added to <see cref="Es1Mech.DamageInPlayerSquad"/>, nothing more while
	/// that is under <see cref="SquadDamageThreshold"/>;</item>
	/// <item>nothing more for the player's machine (<c>+0x9d</c>), while the retarget countdown runs, or when the attacker
	/// is the target of a committed machine (descriptor flag 1); otherwise the countdown reloaded with
	/// <see cref="RetargetInterval"/>;</item>
	/// <item>a machine not committed in a group whose first member is the player's machine posts
	/// <see cref="UnderFireMessage"/> for a HERC attacker (class 0), and clears <see cref="Es1Mech.IgnorePlayerSelection"/>
	/// when that member's target is the attacker;</item>
	/// <item>by the descriptor's flags: 2 (<c>+0xa</c>), holding a place, makes the standing order 4 with no target in a
	/// group the player's machine leads and, in any group, zeroes the dwell countdown (<c>+0x4c</c>), so the reassess runs on
	/// the next tick; 3 (<c>+0xb</c>), nothing; neither, <see cref="Es1Targets.SelectTarget"/> with mask 0 installed
	/// (<see cref="Es1Mech.InstallTarget"/>), nothing found after holding a target running
	/// <see cref="Es1StateChoice.SelectBehaviour"/>, and a find setting <see cref="Es1Mech.TargetHandedOver"/> and running
	/// <see cref="Es1CombatReassess.EnterCombat"/>.</item>
	/// </list>
	/// </summary>
	public static void OnTakingFire(Es1Mech mech, short damage, IEs1SimObject attacker, Es1SimGlobals globals) {
		// Retail reads PlayerMech's group unchecked; with no PlayerMech the machine is in no player's group here.
		var player = globals.PlayerMech;
		bool playerSquad = player != null && ReferenceEquals(mech.Group, player.Group);
		if (ReferenceEquals(attacker, player) && playerSquad) {
			FriendlyFireComplaint(mech, globals);
		}

		// Retail reads both groups unchecked; with either missing nothing more is done here, an engine guard.
		if (attacker.Group is not { } attackerGroup || mech.Group is not { } group || attackerGroup.Side == group.Side) {
			return;
		}

		Es1SensorModel.ShareContact(mech, attacker, globals);
		mech.Countdowns.UnderFire = UnderFireWindow;
		if (playerSquad) {
			mech.DamageInPlayerSquad += damage;
			if (mech.DamageInPlayerSquad < SquadDamageThreshold) {
				return;
			}
		}

		bool committed = Es1SquadOrders.Flag(mech, 1);
		if (mech.IsPlayer || mech.Countdowns.Retarget != 0 || (ReferenceEquals(mech.Target, attacker) && committed)) {
			return;
		}

		// The machine is a member of its group, so the group has a first member; one missing is taken as no player's here.
		var leader = group.Members.Count > 0 ? group.Members[0] as IEs1SimObject : null;
		bool playerLed = leader?.IsPlayer == true;
		mech.Countdowns.Retarget = RetargetInterval;
		if (!committed && playerLed) {
			if (attacker.TargetClass == 0) {
				Messages.Es1PilotChannel.PostSquadMessage(globals, mech, false, UnderFireMessage);
			}

			if (ReferenceEquals(leader!.Target, attacker)) {
				mech.IgnorePlayerSelection = false;
			}
		}

		if (Es1SquadOrders.Flag(mech, 2)) {
			if (playerLed) {
				mech.SquadVerb = Es1NavigationStates.EngageSquadVerb;
				mech.SquadTarget = null;
			}

			mech.Behaviour.Countdown = 0;
		} else if (!Es1SquadOrders.Flag(mech, 3)) {
			var previous = mech.Target;
			mech.InstallTarget(Es1Targets.SelectTarget(mech, 0, globals));
			if (mech.Target == null) {
				if (previous != null) {
					Es1StateChoice.SelectBehaviour(mech, globals);
				}
			} else {
				mech.TargetHandedOver = true;
				Es1CombatReassess.EnterCombat(mech, globals);
			}
		}
	}
}
