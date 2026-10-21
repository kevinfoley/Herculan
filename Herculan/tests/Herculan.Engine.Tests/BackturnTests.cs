using Herculan.Engine.Input;
using HercWorks.Core.Data.File.Cfg;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="JoystickBindings.ApplyBackturn"/>: <c>Backturn = Reverse</c> reverses a walker's steering while it backs
/// up, and leaves a RAZOR's roll alone while its nose is pulled up, where retail reverses it.
/// </summary>
public class BackturnTests {
	[Theory]
	[InlineData(false, -0x40)]
	[InlineData(true, 0x40)]
	public void SteeringIsReversedWhileBackingUpInAWalkerOnly(bool razor, short steer) {
		var bindings = new JoystickBindings { PilotingRazor = razor, Keyjoy = new Keyjoy { ReverseBackturn = true } };

		var axes = bindings.ApplyBackturn(new PilotAxes(0x40, 0x80, 0, 0));

		Assert.Equal(steer, axes.Steer);
	}
}
