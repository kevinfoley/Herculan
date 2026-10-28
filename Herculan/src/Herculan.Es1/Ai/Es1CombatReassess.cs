using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// The combat reassess, the reassess of the five combat states and <c>fleeing</c>, with the flee check and the choice of the
/// component the AI aims at (docs/es1/simulation/mech-ai.md#the-combat-reassess--mech_aicombatreassess-00016e04, which links
/// ES2's derivation, docs/retail/simulation/ai-targeting.md#the-combat-reassess--mech_aicombatreassess-0041cf18).
/// </summary>
public static class Es1CombatReassess {
	/// <summary>The class of a structure target, and of an aircraft target.</summary>
	public const short StructureClass = 1, FlyerClass = 2;

	/// <summary>The squad verbs under which the reassess acquires, and the one of them that is a move.</summary>
	public const short MoveSquadVerb = 1, LastAcquiringVerb = 2;

	/// <summary>
	/// <c>AiFlee_InternalComponents</c> (<c>00091cf8</c>), the internals whose damage feeds the fear;
	/// <c>AiFlee_BandThresholds</c> (<c>00091d0c</c>) and <c>AiFlee_BandPenalties</c> (<c>00091d04</c>), each band's Q8
	/// damage bar and what each internal past it adds.
	/// </summary>
	private static readonly short[] FleeInternals = [0, 1, 4, 5, 6, 7], BandThresholds = [60, 120, 180],
		BandPenalties = [5, 25, 50];

	/// <summary>The fear bands' floors, from the top, and the fear each writes to <see cref="Es1Mech.Fear"/>.</summary>
	public const short PanicFear = 0x47, AlarmFear = 0x33, WaryFear = 0x1f;

	/// <inheritdoc cref="PanicFear"/>
	public const short PanicFloor = 1000, AlarmFloor = 600, WaryFloor = 300;

	/// <summary>The holders at and past which an alarmed or wary machine counts as outnumbered.</summary>
	public const short Outnumbered = 3;

	/// <summary>The aim bands' rolls of <c>Math_RandomNext</c> &amp; <c>0x7f</c>: under the first the systems, under the second the mounts.</summary>
	public const int SystemsRoll = 0x28, MountsRoll = 0x50;

	/// <summary>The first slot of the systems' band, and of the mounts'.</summary>
	public const short FirstSystemSlot = 7, FirstMountSlot = 19;

	/// <summary><c>Mech_AiEnterCombat</c> (<c>ES1DBSIM 000174cc</c>)(<paramref name="mech"/>): <see cref="Reassess"/>.</summary>
	public static void EnterCombat(Es1Mech mech, Es1SimGlobals globals) => Reassess(mech, globals);

	/// <summary>
	/// <c>Mech_AiCombatReassess</c> (<c>00016e04</c>)(<paramref name="mech"/>), in order:
	/// <list type="number">
	/// <item>the radar <c>+0x90</c> on unless the radar silence <c>+0x201</c> runs, or off for a machine of a group the
	/// player's machine leads without <see cref="Es1Mech.RadarKeptOn"/>;</item>
	/// <item>with <see cref="Es1Mech.TargetHandedOver"/> clear, under squad verbs 0–2, <see cref="Es1Targets.SelectTarget"/>
	/// with mask 1 installed, unless the machine is committed (descriptor flag 1) under verb 1 or under verb 0 with its group's
	/// order <see cref="Es1MissionGroup.TravellingOrder"/>; under any other verb a destroyed target, read with no null test,
	/// replaced by <see cref="Es1Targets.SelectTarget"/> with mask 0; no target ends in
	/// <see cref="Es1StateChoice.SelectBehaviour"/>. With it set, it is cleared and the target kept;</item>
	/// <item>the group's first member runs <see cref="EnterCombat"/> on each other member, the walk ending at a null one,
	/// that is neither immobilized, destroyed nor committed;</item>
	/// <item>the aim slot −1 and <see cref="FleeCheck"/>; unless it decided, a class-1 target installs <c>attacking
	/// base</c>, class 2 <c>attacking flyer</c>, and any other, after a second flee check, takes
	/// <see cref="Es1CombatRating.Compare"/>: 0 <c>facing off</c>, 1 <c>attacking</c>, 2 <c>flanking</c>, then
	/// <see cref="SelectAimComponent"/>.</item>
	/// </list>
	/// It answers 0.
	/// </summary>
	public static short Reassess(Es1Mech mech, Es1SimGlobals globals) {
		var group = mech.Group;
		// Retail reads the first member unchecked; with none, the machine is taken as led by another here.
		if (Es1Navigation.Leader(mech) is not { IsPlayer: true } || mech.RadarKeptOn) {
			if (mech.Countdowns.RadarSilence == 0) {
				mech.RadarOn = true;
			}
		} else {
			mech.RadarOn = false;
		}

		if (!mech.TargetHandedOver) {
			short verb = mech.SquadVerb;
			if (verb is >= 0 and <= LastAcquiringVerb) {
				// Retail reads the group's order unchecked; with no group, none here.
				bool moving = verb == MoveSquadVerb || (verb == 0 && group?.Order == Es1MissionGroup.TravellingOrder);
				if (moving && mech.Behaviour.Descriptor?.Flags[1] == true) {
					Es1StateChoice.SelectBehaviour(mech, globals);
					return 0;
				}

				mech.InstallTarget(Es1Targets.SelectTarget(mech, Es1Targets.DesignatedOnly, globals));
				if (mech.Target == null) {
					Es1StateChoice.SelectBehaviour(mech, globals);
					return 0;
				}
			} else if (mech.Target?.Destroyed != false) {
				// Retail reads the target unchecked; with none it is replaced as a destroyed one is here.
				mech.InstallTarget(Es1Targets.SelectTarget(mech, 0, globals));
				if (mech.Target == null) {
					Es1StateChoice.SelectBehaviour(mech, globals);
					return 0;
				}
			}
		} else {
			mech.TargetHandedOver = false;
		}

		DragGroupIn(mech, globals);

		mech.AimSlot = -1;
		if (FleeCheck(mech, globals)) {
			return 0;
		}

		// Retail reads the target's class unchecked; a machine handed no target goes back to its order here, an engine guard.
		if (mech.Target is not { } target) {
			Es1StateChoice.SelectBehaviour(mech, globals);
			return 0;
		}

		int state;
		if (target.TargetClass == StructureClass) {
			state = Es1MechBehaviour.AttackingBase;
		} else if (target.TargetClass == FlyerClass) {
			state = Es1MechBehaviour.AttackingFlyer;
		} else {
			// The second flee check draws nothing and reads what the first read, so it gives the first's answer.
			if (FleeCheck(mech, globals)) {
				return 0;
			}

			state = Es1CombatRating.Compare(mech, target, globals) switch {
				0 => Es1MechBehaviour.FacingOff,
				1 => Es1MechBehaviour.Attacking,
				_ => Es1MechBehaviour.Flanking,
			};
			SelectAimComponent(mech, globals);
		}

		mech.Behaviour.SetState(Es1MechBehaviour.States[state], globals.DwellJitter);
		return 0;
	}

	/// <summary>
	/// The leader's drag: for the group's first member, <see cref="EnterCombat"/> on each other member neither immobilized,
	/// destroyed nor committed, in member order up to the count <c>+0x12</c> or a null member.
	/// </summary>
	private static void DragGroupIn(Es1Mech mech, Es1SimGlobals globals) {
		if (mech.Group is not { } group || !ReferenceEquals(Es1Navigation.Leader(mech), mech)) {
			return;
		}

		foreach (var entry in group.Members) {
			if (entry == null) {
				break;
			}

			// A member that is not a HERC, which retail reads as one, is passed over here.
			if (entry is Es1Mech member && !ReferenceEquals(member, mech) && !member.Immobilized && !member.Destroyed
					&& member.Behaviour.Descriptor?.Flags[1] != true) {
				EnterCombat(member, globals);
			}
		}
	}

	/// <summary>
	/// <c>Mech_AiFleeCheck</c> (<c>00016a74</c>)(<paramref name="mech"/>), which answers whether it installed a state. A
	/// machine out of action takes <see cref="Es1StateChoice.SelectBehaviour"/> when its target is of class 1 with its type
	/// record's <c>+0x34</c> at 0, otherwise <c>fleeing</c>, and answers 1. Any other builds its fear: the overall damage
	/// (slot <c>+0x44</c>) plus, for each band of <see cref="BandThresholds"/> in turn, that band's penalty for each internal
	/// of <see cref="FleeInternals"/> whose Q8 damage (full for a negative entry) is past the band, stopping at a band none
	/// passes. At <see cref="PanicFear"/> and up <see cref="Es1Mech.Fear"/> is <see cref="PanicFloor"/> and it flees for any
	/// holder; at <see cref="AlarmFear"/> and up <see cref="AlarmFloor"/>, fleeing for <see cref="Outnumbered"/> holders or
	/// more, or for one or two whose summed ratings (<see cref="Es1CombatRating.SumAttackerRatings"/>) beat its own; at
	/// <see cref="WaryFear"/> and up <see cref="WaryFloor"/>, fleeing for more than two whose average beats its own. Fleeing
	/// installs <c>fleeing</c>.
	/// </summary>
	public static bool FleeCheck(Es1Mech mech, Es1SimGlobals globals) {
		if (mech.OutOfAction) {
			if (mech.Target is { TargetClass: StructureClass } target && StructureTypeWord34IsZero(target)) {
				Es1StateChoice.SelectBehaviour(mech, globals);
			} else {
				mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Fleeing], globals.DwellJitter);
			}

			return true;
		}

		short fear = mech.OverallDamage;
		var ratios = new short[FleeInternals.Length];
		for (int i = 0; i < FleeInternals.Length; i++) {
			// Retail reads the header unchecked and divides by the maximum; with no header, or a maximum of 0, 0 here.
			var header = mech.Damage;
			short entry = header?.InternalDamage[FleeInternals[i]] ?? (short)0;
			short maximum = header?.Maximum(FleeInternals[i]) ?? (short)0;
			ratios[i] = entry < 0 ? Es1DamageHeader.Full : maximum == 0 ? (short)0 : (short)((entry << 8) / maximum);
		}

		for (int band = 0; band < BandThresholds.Length; band++) {
			bool passed = false;
			foreach (short ratio in ratios) {
				if (BandThresholds[band] < ratio) {
					fear += BandPenalties[band];
					passed = true;
				}
			}

			if (!passed) {
				break;
			}
		}

		short holders = mech.TargetHolders;
		bool flee = false;
		if (fear >= PanicFear) {
			mech.Fear = PanicFloor;
			flee = holders != 0;
		} else if (fear >= AlarmFear) {
			mech.Fear = AlarmFloor;
			flee = holders >= Outnumbered
				|| (holders != 0 && mech.CombatRating < Es1CombatRating.SumAttackerRatings(mech, globals));
		} else if (fear >= WaryFear) {
			mech.Fear = WaryFloor;
			flee = holders >= Outnumbered
				&& mech.CombatRating < Es1CombatRating.SumAttackerRatings(mech, globals) / holders;
		}

		if (flee) {
			mech.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Fleeing], globals.DwellJitter);
		}

		return flee;
	}

	/// <summary>
	/// <c>Mech_AiSelectAimComponent</c> (<c>00016cf4</c>)(<paramref name="mech"/>): one roll of <c>Math_RandomNext</c>
	/// &amp; <c>0x7f</c>, drawn first, picks the band: slot 0 alone for a collapsed target (<c>+0xae</c>); otherwise under
	/// <see cref="SystemsRoll"/> slots 7–18, under <see cref="MountsRoll"/> slots 19 to 18 + this machine's own mount count,
	/// and the rest slots 0–6. Of the band's slots the target's occupancy array (<c>+0x1a4</c>) still holds, it keeps the
	/// first with the highest <c>Component_ReadDamagePercent</c> of this machine's <i>own</i> damage plus
	/// <c>Math_RandomNext</c> &amp; <c>0x3f</c>, above 0, in <see cref="Es1Mech.AimSlot"/>, or −1.
	/// </summary>
	public static void SelectAimComponent(Es1Mech mech, Es1SimGlobals globals) {
		short chosen = -1;
		// Retail reads the target as a HERC with no test; a target that is not one is aimed at no component here.
		if (mech.Target is not Es1Mech target) {
			mech.AimSlot = chosen;
			return;
		}

		int roll = globals.Random.Next() & 0x7f;
		short first, end;
		if (target.Collapsed) {
			(first, end) = (0, 1);
		} else if (roll < SystemsRoll) {
			(first, end) = (FirstSystemSlot, FirstMountSlot);
		} else if (roll < MountsRoll) {
			// Retail reads the manager unchecked; with none, the band is empty here.
			(first, end) = (FirstMountSlot, (short)(FirstMountSlot + (mech.Mounts?.Mounts.Count ?? 0)));
		} else {
			(first, end) = (0, FirstSystemSlot);
		}

		short best = 0;
		for (short slot = first; slot < end; slot++) {
			// Retail reads the occupancy array unchecked and past its 28 words; no header, or a slot past it, is empty here.
			if (target.Damage is not { } header || slot >= Es1DamageHeader.ComponentCount || !header.Occupied[slot]) {
				continue;
			}

			short percent = mech.Damage?.ReadDamagePercent(slot) ?? (short)0;
			short score = (short)(percent + (globals.Random.Next() & 0x3f));
			if (best < score) {
				chosen = slot;
				best = score;
			}
		}

		mech.AimSlot = chosen;
	}

	/// <summary>
	/// PLACEHOLDER: whether a class-1 target's type record <c>+0x34</c> is 0, which the flee check and <c>attacking base</c>
	/// read. No structure is a simulation object in the slice (docs/herculan/plan-es1-first-mission.md#what-the-slice-leaves-out),
	/// so no target has class 1 and this is never asked; it answers no until the plan's module 2 (effects and structures).
	/// </summary>
	internal static bool StructureTypeWord34IsZero(IEs1SimObject target) => false;
}
