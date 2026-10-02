using Herculan.Engine.Numerics;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The squadmate slice: the standing order a machine in the player's own group carries, and the
/// handler that installs one. Derivation is docs/simulation/ai-squadmates.md.
///
/// <para>A standing squad order sits <i>above</i> the mission group's order array — see
/// <see cref="SelectBehaviour"/>, where a nonzero <see cref="SquadOrderVerb"/> takes its own path
/// and the group's own verb is never read. It is the only thing in the simulation that can steer a
/// machine off its group's route.</para>
/// </summary>
public partial class MechObject {
	/// <summary>
	/// <c>mech+0x23e</c> — the standing squad order's verb, 0 for none. Only five values are ever
	/// written: <see cref="SquadOrderMove"/>, <see cref="SquadOrderPatrol"/>,
	/// <see cref="SquadOrderEngage"/> and <see cref="SquadOrderGuard"/>, plus zero to clear.
	/// </summary>
	public short SquadOrderVerb { get; private set; }

	/// <summary>
	/// <c>mech+0x240</c> — where a standing order sends the machine. <see cref="NavigationStep"/>
	/// drives at it, in place of the group's route, whenever a verb is standing.
	/// </summary>
	public Vec3i SquadOrderDestination { get; private set; }

	/// <summary><c>mech+0x248</c> — the object <see cref="SquadOrderEngage"/> names.</summary>
	public SimObject? SquadOrderTarget { get; private set; }

	/// <summary>
	/// <c>mech+0x24c</c> — the friendly unit a <see cref="SquadOrderGuard"/> was pointed at, or null
	/// when it was pointed at bare ground. <see cref="GoalPosition"/> prefers it over the stored
	/// point, so a guard ordered onto a machine follows that machine.
	/// </summary>
	public SimObject? SquadOrderGuardSubject { get; private set; }

	/// <summary>
	/// <c>mech+0x250</c> — how finished the order wants its target before the machine may let go,
	/// against <see cref="AiTargeting.TargetStateTier"/>. Latched from the target's own tier at the
	/// moment the order is given, so an order to finish something already fleeing is satisfied by
	/// less than one given while it was fighting.
	/// </summary>
	public int SquadOrderAbandonTier { get; private set; }

	/// <summary>
	/// <c>mech+0x9a</c> — <c>IGNORE MY TARGET</c>'s latch. While it is set,
	/// <see cref="AiTargeting.IsTargetable"/> refuses this machine the player's own current
	/// selection. Cleared by the three orders that ask for a fight, and by taking fire from the very
	/// object the player has selected.
	/// </summary>
	public bool IgnoresPlayerSelection { get; private set; }

	/// <summary>
	/// <c>mech+0xb6</c> — <c>FIRE AT WILL</c>'s latch. Three thinks gate their acquisition on being
	/// the group leader; this bit lets a follower past that gate and think for itself.
	/// <c>HOLD YOUR FIRE</c> clears it.
	/// </summary>
	public bool FireAtWill { get; private set; }

	/// <summary>
	/// <c>mech+0xb2</c> — the squad's radar order, which is what <c>Ai_UpdateWeaponsFree</c> gives a
	/// machine in the player's group in place of the mission file's standing setting.
	/// <c>SCAN FOR HOSTILES</c> sets it and <c>EMCON</c> clears it.
	/// </summary>
	public bool RadarForcedActive { get; private set; }

	/// <summary>
	/// <c>Mech_ReceiveSquadOrder</c> (<c>00420ad4</c>, mech vtable <c>+0x28</c>) — one machine
	/// receiving one order. Sixteen of the eighteen verbs over twelve cases; the pairs that the FLASH COMM page
	/// and the [F7] command display both carry share a case.
	///
	/// <para>Every case ends by choosing a reply id, and the machine says it on the squad channel
	/// when either this is the first recipient or it is the first one to accept — which is what
	/// keeps a three-machine broadcast from producing three answers.</para>
	/// </summary>
	/// <param name="message">The order, with its issuer already stamped by the dispatcher.</param>
	/// <param name="reply">What the recipients before this one answered.</param>
	/// <returns>Whether this machine took the order.</returns>
	internal bool ReceiveSquadOrder(SimWorld world, SquadOrderMessage message, SquadOrderReply reply) {
		int said = NoReply;
		bool accepted = false;

		switch (message.Verb) {
			case SquadCommand.AttackMyTarget: {
				var wanted = world.PlayerMech?.Target;
				IgnoresPlayerSelection = false;

				if (OutOfAction) {
					said = ReplyOutOfAction;
				} else if (Behaviour.State is { BrokenOff: true }) {
					// A machine that has already broken off will not be sent back in.
					said = ReplyBrokenOff;
				} else if (wanted is not { Neutralised: false }) {
					said = ReplyNoTarget;
				} else if (SquadOrderVerb == SquadOrderEngage && ReferenceEquals(wanted, SquadOrderTarget)) {
					said = ReplyAlreadyDoingIt;
				} else {
					TakeEngageOrder(world, wanted);
					said = ReplyEngaging;
					accepted = true;
				}

				break;
			}

			case SquadCommand.IgnoreMyTarget: {
				var wanted = world.PlayerMech?.Target;

				if (OutOfAction) {
					said = ReplyAffirmative;
				} else if (wanted == null) {
					said = ReplyNoTarget;
				} else {
					if (SquadOrderVerb == SquadOrderEngage && ReferenceEquals(wanted, SquadOrderTarget)) {
						SquadOrderVerb = SquadOrderNone;
					}

					if (IgnoresPlayerSelection) {
						said = ReplyAlreadyDoingIt;
					} else {
						said = ReplyBackingOff;
						accepted = true;
						IgnoresPlayerSelection = true;

						// Only a machine actually holding that target has anything to do about it. One
						// holding a place drops its dwell instead of retargeting, so its own state
						// re-decides on the next tick.
						if (Behaviour.State is { Committed: true } && ReferenceEquals(wanted, Target)) {
							if (Behaviour.State is { HoldsPlace: true }) {
								Behaviour.DwellCountdown = 0;
							} else if (AiTargeting.SelectTarget(world, this, TargetFilter.MissionTargetOnly)
									is { } next) {
								EngageOrderedTarget(world, next);
							} else {
								SelectBehaviour(world);
							}
						}
					}
				}

				break;
			}

			case SquadCommand.HelpMeOut: {
				if (OutOfAction) {
					said = ReplyOutOfAction;
					break;
				}

				// Whatever is shooting at the player, nearest first. The 60000 is a hard ceiling, not
				// a preference: nothing further away counts as threatening the player at all.
				MechObject? threat = null;
				int nearest = HelpRange;

				foreach (var candidate in world.Objects) {
					if (candidate is not MechObject other || other.OutOfAction
							|| !ReferenceEquals(other.Target, world.PlayerMech)) {
						continue;
					}

					int range = world.PlayerMech is { } player
						? other.Position.ApproxDistanceTo(player.Position)
						: 0;

					if (range < nearest) {
						nearest = range;
						threat = other;
					}
				}

				if (threat == null) {
					said = ReplyNothingFound;
				} else if (ReferenceEquals(Target, threat) && Behaviour.State is { Committed: true }) {
					said = ReplyAlreadyEngaged;
				} else {
					TakeEngageOrder(world, threat);
					said = ReplyOnMyWay;
					accepted = true;
				}

				break;
			}

			case SquadCommand.JoinOnMe:
			case SquadCommand.Disengage:
			case SquadCommand.JoinOnMeCommand: {
				if (Immobilised) {
					said = ReplyOutOfAction;
					break;
				}

				// The reply says how far there is to come back, and nothing else differs.
				int home = Group?.Leader is { } leader ? Position.ApproxDistanceTo(leader.Position) : 0;
				said = home < FormedUpRange ? ReplyAffirmative : ReplyOnMyWayBack;

				// Patrolling with no standing verb is the state that holds formation on the leader.
				SetBehaviourState(BehaviourState.Patrolling);
				SquadOrderVerb = SquadOrderNone;
				accepted = true;
				DamageFromPlayerGroup = 0;
				break;
			}

			case SquadCommand.ScanForHostiles:
			case SquadCommand.ScanForHostilesCommand:
				if (Scanner) {
					said = ReplyAlreadyDoingIt;
				} else {
					said = ReplyRadarActive;
					Scanner = true;
				}

				accepted = true;
				RadarForcedActive = true;
				break;

			case SquadCommand.Emcon:
			case SquadCommand.EmconCommand:
				Scanner = false;
				said = ReplyRadarPassive;
				accepted = true;
				RadarForcedActive = false;
				break;

			case SquadCommand.FireAtWill: {
				IgnoresPlayerSelection = false;

				if (OutOfAction) {
					said = ReplyOutOfAction;
					break;
				}

				if (Behaviour.State is { Committed: true }) {
					// Already in a fight, so there is nothing to start.
					said = ReplyAlreadyThere;
					accepted = true;
					FireAtWill = true;
					break;
				}

				if (AiTargeting.SelectTarget(world, this, TargetFilter.MissionTargetOnly) is { } found) {
					// A machine guarding something it is still standing near keeps the post and lets
					// its own state pick the fight; anything else goes straight at what it found.
					bool guardingNearby = Group is { OrderVerb: MissionOrder.VerbGuard } group
						&& group.OrderTarget is { } post
						&& Position.ApproxDistanceTo(post.Position) < GuardStillHeldRange;

					SquadOrderVerb = SquadOrderNone;

					if (guardingNearby) {
						SelectBehaviour(world);
					} else {
						EngageOrderedTarget(world, found);
					}
				}

				said = ReplyEngagingAtWill;
				accepted = true;
				FireAtWill = true;
				break;
			}

			case SquadCommand.HoldYourFire:
				if (Behaviour.State is { Committed: true }) {
					SelectBehaviour(world);
					SquadOrderVerb = SquadOrderNone;
				}

				said = ReplyHoldingFire;
				accepted = true;
				FireAtWill = false;
				DamageFromPlayerGroup = 0;
				break;

			case SquadCommand.AttackEnemy:
				IgnoresPlayerSelection = false;

				if (OutOfAction) {
					said = ReplyOutOfAction;
				} else if (SquadOrderVerb == SquadOrderEngage
						&& ReferenceEquals(message.Subject, SquadOrderTarget)) {
					said = ReplyAlreadyDoingIt;
				} else if (message.Subject is not { Neutralised: false } subject) {
					said = ReplyNothingFound;
				} else {
					TakeEngageOrder(world, subject);
					said = ReplyEngaging;
					accepted = true;
				}

				break;

			case SquadCommand.DefendPosition: {
				// "Already guarding this" means the same subject, or — with neither the old order nor
				// the new one naming a subject — near enough the same patch of ground.
				bool same = SquadOrderGuardSubject != null || message.Subject != null
					? SquadOrderGuardSubject != null && message.Subject != null
						&& ReferenceEquals(message.Subject, SquadOrderGuardSubject)
					: SimMath.FastMagnitude2D(SquadOrderDestination.X - message.Point.X,
						SquadOrderDestination.Y - message.Point.Y) < SamePostRange;

				if (OutOfAction) {
					said = ReplyOutOfAction;
				} else if (SquadOrderVerb == SquadOrderGuard && same) {
					said = ReplyAlreadyThere;
				} else {
					SetBehaviourState(BehaviourState.Guarding);
					SquadOrderVerb = SquadOrderGuard;
					said = ReplyHoldingPosition;
					accepted = true;
				}

				// Written whether or not the order was taken, which is what makes a refused re-order
				// still move the post.
				SquadOrderDestination = message.Point;
				SquadOrderGuardSubject = message.Subject;
				break;
			}

			case SquadCommand.PatrolGridpoint:
				if (Immobilised) {
					said = ReplyOutOfAction;
				} else if (SquadOrderVerb == SquadOrderPatrol
						&& Position.ApproxDistanceTo(message.Point) < SamePointRange) {
					said = ReplyAlreadyThere;
				} else {
					SetBehaviourState(BehaviourState.Patrolling);
					SquadOrderVerb = SquadOrderPatrol;
					said = ReplyProceeding;
					accepted = true;
				}

				SquadOrderDestination = message.Point;
				break;

			case SquadCommand.GotoGridpoint:
				if (Immobilised) {
					said = ReplyOutOfAction;
				} else if (SquadOrderVerb == SquadOrderMove
						&& Position.ApproxDistanceTo(message.Point) < SamePointRange) {
					said = ReplyAlreadyThere;
				} else {
					SetBehaviourState(BehaviourState.Patrolling);
					SquadOrderVerb = SquadOrderMove;
					said = ReplyProceeding;
					accepted = true;
				}

				SquadOrderDestination = message.Point;
				break;
		}

		if (reply == SquadOrderReply.Unsent || (reply == SquadOrderReply.Refused && accepted)) {
			PostSquadMessage(world, said);
		}

		return accepted;
	}

	/// <summary>
	/// The three cases that name something to kill, which all install the order the same way: the
	/// verb, the object, the tier the order is satisfied at, and then the engage itself.
	/// </summary>
	private void TakeEngageOrder(SimWorld world, SimObject subject) {
		SquadOrderVerb = SquadOrderEngage;
		SquadOrderTarget = subject;
		SquadOrderAbandonTier = AiTargeting.TargetStateTier(subject);
		EngageOrderedTarget(world, subject);
		DamageFromPlayerGroup = 0;
	}

	/// <summary>
	/// <c>Ai_ClearSquadEngageOrder</c> (<c>0041c478</c>) — always answers "this state is finished",
	/// and on the way clears a standing engage order whose target is the one being let go, so the
	/// machine goes back to its group rather than standing over a wreck.
	/// </summary>
	private bool ClearSquadEngageOrder() {
		if (SquadOrderVerb == SquadOrderEngage && ReferenceEquals(Target, SquadOrderTarget)) {
			SquadOrderVerb = SquadOrderNone;
		}

		return true;
	}

	/// <summary>
	/// <c>Mech_SquadOrderLineIndex</c> (<c>0041bac8</c>) — the <c>STRINGS0.STR</c> group 40 index the
	/// [F7] comm box prints on this pilot's <c>OBJECTIVE:</c> line. It starts from the behaviour
	/// state's own <see cref="BehaviourState.ObjectiveLine"/> and lets a standing squad order
	/// override it, so the line reports what the pilot is <i>doing</i> rather than what they were
	/// last told.
	///
	/// <para>Three things block the override: being immobilised or destroyed, being in the state that
	/// reads <c>FLEE</c>, and being committed to a fight — so a downed squadmate reads <c>DEAD</c> or
	/// <c>IMMOBILE</c> whatever it was ordered to do, and one that has found a fight reads
	/// <c>ATTACK</c>.</para>
	/// </summary>
	public int SquadOrderLineIndex {
		get {
			int line = Behaviour.State?.ObjectiveLine ?? FormUpLine;

			if (Immobilised || Destroyed || line == FleeLine
					|| Behaviour.State is { Committed: true }) {
				return line;
			}

			return SquadOrderVerb switch {
				SquadOrderMove => TravelLine,
				SquadOrderPatrol => PatrolLine,
				3 or SquadOrderGuard => GuardLine,
				_ => line
			};
		}
	}

	/// <summary>Group 40 entry 1 — <c>TRAVEL</c>.</summary>
	private const int TravelLine = 1;

	/// <summary>Group 40 entry 2 — <c>PATROL</c>.</summary>
	private const int PatrolLine = 2;

	/// <summary>Group 40 entry 3 — <c>FORM UP</c>, and what a machine with no state installed reads.</summary>
	private const int FormUpLine = 3;

	/// <summary>Group 40 entry 4 — <c>GUARD</c>.</summary>
	private const int GuardLine = 4;

	/// <summary>Group 40 entry 5 — <c>FLEE</c>, the one line a standing order cannot override.</summary>
	private const int FleeLine = 5;

	/// <summary>How near the leader counts as already formed up — the <c>JOIN ON ME</c> reply split.</summary>
	private const int FormedUpRange = 25000;

	/// <summary>How near a guarded post counts as still being held, in <c>FIRE AT WILL</c>.</summary>
	private const int GuardStillHeldRange = 100000;

	/// <summary>How near two <c>DEFEND POSITION</c> points count as the same post.</summary>
	private const int SamePostRange = 1000;

	/// <summary>How near a <c>PATROL</c> or <c>GOTO</c> point counts as already reached.</summary>
	private const int SamePointRange = 2000;

	/// <summary>Past this the machine will not count something as threatening the player at all.</summary>
	private const int HelpRange = 60000;

	// The reply ids. They index the pilot-and-squad channel's own catalog (Content.SquadMessages);
	// the names below say what raises each one rather than what it reads out, and docs/formats/
	// cockpit-messages.md carries the line each one speaks. ReplyAffirmative is the one whose line
	// and whose arms disagree: one of the arms that post it is a refusal (IGNORE MY TARGET from a
	// machine out of action), and the line is "AFFIRMATIVE!". See PostSquadMessage.
	private const int NoReply = -1;
	private const int ReplyOnMyWay = 0x0b;
	private const int ReplyHoldingFire = 0x0c;
	private const int ReplyBrokenOff = 0x0d;
	private const int ReplyAlreadyDoingIt = 0x0f;
	private const int ReplyEngaging = 0x11;
	private const int ReplyNoTarget = 0x12;
	private const int ReplyOnMyWayBack = 0x14;
	private const int ReplyBackingOff = 0x16;
	private const int ReplyHoldingPosition = 0x17;
	private const int ReplyAlreadyEngaged = 0x1a;
	private const int ReplyOutOfAction = 0x1b;
	private const int ReplyEngagingAtWill = 0x1c;
	private const int ReplyNothingFound = 0x1d;
	private const int ReplyAffirmative = 0x1e;
	private const int ReplyAlreadyThere = 0x20;
	private const int ReplyRadarActive = 0x26;
	private const int ReplyRadarPassive = 0x28;
	private const int ReplyProceeding = 0x2a;
}
