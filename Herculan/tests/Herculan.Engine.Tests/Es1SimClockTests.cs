using Herculan.Es1.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

public class Es1SimClockTests {
	/// <summary>
	/// No tick at two interrupts, one at the third. Catches the wait put back to retail's two interrupts, or its
	/// comparison off by one.
	/// </summary>
	[Fact]
	public void FirstTickIsDueAtThreeInterruptsNotTwo() {
		var clock = new Es1SimClock();

		Assert.False(clock.AdvanceHostFrame(Interrupts(clock, 2)));
		Assert.True(clock.AdvanceHostFrame(Interrupts(clock, 1)));
		Assert.Equal(3, clock.ElapsedInterrupts);
	}

	/// <summary>
	/// The deltas are <c>(e × 256) / 12</c> held to <c>0x40</c>–<c>0x1c2</c>, hand-worked; past a second the countdown
	/// has stopped at 0, so a tick measures a second. Catches the unit taken as ES2's 125 ms, the arguments of the
	/// conversion swapped, the clamp's bounds changed, and a countdown that runs below 0.
	/// </summary>
	[Theory]
	[InlineData(3, 64, 3)]
	[InlineData(21, 448, 21)]
	[InlineData(22, 0x1c2, 22)]
	[InlineData(200, 0x1c2, 120)]
	public void DeltaIsTheElapsedInterruptsInTenthsOfASecondClamped(int interrupts, int delta, int measured) {
		var clock = new Es1SimClock();

		Assert.True(clock.AdvanceHostFrame(Interrupts(clock, interrupts)));
		Assert.Equal(delta, clock.TickDelta);
		Assert.Equal(delta, clock.TickDelta2);
		Assert.Equal(measured, clock.ElapsedInterrupts);
	}

	/// <summary>
	/// Host frames of 2.5 ms, 0.3 of an interrupt each, reach three interrupts on the tenth. Catches the fraction of an
	/// interrupt dropped at each host frame (no tick ever) or rounded up (a tick on the third).
	/// </summary>
	[Fact]
	public void FractionOfAnInterruptCarriesAcrossHostFrames() {
		var clock = new Es1SimClock();
		var frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 400);

		for (int i = 0; i < 9; i++) {
			Assert.False(clock.AdvanceHostFrame(frame));
		}
		Assert.True(clock.AdvanceHostFrame(frame));
		Assert.Equal(3, clock.ElapsedInterrupts);
	}

	/// <summary>
	/// At the longest tick, <c>0x1c2</c>, a rate of <c>0x7fff</c> makes 57598, which ES1's helper sign-extends from 16
	/// bits to −7938 where ES2's saturates at <c>0x7fff</c>; at 1.0, −0x8000 stays itself and is held to −0x7fff.
	/// Catches ES2's saturation of the 32-bit product, and the clamp dropped after the sign extension.
	/// </summary>
	[Fact]
	public void IntegrateRateOverTickWrapsAnOutOfRangeProduct() {
		var longest = new Es1SimClock();
		longest.AdvanceHostFrame(Interrupts(longest, 22));

		Assert.Equal(-7938, longest.IntegrateRateOverTick(0x7fff));
		Assert.Equal(-7938, longest.IntegrateRateOverTick2(0x7fff));
		Assert.Equal(-0x7fff, new Es1SimClock().IntegrateRateOverTick(short.MinValue));
	}

	/// <summary>The host time holding <paramref name="count"/> interrupts, rounded up to a whole <see cref="TimeSpan"/> tick.</summary>
	private static TimeSpan Interrupts(Es1SimClock clock, int count) =>
		TimeSpan.FromTicks((count * TimeSpan.TicksPerSecond + clock.TicksPerSecond - 1) / clock.TicksPerSecond);
}
