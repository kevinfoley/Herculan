using Herculan.Engine.Content;
using Herculan.Engine.Input;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io.Transform.Dbsim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="InputTapeRecorder"/> — that what it writes is a tape the transformer and
/// <see cref="InputTapePlayer"/> read back as the same input: the bundle from the mission's folder, the
/// capability block once ahead of the first frame, and each frame's discrete input on that frame only.
/// </summary>
public class InputTapeRecorderTests : IDisposable {
	private readonly string _directory = Path.Combine(Path.GetTempPath(), "herculan-tape-test-" + Guid.NewGuid().ToString("N"));

	public InputTapeRecorderTests() => Directory.CreateDirectory(_directory);

	public void Dispose() => Directory.Delete(_directory, recursive: true);

	[Fact]
	public void RecordsTheBundleAndFramesPlaybackReadsBack() {
		// A save-slot pair, so the lance file has to be found by the slot's own name.
		string script = Path.Combine(_directory, "script3.dat");
		File.WriteAllBytes(script, new byte[] { 1, 2, 3 });
		File.WriteAllBytes(Path.Combine(_directory, "player3.mec"), new byte[] { 4, 5 });
		File.WriteAllBytes(Path.Combine(_directory, "prefs.cfg"), new byte[] { 6 });
		string tapePath = Path.Combine(_directory, "out.tap");

		var stick = new JoystickCapabilities(Present: true, ButtonCount: 4, HasThrottle: false, HasRudder: true,
			HasHat: true);

		using (var recorder = InputTapeRecorder.Create(tapePath, script, _directory)) {
			recorder.SetHeld(new PilotAxes(-128, 64, 10, -20), trigger: true, rawButtons: 0x81);
			recorder.AddPress(0x10);
			recorder.AddPress(InputTapePlayer.AltBit | 0x02);
			recorder.AddMouse(new InputTape.MouseEvent { X = 324, Y = 298, Buttons = 1, Time = 7 });
			recorder.SetDiscreteStick(1, JoystickHat.North);
			recorder.EmitTick(81, stick);

			// Held input carries on; the discrete input does not.
			recorder.EmitTick(81, stick);
			recorder.EmitPanel(stick);
			Assert.Equal(3, recorder.FrameCount);
		}

		var tape = Assert.IsType<InputTape>(new InputTapeTransformer().Parse(File.ReadAllBytes(tapePath)));

		Assert.Equal(new byte[] { 1, 2, 3 }, tape.Bundle[0]);
		Assert.Equal(new byte[] { 4, 5 }, tape.Bundle[1]);
		Assert.Empty(tape.Bundle[2]);
		Assert.Equal(new byte[] { 6 }, tape.Bundle[3]);
		Assert.Equal(new byte[] { 1, 0, 4, 0, 0, 1, 0x10, 0 }, tape.Capabilities);
		Assert.Equal(3, tape.Frames.Count);

		var first = tape.Frames[0];
		Assert.Equal(new[] { 0x10, InputTapePlayer.AltBit | 0x02 }, InputTapePlayer.PressesOf(first));
		Assert.Equal(new PilotAxes(-128, 64, 10, -20), InputTapePlayer.AxesOf(first));
		Assert.True(InputTapePlayer.TriggerOf(first));
		Assert.True(InputTapePlayer.ButtonOf(first, 1));
		Assert.Equal(JoystickHat.North, InputTapePlayer.HatOf(first));
		Assert.Equal(0x81, first.RawButtonBank);
		Assert.Equal(324, Assert.Single(first.MouseEvents).X);

		var second = tape.Frames[1];
		Assert.False(InputTapePlayer.HasDiscreteInput(second));
		Assert.Equal(new PilotAxes(-128, 64, 10, -20), InputTapePlayer.AxesOf(second));
		Assert.Equal(81, tape.Frames[2].TickDelta);
	}

	[Fact]
	public void AStickThatIsNotThereIsRecordedWithNoCapabilities() {
		Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 },
			InputTapeRecorder.CapabilityBlock(JoystickCapabilities.None));
	}
}
