using HercWorks.Core.Data.Struct.Dbsim;
using Herculan.Engine.Numerics;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Projectiles;

/// <summary>
/// A missile, <c>rocket.cpp</c>'s class (docs/es1/simulation/rockets.md): <c>RocketVtable</c> (<c>ES1DBSIM 00090ad6</c>),
/// built by <c>Rocket_Construct</c> (<c>000074e4</c>) and launched by <c>Rocket_Fire</c> (<c>00007748</c>); it burns,
/// homes on its target or is flown from the missile camera, and stops where the shot sweep says.
/// </summary>
public sealed class Es1Rocket : Es1Projectile {
	/// <summary>The projectile record type a missile looks up.</summary>
	public const short RecordType = 0;

	/// <summary><c>Rocket_Fire</c>'s 500 added to the speed it is given, which the mounts' dispatch passes as 0.</summary>
	public const short LaunchSpeed = 500;

	/// <summary>The subtypes <c>Rocket_HomingSteer</c> singles out: 2, the anti-radiation missile, and 3, the electro-optical one the player flies.</summary>
	public const byte AntiRadiationSubtype = 2, ElectroOpticalSubtype = 3;

	/// <summary><c>Rocket_HomingSteer</c>'s and <c>Rocket_PlayerSteer</c>'s turn rate, <c>0x500</c>.</summary>
	public const short TurnRate = 0x500;

	/// <summary>The ECM spoof's reach either side of the aim, and how far it pushes an error, <c>0xc00</c>.</summary>
	public const short SpoofWobble = 0xc00;

	private short _flameCountdown;

	// The type record's sequence the flame steps, as the last tick read it.
	private int _flameSequence;

	private Es1Rocket(Es1ProjectilePool pool, short subtype)
		: base(Root(pool, subtype), (byte)subtype, pool.LookupRecord(subtype, RecordType)) {
		_flameSequence = TypeRecord(pool) is { } type ? (ushort)type.AnimSequence : 0;
	}

	/// <summary>The cell frame of the type record's flame sequence <c>+0xa</c>.</summary>
	public override int DrawnCellFrame => _flameSequence < CellFrames.Length ? CellFrames[_flameSequence] : 0;

	/// <summary><c>+0x4c</c>, the speed, a rate <c>Math_IntegrateRateOverTick</c> turns into the tick's step.</summary>
	public short Speed { get; private set; }

	/// <summary><c>+0x4e</c>, the age, in ticks.</summary>
	public short Age { get; private set; }

	/// <summary><c>+0x50</c>, the target it homes on, or null.</summary>
	public IEs1SimObject? Target { get; private set; }

	/// <summary><c>+0x54</c>, the target's component it steers at; negative for none. Set only with a target.</summary>
	public short Component { get; private set; }

	/// <summary>
	/// <c>Rocket_Construct</c>'s shape: <c>g_RocketShapes</c>[the record's root <c>+0</c>]. A subtype past the table, or a
	/// root past the shapes, where retail reads past them, builds none.
	/// </summary>
	private static HercWorks.Core.Data.File.Dts.TSObject? Root(Es1ProjectilePool pool, short subtype) =>
		subtype >= 0 && subtype < pool.RocketTypes.Count && pool.RocketTypes[subtype].ModelId is var root
			&& root >= 0 && root < pool.RocketShapes.Count
			? pool.RocketShapes[root]
			: null;

	/// <summary>
	/// <c>Rocket_GetTypeRecord</c> (<c>00006e98</c>): the type record by the subtype byte, which the player's release of a
	/// flown missile rewrites to 0. Null past the table, an engine guard.
	/// </summary>
	private RocketType? TypeRecord(Es1ProjectilePool pool) => Subtype < pool.RocketTypes.Count ? pool.RocketTypes[Subtype] : null;

	/// <summary>
	/// <c>Rocket_Fire</c>(<paramref name="speed"/>, <paramref name="machine"/>, <paramref name="angles"/>,
	/// <paramref name="muzzle"/>, <paramref name="subtype"/>) (docs/es1/simulation/rockets.md#launching--rocket_fire-00007748):
	/// <c>Rocket_Construct</c> from the pool; the position, the angles, the frame word 0, the owner, the speed
	/// <paramref name="speed"/> + <see cref="LaunchSpeed"/> and the age 0; the target, the machine's own, when a machine
	/// that is not the player's fires subtype 3 or the machine's slot <c>+0x70</c> answers lock, and with one the
	/// component its slot <c>+0x58</c> names; the player's machine's missile into <see cref="Es1ProjectilePool.TrackedMissile"/>;
	/// then the type record's sound <see cref="RocketType.FireSoundId"/> + 10 at the muzzle
	/// (<see cref="Sound.Es1Sound.PlayAt"/>). Answers the missile, or null with the pool full, where retail writes its
	/// fields through the null pointer; with no missile or no type record no sound plays, an engine guard.
	/// </summary>
	public static Es1Rocket? Fire(Es1SimGlobals globals, short speed, IEs1SimObject machine,
			(short Pitch, short Roll, short Heading) angles, Vec3i muzzle, short subtype) {
		var pool = globals.Projectiles;
		var missile = Construct(pool, subtype);
		if (missile != null) {
			missile.Position = muzzle;
			missile.Angles = angles;
			missile.RotationWord = 0;
			missile.Owner = machine;
			missile.Speed = (short)(speed + LaunchSpeed);
			missile.Age = 0;
			bool locked = (!machine.IsPlayer && subtype == ElectroOpticalSubtype) || machine.MissileLockState(subtype);
			missile.Target = locked ? machine.Target : null;
			if (missile.Target is { } target) {
				missile.Component = target.ComponentNearestAim(missile.Position, missile.Angles);
			}
		}

		if (machine.IsPlayer) {
			pool.TrackedMissile = missile;
		}

		if (missile?.TypeRecord(pool) is { } type) {
			globals.Sound.PlayAt(muzzle, type.FireSoundId + 10);
		}

		return missile;
	}

	/// <summary>
	/// <c>Rocket_Construct</c>(0, <paramref name="subtype"/>): an element of the pool, none when it is full; the shape, the
	/// subtype byte, no target and the record <c>Proj_LookupRecord</c>(<paramref name="subtype"/>, 0). The flame's
	/// countdown <c>+0x56</c> is not written, so it holds what the element last held, or what the allocator's block held
	/// for an element never used (docs/es1/simulation/rockets.md#launching--rocket_fire-00007748); here it starts at 0.
	/// </summary>
	private static Es1Rocket? Construct(Es1ProjectilePool pool, short subtype) {
		if (!pool.CanAllocate) {
			return null;
		}

		var missile = new Es1Rocket(pool, subtype);
		pool.Add(missile);
		return missile;
	}

	/// <summary>
	/// <c>Rocket_TickUpdate</c> (<c>00007198</c>) (docs/es1/simulation/rockets.md#flight--rocket_tickupdate-00007198): the
	/// flame's cell step every interval <c>+8</c> of the type record's sequence <c>+0xa</c>; the age, ending past the
	/// lifetime <c>+2</c>; the burn, <c>Math_IntegrateRateOverTick</c>(<c>+4</c>) added, averaged with the opening speed and
	/// held to the projectile record's speed; the player's steer and <c>maybe_WeaponChain_EoHold</c> for the player's
	/// subtype-3 missile, the homing otherwise; then the sweep over the step, slack <c>+6</c>, of the record's armor,
	/// shield, splash factor and effects, unscaled, with the subtype as kind. A subtype-3 missile that ends latches the
	/// trigger, <c>Input_LatchButton</c>(1, 1), and clears its byte, whoever fired it.
	/// </summary>
	public override short Tick(Es1ProjectileTick tick) {
		var globals = tick.Globals;
		var clock = globals.Clock;
		var pool = globals.Projectiles;
		var type = TypeRecord(pool);
		short ended = 0;
		if (type is { FrameInterval: not 0 } && clock.CountdownTimerTick(ref _flameCountdown) == 0) {
			_flameCountdown = type.FrameInterval;
			_flameSequence = (ushort)type.AnimSequence;
			StepCellFrame(_flameSequence);
		}

		Age++;
		if (Age > (type?.Lifetime ?? 0)) {
			ended = 1;
		} else {
			short opening = Speed;
			Speed = (short)(Speed + clock.IntegrateRateOverTick(type!.Acceleration));
			Speed = (short)((Speed + opening) >> 1);
			short ceiling = Record?.Speed ?? 0;
			if (Speed > ceiling) {
				Speed = ceiling;
			}

			if (Owner!.IsPlayer && Subtype == ElectroOpticalSubtype) {
				if (tick.Block is { } block) {
					PlayerSteer(block, clock);
				}

				if (tick.Controls is { } controls) {
					controls.MissileFlown = true;
				}
			} else {
				HomingSteer(clock);
			}

			var record = Record;
			short step = clock.IntegrateRateOverTick(Speed);
			if (SweepStep(tick, step, type.ClipRadius, ray => new Es1Shot(ray, Owner) {
					Armor = record?.DamageArmor ?? 0,
					Shield = record?.DamageShield ?? 0,
					SplashFactor = record?.SplashFactor ?? 0,
					Effects = record,
					Kind = record?.SubtypeId ?? 0,
				})) {
				ended = 1;
			}
		}

		if (ended != 0 && Subtype == ElectroOpticalSubtype) {
			tick.Controls?.Latches.Latch(1, 1);
			if (tick.Block is { } block) {
				block.Buttons[0] = 0;
			}
		}

		return ended;
	}

	/// <summary>
	/// <c>Rocket_HomingSteer</c> (<c>00006eb4</c>) (docs/es1/simulation/rockets.md#homing--rocket_homingsteer-00006eb4): with
	/// a target that is still the player's selected one when the player fired, and for subtype 2 one whose radar or
	/// jamming is on, the bearing from the position to the aim point (<see cref="AimPoint"/>), less the pitch and heading;
	/// subtype 3 sets the owner's <see cref="IEs1SimObject.SkipWeaponChoice"/>, and other subtypes but 2 with the owner's
	/// ECM spoof push each error in [−<see cref="SpoofWobble"/>, <see cref="SpoofWobble"/> − 1] that far toward the other
	/// sign; each error, held to ±<see cref="TurnRate"/>, is added to its angle over the tick and the frame word cleared.
	/// </summary>
	private void HomingSteer(Es1SimClock clock) {
		if (Target is not { } target) {
			return;
		}

		var owner = Owner!;
		if (owner.IsPlayer && !ReferenceEquals(owner.Target, target)) {
			return;
		}

		if (Subtype == AntiRadiationSubtype && !target.RadarOn && !target.Jamming) {
			return;
		}

		var (pitch, roll, heading) = Angles;
		var (bearingPitch, _, bearingHeading) = SimTrig.EulerToward(AimPoint(target), Position);
		short headingError = (short)(bearingHeading - heading);
		short pitchError = (short)(bearingPitch - pitch);
		if (Subtype == ElectroOpticalSubtype) {
			owner.SkipWeaponChoice = true;
		} else if (Subtype != AntiRadiationSubtype && owner.EcmSpoofed) {
			headingError = Spoofed(headingError);
			pitchError = Spoofed(pitchError);
		}

		short pitchRate = 0, headingRate = 0;
		SimMath.RateLimitedMoveToward(ref pitchRate, pitchError, TurnRate);
		SimMath.RateLimitedMoveToward(ref headingRate, headingError, TurnRate);
		Angles = ((short)(pitch + clock.IntegrateRateOverTick(pitchRate)), roll,
			(short)(heading + clock.IntegrateRateOverTick(headingRate)));
		RotationWord = 0;
	}

	/// <summary>
	/// The aim point: with a component, the target's slot <c>+0x5c</c> of it; otherwise the translation of the node the
	/// target's slot <c>+0x28</c> gives carried by the target's frame, or with none the target's position.
	/// </summary>
	private Vec3i AimPoint(IEs1SimObject target) {
		if (Component >= 0) {
			return target.ComponentPosition(Component);
		}

		if (target.AimNodeTransform() is not { } node) {
			return target.Position;
		}

		return target.Frame.TransformPoint(node.X, node.Y, node.Z);
	}

	/// <summary>The ECM spoof on one error: one in [−<c>0xc00</c>, <c>0xbff</c>], unsigned, moved <c>0xc00</c> toward the other sign.</summary>
	private static short Spoofed(short error) =>
		(uint)(error + SpoofWobble) < 2 * SpoofWobble ? (short)(error < 0 ? error + SpoofWobble : error - SpoofWobble) : error;

	/// <summary>
	/// <c>Rocket_PlayerSteer</c> (<c>000070dc</c>)(missile, <paramref name="block"/>)
	/// (docs/es1/simulation/rockets.md#the-flown-missile--rocket_playersteer-000070dc): with the trigger clear, the target
	/// dropped and the subtype 0; held, <c>Math_IntegrateRateOverTick</c>(<c>Math_Q8Multiply</c>(<see cref="TurnRate"/>,
	/// <i>axis</i>)) of the axis <c>+0x1d</c> addresses off the heading and of the axis <c>+0x21</c> addresses onto the
	/// pitch, the frame word cleared, and the trigger and both axes zeroed.
	/// </summary>
	private void PlayerSteer(Es1PlayerInputBlock block, Es1SimClock clock) {
		if (block.Buttons[0] == 0) {
			Target = null;
			Subtype = 0;
			return;
		}

		var (x, y) = block.CameraAxisValues;
		short turn = clock.IntegrateRateOverTick((short)SimMath.Q8MultiplyRounded(TurnRate, x));
		short climb = clock.IntegrateRateOverTick((short)SimMath.Q8MultiplyRounded(TurnRate, y));
		var (pitch, roll, heading) = Angles;
		Angles = ((short)(pitch + climb), roll, (short)(heading - turn));
		RotationWord = 0;
		block.Buttons[0] = 0;
		block.ClearCameraAxes();
	}
}
