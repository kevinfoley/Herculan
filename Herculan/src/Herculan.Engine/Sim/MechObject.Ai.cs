using Herculan.Engine.Numerics;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The machine's half of the AI targeting slice: the behaviour block and its tick, the combat
/// reassess and the three decisions hanging off it, and the combat rating everything weighs
/// candidates by. The derivation is docs/simulation/ai-targeting.md and the dispatch model it cites
/// is docs/simulation/ai-dispatch.md.
/// </summary>
public partial class MechObject {
	/// <summary><c>mech+0x4d</c> — the behaviour block. See <see cref="BehaviourBlock"/>.</summary>
	public BehaviourBlock Behaviour;

	/// <summary>
	/// <c>Mech_Constructor</c>'s initial install (<c>00415f33</c>/<c>f46</c>/<c>f59</c>): the player's
	/// machine is settled at construction, everything else starts in <c>deciding</c> and waits for
	/// its group's order — or, with the order layer unported, for something to shoot it.
	/// </summary>
	internal void InstallInitialBehaviour() =>
		SetBehaviourState(IsPlayer
			? Type.IsFlyer ? BehaviourState.PlayerFly : BehaviourState.Player
			: BehaviourState.Deciding);

	/// <summary>
	/// <c>Mech_AiTick</c> (<c>00411cec</c>) — one machine's AI frame. Its only caller is
	/// <c>Group_OrderTick</c>, so this runs from <see cref="MissionGroup.AiTick"/> and a machine that
	/// is not a live group member never thinks.
	///
	/// <para>The original's order is reassess, then the dwell countdown, then move, then think.
	/// <see cref="Tick"/> runs the move slot for every machine, and it runs from the object pass that
	/// precedes this one, so the machine is still integrated on the <i>previous</i> think's decisions
	/// — the original's ordering, arrived at from the other side. Only the states whose think is
	/// ported dispatch one; see <see cref="ThinkSlot"/>.</para>
	///
	/// <para>A think returning nonzero zeroes its own dwell countdown, which is a state saying it has
	/// finished. None of the navigation thinks ever does — a movement order ends through
	/// <see cref="MissionGroup.AiTick"/>, not through its think.</para>
	/// </summary>
	public void AiTick(SimWorld world) {
		if (Behaviour.State is not { } state) {
			return;
		}

		if (Behaviour.DwellCountdown == 0) {
			switch (state.Reassess) {
				case ReassessSlot.SelectBehaviour:
					SelectBehaviour(world);
					break;
				case ReassessSlot.CombatReassess:
					CombatReassess(world);
					break;
			}
		}

		// Only the eight states whose flag bit 0 is clear ever count down; for the rest the countdown
		// is loaded by Behaviour_SetState and never stepped, so their dwell value means nothing and
		// they end on their own terms instead.
		if (Behaviour.State is { SuppressesDwell: false }) {
			SimMath.TimerCountDown(ref Behaviour.DwellCountdown);
		}

		bool finished = Behaviour.State?.Think switch {
			ThinkSlot.Player => PlayerThink(world),
			ThinkSlot.Patrol => PatrolThink(world),
			ThinkSlot.SearchDestroy => SearchDestroyThink(world),
			ThinkSlot.Travel => TravelThink(world),
			ThinkSlot.Follow => FollowThink(world),
			ThinkSlot.Guard => GuardThink(world),
			ThinkSlot.Attack => AttackThink(world),
			ThinkSlot.Flank => FlankThink(world),
			ThinkSlot.FaceOff => FaceOffThink(world),
			ThinkSlot.AttackBase => AttackBaseThink(world),
			ThinkSlot.AttackFlyer => AttackFlyerThink(world),
			ThinkSlot.Skirt => SkirtThink(world),
			ThinkSlot.DriveOff => DriveOffThink(world),
			ThinkSlot.Flee => FleeThink(world),
			ThinkSlot.Sleep => SleepThink(world),
			ThinkSlot.Inert => InertThink(world),
			ThinkSlot.Ram => RamThink(world),
			_ => false
		};

		if (finished) {
			Behaviour.DwellCountdown = 0;
		}

		Behaviour.CountTick();
	}

	/// <summary>
	/// <c>Mech_AiSelectBehaviour</c> (<c>0041eb34</c>) — picks and installs the next state. Three
	/// mutually exclusive paths, of which only the first can be taken here.
	///
	/// <list type="number">
	/// <item><b>The local player</b> takes <c>player fly</c> or <c>player</c> on the type record's
	/// flyer flag, which is the same test the constructor makes.</item>
	/// <item><b>A squad order</b>, for a machine in the player's own group, maps its verb onto
	/// <c>patrolling</c>, <c>guarding</c> or an engage — and the group's own order is never read at
	/// all. An engage whose target is already destroyed clears the order and re-enters.</item>
	/// <item><b>A mission group order</b> maps verbs 0-6 onto seven states. A null order slot reads as
	/// verb <c>0x0b</c> and matches no case, so no descriptor is installed and the machine keeps the
	/// state it already had — <b>which is not what the original does</b>: there the descriptor to
	/// install is a register nothing on that path wrote — zero through the reassess dispatcher, 4 through
	/// the re-entry after a destroyed engage target — so the original faults. See
	/// docs/simulation/ai-goals.md, "A group with no order at all". No retail mission reaches it.</item>
	/// </list>
	///
	/// <para><b>Every path ends by dropping the target</b>, which is the original's own behaviour and
	/// the reason a machine that finds nothing to fight also lets go of what it was holding.</para>
	/// </summary>
	private void SelectBehaviour(SimWorld world) {
		if (IsPlayer) {
			SetBehaviourState(Type.IsFlyer ? BehaviourState.PlayerFly : BehaviourState.Player);
			Target = null;
			return;
		}

		if (Group is { LedByPlayer: true } && SquadOrderVerb != SquadOrderNone) {
			switch (SquadOrderVerb) {
				case SquadOrderMove:
				case SquadOrderPatrol:
					SetBehaviourState(BehaviourState.Patrolling);
					break;
				case SquadOrderEngage:
					if (SquadOrderTarget is { Destroyed: false } ordered) {
						EngageOrderedTarget(world, ordered);
						return;
					}

					SquadOrderVerb = SquadOrderNone;
					SelectBehaviour(world);
					return;
				case SquadOrderGuard:
					SetBehaviourState(BehaviourState.Guarding);
					break;
				default:
					// Verbs 3 and 5 install nothing at all; the machine keeps the state it has.
					return;
			}

			Target = null;
			return;
		}

		var state = (Group?.OrderVerb ?? MissionGroup.NoOrder) switch {
			MissionOrder.VerbSearchDestroy => BehaviourState.SearchDestroy,
			MissionOrder.VerbRam => BehaviourState.Ramming,
			MissionOrder.VerbGuard => BehaviourState.Guarding,
			MissionOrder.VerbPatrol => BehaviourState.Patrolling,
			MissionOrder.VerbSleep => BehaviourState.Sleeping,
			MissionOrder.VerbTravel => Type.TravelsAsBulldog
				? BehaviourState.BulldogTravel
				: BehaviourState.Travelling,
			MissionOrder.VerbFollow => BehaviourState.Following,
			_ => null
		};

		if (state != null) {
			SetBehaviourState(state);
		}

		Target = null;
	}

	/// <summary>
	/// <c>Mech_AiCombatReassess</c> (<c>0041cf18</c>) — the reassess slot of the five combat states
	/// and <c>fleeing</c>. Radar, then keep-or-acquire, then the leader sweep, then the state.
	/// </summary>
	private void CombatReassess(SimWorld world) {
		// A machine in a fight lights its radar up, which is what makes a distant enemy targetable by
		// the player: the player-side [R] toggle only substitutes for this. A squadmate of the
		// player's is put back to passive instead, so the squad does not paint the sky.
		if (Group is not { LedByPlayer: true } || RadarForcedActive) {
			if (RadarSilenceTimer == 0) {
				Scanner = true;
			}
		} else {
			Scanner = false;
		}

		if (_targetHandedOver) {
			// Something has just given this machine a target; the acquisition is skipped for one pass.
			_targetHandedOver = false;
		} else if (SquadOrderVerb is SquadOrderNone or SquadOrderMove or SquadOrderPatrol) {
			// The one case that gives a fight up: a machine that is already committed and has been
			// told to move falls through to Mech_AiSelectBehaviour, which drops the target and puts it
			// back on its order. Being told to move ends a fight.
			bool movementOrder = SquadOrderVerb == SquadOrderMove
				|| (SquadOrderVerb == SquadOrderNone && GroupOrderIsMovement);

			if (!movementOrder || Behaviour.State is not { Committed: true }) {
				Target = AiTargeting.SelectTarget(world, this, TargetFilter.MissionTargetOnly);
			}

			if (Target == null) {
				SelectBehaviour(world);
				return;
			}
		} else {
			Target = SquadOrderTarget;

			if (Target == null || Target is MechObject { Destroyed: true }) {
				Target = AiTargeting.SelectTarget(world, this, TargetFilter.None);

				if (Target == null) {
					SelectBehaviour(world);
					return;
				}
			}
		}

		// The leader drags the group in: one machine finding a fight commits every other member that
		// is not already in one.
		if (Group is { } group && ReferenceEquals(group.Leader, this)) {
			for (int i = 0; i < group.Members.Count; i++) {
				if (group.Members[i] is MechObject member && !ReferenceEquals(member, this)
						&& !member.Neutralised && member.Behaviour.State is { Committed: false }) {
					member.CombatReassess(world);
				}
			}
		}

		AimComponent = NoAimComponent;

		if (FleeCheck(world)) {
			return;
		}

		if (Target is not { } target) {
			return;
		}

		switch (target.TargetClass) {
			case TargetClass.Structure:
			case TargetClass.GroundVehicle:
				SetBehaviourState(BehaviourState.AttackingBase);
				return;
			case TargetClass.Flyer:
				SetBehaviourState(BehaviourState.AttackingFlyer);
				return;
		}

		SetBehaviourState(CompareCombatRating(world, target) switch {
			1 => BehaviourState.Attacking,
			2 when !LegsCrippled && Type.FlankingGate > FlankingGateThreshold => BehaviourState.Flanking,
			_ => BehaviourState.FacingOff
		});

		SelectAimComponent(world);
	}

	/// <summary>
	/// <c>Mech_AiFleeCheck</c> (<c>0041cb94</c>) — whether the machine breaks off, and the one place
	/// <c>fleeing</c> is installed. Returns true when it has taken the decision itself.
	///
	/// <para><b>Fear is damage.</b> It starts at the machine's overall damage reading and adds a
	/// penalty for each of six components past each of three thresholds, so a machine that has been
	/// shot apart runs from odds a fresh one would take. What it runs from is
	/// <see cref="SimObject.TargetedBy"/> — how many machines are pointing at it — weighed against
	/// the combined combat rating of those machines.</para>
	/// </summary>
	private bool FleeCheck(SimWorld world) {
		if (OutOfAction) {
			// A machine that is out of the fight and still reassessing. The structure exception keys
			// on BASES.DAT +0x2e, a field the format reads past without using, so the branch that
			// would send it back to Mech_AiSelectBehaviour cannot be evaluated and it always flees.
			SetBehaviourState(BehaviourState.Fleeing);
			return true;
		}

		int fear = OverallDamage;

		if (Damage is { } damage) {
			for (int band = 0; band < FearBandThresholds.Count; band++) {
				bool any = false;

				for (int i = 0; i < FearComponents.Count; i++) {
					if (damage.DamagePercent(FearComponents[i]) > FearBandThresholds[band]) {
						fear += FearBandPenalties[band];
						any = true;
					}
				}

				if (!any) {
					break;
				}
			}
		}

		int holders = TargetedBy;
		bool flee;

		if (fear >= FearBreaking) {
			Fear = 1000;
			flee = holders != 0;
		} else if (fear >= FearShaken) {
			Fear = 600;
			flee = holders >= 3
				|| (holders != 0 && CombatRating < AiTargeting.SumAttackerRatings(world, this));
		} else if (fear > FearSteady) {
			Fear = 300;
			flee = holders > 2 && CombatRating < AiTargeting.SumAttackerRatings(world, this) / holders;
		} else {
			flee = false;
		}

		if (flee) {
			SetBehaviourState(BehaviourState.Fleeing);
		}

		return flee;
	}

	/// <summary>
	/// <c>Mech_AiSelectAimComponent</c> (<c>0041ce08</c>) — which component slot the machine works at,
	/// written to <see cref="AimComponent"/>. One roll picks a band — the target's systems, its weapon
	/// mounts or its chassis — and within the band it takes the highest damage reading plus a jitter,
	/// skipping slots the target no longer has.
	///
	/// <para><b>It reads its own damage, not the target's</b>, and that is reproduced: the original
	/// passes <c>this+0x206</c> to <c>Component_ReadDamagePercent</c> at <c>0041cec9</c> while
	/// walking the target's occupancy array. So a machine works at whichever of <i>its own</i>
	/// components is worst hurt, constrained to slots the target still has.</para>
	///
	/// <para><b>A Targeting Pod forces the systems band</b> while its own cached damage is at or under
	/// <see cref="TargetingPodLock.AiSystemsBandLimit"/>, whatever the roll said — so a Cybrid
	/// carrying one goes for the internals rather than the armour. It is the only use an AI machine
	/// has for the pod: the three drivers that make it a <c>[Tab]</c> lock are all player-side.</para>
	/// </summary>
	private void SelectAimComponent(SimWorld world) {
		if (Target is not { } target) {
			AimComponent = NoAimComponent;
			return;
		}

		int roll = world.Random.NextMasked(0x7f);
		bool podPrefersSystems =
			Pods.TargetingMount?.ComponentLock is { ComponentDamage: <= TargetingPodLock.AiSystemsBandLimit };
		int first;
		int last;

		if (target is MechObject { Collapsed: true }) {
			first = 0;
			last = 1;
		} else if (roll < SystemsBandRoll || podPrefersSystems) {
			first = FirstSystemComponent;
			last = WeaponMounts.FirstMountComponent;
		} else if (roll < MountBandRoll) {
			// The mount count is this machine's own, not the target's -- the same "reads itself"
			// slip as the damage below, and from the same register.
			first = WeaponMounts.FirstMountComponent;
			last = WeaponMounts.FirstMountComponent + Weapons.Slots.Count;
		} else {
			first = 0;
			last = FirstSystemComponent;
		}

		short best = NoAimComponent;
		int bestReading = 0;

		for (int i = first; i < last; i++) {
			if (target is MechObject { Damage: { } targetDamage } && !targetDamage.IsActive(i)) {
				continue;
			}

			int reading = (Damage?.DamagePercent(i) ?? 0) + world.Random.NextMasked(0x3f);
			if (reading > bestReading) {
				bestReading = reading;
				best = (short)i;
			}
		}

		AimComponent = best;
	}

	/// <summary>
	/// <c>Mech_AiOnTakingFire</c> (<c>0041f7b8</c>, mech vtable <c>+0x50</c>) — something hit this
	/// machine. It applies no damage; what it does is decide whether, and how, to answer.
	/// </summary>
	/// <param name="attacker">The object whose shot this was.</param>
	/// <param name="damage">The shot's damage, which only feeds the accumulator below.</param>
	public override void OnTakingFire(SimWorld world, SimObject attacker, short damage) {
		if (Behaviour.State is not { } state) {
			return;
		}

		bool inPlayerGroup = world.PlayerMech is { } player && Group != null
			&& ReferenceEquals(Group, player.Group);

		if (ReferenceEquals(attacker, world.PlayerMech) && inPlayerGroup) {
			FriendlyFireComplaint(world);
		}

		if (attacker.OutOfAction || attacker.Side == Side) {
			return;
		}

		// Being shot is a way of being spotted: the whole side near the attacker learns where it is,
		// and inside engagement range the attacker is engaged.
		Detection.ShareContact(world, this, attacker);

		UnderFireWindow = UnderFireWindowReload;

		if (inPlayerGroup) {
			DamageFromPlayerGroup += damage;
			if (DamageFromPlayerGroup < PlayerGroupReactionDamage) {
				return;
			}
		}

		if (IsPlayer || RetargetCooldown != 0) {
			return;
		}

		if (ReferenceEquals(attacker, Target) && state.Committed) {
			return;
		}

		RetargetCooldown = RetargetCooldownReload;

		if (!state.Committed && Group is { LedByPlayer: true }) {
			if (attacker.TargetClass == TargetClass.Herc) {
				PostSquadMessage(world, SquadMessageTakingFire);
			}

			// Being shot at by the very thing the player told this machine to leave alone cancels
			// that order: the latch drops and the shooter becomes targetable again.
			if (ReferenceEquals(attacker, world.PlayerMech?.Target)) {
				IgnoresPlayerSelection = false;
			}
		}

		if (state.HoldsPlace) {
			// Defend the post rather than chase the shooter.
			var post = GoalPosition();
			var defence = AiTargeting.SelectDefenceTarget(world, this, post,
				Group?.OrderTarget != null ? AiTargeting.DefenceRange : AiTargeting.OpenDefenceRange);

			if (defence is not MechObject { Target: { } held } || !ReferenceEquals(held, this)) {
				if (SquadOrderVerb == SquadOrderEngage) {
					defence = SquadOrderTarget;
				}
			}

			var chosen = defence ?? attacker;

			Target = chosen;

			if (chosen.TargetClass == TargetClass.Herc) {
				SetBehaviourState(BehaviourState.DrivingOffEnemy);
				SelectAimComponent(world);
			} else {
				EngageOrderedTarget(world, chosen);
			}

			return;
		}

		if (state.IgnoresFire) {
			return;
		}

		var previous = Target;
		var picked = AiTargeting.SelectTarget(world, this,
			TargetFilter.IgnoreCrowding | TargetFilter.IgnoreBearing);

		if (picked is not MechObject { Target: { } aimed } || !ReferenceEquals(aimed, this)) {
			if (SquadOrderVerb == SquadOrderEngage) {
				if (SquadOrderTarget is { Neutralised: false } ordered) {
					picked = ordered;
				} else {
					SquadOrderVerb = SquadOrderNone;
				}
			}
		}

		Target = picked;

		if (Target == null) {
			if (previous != null) {
				SelectBehaviour(world);
			}

			return;
		}

		_targetHandedOver = true;
		CombatReassess(world);
	}

	/// <summary>
	/// <c>Mech_AiEngageOrderedTarget</c> (<c>0041c0f4</c>) — installs a target and the state that goes
	/// with it: a HERC hands the choice to the combat reassess, anything else takes its own state
	/// directly.
	/// </summary>
	private void EngageOrderedTarget(SimWorld world, SimObject target) {
		Target = target;

		switch (target.TargetClass) {
			case TargetClass.Herc:
				_targetHandedOver = true;
				CombatReassess(world);
				break;
			case TargetClass.Flyer:
				SetBehaviourState(BehaviourState.AttackingFlyer);
				break;
			default:
				SetBehaviourState(BehaviourState.AttackingBase);
				break;
		}
	}

	/// <summary>
	/// <c>Mech_AiGoalPosition</c> (<c>0041dbcc</c>) — the place this machine is working to. A guard
	/// order works to the unit it was pointed at or, pointed at bare ground, to the stored point;
	/// anything else falls through to the mission group's current order target. A group with nothing
	/// to work to leaves the machine standing where it is, which is the post a guard holds anyway.
	///
	/// <para>Verbs 3 and 5 have arms here too, and nothing writes either verb — see
	/// docs/simulation/ai-squadmates.md.</para>
	/// </summary>
	private Vec3i GoalPosition() =>
		SquadOrderVerb == SquadOrderGuard
			? SquadOrderGuardSubject?.Position ?? SquadOrderDestination
			: Group?.OrderTargetPosition ?? Position;

	/// <summary>
	/// <c>Mech_AiFriendlyFireComplaint</c> (<c>0041f790</c>) — squad message 8, on the <see cref="ComplaintCooldownReload"/> cooldown.
	/// </summary>
	internal void FriendlyFireComplaint(SimWorld world) {
		if (ComplaintCooldown != 0) {
			return;
		}

		ComplaintCooldown = ComplaintCooldownReload;
		PostSquadMessage(world, SquadMessageFriendlyFire);
	}

	/// <summary>
	/// <c>Ai_PostSquadMessage</c> (<c>00420a98</c>) — posts to the pilot-and-squad message port, the
	/// second instance of the port the cockpit computer uses. A destroyed machine says nothing unless
	/// <paramref name="force"/> is set, which is how a machine gets to cry out as it dies — see
	/// <see cref="CreditNeutralised"/>, the original's only caller that sets it.
	///
	/// <para>The id is also kept, because it is observable without a sound device and the tests read
	/// it — the port itself decides whether anything is heard, and drops the post entirely for a
	/// machine that is not one of the player's three squadmates.</para>
	/// </summary>
	private void PostSquadMessage(SimWorld world, int messageId, bool force = false) {
		if (Destroyed && !force) {
			return;
		}

		LastSquadMessage = messageId;
		world.Sounds?.SquadSay(messageId, this);
	}

	/// <summary>
	/// <c>Mech_CompareCombatRating</c> (<c>0041cabc</c>, mech vtable <c>+0x4c</c>) — this machine's
	/// rating against a candidate's, as the index the AI's three weight tables share: 0 this
	/// machine's is higher, 1 the two are within <see cref="RatingParity"/>, 2 the candidate's is
	/// higher. Anything that is not a HERC answers 0.
	///
	/// <para>Both figures are jittered first, and the jitter is <c>rand &amp; 1000</c> where
	/// <c>rand % 1000</c> was plainly meant — <c>AND AX,0x3e8</c> at <c>0041cadd</c> and
	/// <c>0041caf8</c>. Masking against <c>0x3e8</c> can only produce the values that are subsets of
	/// its bits, so the jitter spans 0-1000 but lands on very few of the numbers in between. It is
	/// transcribed rather than corrected.</para>
	/// </summary>
	public int CompareCombatRating(SimWorld world, SimObject candidate) {
		if (candidate is not MechObject other) {
			return 0;
		}

		int mine = world.Random.NextMasked(RatingJitterMask) + CombatRating;
		int theirs = world.Random.NextMasked(RatingJitterMask) + other.CombatRating;

		int gap = System.Math.Abs(mine - theirs);
		return gap < RatingParity ? 1 : theirs > mine ? 2 : 0;
	}

	/// <summary>
	/// <c>mech+0x29e</c>, computed by <c>Mech_ComputeCombatRating</c> (<c>0041edd8</c>) — what this
	/// machine is worth in a fight, and the figure both the target weights and the flee check read.
	///
	/// <para>It is the chassis' base, plus every live weapon mount's own value scaled by that mount's
	/// condition (an ammunition mount only while <see cref="WeaponMount.CountsInCombatRating"/>), plus every component's maximum scaled by its condition, less a penalty for each
	/// system past 70% damage — so it falls as the machine is shot apart. Retail states the same
	/// base of 1000 and the same penalty of 500 for all 21 chassis, so what separates two machines is
	/// entirely their guns, their armour and their damage.</para>
	///
	/// <para>The original caches it at <c>mech+0x29e</c> behind the <c>mech+0x94</c> dirty flag and
	/// recomputes it from <c>Mech_PerTickSystemsUpdate</c> while the flag is clear; the one clear
	/// found is <c>Mech_DirectFireHitTest</c>'s, on a hit. Computing it on demand gives the same value
	/// on the tick after a hit, but can see a magazine running low, or damage from another path,
	/// before the original would — docs/simulation/ai-targeting.md, "Open".</para>
	/// </summary>
	public int CombatRating {
		get {
			if (Damage is not { } damage) {
				return 0;
			}

			var readouts = damage.ReadDamageReadouts();
			int rating = Type.AiRatingBase;

			var slots = Weapons.Slots;
			for (int i = 0; i < slots.Count; i++) {
				int readout = readouts[ComponentDamage.FirstCombinedReadout + i];
				if (slots[i] is not { } mount || readout < 0 || !mount.CountsInCombatRating) {
					continue;
				}

				rating += SimMath.Q8Multiply(mount.AiRatingValue, 0x100 - readout);
			}

			for (int i = 0; i < ComponentDamage.ArmorReadoutCount; i++) {
				int readout = readouts[ComponentDamage.FirstArmorReadout + i];
				if (readout < 0) {
					continue;
				}

				rating += SimMath.Q8Multiply(damage.Piece(i)?.Armor ?? 0, 0x100 - readout);
			}

			for (int i = 0; i < RatingSystemCount; i++) {
				if (readouts[ComponentDamage.FirstDependentReadout + i] > RatingSystemPenaltyThreshold) {
					rating -= Type.AiRatingSystemPenalty(i);
				}
			}

			return rating >> 4;
		}
	}

	/// <summary>
	/// Mech vtable <c>+0x40</c> (<c>Mech_GetOverallDamage</c>, <c>00415504</c>) — the machine's
	/// overall damage figure, which is where <see cref="FleeCheck"/> starts from. The slot is one
	/// instruction: <c>Component_ReadOverallDamage(mech + 0x206)</c> (<c>0040db2c</c>), the whole-machine aggregate rather than any
	/// one component's.
	/// </summary>
	public override int OverallDamage => Damage?.OverallDamage ?? 0;

	/// <summary>
	/// <c>mech+0x2a2</c> — the component slot this machine is working at, or
	/// <see cref="NoAimComponent"/>. Written by <see cref="SelectAimComponent"/> and read by
	/// <see cref="AimAndFireAtMech"/>, which puts a shot on that component instead of on the target's aim
	/// node — but only inside <see cref="AimComponentNearRange"/>..<see cref="AimComponentFarRange"/>.
	/// </summary>
	public short AimComponent { get; private set; } = NoAimComponent;

	/// <summary>
	/// <c>mech+0xb4</c> — the machine has collapsed: it has finished going down and is lying on the
	/// ground. Latched when the death animation plays out its last frame, in
	/// <see cref="FallDown"/>.
	///
	/// <para>It is a separate condition from <see cref="Destroyed"/> and from
	/// <see cref="Immobilised"/>, and it is the one that takes a machine off the AI's books
	/// entirely: <see cref="Ai.AiTargeting.IsTargetable"/> rejects a collapsed candidate outright,
	/// and so does the mission group's condition test. A machine that is merely down but still
	/// falling is still a target.</para>
	///
	/// <para>The second writer is <see cref="ApplyStartingCondition"/>, which sets it together with
	/// <see cref="Immobilised"/> at spawn for a machine the mission places as a wreck. Such a machine
	/// is already down, so it never plays the fall.</para>
	/// </summary>
	public bool Collapsed { get; private set; }

	/// <summary>
	/// <c>mech+0x2aa</c> — how frightened this machine is, written by <see cref="FleeCheck"/> on each
	/// of its three live bands. Its one reader is <see cref="ChooseWeapon"/>, where it sets the floor a
	/// weapon has to score above: a frightened machine fires anything it has.
	/// </summary>
	public int Fear { get; private set; }

	/// <summary>
	/// <c>mech+0x273</c> — the retarget cooldown, which is what stops a machine under sustained fire
	/// from re-deciding every hit. Stepped by <see cref="AiTimersTick"/>.
	/// </summary>
	public int RetargetCooldown { get; private set; }

	/// <summary><c>mech+0x278</c> — the friendly-fire complaint cooldown.</summary>
	public int ComplaintCooldown { get; private set; }

	/// <summary>
	/// <c>mech+0x27d</c> — the under-fire window. Its expiry is what clears
	/// <see cref="DamageFromPlayerGroup"/>, so the accumulator is a rolling total over
	/// <see cref="UnderFireWindowReload"/> rather than a lifetime one.
	/// </summary>
	public int UnderFireWindow { get; private set; }

	/// <summary>
	/// <c>mech+0x281</c> — damage this machine has taken from the player's own group inside the
	/// under-fire window. A squadmate ignores fire until it passes
	/// <see cref="PlayerGroupReactionDamage"/>.
	/// </summary>
	public int DamageFromPlayerGroup { get; private set; }

	/// <summary>
	/// <c>mech+0x26b</c> — a countdown that holds the radar off. <c>Mech_DirectFireHitTest</c> loads it
	/// with <see cref="RadarSilenceOnArmHit"/> when an anti-radiation round lands: the machine goes
	/// dark and stays dark long enough for the seeker to lose it. See docs/simulation/target-selection.md
	/// ("How an AI machine's radar is set").
	/// </summary>
	public short RadarSilenceTimer { get; private set; }

	/// <summary>How long an ARM hit holds an AI machine's radar off — the original's own 6000.</summary>
	public const short RadarSilenceOnArmHit = 6000;

	/// <summary>
	/// <c>Mech_DirectFireHitTest</c>'s radar reaction, which runs on a hit against any machine but the
	/// player's. A round of the two radar-guided launcher classes lights the target's scanner up; an
	/// anti-radiation round (class 2) shuts it down and silences it, which is the whole point of the
	/// weapon.
	/// </summary>
	private void RadarReactionToHit(short weaponClass) {
		if (LocallyPiloted) {
			return;
		}

		if (weaponClass is 0 or 1) {
			if (RadarSilenceTimer == 0) {
				Scanner = true;
			}
		} else if (weaponClass == 2) {
			Scanner = false;
			RadarSilenceTimer = RadarSilenceOnArmHit;
		}
	}

	/// <summary>
	/// The last id handed to <c>Ai_PostSquadMessage</c>, kept so the callouts are observable while the
	/// squad channel itself is unported.
	/// </summary>
	public int LastSquadMessage { get; private set; }

	/// <summary>
	/// Whether the group's current order is one of the two that keep a machine moving — verbs 5 and 6,
	/// travelling and following.
	/// </summary>
	private bool GroupOrderIsMovement =>
		Group?.OrderVerb is 5 or 6;

	/// <summary>
	/// The AI countdowns <c>Mech_PerTickSystemsUpdate</c> steps, in its order, and the two damage
	/// accumulators their expiries clear. Called once per tick per machine from the systems pass.
	/// </summary>
	internal void AiTimersTick() {
		int retarget = RetargetCooldown;
		SimMath.TimerCountDown(ref retarget);
		RetargetCooldown = retarget;

		int complaint = ComplaintCooldown;
		SimMath.TimerCountDown(ref complaint);
		ComplaintCooldown = complaint;

		short silence = RadarSilenceTimer;
		SimMath.CountdownTimerTick(ref silence);
		RadarSilenceTimer = silence;

		int underFire = UnderFireWindow;
		if (SimMath.TimerCountDown(ref underFire) == 0) {
			DamageFromPlayerGroup = 0;
		}

		UnderFireWindow = underFire;

		short damageWindow = DamageWindowTimer;
		if (SimMath.CountdownTimerTick(ref damageWindow) == 0) {
			damageWindow = DamageWindowReload;
			DamageTaken = 0;
		}

		DamageWindowTimer = damageWindow;
	}

	/// <summary><c>mech+0xac</c> — a target was just handed over; skip one acquisition.</summary>
	private bool _targetHandedOver;

	/// <summary>The value <see cref="AimComponent"/> takes when no component was chosen.</summary>
	public const short NoAimComponent = -1;

	/// <summary>Where the target's system components start; the chassis band runs below it.</summary>
	public const int FirstSystemComponent = 7;

	/// <summary>Below this roll of <c>rand &amp; 0x7f</c> the systems band is taken.</summary>
	private const int SystemsBandRoll = 0x28;

	/// <summary>Below this roll, and above <see cref="SystemsBandRoll"/>, the mount band is taken.</summary>
	private const int MountBandRoll = 0x50;

	/// <summary>Ratings within this of each other count as evenly matched.</summary>
	public const int RatingParity = 400;

	/// <summary>The original's <c>AND AX,0x3e8</c>. See <see cref="CompareCombatRating"/>.</summary>
	private const int RatingJitterMask = 1000;

	/// <summary>How many of the twelve system readouts the rating penalises.</summary>
	private const int RatingSystemCount = 10;

	/// <summary>Q8 damage past which a system costs the rating its penalty — 70%.</summary>
	private const int RatingSystemPenaltyThreshold = 0xb4;

	/// <summary>
	/// Forward speed at or below which an outgunned machine squares up instead of flanking — see
	/// <see cref="MechTypeRecord.FlankingGate"/> for why the gate reads speed.
	/// </summary>
	private const int FlankingGateThreshold = 0xb9;

	/// <summary>Fear at or above which any attacker at all is enough.</summary>
	private const int FearBreaking = 71;

	/// <summary>Fear at or above which three attackers, or a stronger pair, are enough.</summary>
	private const int FearShaken = 51;

	/// <summary>Fear at or below which the machine never breaks off.</summary>
	private const int FearSteady = 30;

	/// <summary><c>0049a328</c> — the six components the flee check weighs.</summary>
	private static readonly IReadOnlyList<int> FearComponents = new[] { 0, 1, 4, 5, 6, 7 };

	/// <summary><c>0049a33a</c> — the Q8 damage each fear band starts at.</summary>
	private static readonly IReadOnlyList<int> FearBandThresholds = new[] { 60, 120, 180 };

	/// <summary><c>0049a334</c> — what a component past each band costs in fear.</summary>
	private static readonly IReadOnlyList<int> FearBandPenalties = new[] { 5, 25, 50 };

	/// <summary>
	/// The under-fire window, in <see cref="SimMath.TimerCountDown"/>'s unit rather than
	/// milliseconds: about 15 seconds.
	/// </summary>
	private const int UnderFireWindowReload = 30000;

	/// <summary>The retarget cooldown, in the same unit — about 4.9 seconds.</summary>
	private const int RetargetCooldownReload = 10000;

	/// <summary>The friendly-fire complaint cooldown, in the same unit — about 20 seconds.</summary>
	private const int ComplaintCooldownReload = 40000;

	/// <summary>
	/// How much damage from the player's own group a squadmate absorbs before it reacts at all.
	/// </summary>
	public const int PlayerGroupReactionDamage = 8000;

	/// <summary>Squad order verb 0 — none standing.</summary>
	public const short SquadOrderNone = 0;

	/// <summary>Squad order verb 1 — move, which is what ends a fight in the combat reassess.</summary>
	public const short SquadOrderMove = 1;

	/// <summary>Squad order verb 2 — patrol.</summary>
	public const short SquadOrderPatrol = 2;

	/// <summary>Squad order verb 4 — engage the order's target.</summary>
	public const short SquadOrderEngage = 4;

	/// <summary>Squad order verb 6 — guard the order's post.</summary>
	public const short SquadOrderGuard = 6;

	/// <summary>Squad message 3 — taking fire.</summary>
	public const int SquadMessageTakingFire = 3;

	/// <summary>Squad message 8 — the player is shooting one of his own.</summary>
	public const int SquadMessageFriendlyFire = 8;

	/// <summary>Squad message 2 — a squadmate put something out of the fight.</summary>
	public const int SquadMessageScoredAKill = 2;

	/// <summary>Squad message 4 — a squadmate stopped but not destroyed.</summary>
	public const int SquadMessageWentDown = 4;

	/// <summary>Squad message 0x25 — a squadmate destroyed.</summary>
	public const int SquadMessageDestroyed = 0x25;
}
