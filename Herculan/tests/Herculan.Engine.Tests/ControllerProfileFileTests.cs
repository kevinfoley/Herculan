using Herculan.Engine.Input;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// A Custom profile saved in <c>herculan-joystick.cfg</c> reads back as it was saved (<see cref="ControllerProfile.WriteValues"/>
/// and <see cref="ControllerProfile.FromValues"/>, through <see cref="JoystickMapFile"/>), so the writer and the reader agree
/// on every key.
/// </summary>
public sealed class ControllerProfileFileTests : IDisposable {
	private const string Guid = "030000004f04000008b1000000000000";

	private readonly string _folder = Directory.CreateTempSubdirectory("herculan-profile-").FullName;

	public void Dispose() => Directory.Delete(_folder, recursive: true);

	[Fact]
	public void ASavedProfileReadsBackAsSaved() {
		// A default with one binding removed, others changed, and every kind of setting off its default.
		var defaults = ControllerProfile.ForJoystick(axisCount: 5, buttonCount: 12, hatCount: 1);
		var profile = defaults with {
			Axes = defaults.Axes
				.SetItem(AxisFunction.Throttle, new ControllerAxis(2, Invert: true, Deadzone: 7))
				.Remove(AxisFunction.RazorYaw),
			ThrottleReading = ThrottleReading.Bipolar,
			RazorThrottleReading = ThrottleReading.Lever,
			HercBindings = defaults.HercBindings
				.Remove(ControllerInput.Button(2))
				.SetItem(ControllerInput.Button(11), KeyAction.SendFireAtWill)
				.SetItem(ControllerInput.Axis(4, -1), KeyAction.PowerDown),
			RazorBindings = defaults.RazorBindings.SetItem(ControllerInput.Hat(0, JoystickHat.East), KeyAction.LookRight),
		};

		var file = new JoystickMapFile();
		file.SetProfile(isGamepad: false, Guid, "Stick", ControllerMode.Custom, profile);
		string path = Path.Combine(_folder, JoystickMapFile.FileName);
		Assert.True(file.Save(path, out string? error), error);

		var loaded = JoystickMapFile.Load(path);
		var read = loaded.ProfileFor(isGamepad: false, Guid);

		Assert.Equal(ControllerMode.Custom, loaded.ModeFor(isGamepad: false, Guid));
		Assert.NotNull(read);
		Assert.Equal(profile.Axes.OrderBy(entry => entry.Key), read.Axes.OrderBy(entry => entry.Key));
		Assert.Equal(profile.ThrottleReading, read.ThrottleReading);
		Assert.Equal(profile.RazorThrottleReading, read.RazorThrottleReading);
		Assert.Equal(profile.HercBindings.OrderBy(entry => entry.Key), read.HercBindings.OrderBy(entry => entry.Key));
		Assert.Equal(profile.RazorBindings.OrderBy(entry => entry.Key), read.RazorBindings.OrderBy(entry => entry.Key));
	}
}
