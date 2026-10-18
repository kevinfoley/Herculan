using Silk.NET.Input;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>A held key's auto-repeats reaching the cockpit's handlers as fresh presses.</summary>
[Collection(SimTimestepCollection.Name)]
public class CockpitKeyRepeatTests {
	[Fact]
	public void EachRepeatOfAHeldShieldKeyNudgesTheBalanceAgain() {
		if (SimulatorRig.Load() is not { } rig || rig.Player is not { } player) {
			return;
		}

		rig.Run(40);
		var balances = new List<short> { player.Shields.Balance };
		rig.Keys.Hold(Key.RightBracket);
		foreach (bool repeat in new[] { false, true, false, true }) {
			if (repeat) {
				rig.Keys.RepeatNextFrame(Key.RightBracket);
			}

			rig.Frame();
			balances.Add(player.Shields.Balance);
		}

		// The press and the two repeats move it; the frame between them does not.
		Assert.NotEqual(balances[0], balances[1]);
		Assert.NotEqual(balances[1], balances[2]);
		Assert.Equal(balances[2], balances[3]);
		Assert.NotEqual(balances[3], balances[4]);
	}
}
