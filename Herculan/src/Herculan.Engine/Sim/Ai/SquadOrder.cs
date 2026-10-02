using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim.Ai;

/// <summary>
/// The eighteen orders the player can transmit to the squad, as <c>STRINGS0.STR</c> group 0 holds
/// them — which is also the verb <c>Mech_ReceiveSquadOrder</c> (<c>00420ad4</c>, mech vtable
/// <c>+0x28</c>) switches on. Entries 0-8 are the MFD's FLASH COMM page and 10-17 are the [F7]
/// command display's own list; the two overlap in meaning but not in code, and the handler gives
/// each pair its own case where they differ.
///
/// <para>Two entries have no text and no case: 6 and 9, and neither screen can send them. See
/// docs/simulation/ai-squadmates.md.</para>
/// </summary>
public enum SquadCommand {
	/// <summary>0 <c>ATTACK MY TARGET</c> — engage whatever the player currently has selected.</summary>
	AttackMyTarget = 0,

	/// <summary>1 <c>IGNORE MY TARGET</c> — leave the player's selection alone and find something else.</summary>
	IgnoreMyTarget = 1,

	/// <summary>2 <c>HELP ME OUT!</c> — engage whatever is nearest to shooting at the player.</summary>
	HelpMeOut = 2,

	/// <summary>3 <c>JOIN ON ME</c> — drop the standing order and go back to formation.</summary>
	JoinOnMe = 3,

	/// <summary>4 <c>SCAN FOR HOSTILES</c> — go to active radar.</summary>
	ScanForHostiles = 4,

	/// <summary>5 <c>FIRE AT WILL</c> — think for yourself instead of waiting on the leader.</summary>
	FireAtWill = 5,

	/// <summary>7 <c>EMCON</c> — go passive.</summary>
	Emcon = 7,

	/// <summary>8 <c>HOLD YOUR FIRE</c> — the counterpart to <see cref="FireAtWill"/>.</summary>
	HoldYourFire = 8,

	/// <summary>10 <c>DISENGAGE</c> — the command display's own form of <see cref="JoinOnMe"/>.</summary>
	Disengage = 10,

	/// <summary>11 <c>ATTACK ENEMY</c> — engage the unit picked on the map.</summary>
	AttackEnemy = 11,

	/// <summary>12 <c>DEFEND POSITION</c> — guard the point or the friendly unit picked on the map.</summary>
	DefendPosition = 12,

	/// <summary>13 <c>PATROL GRIDPOINT</c> — proceed to the picked point, engaging on the way.</summary>
	PatrolGridpoint = 13,

	/// <summary>14 <c>GOTO GRIDPOINT</c> — proceed to the picked point.</summary>
	GotoGridpoint = 14,

	/// <summary>15 <c>JOIN ON ME</c>, the command display's copy.</summary>
	JoinOnMeCommand = 15,

	/// <summary>16 <c>SCAN FOR HOSTILES</c>, the command display's copy.</summary>
	ScanForHostilesCommand = 16,

	/// <summary>17 <c>EMCON</c>, the command display's copy.</summary>
	EmconCommand = 17
}

/// <summary>
/// The 22-byte record the two order dispatchers fill in and hand to every recipient's
/// <c>Mech_ReceiveSquadOrder</c>: a verb at <c>+0x00</c>, the issuer at <c>+0x02</c>, a world point
/// at <c>+0x06</c> and a subject object at <c>+0x12</c>. Both dispatchers stamp
/// <see cref="Issuer"/> themselves rather than trusting the screen that built the rest.
/// </summary>
/// <param name="Verb">The order.</param>
/// <param name="Issuer">Who sent it — always the player's own machine in retail.</param>
/// <param name="Point">The gridpoint the order names, for the three that take one (12, 13, 14).</param>
/// <param name="Subject">The unit the order names, for the two that take one (11, 12).</param>
public readonly record struct SquadOrderMessage(
	SquadCommand Verb,
	SimObject? Issuer = null,
	Vec3i Point = default,
	SimObject? Subject = null);

/// <summary>
/// How a recipient answered. <c>Mech_ReceiveSquadOrder</c> returns a short that is 1 when the order
/// was taken and 0 when it was refused, and the broadcast dispatcher carries that forward to decide
/// who still needs telling and who gets to say anything about it.
/// </summary>
public enum SquadOrderReply {
	/// <summary>Nobody has been sent the order yet.</summary>
	Unsent = 0,

	/// <summary>It reached someone, who refused it.</summary>
	Refused = 1,

	/// <summary>It reached someone, who took it.</summary>
	Accepted = 2
}

/// <summary>
/// The two ways an order leaves the cockpit. Both end in the same per-machine handler; what differs
/// is who hears it.
///
/// <para>Derivation: docs/simulation/ai-squadmates.md.</para>
/// </summary>
public static class SquadOrders {
	/// <summary>
	/// The pilot-and-squad line <see cref="SendToSlot"/> withdraws: <c>0x22</c> <c>STANDING BY...</c>
	/// in the <c>PILOT&lt;bank&gt;.STR</c> catalog.
	/// </summary>
	public const int StandingByMessage = 0x22;

	/// <summary>
	/// <c>Squad_SendOrderToSlot</c> (<c>00431610</c>) — the [F7] command display's XMIT: one named squadmate, addressed by comm
	/// box. A slot that is empty, that holds the player's own machine, or whose pilot is dead takes
	/// nothing.
	///
	/// <para>Whether or not the order is delivered, the slot's machine then has any
	/// <see cref="StandingByMessage"/> about it withdrawn from the pilot-and-squad port.</para>
	/// </summary>
	/// <returns>Whether the order reached a recipient at all — what the XMIT blip is chosen on.</returns>
	public static bool SendToSlot(SimWorld world, IReadOnlyList<SimObject> squad, int slot,
			SquadOrderMessage message) {
		ArgumentNullException.ThrowIfNull(world);
		ArgumentNullException.ThrowIfNull(squad);

		var occupant = slot >= 0 && slot < squad.Count ? squad[slot] : null;
		bool delivered = false;

		if (occupant is MechObject mate && !ReferenceEquals(mate, world.PlayerMech) && !mate.Destroyed) {
			mate.ReceiveSquadOrder(world, message with { Issuer = world.PlayerMech }, SquadOrderReply.Unsent);
			delivered = true;
		}

		world.Sounds?.SquadUnsay(StandingByMessage, occupant);
		return delivered;
	}

	/// <summary>
	/// <c>Squad_BroadcastOrder</c> (<c>004231a4</c>), reached through <c>Squad_BroadcastOrderFromCockpit</c> (<c>0043166c</c>) — the MFD FLASH COMM page: the whole
	/// of the player's group, best-suited machine first.
	///
	/// <para>Each pass scores every member that has not yet been told with
	/// <see cref="Suitability"/>, keeps the highest score and breaks ties on range to the issuer, and
	/// sends to that one. The reply so far is carried into the next send, which is what makes only
	/// the first recipient — or the first one that accepts — say anything on the radio. <b>The two
	/// orders that name a single target stop at the first acceptance</b>; the rest go round until
	/// everyone has been told.</para>
	/// </summary>
	/// <returns>Whether anyone took it.</returns>
	public static bool Broadcast(SimWorld world, MissionGroup? group, SquadOrderMessage message) {
		ArgumentNullException.ThrowIfNull(world);

		if (group == null) {
			return false;
		}

		message = message with { Issuer = world.PlayerMech };
		var told = new bool[group.Members.Count];
		var reply = SquadOrderReply.Unsent;

		while (true) {
			MechObject? best = null;
			int bestSlot = -1;
			int bestScore = -1;
			int bestRange = int.MaxValue;

			for (int i = 0; i < group.Members.Count; i++) {
				if (told[i] || group.Members[i] is not MechObject member || member.Destroyed
						|| ReferenceEquals(member, message.Issuer)) {
					continue;
				}

				int score = Suitability(message.Verb, member);

				if (score < bestScore) {
					continue;
				}

				int range = message.Issuer is { } issuer
					? member.Position.ApproxDistanceTo(issuer.Position)
					: 0;

				if (range < bestRange || score > bestScore) {
					bestScore = score;
					bestRange = range;
					bestSlot = i;
					best = member;
				}
			}

			if (best == null) {
				break;
			}

			bool accepted = best.ReceiveSquadOrder(world, message, reply);

			if (accepted) {
				reply = SquadOrderReply.Accepted;
			} else if (reply == SquadOrderReply.Unsent) {
				reply = SquadOrderReply.Refused;
			}

			told[bestSlot] = true;

			if (message.Verb is SquadCommand.AttackMyTarget or SquadCommand.HelpMeOut
					&& reply == SquadOrderReply.Accepted) {
				break;
			}
		}

		return reply == SquadOrderReply.Accepted;
	}

	/// <summary>
	/// How well a machine suits an order, which is the only thing that puts the group in an order of
	/// preference. Eight of the eighteen verbs score at all: the three that want someone free to take a
	/// fight prefer a machine that is <b>not</b> committed, the three that end one prefer a machine
	/// that <b>is</b>, and the two radar orders prefer a machine not already in the mode being asked
	/// for. Everything else scores zero for everyone, so the group is simply walked in range order.
	///
	/// <para>The original's score is a stack local it only assigns inside that switch, so for a verb
	/// with no case every member carries whatever the stack held before the call. That ties them all
	/// and walks the group in range order when the leftover is -1 or more, and tells nobody when it
	/// is below. Zero takes the first outcome without reading uninitialised memory; which one retail
	/// lands on is open — docs/simulation/ai-squadmates.md, "Open".</para>
	/// </summary>
	private static int Suitability(SquadCommand verb, MechObject member) {
		bool committed = member.Behaviour.State is { Committed: true };

		return verb switch {
			SquadCommand.AttackMyTarget or SquadCommand.HelpMeOut or SquadCommand.FireAtWill =>
				committed ? 0 : 1,
			SquadCommand.IgnoreMyTarget or SquadCommand.HoldYourFire or SquadCommand.JoinOnMeCommand =>
				committed ? 1 : 0,
			SquadCommand.ScanForHostiles => member.Scanner ? 0 : 1,
			SquadCommand.Emcon => member.Scanner ? 1 : 0,
			_ => 0
		};
	}
}
