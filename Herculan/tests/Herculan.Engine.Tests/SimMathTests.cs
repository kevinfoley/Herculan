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
