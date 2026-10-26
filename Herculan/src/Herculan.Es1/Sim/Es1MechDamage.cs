using Herculan.Engine.Numerics;
using Herculan.Es1.Messages;

namespace Herculan.Es1.Sim;

/// <summary>
/// A HERC's hits and damage (docs/es1/simulation/mech.md#hits-and-damage): the hit test, the damage write every writer
/// ends in, the blasts, and the writers outside a hit, the fall, the starting condition and the outnumbered damage.
/// </summary>
public static class Es1MechDamage {
	/// <summary>The radius of the blast a shot's splash makes on the machine it struck.</summary>
	public const short SplashRadius = 500;

	/// <summary>The shot types whose hit turns an AI machine's radar on, and the type whose hit turns it off.</summary>
	public const short RadarOnKind1 = 0, RadarOnKind2 = 1, RadarOffKind = 2;

	/// <summary>The radar silence a hit by <see cref="RadarOffKind"/> sets.</summary>
	public const short RadarSilenceOnHit = 6000;

	/// <summary>The computer message the player's machine posts once when a shot gets through its shields.</summary>
	public const short ShieldsMessage = 8;

	/// <summary>The first mount component, hardpoint slot 0's.</summary>
	public const int FirstMountComponent = 19;

	/// <summary>The mount knock-out roll's odds, times <see cref="MountRollScale"/> in 4096: side 0's and the other side's.</summary>
	public const int MountRollOddsSide0 = 3, MountRollOddsOther = 10, MountRollScale = 0x29;

	/// <summary>What the roll writes a knocked-out mount's component off with.</summary>
	public const short MountFinishOff = 10000;

	/// <summary>The roll a component takes a blast at, under this of the low 12 bits.</summary>
	public const int BlastOdds = 0x802;

	/// <summary><c>DamageScaleByDifficulty</c> (<c>000922c4</c>), Q10, by difficulty.</summary>
	public static readonly short[] DifficultyScale = [2800, 2000, 1400, 1000];

	/// <summary>The difficulty <see cref="ScaleByDifficulty"/> reads while <see cref="Es1SimGlobals.MissionModeFlag"/> is not 0.</summary>
	public const int ModeDifficulty = 3;

	/// <summary>
	/// <c>Damage_ScaleByDifficulty</c> (<c>0001f82c</c>)(<paramref name="figure"/>)
	/// (docs/es1/simulation/world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c): Q10 by
	/// <see cref="DifficultyScale"/> at header word 7, or at <see cref="ModeDifficulty"/> while the mode is not 0. Retail
	/// reads past the table for a word outside 0 to 3; the figure stands then, an engine guard.
	/// </summary>
	public static short ScaleByDifficulty(short figure, Es1SimGlobals globals) {
		int i = globals.MissionModeFlag != 0 ? ModeDifficulty : globals.HeaderWord7;
		return i >= 0 && i < DifficultyScale.Length ? (short)SimMath.Q10MultiplyRounded(figure, DifficultyScale[i]) : figure;
	}

	/// <summary>
	/// <c>Mech_DirectFireHitTest</c> (<c>00013b44</c>), slot <c>+0x24</c>, (<paramref name="machine"/>,
	/// <paramref name="shot"/>) (docs/es1/simulation/mech.md#the-hit-test--mech_directfirehittest-00013b44):
	/// <list type="number">
	/// <item>0 when the ray's start is farther from the machine than its clearance, its length and the hit radius;</item>
	/// <item>the shot's shield and armor figures, each through <see cref="ScaleByDifficulty"/> for an owner on side 0;</item>
	/// <item>a machine not the player's turns its radar on for shot type 0 or 1 while its radar silence is 0, and off for
	/// type 2 with the silence set to <see cref="RadarSilenceOnHit"/>;</item>
	/// <item>the shields (<see cref="Es1Shields.AbsorbDirectFire"/>) in the machine's frame through the ray's; a miss is 0;</item>
	/// <item>a hit clears <see cref="Es1Mech.SkipCombatRating"/> and adds what the shields took to
	/// <see cref="Es1Mech.DamageTaken"/>. A shot they stopped ends at the entry. Otherwise the player's machine posts
	/// <see cref="ShieldsMessage"/> once, latched on <see cref="Es1Mech.ShieldsMessagePosted"/>, and the struck component
	/// is picked (<see cref="Es1HitSpheres.SelectStruckComponent"/>), 0 for a miss;</item>
	/// <item>a struck component adds the armor figure to <see cref="Es1Mech.DamageTaken"/>; the splash is Q10 of the armor
	/// figure by the shot's factor; the rest goes through <see cref="ComponentDamageWrite"/> and the splash, when there is
	/// one, through <see cref="ApplyExplosiveDamage"/> at <see cref="SplashRadius"/>. A reading's band (≫ 5) that changed
	/// starts the hit shake (<see cref="View.Es1HitShake.Start"/>) for component 0 or 1 of the player's machine and runs
	/// the mount knock-out roll (<see cref="MountKnockOutRoll"/>);</item>
	/// <item>unless the component now reads full, the generator is drawn for the impact effect, entry 4<i>n</i> + the draw
	/// &amp; 3 of the shot's effects, <i>n</i> 0 for a stopped shot, 1 for an unchanged band, 2 for a changed one.</item>
	/// </list>
	/// Answers the entry distance, or the component's after a strike.
	/// </summary>
	public static int HitTest(Es1Mech machine, Es1Shot shot, Es1SimGlobals globals) {
		var ray = shot.Ray;
		var chassis = machine.Type.Chassis;
		if (machine.Position.ApproxDistanceTo(ray.Start) > ray.Clearance + ray.Length + chassis.HitRadius) {
			return 0;
		}

		short shieldFigure = shot.Shield, armorFigure = shot.Armor;
		// Retail reads the owner's group unchecked; an owner with none is not scaled here.
		if (shot.Owner.Group?.Side == 0) {
			shieldFigure = ScaleByDifficulty(shieldFigure, globals);
			armorFigure = ScaleByDifficulty(armorFigure, globals);
		}

		if (!machine.IsPlayer) {
			if (shot.Kind is RadarOnKind1 or RadarOnKind2) {
				if (machine.Countdowns.RadarSilence == 0) {
					machine.RadarOn = true;
				}
			} else if (shot.Kind == RadarOffKind) {
				machine.RadarOn = false;
				machine.Countdowns.RadarSilence = RadarSilenceOnHit;
			}
		}

		var toMuzzle = Transform3.Concat(machine.Frame, ray.WorldToRay);
		short throughShields = shieldFigure;
		int distance = machine.Shields.AbsorbDirectFire(ref throughShields, ray.Length, toMuzzle, chassis.HitCenterHeight,
			chassis.HitRadius);
		if (distance == 0) {
			return 0;
		}

		machine.SkipCombatRating = false;
		machine.DamageTaken += shieldFigure - throughShields;
		int group = 0;
		bool struck = false, full = false;
		if (throughShields != 0) {
			if (machine.IsPlayer && !machine.ShieldsMessagePosted) {
				Es1MessageTicker.ComputerPostMessage(globals, ShieldsMessage);
				machine.ShieldsMessagePosted = true;
			}

			distance = 0;
			if (machine.Damage is { } header
					&& Es1HitSpheres.SelectStruckComponent(header.Model, header.Occupied, toMuzzle, ray, machine) is { } hit) {
				(distance, short component) = hit;
				struck = true;
				machine.DamageTaken += armorFigure;
				var point = ray.Frame.TransformPoint(0, distance, 0);
				short splash = (short)SimMath.Q10MultiplyRounded(armorFigure, shot.SplashFactor);
				short band = (short)(header.ReadDamagePercent(component) >> 5);
				ComponentDamageWrite(machine, shot.Owner, (short)(armorFigure - splash), component, globals);
				if (splash != 0) {
					ApplyExplosiveDamage(machine, shot.Owner, SplashRadius, point, splash, globals);
				}

				short after = header.ReadDamagePercent(component);
				full = after == Es1DamageHeader.Full;
				if (after >> 5 == band) {
					group = 1;
				} else {
					group = 2;
					if (component is 0 or 1 && machine.IsPlayer) {
						// With no cockpit built, which retail always has, nothing shakes.
						globals.CockpitViewInstance?.HitShake.Start();
					}

					MountKnockOutRoll(machine, component, after, full, globals);
				}
			}
		}

		if (distance != 0 && !full) {
			var point = ray.Frame.TransformPoint(0, distance, 0);
			int k = globals.Random.Next() & 3;
			short? effect = group switch {
				0 => shot.Effects?.ImpactFXShield[k],
				1 => shot.Effects?.ImpactFXGround[k],
				_ => shot.Effects?.ImpactFXArmor[k],
			};
			// Explosion_Construct (00005af0)(null, point, effect); a shot with no effects makes none.
			if (effect is { } type) {
				globals.Explosions.Construct(point, type);
			}

			// PLACEHOLDER: for a struck component Debris_ThrowGroup (00005fb8)(group `group` of 000bc1c0, point, the roots
			// 000bc1c6), plan step 7e part B.
			_ = struck;
		}

		return distance;
	}

	/// <summary>
	/// The hit test's mount knock-out roll, ES2's (docs/retail/simulation/weapon-mounts.md#the-chance-path--the-destruction-roll):
	/// for a component past 18 not left reading full on a chassis whose record's <c>+0x56</c> is not 0, the generator's
	/// low 12 bits under <see cref="MountRollOddsSide0"/> or, for a machine whose group's side is not 0,
	/// <see cref="MountRollOddsOther"/>, times <see cref="MountRollScale"/>, take the mount of hardpoint slot component −
	/// 19 (<c>WeaponMounts_MountForHardpointSlot</c>, <c>0000ca04</c>), destroy it with its gun thrown
	/// (<see cref="Es1WeaponMount.Destroy"/>), clear the component's occupancy flag and write it
	/// off with <see cref="MountFinishOff"/> through <see cref="Es1DamageHeader.ApplyDamageAndCascade"/>.
	/// </summary>
	private static void MountKnockOutRoll(Es1Mech machine, short component, short after, bool full, Es1SimGlobals globals) {
		if (full || machine.Type.Chassis.WeaponMountsDestructible == 0 || component <= 18 || machine.Damage is not { } header) {
			return;
		}

		// Retail reads the group's side unchecked; a machine with none rolls side 0's odds here.
		int odds = (machine.Group?.Side ?? 0) == 0 ? MountRollOddsSide0 : MountRollOddsOther;
		if ((globals.Random.Next() & 0xfff) >= odds * MountRollScale) {
			return;
		}

		int slot = component - FirstMountComponent;
		var mount = machine.Mounts?.Mounts.FirstOrDefault(m => m != null && (sbyte)m.Hardpoint.LoadoutSlot == slot);
		// Retail destroys the mount it finds unchecked; with none, nothing is destroyed here.
		mount?.Destroy(true, machine);
		header.Occupied[component] = false;
		if ((machine.Group?.Side ?? 0) == 1) {
			short condition = (short)((short)(0x100 - SimMath.Q10MultiplyRounded(500, after)) * 100 >> 8);
			// PLACEHOLDER: Salvage_QueueWeapon (0001f7e8)(the mount template's +0x56, condition), the mission's salvage
			// list, which the results write reads; no plan step ports the results yet.
			_ = condition;
		}

		header.ApplyDamageAndCascade(MountFinishOff, component, globals);
	}

	/// <summary>
	/// <c>Mech_ComponentDamageWrite</c> (<c>00013250</c>)(<paramref name="machine"/>, <paramref name="attacker"/>,
	/// <paramref name="damage"/>, <paramref name="component"/>), the endpoint every writer ends in
	/// (docs/es1/simulation/mech.md#the-damage-write--mech_componentdamagewrite-00013250):
	/// <list type="number">
	/// <item>nothing on the player's machine while the mode is 0 and header word 6 is 1, nor on a component whose
	/// occupancy flag is clear;</item>
	/// <item>the cascade, <see cref="Es1DamageHeader.ApplyDamageAndCascade"/>;</item>
	/// <item>each mount not yet destroyed whose component's occupancy flag reads clear destroyed, its gun not thrown
	/// (<see cref="Es1WeaponMount.Destroy"/>);</item>
	/// <item>the legs (<see cref="Es1MechOutOfAction.Legs"/>) and death (<see cref="Es1MechOutOfAction.Death"/>);</item>
	/// <item>the reactor, while neither grade is set: internal 5's reading sets <see cref="Es1Mech.ReactorCritical"/>
	/// past <c>0xc0</c>, otherwise <see cref="Es1Mech.ReactorDamaged"/> past <c>0x80</c>.</item>
	/// </list>
	/// </summary>
	public static void ComponentDamageWrite(Es1Mech machine, IEs1SimObject? attacker, short damage, short component,
			Es1SimGlobals globals) {
		if (machine.IsPlayer && globals.MissionModeFlag == 0 && globals.HeaderWord6 == 1) {
			return;
		}

		// Retail reads a component past the flags array past it; nothing is written for one here.
		if (machine.Damage is not { } header || component < 0 || component >= Es1DamageHeader.ComponentCount
				|| !header.Occupied[component]) {
			return;
		}

		header.ApplyDamageAndCascade(damage, component, globals);

		// Step 3. Retail reads the flag of a hardpoint slot outside the components unchecked; such a mount is kept here.
		foreach (var mount in machine.Mounts?.Mounts ?? []) {
			int mountComponent = FirstMountComponent + (sbyte)(mount?.Hardpoint.LoadoutSlot ?? 0);
			if (mount is { Destroyed: false } && mountComponent >= 0 && mountComponent < Es1DamageHeader.ComponentCount
					&& !header.Occupied[mountComponent]) {
				mount.Destroy(false, machine);
			}
		}

		Es1MechOutOfAction.Legs(machine, attacker, header, globals);
		Es1MechOutOfAction.Death(machine, attacker, header, globals);

		if (!machine.ReactorDamaged && !machine.ReactorCritical) {
			short reading = header.InternalReading(ReactorInternal);
			if (reading > ReactorCriticalReading) {
				machine.ReactorCritical = true;
			} else if (reading > ReactorDamagedReading) {
				machine.ReactorDamaged = true;
			}
		}
	}

	/// <summary>The reactor's internal.</summary>
	public const int ReactorInternal = 5;

	/// <summary>The readings past which the reactor grades are set.</summary>
	public const short ReactorCriticalReading = 0xc0, ReactorDamagedReading = 0x80;

	/// <summary>
	/// <c>Mech_ApplyExplosiveDamage</c> (<c>00013a7c</c>)(<paramref name="machine"/>, <paramref name="attacker"/>,
	/// <paramref name="radius"/>, <paramref name="point"/>, <paramref name="damage"/>)
	/// (docs/es1/simulation/mech.md#a-blast--mech_applyexplosivedamage-00013a7c): no facing and no shields; each
	/// occupied component in turn whose roll, the generator's low 12 bits, falls under <see cref="BlastOdds"/> is placed
	/// (<see cref="Es1Mech.ComponentPosition"/>), and one nearer the point than <paramref name="radius"/> takes
	/// <paramref name="damage"/> × (radius − distance) / radius through <see cref="ComponentDamageWrite"/>.
	/// </summary>
	public static void ApplyExplosiveDamage(Es1Mech machine, IEs1SimObject? attacker, short radius, Vec3i point,
			short damage, Es1SimGlobals globals) {
		if (machine.Damage is not { } header) {
			return;
		}

		for (short component = 0; component < Es1DamageHeader.ComponentCount; component++) {
			if (!header.Occupied[component] || (globals.Random.Next() & 0xfff) >= BlastOdds) {
				continue;
			}

			int distance = point.ApproxDistanceTo(machine.ComponentPosition(component));
			if (distance < radius) {
				ComponentDamageWrite(machine, attacker, (short)(damage * (radius - distance) / radius), component, globals);
			}
		}
	}

	/// <summary>
	/// <c>Damage_ExplosiveBlastSweep</c> (<c>0001f75c</c>)(<paramref name="damage"/>, <paramref name="radius"/>,
	/// <paramref name="point"/>) (docs/es1/simulation/world.md#blasts--damage_explosiveblastsweep-0001f75c): the mech pool
	/// oldest first, as <c>Pool_Prev</c> walks it from the end, each machine whose group holds no action and whose position is nearer the point than the radius
	/// taking <see cref="ApplyExplosiveDamage"/> with no attacker; answers whether any did. Retail reads the group
	/// unchecked; a machine with none is taken as holding no action.
	/// </summary>
	public static bool ExplosiveBlastSweep(short damage, short radius, Vec3i point, Es1SimGlobals globals) {
		bool any = false;
		for (int i = 0; i < globals.MechPool.Count; i++) {
			var mech = globals.MechPool[i];
			if (mech.Group?.Action == null && point.ApproxDistanceTo(mech.Position) < radius) {
				any = true;
				ApplyExplosiveDamage(mech, null, radius, point, damage, globals);
			}
		}

		return any;
	}

	/// <summary>
	/// <c>Mech_SpreadImpactDamage</c> (<c>00013078</c>)(<paramref name="machine"/>, <paramref name="odds"/>,
	/// <paramref name="damage"/>) (docs/retail/simulation/component-damage.md#spread-impact-damage--mech_spreadimpactdamage-00417a04):
	/// nothing for a damage of 0; otherwise each occupied component whose roll, the generator's low byte, is under
	/// <paramref name="odds"/> takes Q8(<see cref="Es1DamageHeader.TotalArmor"/>, <c>Math_RandomBelow</c>(damage) +
	/// damage / 2) through <see cref="ComponentDamageWrite"/> with no attacker.
	/// </summary>
	public static void SpreadImpactDamage(Es1Mech machine, short odds, short damage, Es1SimGlobals globals) {
		if (damage == 0 || machine.Damage is not { } header) {
			return;
		}

		for (short component = 0; component < Es1DamageHeader.ComponentCount; component++) {
			if (!header.Occupied[component] || (short)(globals.Random.Next() & 0xff) >= odds) {
				continue;
			}

			short share = (short)(globals.Random.NextBelow(damage) + (damage >> 1));
			short taken = (short)SimMath.Q8MultiplyRounded(header.TotalArmor(component), share);
			ComponentDamageWrite(machine, null, taken, component, globals);
		}
	}

	/// <summary>A wreck's two leg components, and a four-legged chassis' rear pair.</summary>
	public const short WreckLegOdd = 7, WreckLegEven = 8, WreckRearLegOdd = 0xd, WreckRearLegEven = 0xe;

	/// <summary>
	/// <c>Mech_ApplyStartingCondition</c> (<c>00012f20</c>)(<paramref name="machine"/>, <paramref name="condition"/>), from a
	/// block-6 record's <c>0x68</c> (docs/retail/simulation/component-damage.md#starting-condition--mech_applystartingcondition-004178e8):
	/// a condition outside 0 to <c>0x4f</c> spreads nothing. Under <c>0x14</c>, a wreck: component 7 for an odd draw or 8
	/// for an even one written off with <see cref="Es1DamageHeader.FinishOff"/>, and 13 or 14 the same way on a chassis
	/// whose record's <c>+0x4a</c> is above 2; then immobilized, worth no salvage and collapsed; damage <c>0x96</c>.
	/// <c>0x14</c> to <c>0x27</c>: internal 5 written at its maximum (Q8 by <c>0x100</c>) and worth no salvage; <c>0x78</c>.
	/// <c>0x28</c> to <c>0x3b</c>: <c>0x50</c>. <c>0x3c</c> to <c>0x4f</c>: <c>0x32</c>. Then
	/// <see cref="SpreadImpactDamage"/> at the damage + <c>0x19</c>.
	/// </summary>
	public static void ApplyStartingCondition(Es1Mech machine, short condition, Es1SimGlobals globals) {
		short damage = 0;
		if (condition >= 0 && condition < 0x50) {
			if (condition >= 0x3c) {
				damage = 0x32;
			} else if (condition >= 0x28) {
				damage = 0x50;
			} else if (condition >= 0x14) {
				damage = 0x78;
				if (machine.Damage is { } header) {
					header.InternalDamage[ReactorInternal] =
						(short)SimMath.Q8MultiplyRounded(header.Maximum(ReactorInternal), Es1DamageHeader.Full);
				}

				machine.WorthNoSalvage = true;
			} else {
				damage = 0x96;
				short leg = (globals.Random.Next() & 1) == 0 ? WreckLegEven : WreckLegOdd;
				ComponentDamageWrite(machine, null, Es1DamageHeader.FinishOff, leg, globals);
				if (machine.Type.Chassis.LegCount > 2) {
					short rear = (globals.Random.Next() & 1) == 0 ? WreckRearLegEven : WreckRearLegOdd;
					ComponentDamageWrite(machine, null, Es1DamageHeader.FinishOff, rear, globals);
				}

				machine.Immobilized = true;
				machine.WorthNoSalvage = true;
				machine.Collapsed = true;
			}
		}

		SpreadImpactDamage(machine, (short)(damage + 0x19), damage, globals);
	}

	/// <summary>The reaches the outnumbered damage counts a HERC, a flyer and a structure within.</summary>
	public const int OutnumberedHercRange = 45000, OutnumberedFlyerRange = 50000, OutnumberedStructureRange = 35000;

	/// <summary>The strength a flyer or an armed structure counts; the friendly allowance; the hostile strength an impact answers for.</summary>
	public const int OutnumberedUnitStrength = 500, OutnumberedAllowance = 600, OutnumberedStrengthPerImpact = 400;

	/// <summary>Both the odds and the damage of each outnumbered impact.</summary>
	public const short OutnumberedImpact = 0x40;

	/// <summary>
	/// <c>Mech_ApplyOutnumberedDamage</c> (<c>00015a5c</c>)(<paramref name="machine"/>)
	/// (docs/es1/simulation/mech.md#small-functions): unless the machine is clear of threats
	/// (<see cref="IsClearOfThreats"/>), the HERCs of the mech pool, oldest first, that are neither disarmed, immobilized nor
	/// destroyed, whose group holds no action and that stand within <see cref="OutnumberedHercRange"/>, each counting its
	/// record's <c>+0x54</c> to the friendly sum when its group's side is the machine's and to the hostile one otherwise;
	/// the friendly sum less <see cref="OutnumberedAllowance"/>, floored at 0; then one <see cref="SpreadImpactDamage"/> of
	/// <see cref="OutnumberedImpact"/> for each <see cref="OutnumberedStrengthPerImpact"/> the hostile sum is above it.
	/// </summary>
	public static void ApplyOutnumberedDamage(Es1Mech machine, Es1SimGlobals globals) {
		if (IsClearOfThreats(machine, globals)) {
			return;
		}

		int friendly = 0, hostile = 0;
		for (int i = 0; i < globals.MechPool.Count; i++) {
			var other = globals.MechPool[i];
			// Retail reads both groups unchecked; a machine with none is not counted here.
			if (other.Disarmed || other.Immobilized || other.Destroyed || other.Group is not { Action: null } group
					|| machine.Group is not { } own || other.Position.ApproxDistanceTo(machine.Position) >= OutnumberedHercRange) {
				continue;
			}

			if (group.Side == own.Side) {
				friendly += other.Type.Chassis.SalvageScale;
			} else {
				hostile += other.Type.Chassis.SalvageScale;
			}
		}

		// PLACEHOLDER: the flyer pool within OutnumberedFlyerRange and the base pool's armed structures (record +0x34 not
		// 0) within OutnumberedStructureRange, each OutnumberedUnitStrength; neither pool is built until the plan's
		// flyers-and-bases step.
		friendly = Math.Max(friendly - OutnumberedAllowance, 0);
		for (; friendly < hostile; hostile -= OutnumberedStrengthPerImpact) {
			SpreadImpactDamage(machine, OutnumberedImpact, OutnumberedImpact, globals);
		}
	}

	/// <summary>The reaches within which an object that knows the subject keeps it from being clear: its target's, anyone's.</summary>
	public const int ThreatRangeTarget = 100000, ThreatRange = 80000;

	/// <summary>
	/// <c>Mission_IsClearOfThreats</c> (<c>0000f744</c>)(<paramref name="subject"/>) (docs/es1/simulation/mission-status.md#clear-of-threats):
	/// over the live objects whose group holds no action, on the other side from the subject's group, and neither
	/// disarmed, immobilized nor destroyed: when the subject's group is not <c>LocalPlayerMech</c>'s and the object's
	/// group targets the subject (<see cref="Es1MissionGroup.IsOrderTarget"/>), not clear if that group's route has a
	/// waypoint after its cursor or the object knows the subject (<see cref="Es1SensorModel.KnowsObject"/>); and not clear
	/// when the object knows the subject within <see cref="ThreatRangeTarget"/> as its target, <see cref="ThreatRange"/>
	/// otherwise. Retail reads the groups and <c>LocalPlayerMech</c> unchecked; an object with no group is passed over and
	/// a missing one matches nothing here.
	/// </summary>
	public static bool IsClearOfThreats(IEs1SimObject subject, Es1SimGlobals globals) {
		var group = subject.Group;
		var playerGroup = globals.LocalPlayerMech?.Group;
		foreach (var other in globals.LiveObjects) {
			if (other.Group is not { Action: null } otherGroup || otherGroup.Side == group?.Side || other.Disarmed
					|| other.Immobilized || other.Destroyed) {
				continue;
			}

			bool knows = Es1SensorModel.KnowsObject(other, subject);
			if (!ReferenceEquals(group, playerGroup) && otherGroup.IsOrderTarget(subject)
					&& (otherGroup.WaypointAt(otherGroup.RouteCursor + 1) != null || knows)) {
				return false;
			}

			if (knows) {
				int reach = ReferenceEquals(other.Target, subject) ? ThreatRangeTarget : ThreatRange;
				if (other.Position.ApproxDistanceTo(subject.Position) < reach) {
					return false;
				}
			}
		}

		return true;
	}

	/// <summary>The impulse factor, and the damage the collision's must pass, and the blast's radius.</summary>
	public const short CollisionFactor = 300, CollisionThreshold = 200, CollisionRadius = 1200;

	/// <summary>
	/// <c>Mech_CollisionTest</c>'s damage between two HERCs (docs/es1/simulation/mech-locomotion.md#collision--mech_collisiontest-00014174):
	/// the impulse, Q10 of the mover's speed by its mass (record <c>+0x4e</c>) less Q10 of the struck machine's speed carried
	/// along the mover's forward axis by the struck one's mass, Q10 by <see cref="CollisionFactor"/>; past
	/// <see cref="CollisionThreshold"/>, a blast of it at the two positions' midpoint in X and Y, at the lower of their eye
	/// nodes' (the record's <c>+0xc</c> part) heights in Z, on each machine at <see cref="CollisionRadius"/>
	/// (<see cref="ApplyExplosiveDamage"/>), the mover first. Retail reads a missing eye node through a null pointer; its
	/// height is 0 here, an engine guard.
	/// </summary>
	public static void CollisionDamage(Es1Mech mover, Es1Mech struck, Es1SimGlobals globals) {
		var travel = struck.Frame.RotateVector(0, struck.Speed, 0);
		var moverFrame = mover.Frame;
		moverFrame.TransposeRotation();
		int closing = moverFrame.RotateVector(travel.X, travel.Y, travel.Z).Y;
		short own = (short)SimMath.Q10MultiplyRounded(mover.Speed, mover.Type.Chassis.Mass);
		int impulse = own - SimMath.Q10MultiplyRounded(closing, struck.Type.Chassis.Mass);
		int damage = SimMath.Q10MultiplyRounded(impulse, CollisionFactor);
		if (damage <= CollisionThreshold) {
			return;
		}

		int moverEye = mover.AimNodeTransform()?.Z ?? 0;
		int struckEye = struck.AimNodeTransform()?.Z ?? 0;
		var point = new Vec3i((struck.Position.X + mover.Position.X) >> 1, (struck.Position.Y + mover.Position.Y) >> 1,
			Math.Min(moverEye, struckEye));
		ApplyExplosiveDamage(mover, struck, CollisionRadius, point, (short)damage, globals);
		ApplyExplosiveDamage(struck, mover, CollisionRadius, point, (short)damage, globals);
	}
}
