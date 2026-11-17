using Herculan.Engine.Numerics;

namespace Herculan.Es2.Sim;

/// <summary>
/// ES2's simulation clock: the tick length DBSIM measures once a frame and the helpers that scale by it
/// (docs/retail/simulation/dbsim-physics-notes.md, "Fixed-point math toolkit" and "Timer units"). The pure fixed-point
/// arithmetic they build on is <see cref="SimMath"/>'s; ES1's clock is its own (<c>Es1SimClock</c>).
///
/// <para>It is static because DBSIM's is one global, <c>SimTickDelta</c>, which the simulation's code reads from
/// anywhere; the game's simulation loop owns its value.</para>
/// </summary>
public static class SimClock {
	/// <summary>
	/// The global simulation timestep — DBSIM's <c>SimTickDelta</c> (<c>004d3be8</c>), read by
	/// <see cref="IntegrateRateOverTick"/> and <see cref="CountdownTimerTick"/>. In DBSIM this is
	/// refreshed once per frame from a hardware timer by the per-frame sim tick
	/// (<c>Sim_MainTick</c>, <c>0045f464</c>); here the game's simulation loop owns it the same way.
	///
	/// Everything scaled by it is a "per this tick" quantity, not "per second" — which is what
	/// makes DBSIM a discrete fixed/semi-fixed timestep sim rather than a continuous-time
	/// integrator.
	///
	/// <para><b>It is Q8 where 1.0 = 125 ms</b>, not a count of milliseconds:
	/// <c>Time_BeginSimTick</c> (<c>004677bc</c>) waits out its 40 ms frame cap and then writes
	/// <c>clamp((elapsedMs &lt;&lt; 8) / 125, 0x40, 0x1c2)</c>, which is
	/// <see cref="VanillaTickDelta"/>'s 81 on hardware that keeps up. That makes the unit of every
	/// countdown in the simulation 125/256 ms ≈ 0.49 ms — so a timer reload of 10000 is about 4.9
	/// seconds of wall clock, and a rate is "per 125 ms".</para>
	/// </summary>
	public static short TickDelta { get; set; }

	/// <summary>
	/// <c>Math_IntegrateRateOverTick</c> (<c>00467820</c>) — "apply a per-unit-time rate as this
	/// tick's delta": <c>Q8Multiply(TickDelta, rate)</c>, clamped to signed 16-bit range. Called on
	/// velocity- and acceleration-like fields to get a position or speed delta.
	/// </summary>
	public static int IntegrateRateOverTick(short rate) {
		int value = SimMath.Q8Multiply(TickDelta, rate);
		if (value < -0x7fff) {
			return -0x7fff;
		}
		if (value > 0x7fff) {
			return 0x7fff;
		}
		return value;
	}

	/// <summary>
	/// The <see cref="TickDelta"/> the original produces on hardware fast enough to hit its own
	/// 40 ms frame cap: <c>40 * 256 / 125</c>. It is the reference point
	/// <see cref="ScalePerTickStep"/> measures against, and the value the engine's fixed timestep
	/// pins <see cref="TickDelta"/> to.
	/// </summary>
	public const short VanillaTickDelta = 81;

	/// <summary>
	/// Whether <see cref="ScalePerTickStep"/> rescales at all. On by default. A replayed tape turns it
	/// off: its ticks run at the recording machine's own deltas, and matching the recording means
	/// applying each step once per frame whatever that frame's length, as the original did.
	/// </summary>
	public static bool PerTickStepsScaled { get; set; } = true;

	/// <summary>
	/// Converts a constant the original applied as a raw per-tick step into this tick's equivalent,
	/// <c>step * TickDelta / VanillaTickDelta</c>. <b>An engine invention, not a DBSIM function.</b>
	///
	/// <para>DBSIM feeds a handful of constants — the locomotion accel/decel pair, notably —
	/// straight into <see cref="SimMath.RateLimitedMoveToward"/> without routing them through
	/// <see cref="IntegrateRateOverTick"/>, so their ramps take a fixed number of <i>ticks</i>
	/// rather than a fixed amount of <i>time</i>, and acceleration and turn ramp are frame-rate
	/// dependent. This rescales them to be tick-rate independent. It is exact at
	/// <see cref="VanillaTickDelta"/>, the engine's fixed timestep, where it changes nothing.
	/// <see cref="IntegrateRateOverTick"/> cannot do this job: its Q8 unit is 125 ms, so it would
	/// return about a third of the step.</para>
	///
	/// <para>Steps are slew rates and non-negative. A non-zero step never scales to zero — at a
	/// tick short enough to round it away the result is pinned to 1, so a ramp cannot stall
	/// outright. Below the vanilla tick length that pin is the quantization floor, as there is no
	/// fractional carry. Off while <see cref="PerTickStepsScaled"/> is false.</para>
	/// </summary>
	public static short ScalePerTickStep(short step) {
		if (step <= 0 || !PerTickStepsScaled) {
			return step;
		}

		int value = step * TickDelta / VanillaTickDelta;
		if (value < 1) {
			return 1;
		}
		return value > 0x7fff ? (short)0x7fff : (short)value;
	}

	/// <summary>
	/// <c>Math_CountdownTimerTick</c> (<c>00467944</c>) — decrements a timer by one tick, clamped
	/// at 0, returning the new value. DBSIM takes a pointer into a struct (the timer lives at a
	/// +1 byte offset inside its owner); the engine passes the field by reference instead, which
	/// is the same operation without the unaligned-pointer arithmetic.
	/// The owning record is 3 bytes and the meaning of its leading byte is still open — see
	/// <c>docs/retail/simulation/dbsim-physics-notes.md</c>.
	///
	/// <para><b>These counters are not in milliseconds.</b> What is subtracted is
	/// <see cref="TickDelta"/>, which is Q8 with 1.0 = 125 ms, so one count is 125/256 ms ≈ 0.49 ms
	/// and a reload of 10000 expires in about 4.9 seconds — not ten. Every constant handed to this
	/// function or to <see cref="TimerCountDown"/> is in that unit, and reading one as milliseconds
	/// overstates it by a factor of about two. See <see cref="TickDelta"/>.</para>
	/// </summary>
	public static short CountdownTimerTick(ref short timer) {
		timer = (short)(timer - TickDelta);
		if (timer < 0) {
			timer = 0;
		}
		return timer;
	}

	/// <summary>
	/// <c>Timer_CountDown</c> (<c>004679a4</c>) — the 32-bit sibling of
	/// <see cref="CountdownTimerTick"/>, for the countdowns that hold values a <c>short</c> cannot:
	/// the AI's behaviour dwell runs to 50000 counts and its friendly-fire cooldown to 40000. DBSIM is
	/// handed a pointer one byte before the value and steps the int that follows it, which is the
	/// same operation without the unaligned-pointer arithmetic.
	/// </summary>
	public static int TimerCountDown(ref int timer) {
		timer -= TickDelta;
		if (timer < 0) {
			timer = 0;
		}
		return timer;
	}
}
