using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// The countdowns of a HERC the systems tick runs (docs/es1/simulation/mech.md#the-fields), each the counter of its
/// record, the byte before it. Every one starts at 0, as a field the constructor does not write does
/// (docs/es1/simulation/mech.md#construction).
/// </summary>
public sealed class Es1MechCountdowns {
	/// <summary>The lock countdowns of the five rocket subtypes, the counters of the 3-byte records from <c>+0x1ee</c>; subtype 3's is never run.</summary>
	public short[] Lock { get; } = new short[Es1WeaponMounts.MissileSubtypes];

	/// <summary><c>+0x1fe</c>, the ECM roll's, of the record <c>+0x1fd</c>.</summary>
	public short EcmRoll;

	/// <summary><c>+0x201</c>, the radar silence's, of the record <c>+0x200</c>.</summary>
	public short RadarSilence;

	/// <summary><c>+0x209</c>, the retarget countdown, of the record <c>+0x208</c>.</summary>
	public int Retarget;

	/// <summary><c>+0x20e</c>, the friendly-fire complaint's, of the record <c>+0x20d</c>.</summary>
	public int Complaint;

	/// <summary><c>+0x213</c>, the under-fire window, of the record <c>+0x212</c>; its expiry zeroes <see cref="Es1Mech.DamageFromPlayerSide"/>.</summary>
	public int UnderFire;

	/// <summary><c>+0x21c</c>, the damage window, of the record <c>+0x21b</c>; its expiry zeroes <see cref="Es1Mech.DamageTaken"/>.</summary>
	public short DamageWindow;
}

/// <summary>
/// <c>Mech_LockTonePlay</c>'s two latches, <c>LockTone_PhaseSounded</c> (<c>ES1DBSIM 00091c5c</c>) and
/// <c>LockTone_WasLocked</c> (<c>00091c5d</c>), globals in retail, so shared by every machine that plays it, which only
/// the player's does.
/// </summary>
public sealed class Es1LockTone {
	/// <summary>The sounds: the lock's beep, the lock lost and the target changed.</summary>
	public const short LockedSound = 0x15, LostSound = 0x16, TargetChangedSound = 0x14;

	/// <summary>The bit of the clock <c>000981c4</c> whose set half of each period the lock's beep sounds once in.</summary>
	public const uint PhaseBit = 0x40;

	/// <summary><c>LockTone_WasLocked</c>: a lock was held, so its loss is announced once.</summary>
	public bool WasLocked { get; private set; }

	/// <summary><c>LockTone_PhaseSounded</c>: this phase's beep has sounded.</summary>
	public bool PhaseSounded { get; private set; }

	/// <summary>
	/// <c>Mech_LockTonePlay</c> (<c>0001552c</c>)(<paramref name="machine"/>)
	/// (docs/es1/simulation/mech.md#the-systems-tick--mech_perticksystemsupdate-00014ee0): with the lock lamp lit,
	/// <see cref="WasLocked"/> set and <see cref="LockedSound"/> once in each phase <see cref="PhaseBit"/> of the clock is
	/// set, the phase's latch cleared while it is clear; with it dark, <see cref="LostSound"/> once when a lock was held,
	/// otherwise <see cref="TargetChangedSound"/> while the target has just changed.
	/// </summary>
	public void Play(Es1Mech machine, Es1SimClock clock) {
		if (!machine.LockLamp) {
			if (!WasLocked) {
				if (machine.TargetChanged) {
					// PLACEHOLDER: Sound_Play (0004de84)(0x14) does nothing, until ES1's sound is ported, which no plan
					// step schedules yet.
				}
			} else {
				// PLACEHOLDER: Sound_Play(0x16) does nothing, as above.
				WasLocked = false;
			}
		} else {
			WasLocked = true;
			if ((clock.Clock & PhaseBit) == 0) {
				PhaseSounded = false;
			} else if (!PhaseSounded) {
				// PLACEHOLDER: Sound_Play(0x15) does nothing, as above.
				PhaseSounded = true;
			}
		}
	}
}

/// <summary>
/// <c>Mech_PerTickSystemsUpdate</c> (<c>ES1DBSIM 00014ee0</c>), (machine), once a tick from <c>Sim_MainTick</c> for each
/// machine of <c>g_MechPool</c> whose group holds no action and that is not destroyed
/// (docs/es1/simulation/mech.md#the-systems-tick--mech_perticksystemsupdate-00014ee0): the energy pool shared between the
/// weapons and the shields, the shields message's re-arm, the countdowns, the ECM and missile lock.
/// </summary>
public static class Es1MechSystems {
	/// <summary>The reactor's rate, per <c>Math_IntegrateRateOverTick</c>.</summary>
	public const short ReactorRate = 15;

	/// <summary>The Q10 scales of the reactor's rate while <see cref="Es1Mech.ReactorCritical"/> or <see cref="Es1Mech.ReactorDamaged"/> is set.</summary>
	public const short CriticalScale = 200, DamagedScale = 600;

	/// <summary>The pool held back from the weapons and the shields, and its ceiling.</summary>
	public const short Reserve = 1500, FullPool = 10000;

	/// <summary>What each of the front and the rear must hold above for the player's shields message to re-arm.</summary>
	public const short ShieldsMessageRearm = 300;

	/// <summary>The damage window's reload.</summary>
	public const short DamageWindowReload = 4000;

	/// <summary>The ECM roll: spoofed when the generator's low 12 bits fall below <see cref="EcmOdds"/>, held <see cref="EcmSpoofHold"/>, else rolled again after <see cref="EcmRetry"/>.</summary>
	public const int EcmOdds = 0x334;

	/// <inheritdoc cref="EcmOdds"/>
	public const short EcmSpoofHold = 5000, EcmRetry = 1500;

	/// <summary>The bearing either side of the turret's line inside which the lock countdowns run.</summary>
	public const short LockArc = 0x3000;

	/// <summary>The subtypes whose lock needs the machine's own radar on, in the order retail runs them.</summary>
	private static readonly int[] RadarLockedSubtypes = [0, 4];

	/// <summary>
	/// The systems tick on <paramref name="machine"/>. Retail's machine always holds a manager; one with none, before
	/// <c>Mech_ConfigureLoadout</c> has run, gives its weapons nothing and holds no lock.
	/// </summary>
	public static void PerTickSystemsUpdate(Es1Mech machine, Es1SimGlobals globals) {
		var clock = globals.Clock;
		var mounts = machine.Mounts;

		// The energy: the reactor's gain, cut while the reactor is damaged; the weapons' share through the manager's energy
		// slot and the shields' of what they leave, both less the reserve; the pool what is left plus the reserve.
		short gain = clock.IntegrateRateOverTick(ReactorRate);
		if (machine.ReactorCritical) {
			gain = (short)SimMath.Q10MultiplyRounded(CriticalScale, gain);
		} else if (machine.ReactorDamaged) {
			gain = (short)SimMath.Q10MultiplyRounded(DamagedScale, gain);
		}

		machine.Energy += gain;
		short budget = (short)(machine.Energy - Reserve);
		budget = mounts?.EnergyTurn(budget, globals) ?? budget;
		short left = (short)(machine.Shields.RechargeTick(budget) + Reserve);
		machine.Energy = Math.Clamp(left, (short)0, FullPool);

		// The shields message re-arms on the player's machine.
		if (machine.IsPlayer && machine.Shields.Front > ShieldsMessageRearm && machine.Shields.Rear > ShieldsMessageRearm) {
			machine.ShieldsMessagePosted = false;
		}

		// The countdowns.
		var countdowns = machine.Countdowns;
		clock.TimerCountDown(ref countdowns.Retarget);
		clock.TimerCountDown(ref countdowns.Complaint);
		clock.CountdownTimerTick(ref countdowns.RadarSilence);
		if (clock.TimerCountDown(ref countdowns.UnderFire) == 0) {
			machine.DamageFromPlayerSide = 0;
		}

		if (clock.CountdownTimerTick(ref countdowns.DamageWindow) == 0) {
			countdowns.DamageWindow = DamageWindowReload;
			machine.DamageTaken = 0;
		}

		// The combat rating, recomputed while +0x8e is clear.
		if (!machine.SkipCombatRating) {
			Ai.Es1CombatRating.Compute(machine);
		}

		// ECM: a switched-on ECM holds the target's engagement action back; the machine jams while its own is on.
		var target = machine.Target;
		if (target != null && EcmPodSwitch(machine) != 0) {
			target.EngagementGate = true;
		}

		machine.Jamming = IsEcmSwitchedOn(machine);

		Lock(machine, mounts, globals);
	}

	/// <summary>
	/// The lock: the counts of rounds per rocket subtype (<see cref="Es1WeaponMounts.RoundsByMissileType"/>), the five
	/// lock flags and the lamp cleared; then with a target, the ECM roll against a jamming HERC, and unless the target has
	/// just changed or its bearing from the turret's line is outside ±<see cref="LockArc"/>, each subtype carried runs
	/// its countdown from the reload, the distance ≫ 2 held to <c>0x7fff</c>, and latches its flag at 0; otherwise
	/// subtypes 0, 1 and 2 take the reload, and the target-changed flag clears at the end. For the player's machine the
	/// lamp follows the armed mount's subtype and the lock tone plays.
	/// </summary>
	private static void Lock(Es1Mech machine, Es1WeaponMounts? mounts, Es1SimGlobals globals) {
		var clock = globals.Clock;
		var rounds = new short[Es1WeaponMounts.MissileSubtypes];
		var locks = mounts?.MissileLocks ?? new short[Es1WeaponMounts.MissileSubtypes];
		mounts?.RoundsByMissileType(rounds);
		Array.Clear(locks);
		machine.LockLamp = false;

		bool held = false;
		if (machine.Target is { } target) {
			// The ECM roll against a HERC whose jammer is on; a HERC whose jammer is off, or any other target, spoofs nothing.
			bool spoofedNow = false;
			var countdowns = machine.Countdowns;
			if (target.TargetClass != 0 || !target.Jamming) {
				machine.EcmSpoofed = false;
			} else if (clock.CountdownTimerTick(ref countdowns.EcmRoll) == 0) {
				if ((globals.Random.Next() & 0xfff) < EcmOdds) {
					machine.EcmSpoofed = true;
					countdowns.EcmRoll = EcmSpoofHold;
					spoofedNow = true;
				} else {
					machine.EcmSpoofed = false;
					countdowns.EcmRoll = EcmRetry;
				}
			}

			short reload = (short)Math.Min(target.Position.ApproxDistanceTo(machine.Position) >> 2, 0x7fff);
			short bearing = (short)(Es1MissionGroup.HeadingToward(machine.Position, target.Position) - machine.Heading
				+ machine.Twist);
			var timers = countdowns.Lock;
			if (!machine.TargetChanged && bearing >= -LockArc && bearing < LockArc) {
				// Subtypes 0 and 4 lock on the machine's own radar, and not in a tick the roll spoofs.
				foreach (int subtype in RadarLockedSubtypes) {
					if (rounds[subtype] == 0) {
						continue;
					}

					if (!machine.RadarOn || spoofedNow) {
						timers[subtype] = reload;
					} else if (clock.CountdownTimerTick(ref timers[subtype]) == 0 && !machine.EcmSpoofed) {
						locks[subtype] = 1;
					}
				}

				// Subtype 1 locks without the radar.
				if (rounds[1] != 0) {
					if (spoofedNow) {
						timers[1] = reload;
					} else if (clock.CountdownTimerTick(ref timers[1]) == 0 && !machine.EcmSpoofed) {
						locks[1] = 1;
					}
				}

				// Subtype 2, the anti-radiation missile, locks on a target that emits, spoofed or not.
				if (rounds[2] != 0) {
					if (!target.RadarOn && !target.Jamming) {
						timers[2] = reload;
					} else if (clock.CountdownTimerTick(ref timers[2]) == 0) {
						locks[2] = 1;
					}
				}
			} else {
				timers[0] = reload;
				timers[1] = reload;
				timers[2] = reload;
				held = true;
			}

			if (machine.IsPlayer) {
				machine.LockLamp = LockLampLit(mounts, locks);
				globals.LockTone.Play(machine, clock);
			}
		}

		if (held) {
			machine.TargetChanged = false;
		}
	}

	/// <summary>
	/// The lamp from the armed mount's subtype (slot <c>+0x64</c>, <see cref="Es1WeaponMount.AmmunitionSubtype"/>), or
	/// <see cref="Es1WeaponMount.NotALauncher"/> with none armed: a mount that is not a launcher lights it when any flag is
	/// set, a launcher when its own subtype's is. An armed hole, which retail reads through, counts as no launcher, and a
	/// subtype past the five flags lights nothing, engine guards.
	/// </summary>
	private static bool LockLampLit(Es1WeaponMounts? mounts, short[] locks) {
		int armed = mounts?.ArmedIndex ?? -1;
		short subtype = armed >= 0 && mounts!.Mounts[armed] is { } mount
			? mount.AmmunitionSubtype(out _)
			: Es1WeaponMount.NotALauncher;
		if (subtype == Es1WeaponMount.NotALauncher) {
			return Array.Exists(locks, flag => flag != 0);
		}

		return subtype >= 0 && subtype < locks.Length && locks[subtype] != 0;
	}

	/// <summary>
	/// <c>Mech_IsEcmSwitchedOn</c> (<c>00014e68</c>)(machine) (docs/es1/simulation/weapon-mount-classes.md#the-ecm): with an
	/// ECM mount (<see cref="Es1WeaponMounts.FindEcmMount"/>), its <c>+0x7b</c> for the player's machine, which the pod
	/// row's switch writes, and the radar <c>+0x90</c> for any other; with none, off.
	/// </summary>
	private static bool IsEcmSwitchedOn(Es1Mech machine) {
		if (machine.Mounts?.FindEcmMount() is not { } ecm) {
			return false;
		}

		return machine.IsPlayer ? ecm.ChargeTarget != 0 : machine.RadarOn;
	}

	/// <summary><c>Mech_GetEcmPodSwitch</c> (<c>00014eb4</c>)(machine): the ECM mount's <c>+0x7d</c>, or 0 with none.</summary>
	private static int EcmPodSwitch(Es1Mech machine) => machine.Mounts?.FindEcmMount()?.Capacitor ?? 0;
}
