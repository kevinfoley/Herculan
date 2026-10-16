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
	public static TheoryData<short> Steps => new(1, 3, 25, 150, 3000);

	[Theory]
	[MemberData(nameof(Steps))]
	public void ScalePerTickStepIsExactAtTheVanillaTick(short step) {
		WithTickDelta(SimMath.VanillaTickDelta, () => Assert.Equal(step, SimMath.ScalePerTickStep(step)));
	}

	[Theory]
	[MemberData(nameof(Steps))]
	public void ScalePerTickStepTracksTheTickLength(short step) {
		// Twice the tick length covers twice the ramp.
		WithTickDelta((short)(SimMath.VanillaTickDelta * 2),
			() => Assert.Equal(step * 2, SimMath.ScalePerTickStep(step)));
	}

	/// <summary>A tick of 40 against the vanilla 81 covers a little under half the ramp, truncated.</summary>
	[Theory]
	[InlineData(3, 1)]
	[InlineData(25, 12)]
	[InlineData(150, 74)]
	[InlineData(3000, 1481)]
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
