using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// What an AI machine does once it has something to shoot at (docs/es1/simulation/mech-ai.md#weapons, which links ES2's
/// derivation, docs/retail/simulation/ai-weapons.md): bring the turret onto it, choose a mount and fire it. The turret
/// moves through the player's aim primitive (<see cref="Es1Turret.TrackPoint"/>) and centring
/// (<see cref="Es1Turret.CenterTorsoTick"/>), and a mount fires through the same fire slot the player's trigger calls
/// (<see cref="Es1WeaponMount.Fire"/>), so an AI shot takes the same rounds, hits and damage; the trigger and its
/// arbitration (<see cref="Es1WeaponMounts.FireTrigger"/>) are the player's alone.
/// </summary>
public static class Es1AiWeapons {
	/// <summary>The 3D range band in which a machine with an aim slot aims at that component: from the first, under the second.</summary>
	public const int AimSlotNear = 5000, AimSlotFar = 20000;

	/// <summary>The residual aim error, either way, within which a mount that fires no rocket fires.</summary>
	public const short FireWindow = 1000;

	/// <summary>The weapon id <see cref="FireAtPoint"/> keeps latched, <c>ELF</c>.</summary>
	public const short LatchedWeapon = 3;

	/// <summary><c>AiAimScatterByDifficulty</c> (<c>00091ce0</c>), the scatter of a non-zero side's aim, by difficulty.</summary>
	public static readonly short[] AimScatter = [1000, 666, 333, 0];

	/// <summary>The weapon choice's floor: the fear (<see cref="Es1Mech.Fear"/>) mapped from 0–<see cref="FearTop"/> onto <see cref="CalmFloor"/>–<see cref="FrightenedFloor"/>.</summary>
	public const short FearTop = 0x400, CalmFloor = 150, FrightenedFloor = -100;

	/// <summary>The two jitter draws' bound: against a HERC, and against anything else.</summary>
	public const short MechJitter = 35, OtherJitter = 30;

	/// <summary>What a shot that breaks the target's shield adds to its armor damage in the weapon's score.</summary>
	public const int ShieldBreakBonus = 10000;

	/// <summary>The Q10 scales of the damage and of the template's cost (<see cref="AiCost"/>) in the score.</summary>
	public const short DamageScale = 100, CostScale = 1000;

	/// <summary>What the dry run raises <see cref="Es1SimGlobals.MissionPollCountdown"/> to.</summary>
	public const short DryPollDelay = 1000;

	/// <summary>The template's offset of the cost the score weighs, <c>+0x34</c>, in its tail (docs/es1/formats/weapons-dat-sim.md).</summary>
	public const int AiCostTailOffset = 0x34 - 0x22;

	/// <summary>
	/// <c>Ai_AimAndFire</c> (<c>ES1DBSIM 00018894</c>)(<paramref name="mech"/>, <paramref name="target"/>,
	/// <paramref name="aspect"/>), a null target meaning the machine's own (<c>+0x15e</c>): a target of class 0
	/// (<c>+0x162</c>) to <see cref="AimAndFireAtMech"/>; any other to <see cref="FireAtPoint"/> at its position plus its
	/// slot <c>+0x34</c>'s first triple (<see cref="IEs1SimObject.AimOffset"/>). Every object the slice builds is a HERC,
	/// class 0. Retail reads the target with no null test; with none, nothing here, an engine guard.
	/// </summary>
	public static void AimAndFire(Es1Mech mech, IEs1SimObject? target, short aspect, Es1SimGlobals globals) {
		target ??= mech.Target;
		if (target == null) {
			return;
		}

		if (target.TargetClass == 0) {
			AimAndFireAtMech(mech, target, aspect, globals);
		} else {
			FireAtPoint(mech, target, aspect, target.Position + target.AimOffset, globals);
		}
	}

	/// <summary>
	/// <c>Ai_AimAndFireAtMech</c> (<c>0001879c</c>)(<paramref name="mech"/>, <paramref name="target"/>,
	/// <paramref name="aspect"/>): with the bearing error to the target (<c>Math_HeadingToward</c> less the heading) plus
	/// the type record's twist limit <c>+0x22</c> below twice the limit, both as unsigned 16-bit values, the point is the
	/// aim slot's (<see cref="Es1Mech.AimSlot"/>) component (slot <c>+0x5c</c>) when the slot is not negative and the 3D
	/// range (<c>Math_DistanceBetweenPoints</c>) is from <see cref="AimSlotNear"/> to under <see cref="AimSlotFar"/>, and
	/// otherwise the translation of the target's aim node (slot <c>+0x28</c>) through its frame
	/// (<c>Transform_ApplyToPoint</c>); then <see cref="FireAtPoint"/>. Beyond the limit, <c>Mech_CenterTorsoTick</c>(machine, 0).
	/// Retail reads the node through the null a shape without the part gives; the target's frame origin here, an engine guard.
	/// </summary>
	public static void AimAndFireAtMech(Es1Mech mech, IEs1SimObject target, short aspect, Es1SimGlobals globals) {
		short limit = mech.Type.Chassis.TorsoTwistLimit;
		short error = (short)(Es1MissionGroup.HeadingToward(mech.Position, target.Position) - mech.Heading);
		if ((ushort)(error + limit) >= (ushort)(limit * 2)) {
			mech.Turret.CenterTorsoTick(globals.Clock, globals.TurretServos, 0);
			return;
		}

		int range = target.Position.ApproxDistanceTo(mech.Position);
		Vec3i point;
		if (mech.AimSlot >= 0 && (uint)(range - AimSlotNear) < AimSlotFar - AimSlotNear) {
			point = target.ComponentPosition(mech.AimSlot);
		} else {
			var node = target.AimNodeTransform() ?? Transform3.Identity;
			point = target.Frame.TransformPoint(node.X, node.Y, node.Z);
		}

		FireAtPoint(mech, target, aspect, point, globals);
	}

	/// <summary>
	/// <c>Ai_FireAtPoint</c> (<c>000190ec</c>)(<paramref name="mech"/>, <paramref name="target"/>, <paramref name="aspect"/>,
	/// <paramref name="point"/>). With the ground range (<c>Math_DistanceBetweenPoints2D</c>) to the point, the mount is the
	/// latched one (<see cref="Es1Mech.LatchedMount"/>, <c>+0x242</c>) while it is ready (slot <c>+0x30</c>) and the range
	/// allows it; otherwise none when <see cref="Es1Mech.SkipWeaponChoice"/> (<c>+0xaf</c>) is set, which is cleared, or
	/// <see cref="ChooseWeapon"/>'s with the latch cleared. With none, the turret is aimed at the point
	/// (<see cref="Es1Turret.TrackPoint"/>) and nothing fires. With one:
	/// <list type="number">
	/// <item>the lead: with the projectile record's speed (<c>+0xa</c>) above 0 and the target's speed (slot <c>+0x3c</c>)
	/// not 0, the point moved along the target's heading by (<c>int16</c>)((<c>int16</c>)(range ≫ 2) × speed × 4 /
	/// projectile speed) (<c>Math_OffsetPointByBearing</c>);</item>
	/// <item>the scatter: for a group whose side byte (<c>+0x14</c>) is not 0, each of X, Y and Z raised by
	/// <c>Math_RandomBelow</c> of <see cref="AimScatter"/>[<see cref="Es1SimGlobals.DifficultyIndex"/>] when that is not 0;</item>
	/// <item>the turret aimed at the point; a mount whose subtype (slot <c>+0x64</c>) is not
	/// <see cref="Es1WeaponMount.NotALauncher"/> fires at once, and one that is fires only with both the residual's pitch and
	/// yaw within [−<see cref="FireWindow"/>, <see cref="FireWindow"/>);</item>
	/// <item>it fires through slot <c>+0x2c</c>(mount, 0, machine), the free-shot flag 0, and a mount of weapon
	/// <see cref="LatchedWeapon"/> (the template's <c>+0x56</c>) is latched.</item>
	/// </list>
	/// Retail reads the group and the projectile record with no null test; no group is side 0 and no record speed 0 here,
	/// engine guards, as is no scatter for a difficulty outside the table, which retail reads past.
	/// </summary>
	public static void FireAtPoint(Es1Mech mech, IEs1SimObject target, short aspect, Vec3i point, Es1SimGlobals globals) {
		int range = Es1PlayerThink.DistanceBetweenPoints2D(point, mech.Position);
		var mount = mech.LatchedMount;
		if (mount == null || !mount.CanFire() || !mount.RangeAllows(range)) {
			if (mech.SkipWeaponChoice) {
				mount = null;
				mech.SkipWeaponChoice = false;
			} else {
				mount = ChooseWeapon(mech, target, range, aspect, globals);
				mech.LatchedMount = null;
			}
		}

		if (mount == null) {
			mech.Turret.TrackPoint(globals.Clock, globals.TurretServos, point);
			return;
		}

		short projectileSpeed = mount.Projectile?.Speed ?? 0;
		short targetSpeed = target.GetSpeed();
		if (projectileSpeed > 0 && targetSpeed != 0) {
			int product = unchecked((short)(range >> 2) * (targetSpeed * 4));
			short lead = (short)(product / projectileSpeed);
			point = Es1CombatStates.OffsetPointByBearing(point, target.Heading, lead);
		}

		if (mech.Group is { Side: not 0 }) {
			int difficulty = globals.DifficultyIndex;
			short spread = difficulty >= 0 && difficulty < AimScatter.Length ? AimScatter[difficulty] : (short)0;
			if (spread != 0) {
				int x = (short)globals.Random.NextBelow(spread);
				int y = (short)globals.Random.NextBelow(spread);
				int z = (short)globals.Random.NextBelow(spread);
				point = new Vec3i(point.X + x, point.Y + y, point.Z + z);
			}
		}

		var residual = mech.Turret.TrackPoint(globals.Clock, globals.TurretServos, point);
		if (mount.AmmunitionSubtype(out _) == Es1WeaponMount.NotALauncher
				&& ((uint)(residual.Pitch + FireWindow) >= 2 * FireWindow || (uint)(residual.Yaw + FireWindow) >= 2 * FireWindow)) {
			return;
		}

		mount.Fire(false, mech, globals);
		if (mount.WeaponId == LatchedWeapon) {
			mech.LatchedMount = mount;
		}
	}

	/// <summary>
	/// <c>Ai_ChooseWeapon</c> (<c>00018e50</c>)(<paramref name="mech"/>, <paramref name="target"/>, <paramref name="range"/>,
	/// <paramref name="aspect"/>). The floor is <c>Math_MapRange</c>(<see cref="Es1Mech.Fear"/>, 0, <see cref="FearTop"/>,
	/// <see cref="CalmFloor"/>, <see cref="FrightenedFloor"/>); the shield is the target's slot <c>+0x38</c> asked with 1 for
	/// an aspect within ±<c>0x4000</c> and 0 otherwise, so a HERC's front shield either way; the jitter bound
	/// <see cref="MechJitter"/> for a class-0 target and <see cref="OtherJitter"/> otherwise. Each mount neither absent nor
	/// spent (slot <c>+0x60</c>), in order:
	/// <list type="number">
	/// <item>a subtype (slot <c>+0x64</c>) 0 in range (<c>WeaponMount_RangeAllows</c>) turns the radar <c>+0x90</c> on while
	/// it is off and the radar silence <c>+0x201</c> is 0;</item>
	/// <item>one in range, of subtype <see cref="Es1WeaponMount.NotALauncher"/> or 3 or with its lock flag at the manager's
	/// <c>+6</c> set, and ready (slot <c>+0x30</c>), scores Q10(armor damage when the shield is 0, otherwise armor damage +
	/// <see cref="ShieldBreakBonus"/> when the shield is below the shield damage, otherwise the shield damage,
	/// <see cref="DamageScale"/>) − Q10(<see cref="CostScale"/>, <see cref="AiCost"/>) + the product of two
	/// <c>Math_RandomBelow</c> draws of the jitter bound, the damage from the projectile record (<c>+0x20</c>);</item>
	/// <item>a score above the best, compared as 32 bits against the best's 16, becomes the best, cut to 16 bits, and its
	/// mount the choice.</item>
	/// </list>
	/// With every mount absent or spent and <see cref="Es1Mech.Disarmed"/> (<c>+0x9f</c>) clear, it is set, the action
	/// <c>+0x170</c> activated and <see cref="Es1SimGlobals.MissionPollCountdown"/> raised to <see cref="DryPollDelay"/>.
	/// Answers the choice, or null. Retail reads the manager and a ready mount's projectile record with no null test; a
	/// machine with no manager chooses nothing and does not run dry, and a mount with no record scores 0 damage here,
	/// engine guards, as is a subtype past the five lock flags counting as unlocked.
	/// </summary>
	public static Es1WeaponMount? ChooseWeapon(Es1Mech mech, IEs1SimObject target, int range, short aspect,
			Es1SimGlobals globals) {
		short best = (short)SimMath.MapRange(mech.Fear, 0, FearTop, CalmFloor, FrightenedFloor);
		short shield = target.GetShieldByHeading((uint)(aspect + 0x4000) < 0x8000 ? (short)1 : (short)0);
		if (mech.Mounts is not { } manager) {
			return null;
		}

		short jitter = target.TargetClass == 0 ? MechJitter : OtherJitter;
		Es1WeaponMount? chosen = null;
		bool dry = true;
		foreach (var mount in manager.Mounts) {
			if (mount == null || mount.IsSpent) {
				continue;
			}

			short subtype = mount.AmmunitionSubtype(out _);
			dry = false;
			bool inRange = mount.RangeAllows(range);
			if (subtype == 0 && inRange && !mech.RadarOn && mech.Countdowns.RadarSilence == 0) {
				mech.RadarOn = true;
			}

			bool locked = subtype == Es1WeaponMount.NotALauncher || subtype == Projectiles.Es1Rocket.ElectroOpticalSubtype
				|| (subtype >= 0 && subtype < manager.MissileLocks.Length && manager.MissileLocks[subtype] != 0);
			if (!locked || !inRange || !mount.CanFire()) {
				continue;
			}

			short armor = mount.Projectile?.DamageArmor ?? 0, shieldDamage = mount.Projectile?.DamageShield ?? 0;
			int damage = shield == 0 ? armor : shield < shieldDamage ? armor + ShieldBreakBonus : shieldDamage;
			int score = SimMath.Q10MultiplyRounded(damage, DamageScale) - SimMath.Q10MultiplyRounded(CostScale, AiCost(mount));
			short first = (short)globals.Random.NextBelow(jitter);
			short second = (short)globals.Random.NextBelow(jitter);
			score += second * first;
			if (score > best) {
				best = (short)score;
				chosen = mount;
			}
		}

		if (dry && !mech.Disarmed) {
			mech.Disarmed = true;
			mech.Actions.Second?.Activate(globals.MissionVars);
			if (globals.MissionPollCountdown < DryPollDelay) {
				globals.MissionPollCountdown = DryPollDelay;
			}
		}

		return chosen;
	}

	/// <summary>The template's <c>+0x34</c>, the cost the weapon choice weighs against the damage; 0 for a tail too short to hold it.</summary>
	public static short AiCost(Es1WeaponMount mount) =>
		mount.Template.Tail is { Length: >= AiCostTailOffset + 2 } tail ? BitConverter.ToInt16(tail, AiCostTailOffset) : (short)0;
}
