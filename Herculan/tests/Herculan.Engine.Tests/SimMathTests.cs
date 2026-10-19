using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Covers <see cref="SimMath.ScalePerTickStep"/>, the engine's one deliberate departure from
/// DBSIM's locomotion timing: the accel/decel constants are raw per-tick steps in the original and
/// are rescaled by tick length here, so that a HERC's acceleration and turn ramp depend on elapsed
/// time rather than on <see cref="SimWorld.TicksPerSecond"/>.
///
/// <para>These tests write <see cref="SimMath.TickDelta"/>, which is global sim state that
/// <see cref="SimWorld.Tick"/> also writes — hence the shared collection with the other tests that
/// run a world, so they never interleave.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public class SimMathTests {
	[Fact]
	public void ScalePerTickStepIsExactAtTheVanillaTick() {
		WithTickDelta(SimMath.VanillaTickDelta, () => Assert.Equal(3000, SimMath.ScalePerTickStep(3000)));
	}

	[Fact]
	public void ScalePerTickStepTracksTheTickLength() {
		// Twice the tick length covers twice the ramp.
		WithTickDelta((short)(SimMath.VanillaTickDelta * 2), () => Assert.Equal(6000, SimMath.ScalePerTickStep(3000)));
	}

	/// <summary>
	/// A tick of 40 against the vanilla 81 covers a little under half the ramp, truncated: 6 × 40 / 81
	/// is 2.96 and 200 × 40 / 81 is 98.8, which rounding would carry up to 3 and 99.
	/// </summary>
	[Theory]
	[InlineData(6, 2)]
	[InlineData(200, 98)]
	public void ScalePerTickStepTruncatesAShorterTick(int step, int expected) {
		WithTickDelta(40, () => Assert.Equal(expected, SimMath.ScalePerTickStep((short)step)));
	}

	[Fact]
	public void ScalePerTickStepNeverStallsARamp() {
		// A tick short enough to round a small step away pins it to 1 rather than to zero, so the
		// ramp still makes progress. This is the quantization floor, not an intended rate.
		WithTickDelta(8, () => Assert.Equal(1, SimMath.ScalePerTickStep(1)));
	}

	[Fact]
	public void ScalePerTickStepLeavesNonPositiveStepsAlone() {
		WithTickDelta(8, () => Assert.Equal(0, SimMath.ScalePerTickStep(0)));
	}

	/// <summary>
	/// ES1's rounded Q14 multiply, hand-worked from its disassembly (<c>ES1DBSIM 0005e838</c>). Catches the
	/// overflow branch dropped for a plain <c>+ 0x2000</c>: 0x10000000 × 16 is 2³², which overflows, so it
	/// gains 0xffffdfff and shifts to 524287 where a rounding would give 262144.
	/// </summary>
	[Theory]
	[InlineData(0x10000000, 16, 524287)]
	[InlineData(3, 0x2000, 2)] // 1.5 rounds up.
	[InlineData(-3, 0x2000, -1)] // −1.5 rounds up too: the half is added, not taken toward zero.
	public void Q14MultiplyRoundedFollowsEs1sAdd(int a, short b, int expected) {
		Assert.Equal(expected, SimMath.Q14MultiplyRounded(a, b));
	}

	/// <summary>
	/// ES1's rounded Q8 and Q10 multiplies, hand-worked: 0x10000000 × 16 is 2³² and 0x10000000 × 64 is 2³⁴, which
	/// overflow, so they gain <c>0xffffff7f</c> and <c>0xfffffdff</c> before the shift. Catches either left on a plain
	/// half-unit add (as the Q10 multiply was), or the complement sign-extended rather than added unsigned (which gives
	/// the rounding's 2²⁴ less one).
	/// </summary>
	[Fact]
	public void Q8AndQ10MultiplyRoundedAddTheComplementUnsignedOnOverflow() {
		Assert.Equal(0x1ffffff, SimMath.Q8MultiplyRounded(0x10000000, 16));
		Assert.Equal(0x13fffff, SimMath.Q10MultiplyRounded(0x10000000, 64));
		Assert.Equal(2, SimMath.Q10MultiplyRounded(3, 0x200)); // 1.5 rounds up.
	}

	/// <summary>
	/// ES1's rounded Q16 divide, hand-worked: the magnitudes are divided and rounded half away from zero, then the
	/// sign restored. Catches a truncating divide (−43690 for 2 / −3), and a signed rounding such as
	/// <c>((a &lt;&lt; 16) + b / 2) / b</c>, which takes −0.5 to 0.
	/// </summary>
	[Theory]
	[InlineData(1, 3, 21845)] // 0.333 rounds down: the remainder 1 is under 2, three halved and rounded up.
	[InlineData(2, 3, 43691)]
	[InlineData(2, -3, -43691)]
	[InlineData(-2, -3, 43691)]
	[InlineData(-1, 0x20000, -1)] // −0.5
	public void Q16DivideRoundedRoundsTheMagnitude(int a, int b, int expected) {
		Assert.Equal(expected, SimMath.Q16DivideRounded(a, b));
	}

	private static void WithTickDelta(short delta, System.Action body) {
		short previous = SimMath.TickDelta;
		SimMath.TickDelta = delta;
		try {
			body();
		} finally {
			SimMath.TickDelta = previous;
		}
	}
}

/// <summary>
/// Serializes every test class that reads or writes <see cref="SimMath.TickDelta"/>. xUnit runs
/// separate classes in parallel by default, and that global is shared.
/// </summary>
[CollectionDefinition(Name)]
public class SimTimestepCollection {
	public const string Name = "sim timestep";
}
