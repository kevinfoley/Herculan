using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// The AI's half of firing: where on a target to shoot, which hardpoint to shoot with, and when to
/// pull the trigger. Everything below the turret — the turret ticks, the mount's own fire dispatch —
/// is the same machinery the player's trigger reaches, which is the original's own arrangement.
///
/// <para>Derived in docs/retail/simulation/ai-weapons.md.</para>
/// </summary>
public sealed partial class MechObject {
	/// <summary>
	/// <c>Ai_AimAndFire</c> (<c>0041ea7c</c>) — the tail every combat state ends on, and the one
	/// <c>travelling</c> and <c>following</c> call against whatever they are watching. It picks where
	/// on the target the shot should go, by the target's own class, and hands the point on.
	/// </summary>
	/// <param name="subject">The object to shoot at; null takes <see cref="Target"/>.</param>
	/// <param name="aspect">
	/// How far the target's own aim is from this machine. The original computes it in three places and
	/// reads it in exactly one — see <see cref="AspectOf"/>.
	/// </param>
	public void AimAndFire(SimWorld world, SimObject? subject, short aspect) {
		if ((subject ?? Target) is not { } target) {
			return;
		}

		switch (target) {
			case MechObject machine when machine.TargetClass == TargetClass.Herc:
				AimAndFireAtMech(world, machine, aspect);
				return;

			// A structure is shot at its first surviving component: the original's vtable +0x54
			// (Base_FirstLiveComponent, 00406868) with the null second argument Ai_AimAndFire passes.
			case BaseObject structure when structure.TargetClass == TargetClass.Structure:
				FireAtPoint(world, structure.ComponentPosition(structure.FirstLiveComponent()), aspect,
					structure);
				return;

			// Everything else takes its own aim offset over its position — the original's vtable +0x30:
			// a ground vehicle's BASES.DAT +0x2c height (Base_GetAimPoint), zero for a flyer.
			default:
				FireAtPoint(world, target.AimPoint, aspect, target);
				return;
		}
	}

	/// <summary>
	/// <c>Ai_AimAndFireAtMech</c> (<c>0041e984</c>) — the machine branch, and the only one with a
	/// turret gate in front of it.
	///
	/// <para>A target more than the chassis' own twist limit off the nose is not shot at: the turret
	/// cannot be brought onto it, so it is centred instead — which also squares the guns up, since
	/// centring passes a convergence range of zero. Bringing the target back round is the navigation
	/// layer's job.</para>
	///
	/// <para>The aim component is used only between <see cref="AimComponentNearRange"/> and
	/// <see cref="AimComponentFarRange"/>. Outside that band the shot goes at the target's aim node and
	/// takes whatever component the hit test gives it.</para>
	/// </summary>
	private void AimAndFireAtMech(SimWorld world, MechObject target, short aspect) {
		short bearingError = (short)(Detection.HeadingToward(target.Position, Position) - (short)Heading);

		if (Abs(bearingError) >= Type.TorsoTwistLimit) {
			CenterTorsoTick(world);
			return;
		}

		// The one weapon range in the AI measured in three dimensions; every range a navigation steer
		// is computed from is the ground-plane form.
		int range = Position.ApproxDistanceTo(target.Position);

		var point = AimComponent >= 0
				&& range >= AimComponentNearRange && range < AimComponentFarRange
			? target.ComponentWorldPosition(AimComponent)
			: target.AimPoint;

		FireAtPoint(world, point, aspect, target);
	}

	/// <summary>
	/// <c>Ai_FireAtPoint</c> (<c>0041f5a0</c>) — choose a hardpoint, lead the shot, slew the turret and
	/// fire. <b>The turret is slewed whether or not anything fires</b>, so an AI machine tracks
	/// continuously and shoots intermittently.
	/// </summary>
	private void FireAtPoint(SimWorld world, Vec3i point, short aspect, SimObject target) {
		int range = GroundDistanceTo(point);
		var weapon = _latchedWeapon;

		if (weapon == null || !weapon.CanFire || !weapon.RangeAllows(range)) {
			if (WeaponSelectionSuppressed) {
				// Costs exactly one selection, and only on the path that re-chooses: a latched weapon
				// keeps firing through it.
				weapon = null;
				WeaponSelectionSuppressed = false;
			} else {
				weapon = ChooseWeapon(world, aspect, range, target);
				_latchedWeapon = null;
			}
		}

		if (weapon == null) {
			TrackWorldPoint(world, point);
			return;
		}

		// range * targetSpeed / projectileSpeed along the target's own heading. A Beam record carries
		// speed 0 and takes no lead, and neither does a structure, whose speed accessor returns zero.
		short travel = target.TravelSpeed;

		if (weapon.Projectile is { Speed: > 0 } projectile && travel != 0) {
			point = SimTrig.OffsetPointByBearing(point, (short)target.Heading,
				(short)((range >> 2) * travel * 4 / projectile.Speed));
		}

		if (Group is { Side: not 0 } && AiAimScatter[world.Difficulty] is var spread and not 0) {
			// One-sided: Math_RandomBelow draws in [0, bound), so the aim is only ever displaced the
			// same way. Reproduced, because it is what the retail enemy's aim does.
			point = new Vec3i(
				point.X + world.Random.NextBelow(spread),
				point.Y + world.Random.NextBelow(spread),
				point.Z + world.Random.NextBelow(spread));
		}

		var (yaw, pitch) = TrackWorldPoint(world, point);

		// A launcher fires the moment it is chosen — the round steers itself. Only a gun waits for the
		// turret to arrive.
		if (weapon.AmmoType != WeaponMount.NotAMissile
				|| (Abs(yaw) < AimedFireTolerance && Abs(pitch) < AimedFireTolerance)) {
			weapon.Fire(this, world);

			if (weapon.WeaponId is WeaponMount.ElfWeaponId or WeaponMount.Elf2WeaponId) {
				_latchedWeapon = weapon;
			}
		}
	}

	/// <summary>
	/// <c>Ai_ChooseWeapon</c> (<c>0041f358</c>) — score every mount and take the best above a floor.
	/// Nothing above the floor means nothing fires this tick, so the floor is as much the machine's
	/// rate of fire as its taste in weapons.
	///
	/// <list type="bullet">
	/// <item><b>The floor is fear.</b> <see cref="Fear"/> mapped through
	/// <c>[0, 0x400] → [150, −100]</c>: a calm machine needs a real reason to shoot and a frightened
	/// one will fire anything it has.</item>
	/// <item><b>The score is damage against cost</b>, and the <see cref="ShieldBreakBonus"/> is what
	/// pays for a missile: off the shield-breaking shot an <c>MSL</c> launcher scores well below
	/// zero and fires only when the jitter rescues it.</item>
	/// <item><b>The jitter is as large as the signal.</b> Two draws below 35 multiplied together
	/// average 289, so the pick is mostly noise with a bias.</item>
	/// </list>
	///
	/// <para>The retail figures behind both are in docs/retail/simulation/ai-weapons.md
	/// ("Choosing a weapon").</para>
	/// </summary>
	private WeaponMount? ChooseWeapon(SimWorld world, short aspect, int range, SimObject target) {
		int best = SimMath.MapRange(Fear, 0, FearFloorSpan, WeaponScoreFloor, WeaponScoreFloorAfraid);

		// The front/rear test is evaluated here and the RESULT handed to an accessor that expects a
		// heading — both 0 and 1 land in its front quadrant, so the front shield is what comes back
		// whichever way the target is facing. Reproduced; see KNOWN_ISSUES.md.
		short shield = target.ShieldByHeading(
			(short)(Abs(aspect) <= BinaryAngle.QuarterTurn - 1 ? 1 : 0));

		short jitter = target.TargetClass == TargetClass.Herc ? MechJitterBound : OtherJitterBound;
		WeaponMount? chosen = null;
		bool anyArmed = false;

		foreach (var mount in Weapons.Mounts) {
			// The mount's own vtable +0x5c: a destroyed mount, an empty magazine and every pod are
			// passed over before the mount counts as armed.
			if (mount.IsSpent) {
				continue;
			}

			anyArmed = true;
			short kind = mount.AmmoType;
			bool inRange = mount.RangeAllows(range);

			// A semi-active launcher has to illuminate what it shoots at, so carrying one in range is
			// itself a reason to go active.
			if (kind == SemiActiveHomingType && inRange && !Scanner && RadarSilenceTimer == 0) {
				Scanner = true;
			}

			if (!inRange || mount.Projectile is not { } projectile || !mount.CanFire) {
				continue;
			}

			// Subtypes 5 (not a launcher) and 3 (the pilot-flown EO round) bypass the lock; every other
			// launcher needs its own subtype locked before it can be scored at all.
			if (kind != WeaponMount.NotAMissile && kind != PilotFlownHomingType && !Weapons.Locked(kind)) {
				continue;
			}

			int credit;

			if (shield == 0) {
				credit = projectile.DamageArmor;
			} else if (shield < projectile.DamageShield) {
				credit = projectile.DamageArmor + ShieldBreakBonus;
			} else {
				credit = projectile.DamageShield;
			}

			int score = SimMath.Q10Multiply(DamageCreditGain, credit)
				- SimMath.Q10Multiply(ShotCostGain, mount.AiShotCost)
				+ world.Random.NextBelow(jitter) * world.Random.NextBelow(jitter);

			if (score > best) {
				best = score;
				chosen = mount;
			}
		}

		if (!anyArmed && !Disarmed) {
			// Running dry is a mission event: the machine's own block-5 action fires and the next
			// objective poll is put at least DisarmedPollDeferral away. This is the fourth of that
			// action's firing sites and the only one that is not a death -- see SimObject.DefeatAction.
			Disarmed = true;
			ActivateDefeatAction(world);
			world.Mission.Objectives.DeferPoll(DisarmedPollDeferral);
		}

		return chosen;
	}

	/// <summary>
	/// The angle the fire path carries and reads in exactly one place — the bearing from the target
	/// back to this machine, in the target's own turret frame. Built the same way by
	/// <c>Ai_BuildCombatGeometry</c> and by both travel thinks.
	///
	/// <para>Its only consumer is <see cref="ChooseWeapon"/>'s shield-facing test, which is broken, so
	/// nothing observable depends on it. Kept because the port is of the original's shape, not of its
	/// working subset.</para>
	/// </summary>
	public short AspectOf(SimObject target) => (short)(
		Detection.HeadingToward(Position, target.Position) - (short)target.Heading + target.AimTwist);


	/// <summary>
	/// <c>mech+0xb5</c> — skip the next weapon selection. Set by the seeker of an electro-optical
	/// round in flight, once per tick it steers, so a machine that has one in the air fires nothing
	/// else while it flies. See docs/retail/simulation/rockets.md.
	/// </summary>
	public bool WeaponSelectionSuppressed { get; set; }

	/// <summary>
	/// <c>mech+0xa5</c> — this machine has nothing left to fight with. Latched by
	/// <see cref="ChooseWeapon"/> the first time every mount answers <see cref="WeaponMount.IsSpent"/>
	/// — destroyed, out of rounds, or a pod — and read
	/// beside the damage latches by every "dead or dying" test in the AI, which is why a disarmed
	/// machine flees and is abandoned as a target.
	/// </summary>
	public bool Disarmed { get; private set; }

	/// <summary><c>mech+0x2ac</c> — the latched mount. Only an ELF is ever kept.</summary>
	private WeaponMount? _latchedWeapon;

	/// <summary>
	/// How far <see cref="ChooseWeapon"/> pushes the next objective poll when a machine runs dry, in
	/// <see cref="SimMath.CountdownTimerTick"/> counts: about half a second.
	/// </summary>
	public const short DisarmedPollDeferral = 1000;

	/// <summary>The turret error a gun will fire inside, in binary angle — about 5.5°.</summary>
	public const short AimedFireTolerance = 1000;

	/// <summary>Nearest range the aim component is used at; below it the shot goes at the aim node.</summary>
	public const int AimComponentNearRange = 5000;

	/// <summary>And the furthest — 120 m, past which the AI stops picking parts.</summary>
	public const int AimComponentFarRange = 20000;

	/// <summary>The weapon-score floor for a machine with no fear at all.</summary>
	private const int WeaponScoreFloor = 150;

	/// <summary>And for one at the top of the fear scale, which is why a frightened machine fires anything.</summary>
	private const int WeaponScoreFloorAfraid = -100;

	/// <summary>The fear span the floor is mapped over.</summary>
	private const int FearFloorSpan = 0x400;

	/// <summary>Q10 gain on the shot's damage credit.</summary>
	private const int DamageCreditGain = 100;

	/// <summary>Q10 gain on the weapon's own cost, ten times the credit's — which is the whole model.</summary>
	private const int ShotCostGain = 1000;

	/// <summary>What a shot that will break the target's shield outright is worth on top of its damage.</summary>
	private const int ShieldBreakBonus = 10000;

	/// <summary>Jitter bound for a machine target; both draws are below it and multiplied together.</summary>
	private const short MechJitterBound = 0x23;

	/// <summary>And for anything else, slightly tighter.</summary>
	private const short OtherJitterBound = 0x1e;

	/// <summary>The semi-active launcher subtype, which needs its own illumination to guide.</summary>
	private const short SemiActiveHomingType = 0;

	/// <summary>The electro-optical subtype, which the pilot flies and which never forms a lock.</summary>
	private const short PilotFlownHomingType = 3;

	/// <summary>
	/// <c>AiAimScatterByDifficulty</c> (<c>0049a30c</c>) — how far a Cybrid machine's aim is thrown off, by
	/// <see cref="SimWorld.Difficulty"/>, for any group whose side is not 0. The enemy shoots
	/// straighter the harder the game is set, and no machine on the human side is perturbed at all.
	/// </summary>
	public static readonly short[] AiAimScatter = { 1000, 800, 400, 200 };
}
