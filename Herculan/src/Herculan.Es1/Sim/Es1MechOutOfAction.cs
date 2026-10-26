using Herculan.Engine.Numerics;
using Herculan.Es1.Messages;

namespace Herculan.Es1.Sim;

/// <summary>
/// How the damage write puts a HERC out of the fight (docs/es1/simulation/mech.md#the-damage-write--mech_componentdamagewrite-00013250):
/// <c>Mech_ComponentDamageWrite</c>'s (<c>ES1DBSIM 00013250</c>) steps 5 to 8, the legs, the disabled machine and its
/// death, and what the attacker is told, <c>Mech_CreditNeutralisedTarget</c> (<c>00010f2c</c>).
/// </summary>
public static class Es1MechOutOfAction {
	/// <summary>The internals of the two leg sides, and of the rear pair on a chassis of more than two legs.</summary>
	public const int LeftLegInternal = 0, RightLegInternal = 1, RearLeftLegInternal = 10, RearRightLegInternal = 11;

	/// <summary>The legs that average their sides with the rear pair's.</summary>
	public const int FourLegs = 4;

	/// <summary>The averaged side past which <see cref="Es1Mech.LegsDamaged"/> is set, and from which <see cref="Es1Mech.LegsCrippled"/> is.</summary>
	public const short LegsDamagedReading = 0x80, LegsCrippledReading = 0xc1;

	/// <summary>The pilot's internal, which kills at a full reading.</summary>
	public const int PilotInternal = 9;

	/// <summary>The cockpit components, either of which kills when fully destroyed.</summary>
	public const int CockpitComponent0 = 0, CockpitComponent1 = 1;

	/// <summary>The computer messages for a machine the player disabled and for one the player destroyed.</summary>
	public const short DisabledMessage = 7, DestroyedMessage = 3;

	/// <summary>Where a machine whose record's <c>+0x4c</c> is set goes on its death, out of the world.</summary>
	public const int LimboDepth = -100000;

	/// <summary>The squad messages of a squadmate's kill, of the player's kill, and of a squadmate disabled and destroyed.</summary>
	public const short SquadmateKillCall = 2, PlayerKillCall = 5, DisabledCry = 4, DestroyedCry = 0x25;

	/// <summary>The reach within which the squadmate nearest the player calls out the player's kill.</summary>
	public const int PlayerKillCallRange = 30000;

	/// <summary>
	/// Step 5, the legs, on <paramref name="machine"/>'s <paramref name="header"/> after a write by
	/// <paramref name="attacker"/>: the readings of <see cref="LeftLegInternal"/> and <see cref="RightLegInternal"/>, and
	/// of <see cref="RearLeftLegInternal"/> and <see cref="RearRightLegInternal"/> for a record whose <c>+0x4a</c> is above
	/// 2; for each leg up to that count reading full, its shadow destroyed and its slot emptied. Then, while not
	/// immobilized and with legs: half the legs or more reading full immobilizes the machine, through
	/// <see cref="Disable"/>; otherwise the two sides, each averaged with its rear leg on four legs, set
	/// <see cref="Es1Mech.LegsCrippled"/> when either is <see cref="LegsCrippledReading"/> or more, and otherwise
	/// <see cref="Es1Mech.LegsDamaged"/> when either is past <see cref="LegsDamagedReading"/>. Past four legs retail reads
	/// readings it never wrote; those read 0 here, an engine guard.
	/// </summary>
	public static void Legs(Es1Mech machine, IEs1SimObject? attacker, Es1DamageHeader header, Es1SimGlobals globals) {
		int legs = machine.Type.Chassis.LegCount;
		var readings = new short[Math.Max(legs, FourLegs)];
		readings[0] = header.InternalReading(LeftLegInternal);
		readings[1] = header.InternalReading(RightLegInternal);
		if (legs > 2) {
			readings[2] = header.InternalReading(RearLeftLegInternal);
			readings[3] = header.InternalReading(RearRightLegInternal);
		}

		// Retail reads the shadow array past its count for a leg without a shadow; such a leg destroys nothing here.
		for (int i = 0; i < legs; i++) {
			if (readings[i] == Es1DamageHeader.Full && i < machine.Shadows.Count && machine.Shadows[i] is { } shadow) {
				shadow.Destroy();
				machine.Shadows[i] = null;
			}
		}

		if (machine.Immobilized || legs == 0) {
			return;
		}

		short left = readings[0], right = readings[1];
		if (legs == FourLegs) {
			left = (short)(readings[0] + readings[2] >> 1);
			right = (short)(readings[1] + readings[3] >> 1);
		}

		int gone = 0;
		for (int i = 0; i < legs; i++) {
			if (readings[i] == Es1DamageHeader.Full && ++gone >= legs >> 1) {
				Disable(machine, attacker, globals);
				return;
			}
		}

		if (left >= LegsCrippledReading || right >= LegsCrippledReading) {
			machine.LegsCrippled = true;
		} else if (left > LegsDamagedReading || right > LegsDamagedReading) {
			machine.LegsDamaged = true;
		}
	}

	/// <summary>
	/// Step 5's immobilizing branch and steps 6 and 7: while not destroyed, the attacker told
	/// (<see cref="IEs1SimObject.CreditNeutralisedTarget"/>, not already immobilized), the report
	/// (<see cref="Es1Mech.ReportOutOfAction"/>) and <c>disabled</c> installed; then <see cref="Es1Mech.Immobilized"/> set;
	/// <see cref="DisabledMessage"/> posted when the attacker is <c>LocalPlayerMech</c> and the machine its target; and
	/// for <c>LocalPlayerMech</c> itself, <see cref="TurnToCamera"/>.
	/// </summary>
	private static void Disable(Es1Mech machine, IEs1SimObject? attacker, Es1SimGlobals globals) {
		if (!machine.Destroyed) {
			attacker?.CreditNeutralisedTarget(false, machine, globals);
			machine.ReportOutOfAction(globals);
			// PLACEHOLDER: Action_Activate (0001cc5c) of the machine's +0x170 action when it has one, plan step 8.
			machine.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Disabled], globals.DwellJitter);
		}

		machine.Immobilized = true;
		if (attacker != null && TargetedByLocalPlayer(machine, attacker, globals)) {
			Es1MessageTicker.ComputerPostMessage(globals, DisabledMessage);
		}

		if (ReferenceEquals(machine, globals.LocalPlayerMech)) {
			TurnToCamera(machine);
		}
	}

	/// <summary>
	/// Step 7, the player's machine disabled: its heading made the yaw of the camera node (the record's <c>+0xc</c> part,
	/// <see cref="Es1Mech.AimNodeTransform"/>) composed with the machine's frame (<c>Rotation_Concat</c>,
	/// <c>00064fe4</c>, then <c>Transform_RotationToEuler</c>); <see cref="Es1Mech.RotationWord"/> and the twist cleared;
	/// the twist thread seeked to position 0; and root motion run for 1 (<see cref="Es1Mech.ApplyRootMotionIfEnabled"/>).
	/// So the body turns to where the turret pointed and the turret centers. Retail composes a missing node through a null
	/// pointer; the frame alone is taken here, an engine guard.
	/// </summary>
	private static void TurnToCamera(Es1Mech machine) {
		var node = machine.AimNodeTransform() ?? Transform3.Identity;
		var (_, _, heading) = Transform3.Concat(node, machine.Frame).ToEuler();
		machine.Rotation = (machine.Rotation.X, machine.Rotation.Y, heading);
		machine.RotationWord = 0;
		machine.Twist = 0;
		Es1Turret.Seek(machine.TwistThread, machine.Type.Chassis.AnimId_TorsoTwist, 0);
		machine.ApplyRootMotionIfEnabled(1);
	}

	/// <summary>
	/// Step 8, death, while not destroyed: when component <see cref="CockpitComponent0"/> or
	/// <see cref="CockpitComponent1"/> is fully destroyed (<see cref="Es1DamageHeader.IsFullyDestroyed"/>) or
	/// <see cref="PilotInternal"/> reads full. <see cref="Es1Mech.Destroyed"/> set; the attacker told with whether the
	/// machine was already immobilized; the report when it was not; the radar off; <see cref="DestroyedMessage"/> on step
	/// 6's test; then <c>dead</c> installed, or for a record whose <c>+0x4c</c> is set <see cref="Es1Mech.Byte32Set"/>
	/// set, the machine sunk to <see cref="LimboDepth"/>, <c>in limbo</c> installed and every shadow destroyed with the
	/// count zeroed.
	/// </summary>
	public static void Death(Es1Mech machine, IEs1SimObject? attacker, Es1DamageHeader header, Es1SimGlobals globals) {
		if (machine.Destroyed) {
			return;
		}

		if (!header.IsFullyDestroyed(CockpitComponent0) && !header.IsFullyDestroyed(CockpitComponent1)
				&& header.InternalReading(PilotInternal) != Es1DamageHeader.Full) {
			return;
		}

		machine.Destroyed = true;
		bool immobilized = machine.Immobilized;
		attacker?.CreditNeutralisedTarget(immobilized, machine, globals);
		if (!immobilized) {
			machine.ReportOutOfAction(globals);
			// PLACEHOLDER: Action_Activate (0001cc5c) of the machine's +0x170 action when it has one, plan step 8.
		}

		machine.RadarOn = false;
		if (attacker != null && TargetedByLocalPlayer(machine, attacker, globals)) {
			Es1MessageTicker.ComputerPostMessage(globals, DestroyedMessage);
		}

		if (machine.Type.Chassis.VanishesOnDeath == 0) {
			machine.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Dead], globals.DwellJitter);
			return;
		}

		machine.Byte32Set = true;
		machine.Position = new Vec3i(machine.Position.X, machine.Position.Y, LimboDepth);
		machine.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.InLimbo], globals.DwellJitter);
		foreach (var shadow in machine.Shadows) {
			shadow?.Destroy();
		}

		machine.Shadows.Clear();
	}

	/// <summary>
	/// Steps 6's and 8's test: <paramref name="attacker"/> is <c>LocalPlayerMech</c> and that machine's target
	/// <c>+0x15e</c> is <paramref name="machine"/>. Retail reads <c>LocalPlayerMech</c> unchecked; with none, never.
	/// </summary>
	private static bool TargetedByLocalPlayer(Es1Mech machine, IEs1SimObject attacker, Es1SimGlobals globals) =>
		globals.LocalPlayerMech is { } local && ReferenceEquals(attacker, local) && ReferenceEquals(local.Target, machine);

	/// <summary>
	/// <c>Mech_CreditNeutralisedTarget</c> (<c>00010f2c</c>), slot <c>+0x64</c>, (<paramref name="attacker"/>,
	/// <paramref name="alreadyImmobilized"/>, <paramref name="victim"/>) (docs/es1/simulation/mech.md#the-damage-write--mech_componentdamagewrite-00013250):
	/// <list type="number">
	/// <item>for a victim not already immobilized, on a side other than the attacker's group's: when the attacker is in
	/// <c>PlayerMech</c>'s group, for <c>PlayerMech</c> itself the nearest live squadmate
	/// (<see cref="Es1MissionGroup.NearestLiveMember"/>) within <see cref="PlayerKillCallRange"/> of it posts
	/// <see cref="PlayerKillCall"/>, and any other attacker posts <see cref="SquadmateKillCall"/>, each on the pilot
	/// channel (<see cref="Es1PilotChannel.PostSquadMessage"/>, not forced); and the attacker's
	/// <see cref="Es1Mech.KillTallies"/> entry for the victim's class goes up by 1;</item>
	/// <item>a victim in <c>PlayerMech</c>'s group other than <c>PlayerMech</c> cries out, forced:
	/// <see cref="DestroyedCry"/> when destroyed, <see cref="DisabledCry"/> otherwise.</item>
	/// </list>
	/// Retail reads <c>PlayerMech</c> and the two groups unchecked; with no <c>PlayerMech</c> nothing is posted here, and
	/// a machine with no group is on no side.
	/// </summary>
	public static void CreditNeutralisedTarget(Es1Mech attacker, bool alreadyImmobilized, Es1Mech victim, Es1SimGlobals globals) {
		var player = globals.PlayerMech;
		if (!alreadyImmobilized) {
			bool opposed = victim.Group is { } victimGroup && attacker.Group is { } attackerGroup
				&& victimGroup.Side != attackerGroup.Side;
			if (opposed && player != null && ReferenceEquals(attacker.Group, player.Group)) {
				if (!ReferenceEquals(attacker, player)) {
					Es1PilotChannel.PostSquadMessage(globals, attacker, false, SquadmateKillCall);
				} else if (attacker.Group!.NearestLiveMember(attacker) is Es1Mech nearest
						&& player.Position.ApproxDistanceTo(nearest.Position) < PlayerKillCallRange) {
					Es1PilotChannel.PostSquadMessage(globals, nearest, false, PlayerKillCall);
				}
			}

			// Retail indexes the tallies by the class unchecked; a class past them counts nothing here.
			if (opposed && victim.TargetClass >= 0 && victim.TargetClass < attacker.KillTallies.Length) {
				attacker.KillTallies[victim.TargetClass]++;
			}
		}

		if (player != null && ReferenceEquals(victim.Group, player.Group) && !ReferenceEquals(victim, player)) {
			Es1PilotChannel.PostSquadMessage(globals, victim, true, victim.Destroyed ? DestroyedCry : DisabledCry);
		}
	}
}
