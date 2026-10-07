namespace Herculan.Engine.Numerics;

/// <summary>
/// Direct port of DBSIM.EXE's shared fixed-point math toolkit — the primitives every other
/// simulation subsystem builds on. Each method below corresponds 1:1 to a specific
/// reverse-engineered function; see docs/retail/simulation/dbsim-physics-notes.md ("Fixed-point math
/// toolkit") for the full writeup, and docs/herculan/planning.md's "Math" decision for why the
/// engine ports these rather than using floating-point System.Numerics throughout.
///
/// These are deliberately literal translations, quantization and clamping included — a naive
/// float reimplementation drifts from the original's behavior even when it looks equivalent on
/// paper. Rendering is free to convert to float at its own boundary (see Render/), but nothing
/// that feeds back into simulation state should.
///
/// The namespace is Herculan.Engine.Numerics rather than ...Math on purpose: a namespace whose
/// last segment is "Math" shadows System.Math for every file that imports it.
///
/// Ghidra symbol names (project ES2Recon, DBSIM.EXE) are given per method so a future session can
/// re-check a translation against the disassembly without re-deriving which function it came from.
/// </summary>
public static class SimMath {
	/// <summary>
	/// The global simulation timestep — DBSIM's <c>SimTickDelta</c> (<c>004d3be8</c>), read by
	/// <see cref="IntegrateRateOverTick"/> and <see cref="CountdownTimerTick"/>. In DBSIM this is
	/// refreshed once per frame from a hardware timer by the per-frame sim tick
	/// (<c>Sim_MainTick</c>, <c>0045f464</c>); here the sim loop owns it the same way (see <c>Sim/SimWorld</c>).
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
	/// <c>Math_Q8Multiply</c> (<c>0047df94</c>) — Q8 fixed-point multiply: <c>(int64)a * b >> 8</c>.
	/// The original's <c>SHRD EAX,EDX,0x8</c> decompiles as a low-word/high-word recombination;
	/// taking the low 32 bits of the 64-bit shifted product is the same value.
	/// </summary>
	public static int Q8Multiply(int a, int b) => (int)(((long)a * b) >> 8);

	/// <summary>
	/// Q10 sibling of <see cref="Q8Multiply"/> — same <c>IMUL</c>+<c>SHRD</c> shape, shift
	/// <c>0xa</c>, sitting immediately adjacent in the binary. Its domain is the simulation's
	/// normalized scalars: throttle and speed (<c>Mech_GetSpeed</c>, <c>00415498</c>, is
	/// <c>Q10(2000, mech+0x28e)</c>), reactor and shield rates, and a shot's damage against its
	/// capacitor charge. See docs/retail/simulation/dbsim-physics-notes.md, "Fixed-point math toolkit".
	/// </summary>
	public static int Q10Multiply(int a, int b) => (int)(((long)a * b) >> 10);

	/// <summary>
	/// <c>Math_Q16Multiply</c> (<c>0047df81</c>) — Q16 fixed-point multiply: <c>(int64)a*b >> 16</c>.
	/// Pairs with <see cref="Q16Divide"/> to apply a ratio built from two integers; that is exactly
	/// what <c>MechType_InitOne</c> does to rescale a mech type's speed fields at load
	/// (see <see cref="Sim.MechTypeRecord"/>).
	/// </summary>
	/// <summary>
	/// <c>Math_MapRange</c> (<c>0047de3c</c>) — a linear remap of <paramref name="value"/> from
	/// <c>[fromLow, fromHigh]</c> onto <c>[toLow, toHigh]</c>, with a rounding bias of one less than
	/// the input span, signed to match the output's direction.
	/// </summary>
	public static int MapRange(int value, int fromLow, int fromHigh, int toLow, int toHigh) {
		int span = fromHigh - fromLow;
		long bias = System.Math.Abs(span) - 1;
		if (toHigh < toLow) {
			bias = -bias;
		}

		return (int)(((long)(value - fromLow) * (toHigh - toLow) + bias) / span) + toLow;
	}

	public static int Q16Multiply(int a, int b) => (int)(((long)a * b) >> 16);

	/// <summary>
	/// <c>Math_Q16Divide</c> (<c>0047df5c</c>) — Q16 fixed-point divide: <c>((int64)a &lt;&lt; 16) / b</c>.
	/// </summary>
	public static int Q16Divide(int a, int b) => (int)(((long)a << 16) / b);

	/// <summary>
	/// Q14 sibling of <see cref="Q8Multiply"/> — shift <c>0xe</c> with a 16-bit signed operand.
	/// <c>Math_Q14Multiply</c> (<c>0047dfb4</c>) is the scale of the original's sine/cosine table at
	/// <c>004a25dc</c>, whose entries run -1.0..1.0. See <see cref="BinaryAngle"/> for the engine's
	/// trig, which uses this scale.
	/// </summary>
	public static int Q14Multiply(int a, short b) => (int)(((long)a * b) >> 14);

	/// <summary>
	/// <c>Math_IntegrateRateOverTick</c> (<c>00467820</c>) — "apply a per-unit-time rate as this
	/// tick's delta": <c>Q8Multiply(TickDelta, rate)</c>, clamped to signed 16-bit range. Called on
	/// velocity- and acceleration-like fields to get a position or speed delta.
	/// </summary>
	public static int IntegrateRateOverTick(short rate) {
		int value = Q8Multiply(TickDelta, rate);
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
	/// pins <see cref="TickDelta"/> to (see <c>Sim/SimWorld</c>).
	/// </summary>
	public const short VanillaTickDelta = 81;

	/// <summary>
	/// Whether <see cref="ScalePerTickStep"/> rescales at all. On by default. A replayed tape turns it
	/// off: its ticks run at the recording machine's own deltas, and matching the recording means
	/// applying each step once per frame whatever that frame's length, as the original did.
	/// </summary>
	public static bool PerTickStepsScaled { get; set; } = true;

	/// <summary>
	/// <b>Not a DBSIM function — a deliberate deviation.</b> Converts a constant the original
	/// applied as a raw per-tick step into this tick's equivalent:
	/// <c>step * TickDelta / VanillaTickDelta</c>.
	///
	/// <para>DBSIM feeds a handful of constants — the locomotion accel/decel pair, notably —
	/// straight into <see cref="RateLimitedMoveToward"/> without routing them through
	/// <see cref="IntegrateRateOverTick"/>, so their ramps take a fixed number of <i>ticks</i>
	/// rather than a fixed amount of <i>time</i>. On the original that made acceleration and turn
	/// ramp frame-rate dependent; here it would tie them to the engine's chosen tick rate. This
	/// rescales them to be tick-rate independent instead.</para>
	///
	/// <para>It is exact at <see cref="VanillaTickDelta"/>, which is what the engine's fixed
	/// timestep runs at, so it changes nothing today. <see cref="IntegrateRateOverTick"/> cannot be
	/// used directly for this: its Q8 unit is 125 ms, so it would return about a third of the step
	/// and visibly slacken the ramp.</para>
	///
	/// <para>Steps are slew rates and non-negative. A non-zero step never scales to zero — at a
	/// tick short enough to round it away the result is pinned to 1, so a ramp cannot stall
	/// outright. Below the vanilla tick length that pin is the quantization floor of a fixed-point
	/// port with no fractional carry, and the accel constants would want re-checking there.</para>
	///
	/// <para>Off while <see cref="PerTickStepsScaled"/> is false, which is how a replayed tape gets
	/// the original's own frame-rate-dependent ramps back.</para>
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

	/// <summary>
	/// <c>Math_RateLimitedMoveToward</c> (<c>004679d8</c>) — generic per-tick slew-rate limiter.
	/// Moves <paramref name="current"/> toward <paramref name="target"/> by at most
	/// <paramref name="step"/> (never overshooting), and returns the remaining error — 0 once it
	/// has arrived. Used by rocket guidance's turn-rate cap and by the shield recharge tick.
	/// </summary>
	public static short RateLimitedMoveToward(ref short current, short target, short step) {
		if (target < current) {
			current = (short)(current - step);
			if (target < current) {
				return (short)(current - target);
			}
			current = target;
		} else if (current < target) {
			current = (short)(current + step);
			if (current < target) {
				return (short)(target - current);
			}
			current = target;
		}
		return 0;
	}

	/// <summary>
	/// <c>Math_FastMagnitude3D</c> (<c>0047dd66</c>) — sqrt-free 3D magnitude approximation.
	/// Sorts <c>|dx|,|dy|,|dz|</c> into largest/mid/smallest and returns
	/// <c>L + M*0.34375 + S*0.25</c> (an alpha-max-plus-beta-min-style approximation). Its error is
	/// direction-dependent: about 8% low on the body diagonal, up to about 8.7% high elsewhere.
	///
	/// This is a general math-library utility in the original, not something purpose-built for one
	/// subsystem — collision bounding-sphere radii, sound placement and the rocket proximity beep all
	/// use it — so reproducing hit detection faithfully means using this rather than substituting a
	/// real sqrt.
	/// The branch structure below is transcribed from the decompilation as-is rather than rewritten
	/// as a clean three-way sort, to keep it checkable against the disassembly.
	/// </summary>
	public static int FastMagnitude3D(int dx, int dy, int dz) {
		int ax = Abs(dx);
		int ay = Abs(dy);
		int az = Abs(dz);

		// maxXy/mid start as the sorted pair of |dx|,|dy|; |dz| is then folded in, after which
		// large/mid/small hold the full L >= M >= S ordering.
		int maxXy = ax;
		int mid = ay;
		if (ax < ay) {
			maxXy = ay;
			mid = ax;
		}

		int small = az;
		int large = maxXy;
		if (mid < az) {
			small = mid;
			mid = az;
			if (maxXy < az) {
				large = az;
				mid = maxXy;
			}
		}

		return (small >> 2) + large + (mid >> 2) + (mid >> 4) + (mid >> 5);
	}

	/// <summary>
	/// <c>Math_ISqrt</c> (<c>0047de90</c>) — <c>floor(sqrt(n))</c>, the exact integer root the
	/// original takes bit by bit. <c>Point3I_ExactMagnitude</c> (<c>0047e958</c>) is it over a
	/// summed-squares, which <c>Plane_FromPointNormal</c> uses for a short normal instead of
	/// <see cref="FastMagnitude3D"/>. A double's square root of a 32-bit integer floors to the same
	/// value, so no loop is needed to get it.
	/// </summary>
	public static int ISqrt(uint n) => (int)System.Math.Sqrt(n);

	/// <summary>
	/// <c>Math_FastMagnitude2D</c> (<c>0047dd40</c>) — the 2D counterpart of
	/// <see cref="FastMagnitude3D"/>: <c>max + min/2</c>, the octagonal estimate. Exact on axis, up
	/// to ~11.8% high on a diagonal. This is the distance the HUD's own range readouts display, so
	/// the error is part of the original's on-screen behavior.
	/// </summary>
	public static int FastMagnitude2D(int dx, int dy) {
		int ax = Abs(dx);
		int ay = Abs(dy);
		return ay <= ax ? (ay >> 1) + ax : ay + (ax >> 1);
	}

	/// <summary>
	/// <c>Math_NormalizeVec3ToLength</c> (<c>004926e4</c>) — rescales a vector in place to
	/// <paramref name="length"/>, which the original uses wherever it needs a step of a fixed size
	/// along an arbitrary direction (a beam chain's node spacing, a collision volume's face
	/// offsets).
	///
	/// <para>The scale factor is a Q16 fraction <b>narrowed to a signed 16-bit value</b> before it is
	/// applied. That truncation is the original's own, not a rounding choice here, and it is why a
	/// step over a very short delta comes out shorter than asked for.</para>
	/// </summary>
	public static void ScaleToLength(ref int x, ref int y, ref int z, short length) {
		int magnitude = FastMagnitude3D(x, y, z);
		if (magnitude == 0) {
			x = y = z = 0;
			return;
		}

		short scale = (short)Q16Divide(length, magnitude);
		x = Q16Multiply(x, scale);
		y = Q16Multiply(y, scale);
		z = Q16Multiply(z, scale);
	}

	/// <summary>
	/// The branchless absolute value the original compiles to (<c>(x ^ (x >> 31)) - (x >> 31)</c>).
	/// Used instead of <see cref="System.Math.Abs(int)"/> because that throws on
	/// <see cref="int.MinValue"/> where the original silently wraps — a faithful port shouldn't
	/// introduce an exception the game never had.
	/// </summary>
	private static int Abs(int value) {
		int sign = value >> 31;
		return (value ^ sign) - sign;
	}
}
