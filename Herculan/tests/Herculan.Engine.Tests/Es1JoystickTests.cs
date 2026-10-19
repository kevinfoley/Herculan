using Herculan.Es1.Input;
using Xunit;

namespace Herculan.Engine.Tests;

public class Es1JoystickTests {
	/// <summary>
	/// Mode 1's response at the values docs/es1/simulation/joystick.md#the-record works by hand: full deflection gives
	/// +252 and −256, and the zero band runs from −56 to +57. Catches the band of 25 or the rescale dropped, the square's
	/// zero test made on the value rather than the square, and a shift or rounding off by one.
	/// </summary>
	[Theory]
	[InlineData(128, 252)]
	[InlineData(-128, -256)]
	[InlineData(57, 0)]
	[InlineData(58, 25)]
	[InlineData(-56, 0)]
	[InlineData(-57, -25)]
	public void SquaredResponseMatchesTheWorkedValues(int calibrated, int expected) {
		var port = new TestPort(0b0011) { Axes = { [0] = calibrated } };
		var devices = Es1JoystickDevices.Create(Prefs((Es1Preferences.StickConfigurationOption, 1)), port);

		devices.Poll();

		Assert.Equal(expected, devices.ReadJoystickState(Es1JoystickDevices.FirstKind)!.Axes[0]);
	}

	/// <summary>
	/// Option 7 = 1 with option 9: the rudder pedals' axis 2, read by the first device's poll, lands negated at
	/// <c>+0x15</c> and their mask 4 is or'd into <c>+0x26</c> beside the stick's 3. Catches the negation dropped, the
	/// second device left without the first's read of the port, and the masks not merged.
	/// </summary>
	[Fact]
	public void RudderPedalsMergeTheirNegatedAxisAndMaskIntoTheLiveBlock() {
		var port = new TestPort(0b0111) { Axes = { [0] = 128, [2] = 128 }, Buttons = 0b0011 };
		var prefs = Prefs((Es1Preferences.StickConfigurationOption, 1), (Es1Preferences.ThirdAxisOption, 1));
		var poll = new Es1DevicePoll(new Es1KeyboardInput(0), new Es1HostMouse(), Es1JoystickDevices.Create(prefs, port));

		var block = poll.Poll();

		Assert.Equal(252, block.StickAxes[0]);
		Assert.Equal(-252, block.StickAxes[2]);
		Assert.Equal(new byte[] { 1, 1, 0, 0 }, block.StickButtons[..4]);
		Assert.Equal(3 | 4, block.StickExtra[0]);
		Assert.Equal(3, block.StickExtra[1]);
	}

	/// <summary>
	/// Option 7 = 2 with option 9 clears option 9 in memory and builds the port's second stick, not the pedals, as the
	/// second device. Catches the clear dropped, which would put the third axis on the twist, and the pedals built
	/// under configuration 2.
	/// </summary>
	[Fact]
	public void ConfigurationTwoClearsOptionNine() {
		var prefs = Prefs((Es1Preferences.StickConfigurationOption, 2), (Es1Preferences.ThirdAxisOption, 1));

		var devices = Es1JoystickDevices.Create(prefs, new TestPort(0b1111));

		Assert.False(prefs.ThirdAxis);
		Assert.IsType<Es1Joystick2Device>(devices.Second);
	}

	/// <summary>
	/// The Thrustmaster FCS's hat from axis 3: −113 sets <c>+0x14</c>, −112 the next byte, <c>+0x16</c>, and −16 none,
	/// with the fourth axis cleared (docs/es1/simulation/joystick.md#hats-and-throttles). Catches a threshold off by one,
	/// the hat bytes in the wrong order, and the throttle axis left in the record.
	/// </summary>
	[Theory]
	[InlineData(-113, 4)]
	[InlineData(-112, 6)]
	[InlineData(-16, -1)]
	public void ThrustmasterHatPicksOneByteByThreshold(int axis3, int hatByte) {
		var port = new TestPort(0b1011) { Axes = { [3] = axis3 } };
		var devices = Es1JoystickDevices.Create(Prefs((Es1Preferences.StickConfigurationOption, 3)), port);

		devices.Poll();
		var record = devices.ReadJoystickState(Es1JoystickDevices.FirstKind)!;

		for (int i = 4; i < 8; i++) {
			Assert.Equal(i == hatByte ? 1 : 0, record.Buttons[i]);
		}

		Assert.Equal(0, record.Axes[3]);
	}

	private static Es1Preferences Prefs(params (int Option, byte Value)[] options) {
		var bytes = new byte[Es1Preferences.OptionCount];
		foreach (var (option, value) in options) {
			bytes[option] = value;
		}

		return Es1Preferences.FromFile(bytes);
	}

	private sealed class TestPort(int answering) : IEs1GamePort {
		public int[] Axes { get; } = new int[4];

		public byte Buttons { get; init; }

		public bool AxisAnswers(int axis) => (answering & (1 << axis)) != 0;

		public int ReadAxis(int axis) => Axes[axis];
	}
}
