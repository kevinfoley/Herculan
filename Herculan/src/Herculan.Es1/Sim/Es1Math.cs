namespace Herculan.Es1.Sim;

/// <summary>ES1's fixed-point primitives where they differ from ES2's (docs/es1/simulation/fixed-point-math.md#the-primitives).</summary>
public static class Es1Math {
	/// <summary>ES1's <c>Math_Q10Multiply</c>, which rounds where ES2's truncates.</summary>
	public static int Q10Multiply(int a, int b) => (int)(((long)a * b + 0x200) >> 10);
}
