using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;
using Herculan.Es1.Settings;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Projectiles;

/// <summary>
/// A gun round, <c>bullet.cpp</c>'s class (docs/es1/simulation/projectiles.md#gun-rounds--bulletcpp): <c>BulletVtable</c>
/// (<c>ES1DBSIM 00090c56</c>), built by <c>Bullet_Construct</c> (<c>00007d08</c>) and fired by <c>Bullet_Fire</c>
/// (<c>00008134</c>) and <c>Bullet_FirePowered</c> (<c>000081d4</c>); it flies straight at its weapon's speed for its
/// lifetime in ticks.
/// </summary>
public sealed class Es1Bullet : Es1Projectile {
	/// <summary>The projectile record type a gun round looks up.</summary>
	public const short RecordType = 2;

	/// <summary>The kind word <c>+0x12</c> of a gun round's shot.</summary>
	public const short ShotKind = 5;

	private short _frameCountdown;

	private Es1Bullet(Es1ProjectilePool pool, short subtype)
		: base(Root(pool, subtype), (byte)subtype, pool.LookupRecord(subtype, RecordType)) {
		_frameCountdown = TypeRecord(pool)?.FrameInterval ?? 0;
	}

	/// <summary><c>+0x4c</c>, the speed, the projectile record's.</summary>
	public short Speed { get; private set; }

	/// <summary>
	/// <c>+0x4e</c>, the age: in ticks, as retail counts it, or with <see cref="RealTimeLife"/> in 1/<see cref="AgeRate"/>
	/// of 100 ms.
	/// </summary>
	public short Age { get; private set; }

	/// <summary>
	/// The rate the age gains through <c>Math_IntegrateRateOverTick</c> with <see cref="RealTimeLife"/>, and the scale of
	/// the lifetime it is held against: ES2's <c>Bullet_TickUpdate</c>'s <c>0x200</c>
	/// (docs/retail/simulation/projectiles.md#flight--bullet_tickupdate-0040b124).
	/// </summary>
	public const short AgeRate = 0x200;

	/// <summary>
	/// Whether a round's age counts time rather than ticks, from <see cref="Es1TweakSettingDefinitions.RealTimeRoundLife"/>.
	/// The age gains <c>Math_IntegrateRateOverTick</c>(<see cref="AgeRate"/>) a tick and the round ends past the lifetime
	/// × <see cref="AgeRate"/>, as ES2's ages, so the lifetime counts ES1's 100 ms tick unit and a round reaches the
	/// lifetime × its speed at any frame rate: 85,000 units for an autocannon and 60,000 for an <c>EMP</c>, retail's reach
	/// at 10 frames a second (docs/es1/simulation/projectiles.md#the-reach). This engine's invention; off, the age counts
	/// ticks, as retail's does.
	/// </summary>
	public static bool RealTimeLife => TweakSettings.Current.GetSettingValue(Es1TweakSettingDefinitions.RealTimeRoundLife);

	/// <summary><c>+0x50</c>, the power, a Q10 scale of both damage figures; 0 for none.</summary>
	public short Power { get; private set; }

	/// <summary>
	/// <c>Bullet_Construct</c>'s shape: the root <c>+0</c> of the type record of the subtype. A subtype past
	/// the table, or a root past the shapes, where retail reads past them, builds none.
	/// </summary>
	/// <param name="pool">The projectile pool.</param>
	/// <param name="subtype">The projectile subtype.</param>
	private static HercWorks.Core.Data.File.Dts.TSObject? Root(Es1ProjectilePool pool, short subtype) =>
		subtype >= 0 && subtype < pool.BulletTypes.Count && pool.BulletTypes[subtype].ModelId is var root
			&& root >= 0 && root < pool.BulletShapes.Count
			? pool.BulletShapes[root]
			: null;

	/// <summary><c>Bullet_GetTypeRecord</c> (<c>00007b94</c>): the type record by the subtype byte; null past the table, an engine guard.</summary>
	private Es1BulletType? TypeRecord(Es1ProjectilePool pool) => Subtype < pool.BulletTypes.Count ? pool.BulletTypes[Subtype] : null;

	/// <summary>
	/// <c>Bullet_Fire</c>(machine, angles, muzzle,
	/// subtype) (docs/es1/simulation/projectiles.md#firing--bullet_fire-00008134): <c>Bullet_Construct</c>
	/// from the pool; the position, the angles, the frame word 0, the owner, the power and the age 0, and the speed the
	/// projectile record's <c>+0xa</c>; then the type record's sound <c>+8</c> + 10 at the muzzle
	/// (<see cref="Sound.Es1Sound.PlayAt"/>). Answers the round, or null with the pool full, where retail writes its fields
	/// through the null pointer; with no round or no type record no sound plays, an engine guard.
	/// </summary>
	/// <param name="globals">The simulation's globals.</param>
	/// <param name="machine">The firing machine.</param>
	/// <param name="angles">The shot's angles.</param>
	/// <param name="muzzle">The muzzle's position.</param>
	/// <param name="subtype">The projectile subtype.</param>
	public static Es1Bullet? Fire(Es1SimGlobals globals, IEs1SimObject machine, (short Pitch, short Roll, short Heading) angles,
			Vec3i muzzle, short subtype) {
		var round = Construct(globals.Projectiles, subtype);
		if (round != null) {
			round.Position = muzzle;
			round.Angles = angles;
			round.RotationWord = 0;
			round.Owner = machine;
			round.Power = 0;
			round.Age = 0;
			round.Speed = round.Record?.Speed ?? 0;
		}

		if (round?.TypeRecord(globals.Projectiles) is { } type) {
			globals.Sound.PlayAt(muzzle, type.FireSoundId + 10);
		}

		return round;
	}

	/// <summary>
	/// <c>Bullet_FirePowered</c>(power, machine, angles,
	/// muzzle, subtype): <see cref="Fire"/> with the power stored after it.
	/// </summary>
	/// <param name="globals">The simulation's globals.</param>
	/// <param name="power">The shot's power.</param>
	/// <param name="machine">The firing machine.</param>
	/// <param name="angles">The shot's angles.</param>
	/// <param name="muzzle">The muzzle's position.</param>
	/// <param name="subtype">The projectile subtype.</param>
	public static Es1Bullet? FirePowered(Es1SimGlobals globals, short power, IEs1SimObject machine,
			(short Pitch, short Roll, short Heading) angles, Vec3i muzzle, short subtype) {
		var round = Fire(globals, machine, angles, muzzle, subtype);
		if (round != null) {
			round.Power = power;
		}

		return round;
	}

	/// <summary>
	/// <c>Bullet_Construct</c>(0, subtype) (docs/es1/formats/bullets-skimmer-dat.md#the-load): an element
	/// of the pool, none when it is full; the shape, the subtype byte, the record <c>Proj_LookupRecord</c>(
	/// subtype, 2) and the frame countdown at the type record's interval <c>+6</c>.
	/// </summary>
	/// <param name="pool">The projectile pool.</param>
	/// <param name="subtype">The projectile subtype.</param>
	private static Es1Bullet? Construct(Es1ProjectilePool pool, short subtype) {
		if (!pool.CanAllocate) {
			return null;
		}

		var round = new Es1Bullet(pool, subtype);
		pool.Add(round);
		return round;
	}

	/// <summary>
	/// <c>Bullet_TickUpdate</c> (<c>00007f84</c>) (docs/es1/simulation/projectiles.md#flight--bullet_tickupdate-00007f84):
	/// the first cell frame's step every interval <c>+6</c>; the age, ending past the lifetime <c>+2</c>, or in time with
	/// <see cref="RealTimeLife"/>; then the sweep
	/// over the step <c>Math_IntegrateRateOverTick</c>(speed), slack <c>+4</c>, of the record's armor and shield, each
	/// <c>Math_Q10Multiply</c>'d by the power when it is not 0, its splash factor and effects, with
	/// <see cref="ShotKind"/>.
	/// </summary>
	public override short Tick(Es1ProjectileTick tick) {
		var clock = tick.Globals.Clock;
		var type = TypeRecord(tick.Globals.Projectiles);
		if (type is { FrameInterval: not 0 } && clock.CountdownTimerTick(ref _frameCountdown) == 0) {
			_frameCountdown = type.FrameInterval;
			StepCellFrame(0);
		}

		int lifetime = type?.Lifetime ?? 0;
		if (RealTimeLife) {
			Age = (short)(Age + clock.IntegrateRateOverTick(AgeRate));
			lifetime *= AgeRate;
		} else {
			Age++;
		}

		if (Age > lifetime) {
			return 1;
		}

		var record = Record;
		short armor = record?.DamageArmor ?? 0, shield = record?.DamageShield ?? 0;
		if (Power != 0) {
			armor = (short)SimMath.Q10MultiplyRounded(armor, Power);
			shield = (short)SimMath.Q10MultiplyRounded(shield, Power);
		}

		short step = clock.IntegrateRateOverTick(Speed);
		return SweepStep(tick, step, type!.ClipRadius, ray => new Es1Shot(ray, Owner!) {
			Armor = armor,
			Shield = shield,
			SplashFactor = record?.SplashFactor ?? 0,
			Effects = record,
			Kind = ShotKind,
		}) ? (short)1 : (short)0;
	}
}
