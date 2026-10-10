using Herculan.Engine.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The shield gauge's two numbers: retail's truncated readout, which a forward press from centre
/// leaves at 119/81, and the rounding tweak, which reads every reachable balance as a multiple of 20.
/// See docs/retail/simulation/cockpit-hud-widgets.md, "Readouts".
/// </summary>
public class ShieldReadoutTests {
	/// <summary>
	/// Retail's ladder from the power-up centre: forward stops fall just short of their multiple of 20
	/// and truncate a point low, rear stops fall just past it and read clean.
	/// </summary>
	[Theory]
	[InlineData(1, 119, 81)]
	[InlineData(5, 199, 1)]
	[InlineData(-1, 80, 120)]
	[InlineData(-5, 0, 200)]
	public void RetailTruncatesTheFrontNumber(int presses, int front, int rear) {
		var shields = Press(new ShieldCharge(3500), presses);

		Assert.Equal((front, rear), shields.Readout(rounded: false));
	}

	/// <summary>
	/// The tweak reads a multiple of 20 on every stop the keys reach: out from the centre, against
	/// either clamped end, and back from each end, where the steps sit on a different ladder. A run
	/// back from an end drifts further than half a point, so rounding to the nearest point would fail
	/// here (310, seven steps back from full forward, would read 61).
	/// </summary>
	[Fact]
	public void TheTweakReadsEvenStepsEverywhereTheKeysReach() {
		var shields = new ShieldCharge(3500);
		int[] runs = { 6, -12, 12, -6 };

		foreach (int run in runs) {
			for (int i = 0; i < Math.Abs(run); i++) {
				shields.AdjustBalance(towardFront: run > 0);
				var (front, rear) = shields.Readout(rounded: true);

				Assert.Equal(0, front % 20);
				Assert.Equal(200, front + rear);
			}
		}
	}

	/// <summary>One forward press reads 120/80 with the tweak, where retail reads 119/81.</summary>
	[Fact]
	public void TheTweakReadsAForwardPressAsTwenty() {
		var shields = Press(new ShieldCharge(3500), 1);

		Assert.Equal((120, 80), shields.Readout(rounded: true));
	}

	private static ShieldCharge Press(ShieldCharge shields, int presses) {
		for (int i = 0; i < Math.Abs(presses); i++) {
			shields.AdjustBalance(towardFront: presses > 0);
		}

		return shields;
	}
}
