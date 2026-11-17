namespace Herculan.Engine.Numerics;

/// <summary>
/// Direct port of DBSIM.EXE's shared fixed-point math toolkit — the primitives every other
/// simulation subsystem builds on. Each method below corresponds 1:1 to a specific
/// reverse-engineered function; see docs/retail/simulation/dbsim-physics-notes.md ("Fixed-point math
/// toolkit") for the full writeup, and docs/herculan/planning.md's "Math" decision for why the
/// engine ports these rather than using floating-point System.Numerics throughout.
///
/// Nothing here reads the tick length. Each game's clock owns its tick and the helpers that scale by it:
/// ES2's <c>SimClock</c> and ES1's <c>Es1SimClock</c>.
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
	/// what <c>MechType_InitOne</c> does to rescale a mech type's speed fields at load.
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
	/// ES1's <c>Math_Q14Multiply</c> (<c>ES1DBSIM 0005e838</c>), which rounds to nearest where
	/// <see cref="Q14Multiply"/> truncates: <c>0x2000</c> added to the 64-bit product before the shift, or
	/// <c>0xffffdfff</c>, unsigned, when the product overflows 32 bits. See
	/// docs/es1/simulation/fixed-point-math.md, "The primitives".
	/// </summary>
	public static int Q14MultiplyRounded(int a, short b) => RoundedShift((long)a * b, 14);

	/// <summary>
	/// ES1's <c>Math_Q8Multiply</c> (<c>ES1DBSIM 0005e7f8</c>), which rounds where <see cref="Q8Multiply"/> truncates, and
	/// takes its second operand as 16 bits (<c>MOVSX ECX,word ptr [EBP+0xc]</c>): <c>0x80</c> added to the 64-bit product
	/// before the shift, or <c>0xffffff7f</c>, unsigned, when the product overflows 32 bits. See
	/// docs/es1/simulation/fixed-point-math.md, "The primitives".
	/// </summary>
	public static int Q8MultiplyRounded(int a, short b) => RoundedShift((long)a * b, 8);

	/// <summary>
	/// ES1's <c>Math_Q10Multiply</c> (<c>ES1DBSIM 0005e818</c>), which rounds where <see cref="Q10Multiply"/> truncates,
	/// and takes its second operand as 16 bits (<c>MOVSX ECX,word ptr [EBP+0xc]</c>): <c>0x200</c> added to the 64-bit
	/// product before the shift, or <c>0xfffffdff</c>, unsigned, when the product overflows 32 bits. See
	/// docs/es1/simulation/fixed-point-math.md, "The primitives".
	/// </summary>
	public static int Q10MultiplyRounded(int a, short b) => RoundedShift((long)a * b, 10);

	/// <summary>
	/// ES1's <c>Math_Q16Multiply</c> (<c>ES1DBSIM 00073574</c>) and its copy <c>Math_Q16Multiply2</c> (<c>0007358c</c>),
	/// which round where <see cref="Q16Multiply"/> truncates: <c>0x8000</c> added to the 64-bit product before the shift,
	/// with no overflow branch. See docs/es1/simulation/fixed-point-math.md, "The primitives".
	/// </summary>
	public static int Q16MultiplyRounded(int a, int b) => (int)(((long)a * b + 0x8000) >> 16);

	/// <summary>
	/// ES1's <c>Math_Q16Divide</c> (<c>ES1DBSIM 00073534</c>), which rounds where <see cref="Q16Divide"/> truncates:
	/// <c>|a| &lt;&lt; 16</c> divided unsigned by <c>|b|</c>, 1 added when the remainder is at least <c>|b|</c> halved
	/// and rounded up, the sign restored when exactly one operand is negative. Retail's <c>DIV</c> faults on a quotient
	/// past 32 bits; this keeps its low 32 bits, as <see cref="Q16Divide"/> does. See
	/// docs/es1/simulation/fixed-point-math.md, "The primitives".
	/// </summary>
	public static int Q16DivideRounded(int a, int b) {
		bool negative = (a < 0) != (b < 0);
		ulong dividend = (ulong)(uint)Abs(a) << 16;
		uint divisor = (uint)Abs(b);
		uint quotient = (uint)(dividend / divisor);
		uint halfLessOne = (divisor >> 1) + (divisor & 1) - 1;
		if (halfLessOne < dividend % divisor) {
			quotient++;
		}

		return negative ? (int)(0 - quotient) : (int)quotient;
	}

	/// <summary>
	/// The rounding of ES1's Q8, Q10 and Q14 multiplies: <c>SBB ECX,ECX</c> / <c>XOR ECX,half</c> makes the half unit
	/// when <c>IMUL</c> leaves the product in 32 bits and its complement when it does not, and <c>ADD EAX,ECX</c> /
	/// <c>ADC EDX,0</c> adds that unsigned, so an overflowing product gains almost 2³² before the shift.
	/// </summary>
	private static int RoundedShift(long product, int shift) {
		uint half = 1u << (shift - 1);
		uint add = product == (int)product ? half : ~half;
		return (int)((product + add) >> shift);
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
