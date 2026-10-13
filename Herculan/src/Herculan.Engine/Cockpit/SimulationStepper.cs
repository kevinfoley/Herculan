using Herculan.Engine.Input;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// When the simulation ticks. Live, it advances in whole ticks of the same length on a fixed-timestep
/// accumulator, so the ported fixed-point integration stays reproducible no matter how the frame rate varies;
/// a replay ticks on the tape's frames instead, each for the time it recorded; a recording writes each tick
/// onto its tape. Every modal panel freezes it.
/// </summary>
public sealed class SimulationStepper {
	private const double SecondsPerTick = 1.0 / SimWorld.TicksPerSecond;

	// Clamping the accumulator stops a long stall (a breakpoint, a window drag) from turning into a burst of
	// catch-up ticks that would teleport everything.
	private const double MaxAccumulatedSeconds = 0.25;

	private readonly SimWorld _world;
	private readonly TapePlayback _tape;
	private readonly TapeRecording _recording;
	private readonly ModalPanels _panels;
	private readonly MissionOutcome _outcome;
	private readonly DeveloperKeys _developerKeys;
	private readonly CockpitView _view;
	private readonly PilotControls _pilot;
	private readonly ISimulatorInput _input;

	private double _tickAccumulator;

	public SimulationStepper(SimWorld world, TapePlayback tape, TapeRecording recording, ModalPanels panels,
			MissionOutcome outcome, DeveloperKeys developerKeys, CockpitView view, PilotControls pilot,
			ISimulatorInput input) {
		_world = world;
		_tape = tape;
		_recording = recording;
		_panels = panels;
		_outcome = outcome;
		_developerKeys = developerKeys;
		_view = view;
		_pilot = pilot;
		_input = input;
	}

	/// <summary>
	/// Raised after each tick and the view chain's share of it, before a recording's or a replay's half. Beams are
	/// resolved and forgotten inside the tick, so anything that wants to see one has to look here — see
	/// <see cref="SimWorld.Beams"/>.
	/// </summary>
	public event Action? Ticked;

	/// <summary>
	/// The top of a host frame, before any handler reads input: a replay's next frame goes in, when it is due,
	/// so the handlers read its keystrokes and pointer exactly as they would a player's; and a recording's
	/// keystrokes are read at the same moment the handlers will read them.
	/// </summary>
	/// <param name="hostTakesEscape">Whether this frame's [Esc] goes to the host's menu rather than the game.</param>
	public void BeginFrame(double deltaSeconds, bool hostTakesEscape = false) {
		if (_tape.TakeFrame(deltaSeconds, _panels.AnyOpen, _outcome.Over, MaxAccumulatedSeconds)) {
			EndTape();
		}

		_recording.BeginFrame(_panels.AnyOpen, _input.LiveKeys, _input.ImGuiHasKeyboard, _view.Piloting,
			hostTakesEscape);
	}

	/// <summary>
	/// The frame's ticks, after every input handler has seen this host frame's input. A frozen sim neither ticks
	/// nor accumulates, so dismissing a panel carries on from where it stopped rather than catching up. Every
	/// modal freezes the simulation behind it, which is the original's own behaviour: each of these panels
	/// raises DAT_004d2576 while it is up and restores it on the way out -- PreferencesPanel_Raise (0045cfd4)
	/// for the preferences panel, and the controls panel is raised over that one.
	/// </summary>
	public void Advance(double deltaSeconds) {
		bool frozen = _outcome.Over || _panels.AnyOpen;

		// A replay ticks on the tape's frames instead, each for the time it recorded.
		if (_tape.Playing || _tape.Frame != null) {
			RunTapeTicks();
			return;
		}

		// A recording writes one frame per host frame a modal panel is up for, as a panel's own loop
		// builds the input once a pass. The frame that takes the panel down also finishes the tick a
		// frame's own input held back when it raised the panel, as playback does.
		if (_recording.Recorder != null && _recording.PanelAtStart) {
			_recording.EmitPanel(_pilot.StickCapabilities);
			if (_recording.DeferredTick && !_outcome.Over && !_panels.AnyOpen) {
				_recording.DeferredTick = false;
				TickOn(_recording.DeferredControls, () => TickLive(emit: false));
				frozen = _outcome.Over || _panels.AnyOpen;
			}
		}

		bool ticked = false;
		if (_developerKeys.StepPending && !frozen) {
			// Alt+keypad +: this tick and no more. Sim_MainTick re-freezes at the top of the next one.
			_world.Tick();
			_view.AdvanceChain(_developerKeys);
			Ticked?.Invoke();
			RecordTick();
			_developerKeys.FinishStep();
			_tickAccumulator = 0;
			ticked = true;
		} else {
			if (!frozen) {
				_tickAccumulator = Math.Min(_tickAccumulator + deltaSeconds, MaxAccumulatedSeconds);
			}
			while (!frozen && _tickAccumulator >= SecondsPerTick) {
				_tickAccumulator -= SecondsPerTick;
				ticked = true;
				frozen = TickLive(emit: true);
			}
		}

		// This frame's own input raised a panel, so nothing ticked: the frame goes on the tape as one
		// that ticks, and playback holds its tick back until the panel is down, as the recording does.
		if (_recording.Recorder != null && !_recording.PanelAtStart && !ticked && !_outcome.Over && _panels.AnyOpen) {
			_recording.EmitTick(SimWorld.TickDelta, _pilot.StickCapabilities);
			_recording.DeferredTick = true;
			_recording.DeferredControls = _view.PilotMech?.Controls ?? MechControls.Neutral;
		}
	}

	// One tick on the controls of the frame that raised a panel, which the panel wrote back as it closed; the
	// ticks after it in this host frame go back to the controls this frame built.
	private void TickOn(MechControls controls, Action tick) {
		var mech = _view.PilotMech;
		var current = mech?.Controls;
		if (mech != null) {
			mech.Controls = controls;
		}

		tick();

		if (mech != null && current is { } restored) {
			mech.Controls = restored;
		}
	}

	// One tick on the engine's own timestep. Returns whether the simulation has stopped behind a modal
	// panel or the mission's end, which only a recording asks about mid-loop — see RecordTick.
	private bool TickLive(bool emit) {
		// Alt+S's freeze is not a panel's: the tick still runs, with only the player's input poll and the
		// mission poll in it. See SimWorld.TickFrozen.
		if (_developerKeys.Frozen) {
			_world.TickFrozen();
		} else {
			_world.Tick();
		}

		_view.AdvanceChain(_developerKeys);
		Ticked?.Invoke();
		return RecordTick(emit);
	}

	// A recording's half of a tick: the frame goes on the tape, and the mission alert goes up straight
	// after the tick that decided it rather than at the top of the next host frame, so the ticks the loop
	// would still run behind it do not happen — as in a replay, where Sim_MainTick raises it itself.
	private bool RecordTick(bool emit = true) {
		if (_recording.Recorder == null) {
			return false;
		}

		if (emit) {
			_recording.EmitTick(SimWorld.TickDelta, _pilot.StickCapabilities);
		}

		_panels.RaisePendingMissionAlert(_outcome.Over);
		return _outcome.Over || _panels.AnyOpen;
	}

	// The simulation's half of a replay, after every input handler has seen this host frame's tape frame.
	private void RunTapeTicks() {
		_tape.NotePanel(_panels.AnyOpen);
		if (_outcome.Over) {
			return;
		}

		var pilotMech = _view.PilotMech;
		if (_tape.Frame is { } frame) {
			if (!_tape.FrameUnderPanel && _panels.AnyOpen) {
				// The frame's own input put a panel up, which in the original runs from inside that frame's
				// tick; the rest of the tick waits for the panel.
				_tape.DeferredFrame = frame;
			} else if (!_tape.FrameUnderPanel) {
				TickTape(frame.TickDelta);
			} else if (!_panels.AnyOpen && _tape.DeferredFrame is { } raising) {
				// This frame took the panel down. The tick that raised it finishes now, on the input of the
				// frame that raised it, which the panel wrote back into the input block as it closed.
				_tape.DeferredFrame = null;
				if (pilotMech != null) {
					pilotMech.Controls = _pilot.TapeControls(raising, centerTorso: false, centerBody: false);
				}

				TickTape(raising.TickDelta);
			}
		}

		// Then every frame behind it that is due and presses nothing.
		while (_tape.Playing && !_outcome.Over) {
			bool underPanel = _panels.AnyOpen;
			if (_tape.TakeHeldFrame(underPanel) is not { } next) {
				break;
			}

			if (!underPanel) {
				if (pilotMech != null) {
					pilotMech.Controls = _pilot.TapeControls(next, centerTorso: false, centerBody: false);
					_view.TakeCameraAxes(pilotMech.Controls);
				}

				TickTape(next.TickDelta);
			}
		}
	}

	// One tick of the tape's own length. The mission alert goes up straight after the tick that decided
	// it, rather than at the top of the next host frame, so the frames behind it are the panel's — as
	// they are in the recording, where Sim_MainTick raises the panel itself.
	private void TickTape(short tickDelta) {
		// Alt+S on the tape freezes the simulation and the tape runs on, as Sim_MainTick keeps polling it.
		if (_developerKeys.Frozen) {
			_world.TickFrozen(tickDelta);
		} else {
			_world.Tick(tickDelta, InputTapePlayer.SecondsOf(tickDelta) * 1000);
		}

		_view.AdvanceChain(_developerKeys);
		Ticked?.Invoke();
		_panels.RaisePendingMissionAlert(_outcome.Over);
		_tape.NotePanel(_panels.AnyOpen);
	}

	// The tape has run out, or [Ctrl+E] stopped it. Under -D that ends the mission, as the abort does in
	// retail; otherwise the controls go back to the player and the engine's own timestep, which is -p's
	// hand-over.
	private void EndTape() {
		if (_tape.Player!.DemoMode) {
			Console.WriteLine("Demo over.");
			_outcome.End();
			return;
		}

		_input.TakeLiveKeys();
		SimMath.PerTickStepsScaled = true;
		_tickAccumulator = 0;
		_tape.DeferredFrame = null;
	}
}
