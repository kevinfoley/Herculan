using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// ES1's simulation clock: the tick length <c>Time_InitSimClock</c> (<c>ES1DBSIM 00063e64</c>) sets up and
/// <c>Time_BeginSimTick</c> (<c>00063f40</c>) measures each frame from the PC timer's interrupts, and the helpers that
/// scale by it (docs/es1/simulation/fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40). The simulation
/// owns one; ES2's tick is <see cref="SimMath.TickDelta"/>.
///
/// <para>The host has no timer interrupt. <see cref="AdvanceHostFrame"/> counts the interrupts a host frame's real
/// time holds, at <see cref="TicksPerSecond"/> a second, the rate the simulator derives from the divisor (the
/// hardware's is 120.05), carrying the fraction of an interrupt to the next host frame, and steps the countdown by
/// each as <c>Time_SimClockTimerTick</c> (<c>00063e3c</c>) does.</para>
///
/// <para><b>Not retail:</b> a tick is due once <see cref="InterruptsPerTick"/>, three, interrupts have passed since the
/// last reload, where <c>Time_BeginSimTick</c> spins while the countdown is above <see cref="TicksPerSecond"/> − 2, a
/// wait of two. Retail's floor counts a two-interrupt frame as 25 ms, so on a machine that keeps up it plays at 1.5
/// times real time (the frame's retrace wait, ported, ends before the second interrupt there); three interrupts are
/// 25 ms, so the game plays in real time as on the hardware of its day. The user's choice,
/// docs/herculan/plan-es1-first-mission.md#decisions, decision 2; listed in <c>KNOWN_ISSUES.md</c>.</para>
/// </summary>
public sealed class Es1SimClock {
	/// <summary>The divisor <c>Sim_StartMission</c> builds the timer with, <c>DVTimer_Ctor(0, 0x26d3)</c> (docs/es1/simulation/timer.md#the-rate).</summary>
	public const ushort TimerDivisor = 0x26d3;

	/// <summary>The interrupts a tick waits for since the last reload: this engine's three, where retail waits for two (see the class summary).</summary>
	public const int InterruptsPerTick = 3;

	/// <summary><c>Time_ElapsedToQ8Clamped</c>'s floor and ceiling.</summary>
	private const short MinimumDelta = 0x40, MaximumDelta = 0x1c2;

	/// <summary>The divisors <c>000bdbd4</c> and <c>000bdbd6</c>, the two deltas' units.</summary>
	private readonly short _unit, _unit2;

	/// <summary><c>SimClock_Countdown</c> (<c>000bba46</c>).</summary>
	private short _countdown;

	/// <summary>The time past the last whole interrupt, in <see cref="TimeSpan"/> ticks times <see cref="TicksPerSecond"/>.</summary>
	private long _phase;

	/// <summary>The interrupts counted since the clock was built, <c>Time_SimClockTimerTick</c>'s calls.</summary>
	private long _interrupts;

	/// <summary>
	/// <c>Time_InitSimClock</c>: both deltas 1.0 (<c>0x100</c>), <see cref="TicksPerSecond"/> from the divisor
	/// (<c>1000000 / Math_Q14Multiply(divisor, 0x35a1)</c>, 120), the two units and <see cref="ElapsedInterrupts"/> a
	/// tenth of it, 12, and the countdown at <see cref="TicksPerSecond"/>.
	/// </summary>
	public Es1SimClock() {
		TickDelta = 0x100;
		TickDelta2 = 0x100;
		TicksPerSecond = InterruptsPerSecond;
		short perTenth = (short)(TicksPerSecond / 10);
		_unit = perTenth;
		_unit2 = perTenth;
		ElapsedInterrupts = perTenth;
		_countdown = TicksPerSecond;
	}

	/// <summary>
	/// The PC timer's interrupts a second as the simulator derives them from <see cref="TimerDivisor"/>,
	/// <c>1000000 / Math_Q14Multiply(divisor, 0x35a1)</c>, 120: <c>Time_InitSimClock</c>'s and <c>CockpitMouse_Init</c>'s.
	/// </summary>
	public static short InterruptsPerSecond => (short)(1000000 / SimMath.Q14MultiplyRounded(TimerDivisor, 0x35a1));

	/// <summary><c>SimClock_TicksPerSecond</c> (<c>000bba44</c>): the PC timer's interrupts a second, 120.</summary>
	public short TicksPerSecond { get; }

	/// <summary>
	/// The clock <c>000981c4</c>, which <c>Time_SimClockTimerTick</c> (<c>00063e3c</c>) adds 1 to on every second
	/// interrupt, 60 times a second (docs/es1/simulation/fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40),
	/// counted from this clock's construction.
	/// </summary>
	public uint Clock => (uint)(_interrupts / 2);

	/// <summary><c>SimTickDelta</c> (<c>000bdbd0</c>): the last tick's length, Q8 with 1.0 = 100 ms, held to <c>0x40</c>–<c>0x1c2</c>.</summary>
	public short TickDelta { get; private set; }

	/// <summary><c>SimTickDelta2</c> (<c>000bdbd2</c>): <see cref="TickDelta"/> over the second unit, equal to it while the two units are.</summary>
	public short TickDelta2 { get; private set; }

	/// <summary>The interrupts the last tick measured, which <c>Time_BeginSimTick</c> keeps in <c>000bdbd8</c>.</summary>
	public short ElapsedInterrupts { get; private set; }

	/// <summary>
	/// Counts the interrupts in <paramref name="elapsed"/>, the host frame's real time, running the countdown down by
	/// each and stopping it at 0; and when <see cref="InterruptsPerTick"/> have passed since the last reload, sets both
	/// deltas from the elapsed interrupts and reloads the countdown, as <c>Time_BeginSimTick</c> does after its wait.
	/// Returns whether a tick is due; at most one is per call.
	/// </summary>
	public bool AdvanceHostFrame(TimeSpan elapsed) {
		Hold(elapsed);
		if (_countdown > TicksPerSecond - InterruptsPerTick) {
			return false;
		}

		ElapsedInterrupts = (short)(TicksPerSecond - _countdown);
		TickDelta = ElapsedToQ8Clamped(ElapsedInterrupts, _unit);
		TickDelta2 = ElapsedToQ8Clamped(ElapsedInterrupts, _unit2);
		_countdown = TicksPerSecond;
		return true;
	}

	/// <summary>
	/// <c>Sim_MainTick</c>'s store during playback, <c>SimTickDelta</c> = the input block's <c>+0x29</c>, the frame's
	/// recorded delta (docs/es1/simulation/main-loop.md#the-tick--sim_maintick-00059700). <see cref="TickDelta2"/> is
	/// not written: <c>Time_BeginSimTick</c>, its one writer after <c>Time_InitSimClock</c>, does not run while a tape plays.
	/// </summary>
	public void SetRecordedTickDelta(short delta) => TickDelta = delta;

	/// <summary>
	/// Counts the interrupts in <paramref name="elapsed"/> as <see cref="AdvanceHostFrame"/> does, running the countdown
	/// down and stopping it at 0, without asking whether a tick is due: the PC timer runs on while retail's frame stands
	/// in a routine that waits for the monitor, a view's slide (docs/es1/simulation/cockpit-views.md#cockpitview_stepviewtransition-00022904)
	/// or the frame's last retrace wait (docs/es1/simulation/cockpit-views.md#cockpitview_processviewcommand-000226f0),
	/// so the tick after it measures that time.
	/// </summary>
	public void Hold(TimeSpan elapsed) {
		ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
		_phase += elapsed.Ticks * TicksPerSecond;
		long interrupts = _phase / TimeSpan.TicksPerSecond;
		_phase -= interrupts * TimeSpan.TicksPerSecond;
		_interrupts += interrupts;
		_countdown = (short)Math.Max(0L, _countdown - interrupts);
		for (long i = 0; i < interrupts && _callbacks.Count > 0; i++) {
			RunCallbacks();
		}
	}

	/// <summary>
	/// <c>Timer_AddCallback</c> (<c>0006d289</c>)(<paramref name="count"/>, <paramref name="callback"/>): the callback run
	/// on every <paramref name="count"/>th interrupt from now, after the slots taken before it
	/// (docs/es1/simulation/timer.md#callbacks). Returns the slot + 1. <c>Time_SimClockTimerTick</c>, slot 0 in retail,
	/// is this clock's own count and not a slot here.
	/// </summary>
	public int AddCallback(int count, Action callback) {
		ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
		_callbacks.Add(new TimerCallback(count, callback) { Counter = count });
		return _callbacks.Count;
	}

	/// <summary>One interrupt's pass over the slots, lowest first: each counter down by 1, and at 0 its callback run and the counter reloaded.</summary>
	private void RunCallbacks() {
		foreach (var slot in _callbacks) {
			if (--slot.Counter == 0) {
				slot.Counter = slot.Period;
				slot.Callback();
			}
		}
	}

	/// <summary>The periodic slots <see cref="AddCallback"/> took, in slot order.</summary>
	private readonly List<TimerCallback> _callbacks = [];

	/// <summary>A periodic slot: its period <c>0009ce6c</c>, counter <c>0009cdec</c> and callback <c>0009cd6c</c>.</summary>
	private sealed class TimerCallback(int period, Action callback) {
		public int Period { get; } = period;

		public Action Callback { get; } = callback;

		public int Counter { get; set; }
	}

	/// <summary>
	/// <c>Math_IntegrateRateOverTick</c> (<c>00063fa4</c>): <paramref name="rate"/> times <see cref="TickDelta"/> by the
	/// rounding Q8 multiply, sign-extended from 16 bits, then held to ±<c>0x7fff</c>, so an out-of-range product wraps
	/// (docs/es1/simulation/fixed-point-math.md#rate-over-tick).
	/// </summary>
	public short IntegrateRateOverTick(short rate) => WrapToTick(SimMath.Q8MultiplyRounded(rate, TickDelta));

	/// <summary><c>Math_IntegrateRateOverTick2</c> (<c>00063fe0</c>): <see cref="IntegrateRateOverTick"/> with <see cref="TickDelta2"/>.</summary>
	public short IntegrateRateOverTick2(short rate) => WrapToTick(SimMath.Q8MultiplyRounded(rate, TickDelta2));

	/// <summary>
	/// <c>Math_CountdownTimerTick</c> (<c>0006410c</c>): ES2's <see cref="SimMath.CountdownTimerTick"/> with
	/// <see cref="TickDelta"/>; one count is 100/256 ms (docs/es1/simulation/fixed-point-math.md#helpers-that-match).
	/// </summary>
	public short CountdownTimerTick(ref short timer) {
		timer = (short)(timer - TickDelta);
		if (timer < 0) {
			timer = 0;
		}
		return timer;
	}

	/// <summary><c>Timer_CountDown</c> (<c>00064178</c>): ES2's <see cref="SimMath.TimerCountDown"/> with <see cref="TickDelta"/>.</summary>
	public int TimerCountDown(ref int timer) {
		timer -= TickDelta;
		if (timer < 0) {
			timer = 0;
		}
		return timer;
	}

	/// <summary><c>Time_ElapsedToQ8Clamped</c> (<c>00063f04</c>): <c>(elapsed &lt;&lt; 8) / unit</c>, its low 16 bits held to <c>0x40</c>–<c>0x1c2</c>.</summary>
	private static short ElapsedToQ8Clamped(short elapsed, short unit) {
		short value = (short)((elapsed << 8) / unit);
		if (value > MaximumDelta) {
			return MaximumDelta;
		}
		return value < MinimumDelta ? MinimumDelta : value;
	}

	/// <summary>The rate-over-tick helpers' sign extension from 16 bits and ±<c>0x7fff</c> clamp.</summary>
	private static short WrapToTick(int product) => (short)Math.Clamp((int)(short)product, -0x7fff, 0x7fff);
}
