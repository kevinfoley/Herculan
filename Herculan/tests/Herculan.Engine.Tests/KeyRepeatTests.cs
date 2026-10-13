using Herculan.Engine.Input;
using Silk.NET.Input;
using Xunit;

namespace Herculan.Engine.Tests;

public class KeyRepeatTests {
	// Binary fractions, so the delay is exactly 32 frames and the interval exactly 8.
	private const double Frame = 1.0 / 64;

	private static KeyRepeat Repeat() => new((0.5, 0.125));

	// Runs whole frames with the given keys held, and counts the repeats each key got.
	private static Dictionary<Key, int> Run(KeyRepeat repeat, int frames, params Key[] held) {
		var counts = new Dictionary<Key, int>();
		for (int i = 0; i < frames; i++) {
			repeat.Advance(held.Contains, Frame);
			if (repeat.Repeated is { } key) {
				counts[key] = counts.GetValueOrDefault(key) + 1;
			}
		}

		return counts;
	}

	[Fact]
	public void AHeldKeyRepeatsAfterTheDelayAtTheRate() {
		var repeat = Repeat();
		Assert.Empty(Run(repeat, 32, Key.Equal));
		Assert.Equal(12, Run(repeat, 96, Key.Equal)[Key.Equal]);
	}

	[Fact]
	public void OnlyTheKeyPressedLastRepeats() {
		var repeat = Repeat();
		Run(repeat, 30, Key.Equal);
		var counts = Run(repeat, 100, Key.Equal, Key.Minus);
		Assert.False(counts.ContainsKey(Key.Equal));
		Assert.Equal(9, counts[Key.Minus]);
	}

	[Fact]
	public void AModifierGoingDownTakesTheRepeatOff() {
		var repeat = Repeat();
		Run(repeat, 60, Key.Equal);
		Assert.DoesNotContain(Key.Equal, Run(repeat, 100, Key.Equal, Key.AltLeft).Keys);
	}

	[Fact]
	public void LettingGoOfTheRepeatingKeyStopsTheRepeatForKeysStillHeld() {
		var repeat = Repeat();
		Run(repeat, 10, Key.Equal);
		Run(repeat, 10, Key.Equal, Key.Minus);
		Assert.Empty(Run(repeat, 100, Key.Equal));
	}
}
