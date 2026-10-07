using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Silk.NET.Input;
using Silk.NET.Maths;
using InputTape = HercWorks.Core.Data.File.Dbsim.InputTape;

namespace Herculan.Engine.Input;

/// <summary>
/// An input tape driving the mission (<c>--play</c>, <c>--demo</c>): which of its frames is taken this host
/// frame, the keystrokes and pointer it leaves for the handlers to read, and the live keys that still stop
/// it. With no tape every member is inert, so callers ask <see cref="Playing"/> rather than whether there is
/// one. The ticks a replay runs are the host's frame stepper's.
/// </summary>
public sealed class TapePlayback {
	private readonly SimulatorPreferences _preferences;
	private readonly Func<Vector2D<int>> _framebufferSize;
	private readonly CockpitInput _cockpitInput;

	// The replay's own state. Frame is the frame taken this host frame, if one was due; the keys it
	// pressed are down for this frame only, and the next frame leaves them up before another press can
	// land (_pulsed). A frame taken while a modal panel is up is the panel's, not the simulation's.
	private bool _pulsed;
	private bool _panelShown;
	private bool _liveStopRequested;

	/// <param name="player">The tape, or null when nothing is replaying.</param>
	/// <param name="preferences">The install's simulator preferences, whose video mode scales the tape's mouse.</param>
	/// <param name="framebufferSize">The window's framebuffer size, which the tape's mouse is placed on.</param>
	/// <param name="cockpitInput">The cockpit's click queue, which the tape's clicks go into.</param>
	public TapePlayback(InputTapePlayer? player, SimulatorPreferences preferences, Func<Vector2D<int>> framebufferSize,
			CockpitInput cockpitInput) {
		Player = player;
		Keys = player != null ? new TapeKeys() : null;
		_preferences = preferences;
		_framebufferSize = framebufferSize;
		_cockpitInput = cockpitInput;
	}

	public InputTapePlayer? Player { get; }

	/// <summary>The tape's keystrokes, which every key handler reads in place of the window's while it plays.</summary>
	public TapeKeys? Keys { get; }

	/// <summary>Whether an input tape is driving the mission.</summary>
	public bool Playing => Player is { Finished: false };

	/// <summary>The frame taken this host frame, if one was due.</summary>
	public InputTape.Frame? Frame { get; private set; }

	/// <summary>Whether <see cref="Frame"/> was taken while a modal panel was up, which makes it the panel's.</summary>
	public bool FrameUnderPanel { get; private set; }

	/// <summary>How much host time is owed to the tape's frames, each of which lasts as long as it recorded.</summary>
	public double Accumulator { get; set; }

	/// <summary>
	/// The tick of a frame whose own input raised a modal panel. Sim_PollPlayerInput runs the panel from
	/// inside Sim_MainTick, so that tick finishes once the panel is down, with the frame's SimTickDelta.
	/// </summary>
	public short? DeferredTick { get; set; }

	/// <summary>
	/// The pointer as the tape's mouse events leave it, in framebuffer pixels. The live pointer does not
	/// reach the cockpit or a panel during a replay, as CockpitMouseLive (004d1e5a) shuts it out in retail.
	/// </summary>
	public (float X, float Y, CockpitMouseButtons Buttons) Pointer { get; private set; } = (0, 0, CockpitMouseButtons.None);

	/// <summary>What the stick on the recording machine could do.</summary>
	public JoystickCapabilities Capabilities => Player!.Capabilities;

	/// <summary>
	/// The two things the live keyboard still does while a tape plays, both of which retail tests on the live
	/// command word rather than the tape's: [Ctrl+E] stops the playback, and under -D any key that makes a
	/// command code raises the abort.
	/// </summary>
	public void ListenTo(LiveKeys liveKeys) {
		if (Player == null) {
			return;
		}

		liveKeys.Device.KeyDown += (device, key, _) => {
			if (Player.Finished) {
				return;
			}

			bool ctrl = device.IsKeyPressed(Key.ControlLeft) || device.IsKeyPressed(Key.ControlRight);
			if (Player.DemoMode || (ctrl && key == Key.E)) {
				_liveStopRequested = true;
			}
		};
	}

	/// <summary>
	/// Takes the replay's next frame once it is due, before any handler reads input this host frame, and
	/// puts its keystrokes and mouse events where those handlers look. At most one frame a host frame goes
	/// in this way: every frame behind it that carries only held state follows in
	/// the frame stepper.
	///
	/// <para>A frame that presses anything waits for a host frame with nothing down, so every key-down edge
	/// the tape records is one the handlers see — which also delays it by a host frame when two such frames
	/// fall due back to back. The delay is time the pacing then catches up.</para>
	///
	/// <para>Returns whether the tape ended this frame, run out or stopped, having said so.</para>
	/// </summary>
	public bool TakeFrame(double deltaSeconds, bool panelOpen, bool missionOver, double maxAccumulatedSeconds) {
		Frame = null;
		if (Player == null) {
			return false;
		}

		Keys?.Release();
		if (Player.Finished || missionOver) {
			return false;
		}

		if (_liveStopRequested) {
			Stop(stopped: true);
			return true;
		}

		if (Player.Peek() is not { } next) {
			Stop(stopped: false);
			return true;
		}

		bool pulsedLastFrame = _pulsed;
		_pulsed = false;

		double duration = InputTapePlayer.DurationOf(next, panelOpen);
		Accumulator = Math.Min(Accumulator + deltaSeconds, maxAccumulatedSeconds);
		if (Accumulator < duration) {
			return false;
		}

		bool discrete = InputTapePlayer.HasDiscreteInput(next);
		if (discrete && pulsedLastFrame) {
			return false;
		}

		Player.Take();
		Accumulator -= duration;
		Frame = next;
		FrameUnderPanel = panelOpen;
		_pulsed = discrete;

		foreach (int code in InputTapePlayer.PressesOf(next)) {
			Keys!.Press(code);
		}

		foreach (var mouseEvent in next.MouseEvents) {
			ApplyMouse(mouseEvent, panelOpen);
		}

		return false;
	}

	/// <summary>
	/// The next frame when it is due and presses nothing, taken — the held-state frames that follow the one
	/// <see cref="TakeFrame"/> took. Null once none is.
	/// </summary>
	public InputTape.Frame? TakeHeldFrame(bool panelOpen) {
		if (!Playing || Player!.Peek() is not { } next || InputTapePlayer.HasDiscreteInput(next)) {
			return null;
		}

		double duration = InputTapePlayer.DurationOf(next, panelOpen);
		if (Accumulator < duration) {
			return null;
		}

		Player.Take();
		Accumulator -= duration;
		return next;
	}

	/// <summary>
	/// Logs the frame each modal panel goes up and comes down on, to set against the tape's own
	/// InferredPanelSpans: where they disagree, the replay has left the recording.
	/// </summary>
	public void NotePanel(bool shown) {
		if (shown != _panelShown && Player != null) {
			_panelShown = shown;
			Console.WriteLine($"Tape frame {Player.Position - 1}: a panel {(shown ? "went up" : "came down")}.");
		}
	}

	// The tape has run out, or [Ctrl+E] stopped it. What follows — the demo ending, or -p's hand-over of the
	// controls — is the caller's.
	private void Stop(bool stopped) {
		Player!.Stop();
		Keys?.Release();
		Console.WriteLine($"Tape {Player.Name} {(stopped ? "stopped" : "ended")} at frame {Player.Position}.");
	}

	// One of the tape's mouse events. Its position is in the game's own screen space — Mouse_DispatchEvent
	// (0048083c) scales a client position to half the back buffer, which is the viewport: the 640x480 a
	// panel centres itself on and the forward view fills, or 320x240 in the low-resolution mode — so it
	// lands wherever this engine puts that screen.
	private void ApplyMouse(InputTape.MouseEvent mouseEvent, bool underPanel) {
		var framebuffer = _framebufferSize();
		int scale = _preferences[Prefs.VideoModeOption] == 1 ? 2 : 1;

		var (x, y) = AlertPanelLayout.Placement.CreateAt(framebuffer.X, framebuffer.Y, 0, 0)
			.ToWindow(mouseEvent.X * scale, mouseEvent.Y * scale);
		var buttons = (CockpitMouseButtons)(mouseEvent.Buttons & 3);
		Pointer = (x, y, buttons);

		// The same queue a live click goes into, and drained by the same code. A panel takes its pointer
		// from Pointer instead, as it takes the live one straight off the device.
		if (!underPanel) {
			_cockpitInput.Enqueue(x, y, buttons);
		}
	}
}
