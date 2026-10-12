namespace Herculan.Es1.Scene;

/// <summary>ES1's fixed-point helpers where they differ from ES2's (docs/es1/simulation/fixed-point-math.md).</summary>
internal static class Es1Math {
	/// <summary>ES1's <c>Math_Q14Multiply</c> (<c>0005e838</c>), which rounds to nearest where ES2's truncates.</summary>
	public static int Q14Multiply(int a, short b) => (int)(((long)a * b + 0x2000) >> 14);
}
