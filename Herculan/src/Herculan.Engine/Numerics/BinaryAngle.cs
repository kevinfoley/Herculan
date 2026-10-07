namespace Herculan.Engine.Numerics;

/// <summary>
/// Binary angle measure (BAM) helpers — the angle representation DBSIM uses throughout: a full
/// turn is 0x10000, so a <see cref="ushort"/> covers the circle exactly and wraps for free.
/// The scale is confirmed from the damage system, where a mech classifies a hit as front vs. rear
/// by testing the angular difference against <c>0x4000</c>, described in the disassembly notes as
/// ±90° (see docs/retail/simulation/damage-system.md, "Explosive damage").
///
/// <para><b>This is not the vanilla table — <see cref="SimTrig"/> is.</b> DBSIM's own trigonometry
/// tables have been located and verified entry-by-entry, and <see cref="SimTrig"/> reproduces them,
/// including their deliberate coarseness (one cosine entry per 16 BAM). The table here is a
/// full-resolution Q14 one generated from double-precision <see cref="System.Math.Sin"/> — finer
/// than the original's, not a port of it.</para>
///
/// <para>So: anything whose result feeds back into simulation state — object and node transforms,
/// heading integration, anything that must match the original tick for tick — belongs on
/// <see cref="SimTrig"/>, because reproducing the original's quantization is the point. This type is
/// for engine-side work with no vanilla counterpart to match, such as camera aiming, where the extra
/// resolution is harmless.</para>
/// </summary>
public static class BinaryAngle {
	/// <summary>One full turn. BAM arithmetic wraps modulo this by construction.</summary>
	public const int FullTurn = 0x10000;

	/// <summary>A quarter turn (90°) — the front/rear threshold the damage system tests against.</summary>
	public const int QuarterTurn = 0x4000;

	/// <summary>A half turn (180°).</summary>
	public const int HalfTurn = 0x8000;

	/// <summary>Fixed-point scale of <see cref="Sin"/>/<see cref="Cos"/> outputs: 1.0 == 0x4000.</summary>
	public const int TrigOne = 1 << 14;

	// One entry per representable angle. 128 KB of shorts, built once — small enough that a
	// quarter-wave table plus symmetry folding would only add branching to the hot path for no
	// practical saving, and a full table keeps the lookup a single array index.
	private static readonly short[] SineTable = BuildSineTable();

	private static short[] BuildSineTable() {
		var table = new short[FullTurn];
		for (int i = 0; i < FullTurn; i++) {
			double radians = i * (2.0 * System.Math.PI / FullTurn);
			table[i] = (short)System.Math.Round(System.Math.Sin(radians) * TrigOne);
		}
		return table;
	}

	/// <summary>Sine of a BAM angle, in Q14 (0x4000 == 1.0).</summary>
	public static short Sin(int angle) => SineTable[angle & 0xffff];

	/// <summary>Cosine of a BAM angle, in Q14 (0x4000 == 1.0).</summary>
	public static short Cos(int angle) => SineTable[(angle + QuarterTurn) & 0xffff];

	/// <summary>
	/// Shortest signed difference <c>from -> to</c>, in the range [-0x8000, 0x8000). Turning
	/// toward a heading means driving this toward zero — the value guidance code feeds to
	/// <see cref="SimMath.RateLimitedMoveToward"/> as its error term.
	/// </summary>
	public static int Delta(int from, int to) => (short)((to - from) & 0xffff);

	/// <summary>Converts a BAM angle to radians, for the render/camera boundary only.</summary>
	public static float ToRadians(int angle) => (angle & 0xffff) * (2f * MathF.PI / FullTurn);

	/// <summary>Converts radians to a BAM angle, for the render/camera boundary only.</summary>
	public static int FromRadians(float radians) =>
		(int)System.Math.Round(radians * (FullTurn / (2.0 * System.Math.PI))) & 0xffff;
}
