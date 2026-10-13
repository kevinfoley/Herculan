using Herculan.Engine.Input;
using Herculan.Engine.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="KeyboardAxes.AtStickRate"/>, the Faster Keyboard Aiming tweak: the keys that aim the turret are
/// worth a stick pushed all the way, and the keys that move the machine are left alone.
/// </summary>
public class KeyboardAimingTests {
	private const short Key = MechControls.KeyboardAxis;

	[Fact]
	public void AFullStickIsWorth258() {
		Assert.Equal(258, KeyboardAxes.StickFullDeflection);
		Assert.Equal(KeyboardAxes.StickFullDeflection, JoystickReading.ApplyResponse(-JoystickReading.RawFull) * -1);
	}

	[Fact]
	public void TheTurretKeysAimAtTheStickRate() {
		var axes = KeyboardAxes.AtStickRate(new PilotAxes(Key, (short)-Key, (short)-Key, Key), firstPairAims: false);

		Assert.Equal(new PilotAxes(Key, (short)-Key, (short)-KeyboardAxes.StickFullDeflection, KeyboardAxes.StickFullDeflection), axes);
	}

	[Fact]
	public void TheArrowsAimAtTheStickRateWhenTheStickSteers() {
		var axes = KeyboardAxes.AtStickRate(new PilotAxes((short)-Key, Key, 0, 0), firstPairAims: true);

		Assert.Equal(new PilotAxes((short)-KeyboardAxes.StickFullDeflection, KeyboardAxes.StickFullDeflection, 0, 0), axes);
	}
}
