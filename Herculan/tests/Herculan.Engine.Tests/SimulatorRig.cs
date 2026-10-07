using System.Globalization;
using System.Reflection;
using System.Text;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Data.Struct.Herc;
using Herculan.Engine.Audio;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Engine.Install;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Ai;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Herculan.Engine.Tests;

/// <summary>
/// The simulator's cockpit stack with no window: the components <c>SimulatorHost</c>'s constructor builds, in its
/// order, over the mission a retail demo tape carries, and one update frame run as <c>SimulatorHost.OnUpdate</c>
/// runs it. The keyboard, the pointer and the stick are scripted.
///
/// <para>The frame leaves out what only the window, the debug UI or the renderer touch: the menu bar and the
/// rest of <c>WindowKeys</c> (so scripts press [Esc] only to take a panel down, and never [/] or [Alt+Enter]),
/// the debug panel's readouts, the draw items and the palette flash. None of those writes simulation state.
/// The ticks are <c>SimulationStepper</c>'s live path, with no tape and no recording.</para>
///
/// <para>The mission is a retail demo tape's own bundle — <c>DEMO2.TAP</c>'s, an OGRE with a squad of three, unless
/// another is asked for — unpacked without the install's <c>data\</c> under it, so
/// it does not change when the install's own <c>script.dat</c> does. Null when there is no install.</para>
/// </summary>
sealed class SimulatorRig {
	public const int Width = 1280;
	public const int Height = 960;

	private const double SecondsPerTick = 1.0 / SimWorld.TicksPerSecond;
	private const double MaxAccumulatedSeconds = 0.25;

	private readonly CockpitInput _cockpitInput = new();
	private double _tickAccumulator;

	private SimulatorRig(SimulatorStart start, StagedStart staged, bool developer) {
		Start = start;
		Scene = start.Scene;
		Audio = start.Audio;
		Staging = new SimulatorStaging(staged, new StagedScreenshot(), screenshotRun: false);

		Art = LoadCockpitArt(start);
		View = new CockpitView(Scene, Art, Staging.Start);

		Tape = new TapePlayback(null, start.Preferences, () => new Vector2D<int>(Width, Height), _cockpitInput);
		Recording = new TapeRecording(null, start.Preferences, Audio);

		Panels = new ModalPanels(start, Pointer, View, hasCockpit: Art != null, Staging.Start.Joystick);
		Panels.OpenStaged(Staging.Start);

		Displays = new CockpitDisplays(start, Art, View, Staging);
		View.BuildChain(Staging.Start.External);

		DeveloperKeys = new DeveloperKeys(developer);
		Staging.StageMachine(View.PilotMech, Scene.World);

		Commands = new CockpitCommands(Displays, View, Scene, Audio, SystemButtonPresses, Tape);
		Pilot = new PilotControls(start, View, Displays, Commands, Tape, Recording, DeveloperKeys, Staging.Start);
		Keyboard = new CockpitKeyboard(Displays, View, Commands, Scene, Audio);
		CockpitUpdate = new PlayerCockpitUpdate(Displays, View, Commands, Scene, Audio);

		Audio.StartMissionMusic(Scene.Mission.Header, 0);
		Displays.PowerUpCockpit(Audio, screenshotRun: false);
		Staging.BeginMission();

		Pilot.Joystick = Stick;
	}

	public SimulatorStart Start { get; }
	public MissionScene Scene { get; }
	public GameAudio Audio { get; }
	public SimulatorStaging Staging { get; }
	public CockpitArt? Art { get; }
	public CockpitView View { get; }
	public TapePlayback Tape { get; }
	public TapeRecording Recording { get; }
	public ModalPanels Panels { get; }
	public CockpitDisplays Displays { get; }
	public DeveloperKeys DeveloperKeys { get; }
	public CockpitCommands Commands { get; }
	public PilotControls Pilot { get; }
	public CockpitKeyboard Keyboard { get; }
	public PlayerCockpitUpdate CockpitUpdate { get; }

	public ScriptedKeys Keys { get; } = new();
	public ScriptedPointer Pointer { get; } = new();
	public ScriptedStick Stick { get; } = new();
	public SystemButtonLog SystemButtonPresses { get; } = new();

	/// <summary><c>MissionOutcome</c>'s two fields.</summary>
	public bool MissionOver { get; private set; }
	public bool QuitGame { get; private set; }

	public SimWorld World => Scene.World;
	public MechObject? Player => View.PilotMech;

	/// <summary>The rig, or null when no install with the demo tapes can be found.</summary>
	public static SimulatorRig? Load(bool developer = false, string tapeName = "DEMO2",
			Action<MissionScene, StagedStart>? stage = null) {
		string? root = GameInstall.Locate(null);
		string tapePath = root == null ? "" : Path.Combine(root, InputTapePlayer.TapesFolderName, tapeName + InputTapePlayer.Extension);
		if (root == null || !File.Exists(tapePath) || InputTapePlayer.Load(tapePath, demoMode: false) is not { } tape) {
			return null;
		}

		// DAT_004a9bf4, the AI's dwell jitter, is a process-wide global that DBSIM starts each mission from, one
		// mission per process. This engine keeps it static and flies several missions in one process, so a rig
		// built after another starts from wherever the last one left it; each starts from zero here.
		typeof(BehaviourBlock).GetField("_jitter", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0);

		string folder = Path.Combine(Path.GetTempPath(), "herculan-tests", "rig-" + Guid.NewGuid().ToString("N"));
		string scriptPath = tape.ExtractBundle(folder, installDataDirectory: null);

		var content = GameContent.MountSimulator(root, null);
		var scene = MissionScene.Load(content, scriptPath, folder);
		var audio = GameAudio.Create(content, scene.World.PresentationRandom, silent: true);
		audio.Attach(scene.World);

		var preferences = SimulatorPreferences.Load(folder) ?? SimulatorPreferences.Defaults();
		preferences.SaveEnabled = false;
		if (audio.Director is { } optionDirector) {
			preferences.RegisterHandler(Prefs.MusicOption,
				value => optionDirector.ApplyMusicOption(value != 0, preferences.Initialising));
			preferences.RegisterHandler(Prefs.SoundsOption,
				value => optionDirector.ApplySoundsOption(value != 0, preferences.Initialising));
		}

		preferences.RegisterHandler(Prefs.PilotMessageOption, value => audio.SpeechEnabled = value != 0);
		preferences.ApplyAll();

		SimMath.PerTickStepsScaled = true;

		var squad = scene.Objects
			.Where(o => o.Placement.IsPlayerLance && !ReferenceEquals(o.Object, scene.PlayerObject?.Object))
			.Take(SquadCommChannel.SlotCount)
			.ToList();

		var start = new SimulatorStart {
			InstallRoot = root,
			Disc = null,
			Content = content,
			Scene = scene,
			Audio = audio,
			Preferences = preferences,
			ScriptPath = scriptPath,
			DataDirectory = folder,
			ShellLaunch = null,
			TapePlayer = null,
			TapeRecorder = null,
			DemoTape = false,
			KeptWindowed = true,
			StartFullScreen = false,
			SoundAvailable = audio.Director != null,
			VoiceAvailable = true,
			SquadPlacements = squad,
			PilotingRazor = scene.PlayerObject is { Placement.TypeName: { } playerTypeName }
				&& HercLUT.GetByAbbrev(playerTypeName)?.Id == ControlsPanel.RazorTypeIndex,
		};

		var staged = new StagedStart();
		stage?.Invoke(scene, staged);
		return new SimulatorRig(start, staged, developer);
	}

	// SimulatorHost.LoadCockpitArt, less its reports.
	private static CockpitArt? LoadCockpitArt(SimulatorStart start) {
		var scene = start.Scene;
		return start.Mission.Player?.TypeName is { } pilotHerc
			? CockpitArt.Load(start.Content, pilotHerc, scene.Theater.PaletteName,
				scene.World.Objects.OfType<MechObject>().Select(m => m.Name).Distinct(),
				start.SquadPlacements
					.Where(o => o.Placement.PilotIndex >= 0)
					.Select(o => PilotRoster.BankName(PilotRoster.PortraitOf(o.Placement.PilotIndex)))
					.Distinct(),
				scene.Theater.ImpactPaletteName)
			: null;
	}

	/// <summary>One host frame's update, in <c>SimulatorHost.OnUpdate</c>'s order.</summary>
	public void Frame(double deltaSeconds = 1 / 60d) {
		Panels.AdvanceClock(deltaSeconds);
		Panels.ReadKeys(Keys, MissionOver);

		var controls = Keys;
		View.ReadCameraKey(controls, Displays.FlashCommHasKeyboard);

		var pilotMech = View.PilotMech;
		if (pilotMech != null && View.Chain != null && !Panels.AnyOpen && !MissionOver) {
			DeveloperKeys.Read(controls, World, pilotMech, View.Chain, tapePlaying: false, deltaSeconds);
		}

		if (View.Chain != null && !Panels.AnyOpen && !MissionOver) {
			View.ReadViewKeys(controls);
		}

		View.ReadOrbitDrag(null, imguiWantsMouse: false);
		Pilot.Update(controls, Panels.AnyOpen, controls);
		var keyPointer = Pointer.Pointer();
		Keyboard.Read(controls, Panels.AnyOpen, pilotInput: View.Piloting, (keyPointer.X, keyPointer.Y), Width, Height);

		ReadPointer(deltaSeconds);

		if (Scene.Targeting is { } playerTargeting) {
			playerTargeting.DropIfInvalid(cockpitShown: !View.ExternalViewActive);
			playerTargeting.PushToPilot();
		}

		Displays.HddCommand?.Update(TimeSpan.FromSeconds(deltaSeconds));
		if (Displays.Hud.Mfd == MfdMode.FlashComm) {
			Displays.FlashComm.Sync();
		}

		View.Advance(deltaSeconds, Art, Width, Height);

		Panels.RaisePendingMissionAlert(MissionOver);
		Staging.RaiseStatusAlert(Panels, World);
		if (Panels.TakeMissionEnding(out bool quitGame)) {
			MissionOver = true;
			QuitGame = quitGame;
		}

		ApplyLivePreferences();

		Audio.MessagesPaused = Panels.AnyOpen;
		Advance(deltaSeconds);

		bool preferencesUp = Panels.Preferences is { IsOpen: true };
		View.AdvancePanelOrbit(preferencesUp, preferencesUp && Panels.Controls is not { IsOpen: true }, deltaSeconds);

		if (View.InMachine) {
			View.UpdateKickAndShake(deltaSeconds);
		}

		View.PlaceCamera();
		var camera = View.Camera;
		Audio.SetListener(camera.Position, -camera.Yaw & 0xffff);
		Audio.Update(TimeSpan.FromSeconds(deltaSeconds));

		Displays.UpdateSquadVideos();
		Staging.AcquireTarget(pilotMech, Scene.Targeting);
		CockpitUpdate.Update(deltaSeconds);
	}

	// SimulatorHost.ReadPointer, less the system buttons' showing, which only the draw reads.
	private void ReadPointer(double deltaSeconds) {
		Panels.PrimePanelStickLatch();

		if (Panels.AnyOpen) {
			Panels.ReadPointer(Width, Height, Pilot.Joystick);
			Panels.Present();
			_cockpitInput.Drain(deltaSeconds, (_, _) => null);
			Displays.Hud = Displays.Hud with { PressedWidget = null };
		} else if (Art != null && !View.ExternalViewActive) {
			Commands.DrainClicks(_cockpitInput, deltaSeconds, Width, Height);
		}

		Panels.SyncPointer(Width, Height);

		if (!Panels.AnyOpen && Art != null && !View.ExternalViewActive) {
			foreach (var popped in Displays.PressFlashes.Service(Audio.CoarseTicks)) {
				_cockpitInput.PopUp(popped);
			}

			Displays.Hud = Displays.Hud with {
				PressedWidget = _cockpitInput.Depressed,
				FlashingWidgets = Displays.PressFlashes.Lit,
			};
		}
	}

	// SimulatorHost.ApplyLivePreferences, less the terrain texture, which only the draw reads.
	private void ApplyLivePreferences() {
		byte effectsDetail = Start.Preferences[Prefs.EffectsDetailOption];
		World.EffectsDetail = effectsDetail;
		if (Audio.Director is { } soundDirector) {
			soundDirector.DetailSetting = effectsDetail;
		}

		Audio.Messages.Mode = (MessageChannelMode)Start.Preferences[Prefs.ComputerMessageOption];
		if (Audio.Squad is { } squadChannel) {
			squadChannel.Port.Mode = (MessageChannelMode)Start.Preferences[Prefs.PilotMessageOption];
		}
	}

	// SimulationStepper.Advance's live path, with no tape and no recording.
	private void Advance(double deltaSeconds) {
		bool frozen = MissionOver || Panels.AnyOpen;
		if (DeveloperKeys.StepPending && !frozen) {
			World.Tick();
			View.AdvanceChain(DeveloperKeys);
			DeveloperKeys.FinishStep();
			_tickAccumulator = 0;
			return;
		}

		if (!frozen) {
			_tickAccumulator = Math.Min(_tickAccumulator + deltaSeconds, MaxAccumulatedSeconds);
		}

		while (!frozen && _tickAccumulator >= SecondsPerTick) {
			_tickAccumulator -= SecondsPerTick;
			if (DeveloperKeys.Frozen) {
				World.TickFrozen();
			} else {
				World.Tick();
			}

			View.AdvanceChain(DeveloperKeys);
		}
	}

	/// <summary>Runs <paramref name="frames"/> frames.</summary>
	public void Run(int frames) {
		for (int i = 0; i < frames; i++) {
			Frame();
		}
	}

	/// <summary>Holds <paramref name="keys"/> down for one frame, then lets them go for one.</summary>
	public void Tap(params Key[] keys) {
		Keys.Hold(keys);
		Frame();
		Keys.Release(keys);
		Frame();
	}

	/// <summary>A mouse event, as the window's listener queues one: where the pointer is and what it holds.</summary>
	public void Mouse(float x, float y, CockpitMouseButtons buttons) {
		Pointer.Set(x, y, buttons);
		_cockpitInput.Enqueue(x, y, buttons);
	}

	/// <summary>
	/// A left click on the first visible widget <paramref name="match"/> picks, at its centre, on this frame's
	/// layout: pressed and released before the next frame drains them. False when no such widget shows.
	/// </summary>
	public bool Click(Func<CockpitWidgetId, bool> match, CockpitMouseButtons button = CockpitMouseButtons.Left) {
		if (Art == null || CockpitWidgets.Visible(Art, Displays.Hud).Where(w => match(w.Id))
				.Select(w => (CockpitWidget?)w).FirstOrDefault() is not { } found) {
			return false;
		}

		var layout = CockpitScreenLayout.Create(Width, Height, Art, View.Pan.OffsetRows, View.Pan.TravelRows,
			View.Glance.OffsetPanels);
		if (layout.Surface(found.Surface) is not { } placed) {
			return false;
		}

		var (x, y) = placed.ArtToWindow((found.X0 + found.X1) / 2f, (found.Y0 + found.Y1) / 2f);
		Mouse(x, y, button);
		Mouse(x, y, CockpitMouseButtons.None);
		Frame();
		return true;
	}

	/// <summary>A left click on one of the two system buttons, which sit on the screen rather than on any art.</summary>
	public void ClickSystemButton(SystemButton button) {
		var rect = SystemButtons.Rect(button);
		var (x, y) = SystemButtons.Place(Width, Height).ToWindow((rect.X0 + rect.X1) / 2f, (rect.Y0 + rect.Y1) / 2f);
		Mouse(x, y, CockpitMouseButtons.Left);
		Mouse(x, y, CockpitMouseButtons.None);
		Frame();
	}

	/// <summary>
	/// Everything the frame's order can change, one line: the player's machine, every object's place, the view,
	/// the displays, the panels and what the fakes were asked to do.
	/// </summary>
	public string Snapshot() {
		var line = new StringBuilder();
		void Add(string name, object? value) =>
			line.Append(name).Append('=').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(' ');

		Add("tick", World.TickCount);
		Add("coarse", Audio.CoarseTicks);
		if (Player is { } mech) {
			Add("pos", mech.Position);
			Add("heading", mech.Heading);
			Add("throttle", mech.Throttle);
			Add("speed", mech.Speed);
			Add("twist", mech.TorsoTwistAngle);
			Add("pitch", mech.TorsoPitchAngle);
			Add("controls", mech.Controls);
			Add("energy", mech.EnergyPool);
			Add("scanner", mech.Scanner);
			Add("target", mech.Target?.ListIndex);
			Add("lock", mech.LockAcquired);
			Add("shields", $"{mech.Shields.Front}/{mech.Shields.Rear}/{mech.Shields.Balance}");
			Add("damage", mech.DamageTaken);
			var weapons = mech.Weapons;
			Add("weapons", $"{weapons.Selected}/{weapons.Group}/{weapons.AutoTrack}/{weapons.SingleFire}");
			foreach (var mount in weapons.Slots) {
				Add("mount", mount == null ? "-" : $"{mount.Name}:{mount.Charge}:{mount.ChargeTarget}:{mount.RefireTimer}:{mount.Linked}");
			}

			Add("centring", $"{mech.CenteringTorso}/{mech.CenteringBody}");
		}

		foreach (var simObject in World.Objects) {
			Add("o", $"{simObject.ListIndex}:{simObject.Position}:{simObject.Heading}:{simObject.Destroyed}");
		}

		Add("shots", $"{World.Projectiles.Count}/{World.Effects.Count}");
		Add("selected", Scene.Targeting?.Selected?.ListIndex);
		Add("steer", World.MissileSteer);
		Add("alert", World.PendingMissionAlert);

		Add("piloting", View.Piloting);
		Add("external", View.ExternalViewActive);
		Add("chain", View.Chain?.Mode);
		Add("pan", View.Pan.OffsetRows);
		Add("glance", View.Glance.OffsetPanels);
		Add("camera", $"{View.Camera.Position}:{View.Camera.Yaw}:{View.Camera.Pitch}:{View.Camera.Roll}");
		Add("kick", $"{View.Kick.OffsetPixels}/{View.Shake.OffsetPixels}");

		var hud = Displays.Hud;
		Add("hud", $"{hud.Mfd}/{hud.Hdd}/{hud.HddDamage}/{hud.MissionTime}/{hud.SpeedKph}/{hud.Throttle}/{hud.TorsoTwist}/"
			+ $"{hud.Heading}/{hud.ShieldFront}/{hud.ShieldRear}/{hud.EnergyFraction}/{hud.ChainGroup}/{hud.AutoTrack}");
		Add("pressed", hud.PressedWidget);
		Add("flashing", hud.FlashingWidgets == null ? null : string.Join(",", hud.FlashingWidgets));
		Add("indicator", hud.Target);
		Add("message", hud.Message);
		Add("pilotMessage", hud.PilotMessage);
		Add("powerup", hud.MfdPowerUpFrame);
		Add("scannerRange", hud.Scanner.RangeIndex);
		Add("missileCam", hud.MissileCamHolding);
		Add("gauge", Displays.ThrottleGauge);
		Add("hddSubject", Displays.HddSubjectSlot);
		Add("hddPending", Displays.PendingHddPress);
		if (Displays.HddCommand is { } command) {
			Add("command", $"{command.SelectedOrder}/{command.SelectedPilot}/{command.AwaitingPick}");
		}

		Add("flashComm", $"{Displays.FlashComm.SelectedRow}/{Displays.FlashComm.SelectedVerb}");

		Add("panels", $"{Panels.StatusAlert?.IsOpen}/{Panels.StatusAlert?.Status}/{Panels.Objectives?.IsOpen}/"
			+ $"{Panels.Preferences?.IsOpen}/{Panels.Controls?.IsOpen}");
		Add("over", $"{MissionOver}/{QuitGame}");
		Add("devkeys", $"{DeveloperKeys.Frozen}/{DeveloperKeys.StepPending}");
		Add("pointer", Pointer.Pointer());
		Add("warps", Pointer.Warps);
		Add("system", $"{SystemButtonPresses.Manual}/{SystemButtonPresses.FullScreen}");
		return line.ToString();
	}
}

/// <summary>A keyboard whose held keys a test sets.</summary>
sealed class ScriptedKeys : IKeyState {
	private readonly HashSet<Key> _down = new();

	public bool IsKeyPressed(Key key) => _down.Contains(key);

	public void Hold(params Key[] keys) => _down.UnionWith(keys);

	public void Release(params Key[] keys) => _down.ExceptWith(keys);

	public void ReleaseAll() => _down.Clear();
}

/// <summary>A pointer a test places, which remembers each warp as the window's would.</summary>
sealed class ScriptedPointer : IPointerDevice {
	private (float X, float Y, CockpitMouseButtons Buttons) _state = (0f, 0f, CockpitMouseButtons.None);

	public int Warps { get; private set; }

	public (float X, float Y, CockpitMouseButtons Buttons) Pointer() => _state;

	public void WarpPointer(float x, float y) {
		if (float.IsNaN(x) || float.IsNaN(y)) {
			return;
		}

		Warps++;
		_state = (x, y, _state.Buttons);
	}

	public void Set(float x, float y, CockpitMouseButtons buttons) => _state = (x, y, buttons);
}

/// <summary>A stick whose reading a test sets.</summary>
sealed class ScriptedStick : IJoystickSource {
	public JoystickCapabilities Capabilities { get; set; } = JoystickCapabilities.None;

	public JoystickReading Reading { get; set; } = JoystickReading.Neutral;

	public JoystickReading Read() => Reading;
}

/// <summary>Counts what the system buttons asked of the window.</summary>
sealed class SystemButtonLog : ISystemButtonActions {
	public int Manual { get; private set; }
	public int FullScreen { get; private set; }

	public void OpenManual() => Manual++;

	public void ToggleFullScreen() => FullScreen++;
}
