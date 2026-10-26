using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// A HERC's throttle, control law and root-motion step (docs/es1/simulation/mech-locomotion.md): <c>Mech_ApplyThrottleInput</c>
/// (<c>ES1DBSIM 00011afc</c>), <c>Mech_LocomotionTick</c> (<c>00012038</c>) and <c>Mech_IntegrateMotion</c>
/// (<c>00014148</c>), on the fields of its <see cref="Es1Mech"/>. The type record's fields are the chassis file's
/// (docs/es1/simulation/mech-locomotion.md#the-control-law--mech_locomotiontick-00012038).
///
/// <para>Every per-tick step (the record's <c>+0x08</c> and <c>+0x0a</c>) is applied once a tick unscaled, as retail
/// applies it: <see cref="Es1SimClock"/> runs retail's ticks. The multiplies are ES1's rounding ones.</para>
/// </summary>
public sealed class Es1Locomotion {
	/// <summary>Full throttle either way.</summary>
	public const short ThrottleFull = 0x400;

	/// <summary>The Q8 factor of the throttle axis' step a tick.</summary>
	private const short ThrottleRate = 0x91;

	/// <summary>The axis deflection up to which the throttle's sign, not the axis', decides the steer's.</summary>
	private const int SteerByThrottleDeflection = 0x3c;

	/// <summary>The steer's clamp.</summary>
	private const short SteerFull = 0x100;

	/// <summary>The Q10 scales of the desired speed for the severe and the milder leg grade.</summary>
	private const short LegsCrippledScale = 400, LegsDamagedScale = 750;

	/// <summary>The speed below which the tent is 0, and the speed it is measured from.</summary>
	public const int MinimumTurningSpeed = 45;

	/// <summary>The least playback rate, either way, of a step-off speeding up.</summary>
	private const short StepOffMinimumRate = 0x3c;

	/// <summary>The rate of a stop or step-off sequence the machine stands in, and of the fall until it reaches the death sequence.</summary>
	private const short StopRate = 100;

	/// <summary>The rate the death sequence plays at.</summary>
	private const short DeathRate = 0x78;

	/// <summary>The steer, either way, past which a standing machine turns in place.</summary>
	private const int TurnInPlaceDeflection = 0x32;

	/// <summary>The Q10 factor of the steer that is the turn in place's rate.</summary>
	private const short TurnInPlaceRate = 0x15e;

	/// <summary>The rate of <c>Math_IntegrateRateOverTick</c> that is the root-motion step's time.</summary>
	private const short RootMotionRate = 100;

	private readonly Es1Mech _mech;

	public Es1Locomotion(Es1Mech mech) {
		_mech = mech;
	}

	/// <summary>
	/// <c>Mech_ApplyThrottleInput</c> (<c>00011afc</c>): the throttle moved by the axis, a rate, and the control law run
	/// on the speed it asks for (docs/es1/simulation/mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc).
	/// </summary>
	/// <param name="throttleLeverMode"><c>ThrottleLeverMode</c> (<c>000919ce</c>), which the caller owns.</param>
	public void ApplyThrottleInput(Es1SimClock clock, short throttleAxis, short steer, short throttleLeverMode) {
		var mech = _mech;
		short axis = throttleLeverMode < 0 ? (short)-throttleAxis : throttleAxis;

		int deflection = Magnitude(axis);
		if (deflection <= SteerByThrottleDeflection) {
			if (mech.Throttle >= 0) {
				steer = (short)-steer;
			}
		} else if (axis < 1) {
			steer = (short)-steer;
		}

		short step = (short)SimMath.Q8MultiplyRounded(-axis, ThrottleRate);
		if (step != 0) {
			mech.ThrottleChanged = true;
			mech.Throttle = MoveThrottle(mech.Throttle, step, throttleLeverMode);
		}

		var chassis = mech.Type.Chassis;
		short top = mech.Throttle < 0 ? (short)-chassis.SpeedReverse : chassis.SpeedForward;
		LocomotionTick(clock, 0, (short)SimMath.Q10MultiplyRounded(mech.Throttle, top), steer);
	}

	/// <summary>
	/// <c>Mech_ApplyThrottleInput</c>'s step 3 for a step that is not 0: the throttle moved by <paramref name="step"/> and
	/// clamped to the lever mode's range, or 0 when it is not 0 and would change sign, so a sign change waits a tick at
	/// zero. The zero test is retail's, of the throttle's low 12 bits.
	/// </summary>
	public static short MoveThrottle(short throttle, short step, short throttleLeverMode) {
		short next = (short)(throttle + step);
		if ((throttle & 0xfff) != 0 && next < 0 != throttle < 0) {
			return 0;
		}

		short upper = throttleLeverMode < 0 ? (short)0 : ThrottleFull;
		short lower = throttleLeverMode < 1 ? (short)-ThrottleFull : (short)0;
		return next >= upper ? upper : next <= lower ? lower : next;
	}

	/// <summary>
	/// <c>Mech_LocomotionTick</c> (<c>00012038</c>): the speed, the gait machine and the turn
	/// (docs/es1/simulation/mech-locomotion.md#the-control-law--mech_locomotiontick-00012038).
	/// </summary>
	/// <param name="avoidanceArgument">The flags <c>Mech_AiObstacleAvoidance</c> takes; <see cref="ApplyThrottleInput"/> passes 0.</param>
	public void LocomotionTick(Es1SimClock clock, short avoidanceArgument, short desired, short steer) {
		var mech = _mech;
		var chassis = mech.Type.Chassis;

		// The speed (docs/es1/simulation/mech-locomotion.md#the-speed).
		if (mech.Immobilized) {
			desired = 0;
			steer = 0;
		} else {
			int countdown = mech.UnstickCountdown;
			int remaining = clock.TimerCountDown(ref countdown);
			mech.UnstickCountdown = countdown;
			if (remaining == 0) {
				desired = desired >= chassis.SpeedForward ? chassis.SpeedForward
					: desired <= chassis.SpeedReverse ? chassis.SpeedReverse
					: desired;
			} else {
				desired = mech.UnstickReverse ? chassis.SpeedReverse : chassis.SpeedForward;
			}

			// PLACEHOLDER: a machine other than the player's (PlayerMech, 000bc42a) runs Mech_AiObstacleAvoidance
			// (00011c80)(machine, avoidanceArgument, &desired, &steer); plan step 8, the AI, ports it.
			_ = avoidanceArgument;
		}

		if (mech.LegsCrippled) {
			desired = (short)SimMath.Q10MultiplyRounded(LegsCrippledScale, desired);
		} else if (mech.LegsDamaged) {
			desired = (short)SimMath.Q10MultiplyRounded(LegsDamagedScale, desired);
		}

		steer = steer >= SteerFull ? SteerFull : steer <= -SteerFull ? (short)-SteerFull : steer;

		short previous = mech.Speed;
		short speed = mech.Speed;
		SimMath.RateLimitedMoveToward(ref speed, desired, chassis.SpeedAccelDecel);
		mech.Speed = speed;

		bool heldTurn = mech.LocomotionThread is { } thread && RunGaitMachine(thread, previous, ref steer);

		// Turning (docs/es1/simulation/mech-locomotion.md#turning).
		short tent = TurnTent(mech.Speed, chassis.SpeedForward, chassis.SpeedTurn, heldTurn);
		short turnRate = mech.TurnRate;
		SimMath.RateLimitedMoveToward(ref turnRate, (short)SimMath.Q8MultiplyRounded(steer, tent), chassis.TurnAccelDecel);
		mech.TurnRate = turnRate;

		var rotation = mech.Rotation;
		mech.Rotation = (rotation.X, rotation.Y, (short)(rotation.Z + mech.TurnRate));
		mech.RotationWord = 0;
	}

	/// <summary>
	/// The turn rate's tent over the speed: 0 when the gait machine <paramref name="heldTurn"/> or |<paramref name="speed"/>|
	/// is under <see cref="MinimumTurningSpeed"/>; otherwise rising from 0 there to <paramref name="topTurnRate"/> half way
	/// to <paramref name="forwardTopSpeed"/>, and falling to half of it at the top speed, in retail's integer arithmetic
	/// (docs/es1/simulation/mech-locomotion.md#turning).
	/// </summary>
	public static short TurnTent(short speed, short forwardTopSpeed, short topTurnRate, bool heldTurn) {
		int magnitude = Magnitude(speed);
		if (heldTurn || magnitude < MinimumTurningSpeed) {
			return 0;
		}

		int span = (short)(forwardTopSpeed - MinimumTurningSpeed);
		int above = (short)(magnitude - MinimumTurningSpeed);
		int half = (short)(span >> 1);
		return half < above
			? (short)(topTurnRate - topTurnRate * (above - half) / ((span - half) * 2))
			: (short)(topTurnRate * above / half);
	}

	/// <summary>
	/// <c>Mech_LocomotionTick</c>'s gait machine on the locomotion thread: ES2's with ES1's three rows that leave the
	/// playback rate as it was, and no crawl lift after it
	/// (docs/es1/simulation/mech-locomotion.md#the-gait-machine, docs/retail/simulation/mech-locomotion.md#the-gait-state-machine).
	/// Returns whether a row held the turn this tick.
	/// </summary>
	private bool RunGaitMachine(AnimationThread thread, short previous, ref short steer) {
		var mech = _mech;
		var chassis = mech.Type.Chassis;
		short walk = chassis.AnimId_Walk, run = chassis.AnimId_Run;
		short stopForward = chassis.AnimId_StopMove, stopReverse = chassis.AnimId_StopReverse;
		short turnInPlace = chassis.AnimId_TurnInPlace, death = chassis.AnimId_Death;
		short threshold = chassis.GaitThreshold;

		int target = thread.TargetSequence;
		int sequence = thread.Sequence;
		int magnitude = Magnitude(mech.Speed);
		int previousMagnitude = Magnitude(previous);
		bool turning = sequence == turnInPlace;
		bool stopping = sequence == stopForward || sequence == stopReverse;
		bool heldTurn = false;

		// Turning in place excludes travel.
		if (magnitude != 0 && turning && !stopping) {
			steer = 0;
			magnitude = 0;
			mech.Speed = 0;
		}

		// A transition in flight freezes the speed.
		if (!thread.IsSettled) {
			mech.Speed = previous;
			return false;
		}

		if (!turning && mech.Immobilized) {
			Fall(thread, death, stopForward, stopReverse);
			return false;
		}

		if (previousMagnitude < magnitude) {
			if (stopping) {
				// Stepping off: the speed held to the walk-to-run speed, the rate at least 60 its way.
				if (mech.Speed < 1) {
					if (mech.Speed < -threshold) {
						mech.Speed = (short)-threshold;
					}
					mech.PlaybackRate = mech.Speed > -StepOffMinimumRate ? (short)-StepOffMinimumRate : mech.Speed;
				} else {
					if (mech.Speed > threshold) {
						mech.Speed = threshold;
					}
					mech.PlaybackRate = mech.Speed < StepOffMinimumRate ? StepOffMinimumRate : mech.Speed;
				}

				heldTurn = true;
				if (target != walk) {
					thread.SetSequence(mech.Speed < 0 ? stopReverse : stopForward, 0, 0);
					thread.SetTarget(walk, -1, 0);
				}
			} else if (sequence == walk) {
				mech.PlaybackRate = mech.Speed;
				if (target == stopForward || target == stopReverse) {
					thread.ClearTarget();
				}

				if (mech.Speed > threshold && target != run) {
					thread.SetTarget(run, -1, 0);
				}
			} else {
				// ES1's: the rate is left as it was.
				thread.ClearTarget();
			}
		} else if (magnitude < previousMagnitude) {
			if (sequence == run) {
				if (magnitude < threshold) {
					short pinned = previous < 1 ? chassis.GaitThresholdReverse : threshold;
					mech.Speed = pinned;
					mech.PlaybackRate = pinned;
					if (target != walk) {
						thread.SetTarget(walk, -1, 0);
					}
				} else {
					// ES1's: the rate is left as it was.
					thread.ClearTarget();
				}
			} else if (sequence == walk) {
				if (magnitude == 0) {
					mech.PlaybackRate = 0;
					if (!stopping) {
						short stop = previous < 1 ? stopReverse : stopForward;
						if (target != stop) {
							thread.SetTarget(stop, -1, 0);
						}

						mech.PlaybackRate = previous < 1 ? (short)-StopRate : StopRate;
						heldTurn = true;
					}
				} else if (target == run) {
					// ES1's: the rate is left as it was.
					thread.ClearTarget();
				}
			}
		} else if (magnitude == 0) {
			int deflection = Magnitude(steer);
			if (deflection <= TurnInPlaceDeflection || !(stopping || turning)) {
				if (turning && !stopping) {
					if (target != stopForward) {
						thread.SetTarget(stopForward, -1, 0);
					}

					mech.PlaybackRate = mech.PlaybackRate < 1 ? (short)-StopRate : StopRate;
				}
			} else if (!turning) {
				if (target != turnInPlace) {
					thread.SetSequence(steer < 0 ? stopReverse : stopForward, 0, 0);
					thread.SetTarget(turnInPlace, -1, 0);
				}

				mech.PlaybackRate = steer < 0 ? (short)-StopRate : StopRate;
			} else {
				mech.PlaybackRate = (short)SimMath.Q10MultiplyRounded(TurnInPlaceRate, steer);
			}
		} else {
			mech.PlaybackRate = mech.Speed;
		}

		return heldTurn;
	}

	/// <summary>
	/// The fall of an immobilized machine that is not turning in place
	/// (docs/retail/simulation/mech-locomotion.md#going-down): aimed at the death sequence at rate 100, a machine in the
	/// reverse stop sequence first cut to the forward one; then rate <c>0x78</c>; and once it has played out,
	/// <see cref="Es1Mech.Collapsed"/> latched.
	/// </summary>
	private void Fall(AnimationThread thread, short death, short stopForward, short stopReverse) {
		var mech = _mech;
		if (thread.Sequence == death) {
			mech.PlaybackRate = DeathRate;
			if (!mech.Collapsed && thread.Frame == thread.NextFrame) {
				mech.Collapsed = true;
				// PLACEHOLDER: Mech_SpreadImpactDamage (00013078)(machine, 0x78, 0x96), plan step 7b (damage); and sound
				// 0x29 at the machine's position, which no plan step ports yet.
			}

			return;
		}

		if (thread.TargetSequence != death) {
			if (thread.Sequence == stopReverse) {
				thread.SetSequence(stopForward, 0, 0);
			}

			thread.SetTarget(death, -1, 0);
			// PLACEHOLDER: sound 0x1e at the machine's position when it is not collapsed, which no plan step ports yet.
		}

		mech.PlaybackRate = StopRate;
	}

	/// <summary>
	/// <c>Mech_IntegrateMotion</c> (<c>00014148</c>): <c>AnimThread_SetPlaybackRate</c> (<c>000626c8</c>) of the playback
	/// rate onto the locomotion thread, then root motion over <c>Math_IntegrateRateOverTick</c>(100), the tick's length in
	/// milliseconds (docs/es1/simulation/mech-locomotion.md#the-move, docs/es1/simulation/mech-locomotion.md#how-fast-it-walks).
	/// </summary>
	public void IntegrateMotion(Es1SimClock clock) {
		var mech = _mech;
		if (mech.LocomotionThread is not { } thread) {
			return;
		}

		thread.Rate = mech.PlaybackRate;
		mech.ApplyRootMotionIfEnabled(clock.IntegrateRateOverTick(RootMotionRate));
	}

	/// <summary>The magnitude of a 16-bit value as retail takes it, −32768 giving <c>0x7fff</c>.</summary>
	private static int Magnitude(short value) => value == short.MinValue ? short.MaxValue : Math.Abs((int)value);
}
