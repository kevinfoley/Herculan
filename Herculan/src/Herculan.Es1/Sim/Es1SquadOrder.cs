namespace Herculan.Es1.Sim;

/// <summary>
/// A squad order record, what <see cref="Es1MissionGroup.BroadcastOrder"/> sends and a HERC's slot <c>+0x2c</c> takes
/// (docs/es1/simulation/mission-groups.md#the-squad-order-broadcast): the verb, an <c>int16</c> at <c>+0</c>, and the
/// issuer at <c>+2</c>. The fields past them, which other verbs read, come with the senders that write them.
/// </summary>
public sealed class Es1SquadOrder {
	/// <summary>The verbs the broadcast and the receiver tell apart: radar on and radar off, the FLASH COMM orders 4 and 7.</summary>
	public const short RadarOnVerb = 4, RadarOffVerb = 7;

	/// <summary>The verbs the receiver takes as <see cref="RadarOnVerb"/> and <see cref="RadarOffVerb"/>.</summary>
	public const short RadarOnVerbAlias = 0x10, RadarOffVerbAlias = 0x11;

	/// <summary><c>+0</c>, the verb: FLASH COMM's stores its row's order as a word.</summary>
	public short Verb { get; set; }

	/// <summary><c>+2</c>, the issuer, which the cockpit's sender stores and the broadcast passes over.</summary>
	public IEs1SimObject? Issuer { get; set; }
}

/// <summary>
/// A HERC's slot <c>+0x2c</c>, the squad order's receiver <c>Mech_ReceiveSquadOrder</c> (<c>ES1DBSIM 0001a1cc</c>)
/// (docs/es1/simulation/mech-ai.md#receiving-a-squad-order--mech_receivesquadorder-0001a1cc): it acts on the order by
/// its verb, answers whether it accepted, and posts its reply on the pilot channel.
/// </summary>
public static class Es1SquadOrders {
	/// <summary>The receiver's reply message ids for radar on (already on, and switched on) and radar off.</summary>
	public const byte RadarAlreadyOnMessage = 0xf, RadarOnMessage = 0x26, RadarOffMessage = 0x28;

	/// <summary>
	/// <c>Mech_ReceiveSquadOrder</c>(<paramref name="mech"/>, <paramref name="reply"/>, <paramref name="order"/>), with the broadcast's
	/// reply so far: for <see cref="Es1SquadOrder.RadarOnVerb"/> (or <c>0x10</c>) the radar <c>+0x90</c> switched on,
	/// message <see cref="RadarOnMessage"/>, or <see cref="RadarAlreadyOnMessage"/> when it was on, and
	/// <see cref="Es1Mech.RadarKeptOn"/> set; for <see cref="Es1SquadOrder.RadarOffVerb"/> (or <c>0x11</c>) the radar
	/// switched off, message <see cref="RadarOffMessage"/>, and <see cref="Es1Mech.RadarKeptOn"/> cleared; either accepts.
	/// The message is posted on the pilot channel (<see cref="Messages.Es1PilotChannel.PostSquadMessage"/>, not forced)
	/// for a reply of 0, or of 1 with the order accepted, so only the first to answer or the first to accept speaks.
	/// Answers 1 for an acceptance, else 0.
	/// </summary>
	/// <param name="globals">The simulation's globals, whose cockpit's pilot channel the reply is posted to.</param>
	public static short Receive(Es1Mech mech, short reply, Es1SquadOrder order, Es1SimGlobals globals) {
		byte message;
		switch (order.Verb) {
			case Es1SquadOrder.RadarOnVerb or Es1SquadOrder.RadarOnVerbAlias:
				message = mech.RadarOn ? RadarAlreadyOnMessage : RadarOnMessage;
				mech.RadarOn = true;
				mech.RadarKeptOn = true;
				break;
			case Es1SquadOrder.RadarOffVerb or Es1SquadOrder.RadarOffVerbAlias:
				mech.RadarOn = false;
				message = RadarOffMessage;
				mech.RadarKeptOn = false;
				break;
			default:
				// PLACEHOLDER: the other verbs, each of which retail acts on through the AI (the squad order's fields
				// +0x1d2 to +0x1e4, Behaviour_SetState, Ai_SelectTarget, Mech_AiEngageOrderedTarget, Mech_AiSelectBehaviour)
				// and answers by the machine's state, posting its reply as below; plan step 8e. Until then it does nothing
				// and answers 0, a refusal.
				return 0;
		}

		if (reply == 0 || reply == 1) {
			Messages.Es1PilotChannel.PostSquadMessage(globals, mech, false, message);
		}

		return 1;
	}
}
