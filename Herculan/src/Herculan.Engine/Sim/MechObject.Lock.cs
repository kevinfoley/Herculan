using Herculan.Engine.Audio;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// Missile lock — the second half of <c>Mech_PerTickSystemsUpdate</c> (<c>0041aa5c</c>), everything
/// after the reactor and shield bookkeeping <see cref="PowerTick"/> ports.
///
/// <para>What <c>manager+0x0a</c> is, how a lock is built, each subtype's hold condition and the ECM
/// roll are in docs/simulation/missile-lock.md. The genuine ammunition count is the separate array
/// <see cref="WeaponMounts.RoundsByMissileType"/> fills, which this block uses only to decide which
/// timers to run. The anti-radiation subtype's flags are the pair its guidance homes on (see
/// <see cref="Rocket"/>); subtype 3 is never locked, because the pilot flies it — see
/// <see cref="Rocket.PlayerFlownSubtype"/> and <see cref="SimWorld.FireRocket"/>.</para>
/// </summary>
public sealed partial class MechObject {
	/// <summary>
	/// Half-width of the cone a target has to stay inside for lock to build, about the turret
	/// centreline — the block's own <c>(bearing + 0x3000) &lt; 0x6000</c>, so ±67.5°. Wider than
	/// <see cref="TargetSelection.ForwardConeHalfWidth"/>: you can keep a lock on something you could
	/// not have selected from here.
	/// </summary>
	public const int LockConeHalfWidth = 0x3000;

	/// <summary>
	/// What the lock countdown is reloaded with: the range to the target over four, saturated at a
	/// short. Lock time is therefore linear in range. At the 81 units a tick a hardware-speed
	/// simulation steps, a target 20000 units away locks after about 62 ticks and one at 57205 after
	/// about 177; a run of <c>SAV/script1.dat</c> (APOCA, two ARH launchers) measured 63 and 178.
	/// </summary>
	private const int LockTimeRangeShift = 2;

	/// <summary>
	/// The ECM roll's numerator, out of <see cref="EcmRollRange"/> — the block's own
	/// <c>0x14 * 0x29</c>, about a 20% chance each time the roll comes round.
	/// </summary>
	private const int EcmRollWeight = 0x14 * 0x29;

	/// <summary>
	/// The same numerator for a machine carrying a healthy Targeting Pod — the block's own
	/// <c>5 * 0x29</c>, a quarter of <see cref="EcmRollWeight"/>. See
	/// <see cref="TargetingPodLock.EcmAssistLimit"/>.
	/// </summary>
	private const int EcmRollWeightWithPod = 5 * 0x29;

	/// <inheritdoc cref="EcmRollWeight"/>
	private const int EcmRollRange = 0x1000;

	/// <summary>Reload for <see cref="_ecmRollTimer"/> after a roll that spoofed.</summary>
	private const short EcmSpoofedInterval = 5000;

	/// <summary>Reload for <see cref="_ecmRollTimer"/> after a roll that did not.</summary>
	private const short EcmClearInterval = 0x5dc;

	/// <summary>
	/// One countdown per missile subtype — <c>mech+0x258</c>, <c>+0x25b</c>, <c>+0x25e</c> and
	/// <c>+0x264</c> in the original, which are four separate fields rather than an array. They are
	/// gathered here because they are addressed by the same subtype index everything else in this
	/// file is. Slot 3 is never used: the original reserves the field at <c>+0x261</c> — the run's
	/// stride of 3 runs through it to reach <c>+0x264</c> — but no tick ever names it.
	/// </summary>
	private readonly short[] _lockTimer = new short[WeaponMount.NotAMissile];

	private readonly short[] _missileRounds = new short[WeaponMount.NotAMissile];

	private short _ecmRollTimer;

	/// <summary>
	/// <c>mech+0x9c</c> — the ECM spoof flag, rolled against a jamming target. While it is set no
	/// lock can complete except the anti-radiation one, and a missile already in flight weaves rather
	/// than converging.
	/// </summary>
	public bool EcmSpoofed { get; private set; }

	/// <summary>
	/// <c>mech+0x9b</c> — <b>LOCK</b>. Whether the armed mount's own missile class has lock; a mount
	/// that is not a launcher lights it when <i>any</i> class does. This is what drives the cockpit's
	/// lock lamp and the intermittent lock tone — see <see cref="LockToneTick"/>.
	/// </summary>
	public bool LockAcquired { get; private set; }

	/// <summary>
	/// The blink period the lock tone repeats on: bit 6 of the coarse-tick clock, so the tone sounds
	/// once every time that bit goes high — one beep per 128 coarse ticks, a little over two seconds.
	/// </summary>
	private const long LockToneBlinkBit = 0x40;

	/// <summary><c>LockTone_WasLocked</c> (<c>0049a1d1</c>) — whether a lock was held, so its loss can be announced once.</summary>
	private bool _lockToneWasLocked;

	/// <summary><c>LockTone_PhaseSounded</c> (<c>0049a1d0</c>) — whether this blink phase's beep has already sounded.</summary>
	private bool _lockToneSounded;

	/// <summary>
	/// <c>Mech_LockTonePlay</c> (<c>0041b0bc</c>) — the cockpit's lock audio. Three sounds come out
	/// of one flag:
	///
	/// <list type="bullet">
	/// <item>Holding lock: <see cref="SoundId.LockTone"/> once per blink phase.</item>
	/// <item>Losing a lock that was held: <see cref="SoundId.LockLost"/>, once.</item>
	/// <item>No lock, but the target changed this tick: <see cref="SoundId.TargetSelect"/> — the
	/// acquisition blip, which is why selecting a target beeps even for a machine carrying no
	/// missiles.</item>
	/// </list>
	///
	/// <para><b>Where it is called from is load-bearing.</b> The call sits inside
	/// <see cref="MissileLockTick"/>'s own <c>target != null</c> block, and inside that block's
	/// locally-piloted arm — so a machine with nothing selected runs no lock audio at all. That
	/// matters because <see cref="TargetChanged"/> is only ever cleared with a target present: called
	/// unconditionally, a selection dropped to null latches the flag and the acquisition blip
	/// re-triggers every tick, which sounds like the front of the sample looping.</para>
	///
	/// <para>Note what the original does <i>not</i> do: the "lock lost" branch returns before the
	/// target-changed test, so switching target while locked plays the loss tone and not the
	/// acquisition blip.</para>
	/// </summary>
	private void LockToneTick(SimWorld world) {
		if (world.Sounds is not { } sounds) {
			return;
		}

		if (LockAcquired) {
			_lockToneWasLocked = true;

			if ((world.CoarseTicks & LockToneBlinkBit) == 0) {
				_lockToneSounded = false;
				return;
			}

			if (!_lockToneSounded) {
				sounds.Play(SoundId.LockTone);
				_lockToneSounded = true;
			}

			return;
		}

		if (_lockToneWasLocked) {
			sounds.Play(SoundId.LockLost);
			_lockToneWasLocked = false;
			return;
		}

		if (TargetChanged) {
			sounds.Play(SoundId.TargetSelect);
		}
	}

	/// <summary>Whether the given missile subtype currently holds lock on <see cref="Target"/>.</summary>
	public bool MissileLocked(int missileType) => Weapons.Locked(missileType);

	/// <summary>
	/// The target block of <c>Mech_PerTickSystemsUpdate</c>, run once per machine per tick.
	///
	/// <para><b>It runs after the sensor model, not before</b> — <c>Sim_MainTick</c> calls
	/// <c>Sim_DetectionTick</c> (<c>004123ac</c>) and then walks the mech list calling this, and the order matters because
	/// the gate below reads the line-of-sight cache that pass maintains. <see cref="SimWorld.Tick"/>
	/// keeps the same order.</para>
	///
	/// <para>The cockpit's lock audio closes the same function and is reached through
	/// <see cref="LockToneTick"/>, not called separately — see there for why the nesting matters.</para>
	/// </summary>
	internal void MissileLockTick(SimWorld world) {
		// Cleared for every machine every tick, whether or not it has a target — so losing a target
		// drops every lock on the following tick with nothing else needing to know.
		Weapons.ClearMissileLock();
		LockAcquired = false;

		Weapons.RoundsByMissileType(_missileRounds);

		// The original's own local_1a: the flag is cleared at the very end of the function, past the
		// target block, not at the point the branch below decides to. That is what puts the clear
		// after the lock audio has already read it, and it is why a selection made this tick blips.
		bool clearTargetChanged = false;

		if (Target is { } target) {
			MissileLockTargetBlock(world, target, ref clearTargetChanged);
		}

		if (clearTargetChanged) {
			TargetChanged = false;
		}
	}

	/// <summary>
	/// The part of <see cref="MissileLockTick"/> that only runs with something selected —
	/// <c>Mech_PerTickSystemsUpdate</c>'s <c>if (mech+0x69 != 0)</c> block, kept as its own method so
	/// that the boundary the original draws there is visible rather than implied by an early return.
	/// </summary>
	private void MissileLockTargetBlock(SimWorld world, SimObject target, ref bool clearTargetChanged) {
		bool spoofedThisRoll = EcmRollTick(world, target);

		// Where the target is, and how long a lock on it should take. A turretless chassis — the
		// RAZOR, the one type whose record sets the flag MechTypeRecord.IsFlyer reads — measures the
		// bearing without a twist it does not have, and locks instantly.
		short bearing = Detection.HeadingToward(target.Position, Position);
		short lockTime;
		short bearingError;

		if (!Type.IsFlyer) {
			int range = Position.ApproxDistanceTo(target.Position) >> LockTimeRangeShift;
			lockTime = (short)Math.Min(range, short.MaxValue);
			bearingError = (short)(bearing - Heading + TorsoTwistAngle);
		} else {
			lockTime = 0;
			bearingError = (short)(bearing - Heading);
		}

		// Line of sight, asked from whichever end keeps the cache row on the object that maintains
		// it: a human-side machine asks about the target, a Cybrid asks the target about itself. Only
		// human-side objects run a sensor sweep, so only their rows are ever refreshed.
		bool sighted = Side == MissionSide.Human
			? Detection.LineOfSight(world, this, target)
			: Detection.LineOfSight(world, target, this);

		bool holding = !TargetChanged
			&& (ushort)(bearingError + LockConeHalfWidth) < LockConeHalfWidth * 2
			&& sighted;

		if (holding) {
			// Subtypes 0 and 4 need this machine's own scanner running; 1 needs nothing; 2 needs the
			// target to be emitting. Every one of them is also blocked by ECM except 2.
			StepLock(0, ScannerActive && !spoofedThisRoll, lockTime, blockedByEcm: true);
			StepLock(4, ScannerActive && !spoofedThisRoll, lockTime, blockedByEcm: true);
			StepLock(1, !spoofedThisRoll, lockTime, blockedByEcm: true);
			StepLock(2, target.ScannerActive || target.JammerActive, lockTime, blockedByEcm: false);
		} else {
			// Everything resets and no lock builds this tick. Subtype 4's timer is deliberately not
			// among them: the original resets 0, 1 and 2 here and leaves 4 running, so that one class
			// keeps a partial lock across a moment of broken sight where the others do not.
			_lockTimer[0] = lockTime;
			_lockTimer[1] = lockTime;
			_lockTimer[2] = lockTime;

			// The target-changed flag is asked to clear here and only here, which is what makes it
			// cost exactly one tick: the switch sets it, the next tick lands in this branch and asks
			// for the clear, and locking may begin on the one after. The clear itself happens past the
			// lock audio below, so that audio still sees the flag this tick.
			clearTargetChanged = true;
		}

		// Both arms above fall into this one. Only the locally-piloted machine has a lamp or a
		// cockpit to sound a tone in, so the original computes neither for anything else — an AI
		// machine's lamp stays clear all mission.
		if (!LocallyPiloted) {
			return;
		}

		// The lock lamp, from the armed mount's own class. A mount that fires no missile at all reads
		// the whole array instead, which is why the lamp lights for a gun when a launcher elsewhere on
		// the machine has lock.
		short armedType = Weapons.Slots.ElementAtOrDefault(Weapons.Selected)?.AmmoType
			?? WeaponMount.NotAMissile;

		LockAcquired = armedType == WeaponMount.NotAMissile
			? Weapons.AnyLocked
			: Weapons.Locked(armedType);

		LockToneTick(world);
	}

	/// <summary>
	/// One subtype's countdown. <paramref name="building"/> false holds the timer at
	/// <paramref name="lockTime"/> so no lock can form; true lets it run, and reaching zero latches
	/// the flag.
	/// </summary>
	/// <param name="missileType">Which <c>PROJ.DAT</c> missile subtype.</param>
	/// <param name="building">That subtype's own emission condition.</param>
	/// <param name="lockTime">The reload, which is the range-derived lock time.</param>
	/// <param name="blockedByEcm">
	/// Whether a standing <see cref="EcmSpoofed"/> also stops the lock completing. Only the
	/// anti-radiation subtype passes false.
	/// </param>
	private void StepLock(int missileType, bool building, short lockTime, bool blockedByEcm) {
		// A class the machine carries no rounds for is not tracked at all — its timer is left exactly
		// as it was, which is the original's behaviour: the whole branch is inside `if (rounds != 0)`.
		if (_missileRounds[missileType] == 0) {
			return;
		}

		if (!building) {
			_lockTimer[missileType] = lockTime;
			return;
		}

		if (SimMath.CountdownTimerTick(ref _lockTimer[missileType]) == 0
				&& !(blockedByEcm && EcmSpoofed)) {
			Weapons.SetMissileLock(missileType);
		}
	}

	/// <summary>
	/// <c>Mech_PerTickSystemsUpdate</c>'s jammer block, through <c>Mech_IsEcmSwitchedOn</c> (<c>0041aa10</c>) — the only writer
	/// of <c>mech+0xa1</c>.
	///
	/// <para>An ECM pod is the outer gate: without one the answer is false and nothing else is
	/// consulted. With one, who is flying decides which switch is read.</para>
	///
	/// <list type="bullet">
	/// <item><b>An AI machine reads its radar mode.</b> It jams exactly while its scanner is ACTIVE,
	/// which <see cref="CombatReassess"/> turns on the moment it enters a fight and off again for a
	/// squadmate of the player's. It has no cockpit row to press, so this is the whole of its
	/// control.</item>
	/// <item><b>The player reads the pod row's button</b> (<see cref="EcmEnabled"/>) and their own
	/// radar mode does not enter into it — running active radar neither starts nor stops their
	/// jammer.</item>
	/// </list>
	///
	/// <para>Called from <see cref="Tick"/> rather than from <see cref="MissileLockTick"/>, so every
	/// machine's jammer is current before any lock is stepped against it. The original derives and
	/// steps within one per-mech pass, so a lock there reads a jammer that is one tick stale for the
	/// machines later in the list; at a roll that re-rolls every few seconds the difference cannot be
	/// observed, and running it as two passes is what keeps a headless tick order-independent.</para>
	/// </summary>
	internal void JammerTick() {
		Jammer = Pods.Ecm && (LocallyPiloted ? EcmEnabled : ScannerActive);
	}

	/// <summary>
	/// <c>Mech_PerTickSystemsUpdate</c>'s block at <c>0041abb1</c> — while this machine's ECM pod
	/// switch is on (<c>Mech_GetEcmPodSwitch</c>, <c>0041aa44</c>, which is <see cref="EcmEnabled"/>),
	/// raises <see cref="SimObject.EngagementActionHeld"/> on its selected target. Only the player's
	/// pod is ever ticked, so only the player's target is ever held (<see cref="PodTick"/>).
	///
	/// <para>Runs from the systems pass after <see cref="Detection.Tick"/>, as the original does, so
	/// the gate is still up when the next tick's sweep and shots read it.</para>
	/// </summary>
	internal void RaiseTargetEngagementGate() {
		if (Target is { } target && Pods.Ecm && EcmEnabled) {
			target.EngagementActionHeld = true;
		}
	}

	/// <summary>
	/// The ECM roll. A target that is not a HERC, or one with its jammer off, clears
	/// <see cref="EcmSpoofed"/> outright; a jamming HERC re-rolls it whenever
	/// <see cref="_ecmRollTimer"/> expires, and the interval that follows depends on which way the
	/// roll went — a spoof holds for <see cref="EcmSpoofedInterval"/>, a clear result is re-rolled
	/// after the much shorter <see cref="EcmClearInterval"/>.
	/// </summary>
	/// <returns>
	/// Whether the roll <i>just now</i> came up spoofed. Distinct from <see cref="EcmSpoofed"/>,
	/// which persists: the original tests both, and the fresh result additionally suppresses the
	/// scanner-gated classes for this one tick.
	/// </returns>
	private bool EcmRollTick(SimWorld world, SimObject target) {
		if (target.TargetClass != TargetClass.Herc || !target.JammerActive) {
			EcmSpoofed = false;
			return false;
		}

		if (SimMath.CountdownTimerTick(ref _ecmRollTimer) != 0) {
			return false;
		}

		// A Targeting Pod on THIS machine — the one holding the lock, not the one jamming it — cuts
		// the weight to a quarter while its own cached damage is under TargetingPodLock.EcmAssistLimit.
		// docs/simulation/target-selection.md has all four of the pod's thresholds.
		int weight = Pods.TargetingMount?.ComponentLock is { ComponentDamage: < TargetingPodLock.EcmAssistLimit }
			? EcmRollWeightWithPod
			: EcmRollWeight;

		if ((world.Random.Next() & (EcmRollRange - 1)) < weight) {
			EcmSpoofed = true;
			_ecmRollTimer = EcmSpoofedInterval;
			return true;
		}

		EcmSpoofed = false;
		_ecmRollTimer = EcmClearInterval;
		return false;
	}
}
