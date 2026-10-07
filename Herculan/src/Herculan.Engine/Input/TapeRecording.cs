using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Audio;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using static Herculan.Engine.Input.KeyChords;
using InputTape = HercWorks.Core.Data.File.Dbsim.InputTape;

namespace Herculan.Engine.Input;

/// <summary>
/// <c>--record</c>: this mission's input going onto a tape, which <see cref="TapePlayback"/> replays. With no
/// recorder every member is inert.
/// </summary>
public sealed class TapeRecording {
	private readonly SimulatorPreferences _preferences;
	private readonly GameAudio _audio;
	private readonly HashSet<int> _keysDown = new();

	public TapeRecording(InputTapeRecorder? recorder, SimulatorPreferences preferences, GameAudio audio) {
		Recorder = recorder;
		_preferences = preferences;
		_audio = audio;
	}

	public InputTapeRecorder? Recorder { get; }

	/// <summary>Whether a modal panel was up as this host frame began, which makes the frame one of the panel's.</summary>
	public bool PanelAtStart { get; private set; }

	/// <summary>Whether a panel raised by a frame's own input is holding that frame's tick back.</summary>
	public bool DeferredTick { get; set; }

	/// <summary>
	/// The top of a recorded host frame: whether it is a panel's, and the keys that went down since the last
	/// one. Keys are edges of the polled state rather than the device's own events, because the handlers poll
	/// too: a key pressed and let go between two frames is one no handler saw. Only what reaches the game is
	/// recorded — nothing while the debug UI has the keyboard or the free camera is being flown.
	/// </summary>
	public void BeginFrame(bool panelOpen, LiveKeys? liveKeys, bool imguiHasKeyboard, bool piloting) {
		if (Recorder == null) {
			return;
		}

		PanelAtStart = panelOpen;
		Recorder.SetHeld(PilotAxes.Centred, false, 0);
		if (liveKeys == null) {
			return;
		}

		bool listening = !imguiHasKeyboard && (piloting || PanelAtStart);
		int modifiers = (AltHeld(liveKeys) ? InputTapePlayer.AltBit : 0)
			| (CtrlHeld(liveKeys) ? InputTapePlayer.CtrlBit : 0);
		foreach (int scancode in TapeKeys.RecordedScancodes) {
			bool down = TapeKeys.KeysOf(scancode).Any(liveKeys.IsKeyPressed);
			if (down && listening && !_keysDown.Contains(scancode)) {
				Recorder.AddPress(scancode | modifiers);
			}

			if (down) {
				_keysDown.Add(scancode);
			} else {
				_keysDown.Remove(scancode);
			}
		}
	}

	/// <summary>
	/// One live mouse event onto the tape, in the game's own screen space — <see cref="TapePlayback"/>'s mouse
	/// replay run backwards. Dropped where the cockpit would not see it: over the debug UI, or in the free camera.
	/// </summary>
	public void AddMouse(float x, float y, CockpitMouseButtons buttons, int framebufferWidth, int framebufferHeight,
			bool panelOpen, bool piloting, bool imguiWantsMouse) {
		if (Recorder == null) {
			return;
		}

		if (!panelOpen && (!piloting || imguiWantsMouse)) {
			return;
		}

		int scale = _preferences[Prefs.VideoModeOption] == 1 ? 2 : 1;
		var (screenX, screenY) = AlertPanelLayout.Placement.CreateAt(framebufferWidth, framebufferHeight, 0, 0)
			.ToPanel(x, y);
		Recorder.AddMouse(new InputTape.MouseEvent {
			X = (int)MathF.Round(screenX / scale),
			Y = (int)MathF.Round(screenY / scale),
			Buttons = (ushort)((int)buttons & 3),
			Time = (int)_audio.CoarseTicks,
		});
	}

	/// <summary>
	/// The stick's discrete half onto the tape: the button that fired and the hat's views. The trigger scan's own
	/// button never claims the slot, so it is already absent, as retail zeroes its byte before the frame is written.
	/// </summary>
	public void AddStick(JoystickPilotInput input, bool tapePlaying) {
		if (Recorder == null || tapePlaying) {
			return;
		}

		Recorder.SetDiscreteStick(input.ClaimedButton, input.Views);
	}

	/// <summary>A tape records the axes ahead of Backturn, which playback applies again.</summary>
	public void SetHeld(PilotAxes axes, bool fire, byte buttons) => Recorder?.SetHeld(axes, fire, buttons);

	public void EmitTick(short tickDelta, JoystickCapabilities capabilities) => Recorder?.EmitTick(tickDelta, capabilities);

	public void EmitPanel(JoystickCapabilities capabilities) => Recorder?.EmitPanel(capabilities);

	/// <summary>Closes the tape, saying how much went onto it.</summary>
	public void Finish() {
		if (Recorder != null) {
			Recorder.Dispose();
			Console.WriteLine($"Recorded {Recorder.FrameCount} frames to {Recorder.Path}.");
		}
	}
}
