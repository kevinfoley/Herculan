using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// A HERC's slot <c>+0x2c</c>, the squad order's receiver <c>Mech_ReceiveSquadOrder</c> (<c>ES1DBSIM 0001a1cc</c>), and the
/// install it runs for an ordered target, <c>Mech_AiEngageOrderedTarget</c> (<c>00015edc</c>)
/// (docs/es1/simulation/ai-squadmates.md#receiving-a-squad-order--mech_receivesquadorder-0001a1cc,
/// docs/es1/simulation/mech-ai.md#targets-and-orders).
/// </summary>
public static class Es1SquadOrders {
	/// <summary>The standing order <c>+0x1d2</c> that guards a point, which verb <c>0xc</c> stores.</summary>
	public const short GuardSquadVerb = 6;

	/// <summary>The group order (<c>+4</c>) under which verb 5 keeps the machine on its post: 6, <c>guarding</c>.</summary>
	public const short GuardOrder = 6;

	/// <summary>
	/// The reply ids, an index into the speaker's pilot file (docs/es1/simulation/cockpit-messages.md#posting): too shot up;
	/// broken off; no selection; out of range; the order already in force (or the radar already on); engaging; affirmative;
	/// ignoring the selection; no threat (or the subject neutralized); busy with its own threat; on its way to help; joining
	/// from afar; nothing found; opening fire; already covered; holding fire; guarding; moving; radar on; radar off.
	/// </summary>
	public const short OutOfActionReply = 0x1b, BrokenOffReply = 0xd, NoSelectionReply = 0x12, OutOfRangeReply = 0x10,
		AlreadyInForceReply = 0xf, EngagingReply = 0x11, AffirmativeReply = 0x1e, IgnoringReply = 0x16, NoThreatReply = 0x1d,
		OwnProblemsReply = 0x1a, HelpingReply = 0xb, JoiningReply = 0x14, NothingFoundReply = 0xe, OpeningFireReply = 0x1c,
		AlreadyCoveredReply = 0x20, HoldingFireReply = 0xc, GuardingReply = 0x17, MovingReply = 0x2a, RadarOnReply = 0x26,
		RadarOffReply = 0x28;

	/// <summary>The reply id a verb with no case keeps, <c>0xffff</c>, posted as it stands.</summary>
	public const short NoCaseReply = -1;

	/// <summary>The range of the selection within which verb 0 is taken.</summary>
	public const int AttackRange = 130000;

	/// <summary>The range of <c>PlayerMech</c> within which verb 2 looks for a machine targeting it.</summary>
	public const int HelpRange = 60000;

	/// <summary>The range of the group's first member within which verb 3's answer is <see cref="AffirmativeReply"/>.</summary>
	public const int JoinRange = 25000;

	/// <summary>The ground range of the guarded point within which verb <c>0xc</c> to the same point is refused.</summary>
	public const int SamePostRange = 1000;

	/// <summary>The range of the ordered point within which verbs <c>0xd</c> and <c>0xe</c> repeated are refused.</summary>
	public const int OnPointRange = 2000;

	/// <summary>
	/// <c>Mech_ReceiveSquadOrder</c>(<paramref name="mech"/>, <paramref name="reply"/>, <paramref name="order"/>): the order
	/// taken or refused by its verb (<see cref="Act"/>) with a reply id; the reply posted on the pilot channel
	/// (<see cref="Messages.Es1PilotChannel.PostSquadMessage"/>, not forced) when the reply so far is 0, or is 1 and the
	/// order was taken, so only the first told, or the first to take it, speaks. Answers 1 when taken, else 0, retail's
	/// <c>AX</c>, the rest of <c>EAX</c> left as it was, which neither caller reads.
	/// </summary>
	/// <param name="globals">The simulation's globals, whose cockpit's pilot channel the reply is posted to.</param>
	public static short Receive(Es1Mech mech, short reply, Es1SquadOrder order, Es1SimGlobals globals) {
		var (id, taken) = Act(mech, order, globals);
		if (reply == 0 || (reply == 1 && taken)) {
			Messages.Es1PilotChannel.PostSquadMessage(globals, mech, false, id);
		}

		return taken ? (short)1 : (short)0;
	}

	/// <summary>
	/// The switch on the verb, through the jump table <c>00091ebc</c> over 0–<c>0x11</c>: the reply id and whether the
	/// order was taken. Out of action is <see cref="Es1Mech.OutOfAction"/>; committed is the descriptor's flag 1
	/// (<c>+9</c>).
	/// <list type="table">
	/// <item><term>0</term><description><see cref="AttackSelection"/>.</description></item>
	/// <item><term>1</term><description><see cref="IgnoreSelection"/>.</description></item>
	/// <item><term>2</term><description><see cref="HelpPlayer"/>.</description></item>
	/// <item><term>3, <c>0xa</c>, <c>0xf</c></term><description>refused <see cref="OutOfActionReply"/> when immobilized;
	/// otherwise <c>patrolling</c>, the standing order and <see cref="Es1Mech.DamageInPlayerSquad"/> cleared, taken with
	/// <see cref="AffirmativeReply"/> within <see cref="JoinRange"/> of the group's first member, else
	/// <see cref="JoiningReply"/>.</description></item>
	/// <item><term>4, <c>0x10</c></term><description>the radar <c>+0x90</c> on, <see cref="RadarOnReply"/>, or
	/// <see cref="AlreadyInForceReply"/> when it was on; <see cref="Es1Mech.RadarKeptOn"/> set; taken.</description></item>
	/// <item><term>5</term><description><see cref="FireAtWill"/>.</description></item>
	/// <item><term>7, <c>0x11</c></term><description>the radar off and <see cref="Es1Mech.RadarKeptOn"/> cleared,
	/// <see cref="RadarOffReply"/>, taken.</description></item>
	/// <item><term>8</term><description><see cref="HoldFire"/>.</description></item>
	/// <item><term><c>0xb</c></term><description><see cref="AttackSubject"/>.</description></item>
	/// <item><term><c>0xc</c></term><description><see cref="GuardPoint"/>.</description></item>
	/// <item><term><c>0xd</c>, <c>0xe</c></term><description><see cref="MoveToPoint"/>, standing order 2 or 1.</description></item>
	/// <item><term>any other</term><description><see cref="NoCaseReply"/>, not taken.</description></item>
	/// </list>
	/// </summary>
	private static (short Id, bool Taken) Act(Es1Mech mech, Es1SquadOrder order, Es1SimGlobals globals) {
		switch (order.Verb) {
			case 0:
				return AttackSelection(mech, globals);
			case 1:
				return IgnoreSelection(mech, globals);
			case 2:
				return HelpPlayer(mech, globals);
			case 3 or 0xa or 0xf:
				return JoinOnLeader(mech, globals);
			case Es1SquadOrder.RadarOnVerb or Es1SquadOrder.RadarOnVerbAlias: {
				short id = mech.RadarOn ? AlreadyInForceReply : RadarOnReply;
				mech.RadarOn = true;
				mech.RadarKeptOn = true;
				return (id, true);
			}
			case 5:
				return FireAtWill(mech, globals);
			case Es1SquadOrder.RadarOffVerb or Es1SquadOrder.RadarOffVerbAlias:
				mech.RadarOn = false;
				mech.RadarKeptOn = false;
				return (RadarOffReply, true);
			case 8:
				return HoldFire(mech, globals);
			case 0xb:
				return AttackSubject(mech, order, globals);
			case 0xc:
				return GuardPoint(mech, order, globals);
			case 0xd:
				return MoveToPoint(mech, order, Es1NavigationStates.PatrolSquadVerb, globals);
			case 0xe:
				return MoveToPoint(mech, order, Es1CombatReassess.MoveSquadVerb, globals);
			default:
				return (NoCaseReply, false);
		}
	}

	/// <summary>
	/// Verb 0: <see cref="Es1Mech.IgnorePlayerSelection"/> cleared first; refused <see cref="OutOfActionReply"/> out of
	/// action, <see cref="BrokenOffReply"/> with the descriptor's flag 4 (<c>+0xc</c>), <see cref="NoSelectionReply"/> with
	/// no selection (<c>CockpitViewInstance</c> <c>+0x20c</c>) or one immobilized or destroyed,
	/// <see cref="OutOfRangeReply"/> with it farther than <see cref="AttackRange"/> (<c>Math_DistanceBetweenPoints</c>), and
	/// <see cref="AlreadyInForceReply"/> when the standing order already engages it; otherwise
	/// <see cref="TakeEngageOrder"/> on it, <see cref="EngagingReply"/>.
	/// </summary>
	private static (short, bool) AttackSelection(Es1Mech mech, Es1SimGlobals globals) {
		// Retail reads the cockpit unchecked; with none built there is no selection here.
		var selection = globals.CockpitViewInstance?.Selection;
		mech.IgnorePlayerSelection = false;
		if (mech.OutOfAction) {
			return (OutOfActionReply, false);
		}

		if (Flag(mech, 4)) {
			return (BrokenOffReply, false);
		}

		if (selection == null || selection.Immobilized || selection.Destroyed) {
			return (NoSelectionReply, false);
		}

		if (selection.Position.ApproxDistanceTo(mech.Position) > AttackRange) {
			return (OutOfRangeReply, false);
		}

		if (EngagesAlready(mech, selection)) {
			return (AlreadyInForceReply, false);
		}

		TakeEngageOrder(mech, selection, globals);
		return (EngagingReply, true);
	}

	/// <summary>
	/// Verb 1: refused <see cref="AffirmativeReply"/> out of action, <see cref="NoSelectionReply"/> with no selection, and
	/// <see cref="AlreadyInForceReply"/> with <see cref="Es1Mech.IgnorePlayerSelection"/> already set; otherwise it is set,
	/// <see cref="IgnoringReply"/>, and a committed machine whose target is the selection zeroes its dwell countdown
	/// (<c>+0x4c</c>) when the descriptor's flag 2 (<c>+0xa</c>) is set, or else takes
	/// <see cref="Es1Targets.SelectTarget"/> with mask 1: <see cref="EngageOrderedTarget"/> on what it finds,
	/// <see cref="Es1StateChoice.SelectBehaviour"/> on nothing.
	/// </summary>
	private static (short, bool) IgnoreSelection(Es1Mech mech, Es1SimGlobals globals) {
		var selection = globals.CockpitViewInstance?.Selection;
		if (mech.OutOfAction) {
			return (AffirmativeReply, false);
		}

		if (selection == null) {
			return (NoSelectionReply, false);
		}

		if (mech.IgnorePlayerSelection) {
			return (AlreadyInForceReply, false);
		}

		mech.IgnorePlayerSelection = true;
		if (Flag(mech, 1) && ReferenceEquals(selection, mech.Target)) {
			if (Flag(mech, 2)) {
				mech.Behaviour.Countdown = 0;
			} else if (Es1Targets.SelectTarget(mech, Es1Targets.DesignatedOnly, globals) is { } found) {
				EngageOrderedTarget(mech, found, globals);
			} else {
				Es1StateChoice.SelectBehaviour(mech, globals);
			}
		}

		return (IgnoringReply, true);
	}

	/// <summary>
	/// Verb 2: refused <see cref="OutOfActionReply"/> out of action; otherwise, of <c>g_MechPool</c> oldest first, as
	/// <c>Pool_Prev</c> walks it from the end, the first nearest <c>PlayerMech</c> (<c>Math_DistanceBetweenPoints</c>)
	/// under <see cref="HelpRange"/> of the machines not out of action whose target is <c>PlayerMech</c>; none refuses
	/// <see cref="NoThreatReply"/>, and one that is already this machine's target with the machine committed
	/// <see cref="OwnProblemsReply"/>; otherwise <see cref="TakeEngageOrder"/> on it, <see cref="HelpingReply"/>.
	/// </summary>
	private static (short, bool) HelpPlayer(Es1Mech mech, Es1SimGlobals globals) {
		if (mech.OutOfAction) {
			return (OutOfActionReply, false);
		}

		// Retail compares with PlayerMech and reads its position unchecked; with none, nothing is found here.
		var player = globals.PlayerMech;
		Es1Mech? nearest = null;
		int best = HelpRange;
		foreach (var other in globals.MechPool) {
			if (player == null || other.OutOfAction || !ReferenceEquals(other.Target, player)) {
				continue;
			}

			int distance = other.Position.ApproxDistanceTo(player.Position);
			if (distance < best) {
				best = distance;
				nearest = other;
			}
		}

		if (nearest == null) {
			return (NoThreatReply, false);
		}

		if (ReferenceEquals(mech.Target, nearest) && Flag(mech, 1)) {
			return (OwnProblemsReply, false);
		}

		TakeEngageOrder(mech, nearest, globals);
		return (HelpingReply, true);
	}

	/// <summary>Verbs 3, <c>0xa</c> and <c>0xf</c>, as <see cref="Act"/> describes them.</summary>
	private static (short, bool) JoinOnLeader(Es1Mech mech, Es1SimGlobals globals) {
		if (mech.Immobilized) {
			return (OutOfActionReply, false);
		}

		// Retail reads the group's first member unchecked; a machine with none is taken as far from it here.
		short id = mech.Group?.Members is [IEs1SimObject first, ..] && first.Position.ApproxDistanceTo(mech.Position) < JoinRange
			? AffirmativeReply
			: JoiningReply;
		mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Patrolling], globals.DwellJitter);
		mech.SquadVerb = 0;
		mech.DamageInPlayerSquad = 0;
		return (id, true);
	}

	/// <summary>
	/// Verb 5: <see cref="Es1Mech.IgnorePlayerSelection"/> cleared first; refused <see cref="OutOfActionReply"/> out of
	/// action; a committed machine takes it with <see cref="AlreadyCoveredReply"/> and nothing more. Otherwise
	/// <see cref="Es1Targets.SelectTarget"/> with mask 1: nothing refuses <see cref="NothingFoundReply"/>; a find, in a group
	/// whose order is <see cref="GuardOrder"/>, makes the standing order 4 with no target and runs
	/// <see cref="Es1StateChoice.SelectBehaviour"/>, so the machine stays on its post, and in any other clears the standing
	/// order and runs <see cref="EngageOrderedTarget"/> on it; taken, <see cref="OpeningFireReply"/>.
	/// </summary>
	private static (short, bool) FireAtWill(Es1Mech mech, Es1SimGlobals globals) {
		mech.IgnorePlayerSelection = false;
		if (mech.OutOfAction) {
			return (OutOfActionReply, false);
		}

		if (Flag(mech, 1)) {
			return (AlreadyCoveredReply, true);
		}

		if (Es1Targets.SelectTarget(mech, Es1Targets.DesignatedOnly, globals) is not { } found) {
			return (NothingFoundReply, false);
		}

		// Retail reads the group unchecked; a machine with none is taken as in a group of another order here.
		if (mech.Group?.Order == GuardOrder) {
			mech.SquadVerb = Es1NavigationStates.EngageSquadVerb;
			mech.SquadTarget = null;
			Es1StateChoice.SelectBehaviour(mech, globals);
		} else {
			mech.SquadVerb = 0;
			EngageOrderedTarget(mech, found, globals);
		}

		return (OpeningFireReply, true);
	}

	/// <summary>
	/// Verb 8: a committed machine runs <see cref="Es1StateChoice.SelectBehaviour"/>, then clears the standing order, and
	/// takes it; any other is taken only when <see cref="Es1Targets.SelectTarget"/> with mask 0 finds something, which it
	/// does not use. The reply is <see cref="HoldingFireReply"/> either way, and <see cref="Es1Mech.DamageInPlayerSquad"/>
	/// is cleared, taken or not.
	/// </summary>
	private static (short, bool) HoldFire(Es1Mech mech, Es1SimGlobals globals) {
		bool taken = true;
		if (Flag(mech, 1)) {
			Es1StateChoice.SelectBehaviour(mech, globals);
			mech.SquadVerb = 0;
		} else {
			taken = Es1Targets.SelectTarget(mech, 0, globals) != null;
		}

		mech.DamageInPlayerSquad = 0;
		return (HoldingFireReply, taken);
	}

	/// <summary>
	/// Verb <c>0xb</c>: <see cref="Es1Mech.IgnorePlayerSelection"/> cleared first; refused <see cref="OutOfActionReply"/> out
	/// of action, <see cref="AlreadyInForceReply"/> when the standing order already engages the order's subject, and
	/// <see cref="NoThreatReply"/> with the subject immobilized or destroyed; otherwise <see cref="TakeEngageOrder"/> on it,
	/// <see cref="EngagingReply"/>.
	/// </summary>
	private static (short, bool) AttackSubject(Es1Mech mech, Es1SquadOrder order, Es1SimGlobals globals) {
		mech.IgnorePlayerSelection = false;
		if (mech.OutOfAction) {
			return (OutOfActionReply, false);
		}

		var subject = order.Subject;
		if (EngagesAlready(mech, subject)) {
			return (AlreadyInForceReply, false);
		}

		// Retail reads the subject unchecked; none is refused as a neutralized one here, an engine guard.
		if (subject == null || subject.Immobilized || subject.Destroyed) {
			return (NoThreatReply, false);
		}

		TakeEngageOrder(mech, subject, globals);
		return (EngagingReply, true);
	}

	/// <summary>
	/// Verb <c>0xc</c>: refused <see cref="OutOfActionReply"/> out of action, and <see cref="AlreadyCoveredReply"/> when the
	/// standing order is already <see cref="GuardSquadVerb"/> and the order's point lies within
	/// <see cref="SamePostRange"/> of <see cref="Es1Mech.SquadPoint"/> on the ground plane
	/// (<c>Math_DistanceBetweenPoints2D</c>); otherwise <c>guarding</c> and the standing order
	/// <see cref="GuardSquadVerb"/>, <see cref="GuardingReply"/>. Then, taken or not, <see cref="StorePoint"/>.
	/// </summary>
	private static (short, bool) GuardPoint(Es1Mech mech, Es1SquadOrder order, Es1SimGlobals globals) {
		(short, bool) result;
		if (mech.OutOfAction) {
			result = (OutOfActionReply, false);
		} else if (mech.SquadVerb == GuardSquadVerb
				&& Es1PlayerThink.DistanceBetweenPoints2D(order.Point, mech.SquadPoint) < SamePostRange) {
			result = (AlreadyCoveredReply, false);
		} else {
			mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Guarding], globals.DwellJitter);
			mech.SquadVerb = GuardSquadVerb;
			result = (GuardingReply, true);
		}

		StorePoint(mech, order);
		return result;
	}

	/// <summary>
	/// Verbs <c>0xd</c> and <c>0xe</c>, with the standing order <paramref name="verb"/> each stores, 2 and 1: refused
	/// <see cref="OutOfActionReply"/> when immobilized, and <see cref="AlreadyCoveredReply"/> when the standing order is
	/// already <paramref name="verb"/> and the machine lies within <see cref="OnPointRange"/> of the order's point
	/// (<c>Math_DistanceBetweenPoints</c>); otherwise <c>patrolling</c> and the standing order <paramref name="verb"/>,
	/// <see cref="MovingReply"/>. Then, taken or not, <see cref="StorePoint"/>.
	/// </summary>
	private static (short, bool) MoveToPoint(Es1Mech mech, Es1SquadOrder order, short verb, Es1SimGlobals globals) {
		(short, bool) result;
		if (mech.Immobilized) {
			result = (OutOfActionReply, false);
		} else if (mech.SquadVerb == verb && mech.Position.ApproxDistanceTo(order.Point) < OnPointRange) {
			result = (AlreadyCoveredReply, false);
		} else {
			mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Patrolling], globals.DwellJitter);
			mech.SquadVerb = verb;
			result = (MovingReply, true);
		}

		StorePoint(mech, order);
		return result;
	}

	/// <summary>
	/// The order's dwords <c>+6</c> and <c>+0xa</c>, the point's X and Y, copied to <c>+0x1d4</c> and <c>+0x1d8</c>; its Z
	/// is not copied, the dword after them being the order's target <c>+0x1dc</c>.
	/// </summary>
	private static void StorePoint(Es1Mech mech, Es1SquadOrder order) =>
		mech.SquadPoint = new Vec3i(order.Point.X, order.Point.Y, mech.SquadPoint.Z);

	/// <summary>Whether the standing order is already to engage <paramref name="target"/>: verb 4 with <c>+0x1dc</c> it.</summary>
	private static bool EngagesAlready(Es1Mech mech, IEs1SimObject? target) =>
		mech.SquadVerb == Es1NavigationStates.EngageSquadVerb && ReferenceEquals(mech.SquadTarget, target);

	/// <summary>
	/// The engage order verbs 0, 2 and <c>0xb</c> store: the standing order 4 on <paramref name="target"/>, its abandon
	/// threshold <see cref="Es1Targets.TargetStateTier"/> of it, <see cref="EngageOrderedTarget"/>, and
	/// <see cref="Es1Mech.DamageInPlayerSquad"/> cleared.
	/// </summary>
	private static void TakeEngageOrder(Es1Mech mech, IEs1SimObject target, Es1SimGlobals globals) {
		mech.SquadVerb = Es1NavigationStates.EngageSquadVerb;
		mech.SquadTarget = target;
		mech.SquadThreshold = Es1Targets.TargetStateTier(target);
		EngageOrderedTarget(mech, target, globals);
		mech.DamageInPlayerSquad = 0;
	}

	/// <summary>
	/// <c>Mech_AiEngageOrderedTarget</c> (<c>00015edc</c>)(<paramref name="mech"/>, <paramref name="target"/>): the target
	/// installed (<see cref="Es1Mech.InstallTarget"/>); then by its class, 1 <c>attacking base</c>, 0
	/// <see cref="Es1Mech.TargetHandedOver"/> set and <see cref="Es1CombatReassess.EnterCombat"/>, 2 <c>attacking
	/// flyer</c>, any other nothing.
	/// </summary>
	public static void EngageOrderedTarget(Es1Mech mech, IEs1SimObject target, Es1SimGlobals globals) {
		mech.InstallTarget(target);
		switch (target.TargetClass) {
			case Es1CombatReassess.StructureClass:
				mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.AttackingBase], globals.DwellJitter);
				break;
			case 0:
				mech.TargetHandedOver = true;
				Es1CombatReassess.EnterCombat(mech, globals);
				break;
			case Es1CombatReassess.FlyerClass:
				mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.AttackingFlyer], globals.DwellJitter);
				break;
		}
	}

	/// <summary>
	/// Flag <paramref name="index"/> of the machine's behaviour descriptor, the byte <c>+8</c> + <paramref name="index"/>;
	/// retail reads it unchecked, and the constructor always installs one, so none reads clear here.
	/// </summary>
	internal static bool Flag(Es1Mech mech, int index) => mech.Behaviour.Descriptor?.Flags[index] == true;
}
