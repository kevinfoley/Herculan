namespace Herculan.Engine.Numerics;

/// <summary>
/// DBSIM's own trigonometry tables, ported. These are the ones the rotation-matrix and
/// euler-extraction code uses (<c>BuildEulerRotationMatrixQ14</c> <c>0047eaac</c>,
/// <c>Math_Atan2Bam</c> (<c>0047d220</c>), <c>Math_AsinBam</c> (<c>0047d940</c>)) and they are deliberately <i>coarser</i> than
/// <see cref="BinaryAngle"/>: the cosine table has one entry per 16 BAM, so a matrix built from an
/// angle quantizes it to 1/4096 of a turn. Reproducing that quantization is the point — it is what
/// the original's animation and object transforms actually see.
///
/// <para><b>Verified against DBSIM.EXE's own tables</b>, by locating each table in the
/// retail binary and comparing every entry against the value generated here:</para>
/// <list type="bullet">
/// <item><c>DAT_004a25dc</c> quarter-wave cosine, 1024 entries — 1020 exact, 4 differ by one Q14
/// unit (0.006%). The four all round up from a fraction below .5, so the original's generator was
/// marginally less precise than <see cref="System.Math.Cos"/>; no rounding rule reproduces them.</item>
/// <item><c>DAT_004a1c4c</c> arctangent correction bytes, 513 entries — <b>all exact</b>.</item>
/// <item><c>DAT_004a1e54</c> coarse arcsine, 448 entries — <b>all exact</b> (truncated, not rounded).</item>
/// <item><c>DAT_004a05d4</c> fine arcsine, 513 entries — <b>all exact</b> (truncated).</item>
/// </list>
/// <para>The four tables sit back to back in <c>.data</c> — fine arcsine ends where the coarse one
/// begins and the cosine table starts eight bytes later — which independently confirms each one's
/// entry count and index range.</para>
/// </summary>
public static class SimTrig {
	/// <summary>Fixed-point scale of <see cref="Cos"/>/<see cref="Sin"/>: 1.0 == 0x4000.</summary>
	public const int One = 1 << 14;

	/// <summary>Angle step between cosine-table entries, in BAM. A table index covers 16 BAM.</summary>
	private const int CosineStepShift = 4;

	/// <summary>Quarter turn in table indices — the table's length.</summary>
	private const int QuarterTurnIndices = 0x400;

	// cos(i * 2pi / 4096) in Q14, one entry per 16 BAM over a quarter turn. See the type summary
	// for how this compares against the retail binary's own copy.
	private static readonly short[] Cosine = BuildCosine();

	// atan(i / 512) in units of 1/0x4000 turn, stored as the correction to add to i*4 -- exactly the
	// form DBSIM stores it in, which is why it fits in a byte.
	private static readonly byte[] ArctangentCorrection = BuildArctangentCorrection();

	// asin over |v| in Q12, split into a coarse table (step 8) below 0xe00 and a fine one (step 1)
	// above it, where the curve steepens. Both truncate rather than round.
	private static readonly ushort[] ArcsineCoarse = BuildArcsine(0, 0x1c1, 8);
	private static readonly ushort[] ArcsineFine = BuildArcsine(FineArcsineBase, 0x201, 1);

	/// <summary>First index the fine arcsine table covers; below it the coarse table is used.</summary>
	private const int FineArcsineBase = 0xe00;

	private static short[] BuildCosine() {
		// One entry past the quarter turn: a half-turn angle folds to index 0x400, which the retail
		// table also carries (it reads 0, and the several words after it are zero too).
		var table = new short[QuarterTurnIndices + 1];
		for (int i = 0; i < table.Length; i++) {
			table[i] = (short)System.Math.Floor(
				System.Math.Cos(i * (2.0 * System.Math.PI / (QuarterTurnIndices * 4))) * One + 0.5);
		}
		return table;
	}

	private static byte[] BuildArctangentCorrection() {
		var table = new byte[513];
		for (int i = 0; i < table.Length; i++) {
			double angle = System.Math.Atan(i / 512.0) * (0x4000 / (2.0 * System.Math.PI));
			table[i] = (byte)(System.Math.Floor(angle + 0.5) - i * 4);
		}
		return table;
	}

	private static ushort[] BuildArcsine(int firstIndex, int count, int step) {
		var table = new ushort[count];
		for (int i = 0; i < count; i++) {
			double sine = System.Math.Min((firstIndex + i * step) / 4096.0, 1.0);
			table[i] = (ushort)System.Math.Floor(
				System.Math.Asin(sine) * (BinaryAngle.FullTurn / (2.0 * System.Math.PI)));
		}
		return table;
	}

	/// <summary>
	/// Cosine of a BAM angle in Q14, quantized to the table's 16-BAM step. This is the lookup
	/// <c>BuildEulerRotationMatrixQ14</c> performs inline at every one of its trig sites.
	/// </summary>
	public static short Cos(short angle) {
		// Round the angle to the nearest table step rather than truncating -- the original adds the
		// dropped bit 3 back in before taking the absolute value.
		int index = (angle >> CosineStepShift) + ((angle >> (CosineStepShift - 1)) & 1);
		int sign = index >> 31;
		index = (index ^ sign) - sign;

		return index < QuarterTurnIndices
			? Cosine[index]
			: (short)-Cosine[QuarterTurnIndices * 2 - index];
	}

	/// <summary>Sine of a BAM angle in Q14 — the same table read a quarter turn earlier.</summary>
	public static short Sin(short angle) => Cos(unchecked((short)(angle - BinaryAngle.QuarterTurn)));

	/// <summary>
	/// <c>Math_Atan2Bam</c> (<c>0047d220</c>) — <c>atan2(y, x)</c> as a BAM angle. Octant-folded: the table covers
	/// 0..45° and the sign and magnitude comparisons place the result in the right octant.
	/// </summary>
	public static int Atan2(int y, int x) {
		int signX = x >> 31;
		int absX = (x ^ signX) - signX;
		int signY = y >> 31;
		int absY = (y ^ signY) - signY;

		uint angle = 0;
		if (absY != 0) {
			if (absX < absY) {
				int index = (int)(((long)absX * 0x200 + (absY >> 1)) / absY);
				int arc = index != 0 ? index * 4 + ArctangentCorrection[index] : 0;
				angle = (uint)-(arc - 0x1000);
			} else {
				int index = (int)(((long)absY * 0x200 + (absX >> 1)) / absX);
				angle = index != 0 ? (uint)(index * 4 + ArctangentCorrection[index]) : 0;
			}
		}

		if (signX < 0) {
			angle = (uint)-(int)(angle - 0x2000);
		}
		if (signY < 0) {
			angle = (uint)-(int)(angle - 0x4000);
		}

		// The whole computation runs in 14-bit angle units; the shift promotes it to BAM.
		return (int)((angle & 0x3fff) << 2);
	}

	/// <summary>
	/// <c>Math_AsinBam</c> (<c>0047d940</c>) — arcsine of a Q14 value, as a BAM angle. Input is a rotation-matrix
	/// element, so it is already clamped to ±1.0 by construction.
	/// </summary>
	public static int Asin(short value) {
		int sign = value >> 31;
		int magnitude = (value ^ sign) - sign;

		// Q14 down to the tables' Q12, rounded.
		int index = (magnitude >> 2) + ((magnitude >> 1) & 1);

		if (index < FineArcsineBase) {
			int coarse = (index >> 3) + ((index >> 2) & 1);
			return (short)((ArcsineCoarse[coarse] ^ sign) - sign);
		}

		int highSign = value >> 15;
		return (ushort)((ArcsineFine[index - FineArcsineBase] ^ highSign) - highSign);
	}

	/// <summary>
	/// <c>Math_EulerToward</c> (<c>00492884</c>) — the euler triple that points <paramref name="from"/>
	/// at <paramref name="to"/>. Roll is always zero; the yaw is the ground-plane bearing shifted back
	/// a quarter turn, because the sim's forward axis is model Y and not model X; the pitch is taken
	/// against the ground-plane distance with the simulation's own sqrt-free magnitude, so it carries
	/// the same few-percent bias every other range in the simulation does. Retail reading:
	/// docs/retail/simulation/dbsim-physics-notes.md, "Fixed-point math toolkit".
	///
	/// <para>Both guidance paths read it — the plasma round's (<c>Bullet_HomingSteer</c>) and a
	/// launcher's (<c>Rocket_HomingSteer</c>) — and both pass the target first and the shot second,
	/// which is the argument order the original's call sites use.</para>
	/// </summary>
	public static (short X, short Y, short Z) EulerToward(Vec3i from, Vec3i to) {
		int dx = from.X - to.X;
		int dy = from.Y - to.Y;
		int dz = from.Z - to.Z;

		// Both components go through Math_Atan2Guarded (00492800), whose first argument is the x of the atan2 and its
		// second the y — so the yaw is atan2(dy, dx) and the pitch is atan2(dz, groundDistance), an
		// elevation above the horizon. Passing these the other way round is not a sign error that
		// mostly works: it mirrors the bearing about the 45-degree line, and it turns a level target
		// into a quarter turn of pitch.
		short yaw = (short)(Atan2Guarded(dy, dx) - BinaryAngle.QuarterTurn);
		short pitch = (short)Atan2Guarded(dz, SimMath.FastMagnitude2D(dx, dy));
		return (pitch, 0, yaw);
	}

	/// <summary>
	/// <c>Math_Atan2Guarded</c> (<c>00492800</c>): <see cref="Atan2"/> with the degenerate pair nudged onto the axis rather
	/// than left at the origin, so a shot sitting exactly on its target still has a bearing.
	///
	/// <para>The nudge lands on <paramref name="x"/>, which is the original's own choice — it takes
	/// its arguments as <c>(x, y)</c> and sets the <i>first</i> to 1 when both are zero, so a
	/// coincident pair reads as a bearing along +x rather than +y.</para>
	/// </summary>
	public static int Atan2Guarded(int y, int x) => Atan2(y, y == 0 && x == 0 ? 1 : x);

	/// <summary>
	/// <c>Math_OffsetPointByBearing</c> (<c>004928f0</c>) — moves a point <paramref name="distance"/>
	/// along a bearing on the ground plane, the bearing quarter-turned back because the simulation's
	/// forward axis is model Y. Z is untouched.
	///
	/// <para><b>The offset is 16-bit.</b> The distance is cut to its low word and each product to a
	/// <c>short</c>, so a distance past ±32767 wraps — see
	/// docs/retail/simulation/ground-vehicles.md#the-control-law--0046a798-and-0046a854 for the
	/// caller that depends on it.</para>
	/// </summary>
	public static Vec3i OffsetPointByBearing(Vec3i point, short bearing, int distance) {
		short turned = (short)(bearing + BinaryAngle.QuarterTurn);
		short length = unchecked((short)distance);
		return new Vec3i(
			point.X + unchecked((short)SimMath.Q14Multiply(length, Cos(turned))),
			point.Y + unchecked((short)SimMath.Q14Multiply(length, Sin(turned))),
			point.Z);
	}
}
