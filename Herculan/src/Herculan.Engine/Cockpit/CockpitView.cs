using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Settings;
using Herculan.Engine.Sim;
using Herculan.Engine.View;
using Silk.NET.Input;
using static Herculan.Engine.Input.KeyChords;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// Where the player is looking from: inside the machine or the observer camera, and inside, which of the
/// cockpit view manager's views — forward, a side glance, panned down to the Heads-Down Display, or one of the
/// external views — with the step kick and the damage shake riding the projection centre. Owns the camera
/// every pass draws from.
/// </summary>
public sealed class CockpitView {
	private readonly MissionScene _scene;

	// The mouse outside view's orbit, for the tweak that swaps retail's for it (see ExternalCamera). Held
	// here because it is state the host owns across frames, as the view kick is. Yaw starts directly behind the machine and pitch level with
	// ExternalCamera's own fixed framing; both only move once the player drags.
	private float _orbitYaw = 0f;
	private float _orbitPitch = ExternalCamera.DefaultOrbitPitchRadians;
	private bool _orbitDragging = false;
	private System.Numerics.Vector2 _orbitLastMouse = System.Numerics.Vector2.Zero;

	// The tick's camera axes and trigger, built with the machine's controls each frame and handed to the
	// chain on each tick — Sim_PollPlayerInput's arguments to Cam_Steer (00401c74).
	private PilotAxes _cameraAxes = PilotAxes.Centred;
	private bool _cameraTrigger = false;

	// Time owed to the orbit behind the preferences panel, spent in whole steps — see AdvancePanelOrbit.
	private double _panelOrbitSeconds;

	private readonly KeyLatch _externalViewKey = new();
	private readonly KeyLatch _viewControlKey = new();
	private readonly KeyLatch _viewNextKey = new();

	public CockpitView(MissionScene scene, CockpitArt? art, StagedStart staging) {
		_scene = scene;
		PilotMech = scene.PlayerMech;
		Piloting = PilotMech != null;
		ViewGeometry = art?.ViewGeometry;

		// How far the display window travels down the cockpit canvas to reach the Heads-Down Display, read
		// from the herc's own vue\<HERC>.VUE rather than assumed. Every retail file says 237 authored rows
		// (474 device), but reading it is what makes the pan the file's statement instead of this host's.
		Pan = new CockpitPan(ViewGeometry?.HeadsDownTravelY ?? CockpitViewGeometry.DefaultHeadsDownOriginY);

		// And the damage shake, which rides the same centre as the step kick and is ticked beside it in the
		// original's own cockpit pass — see CockpitHitShake.
		Shake = new CockpitHitShake(scene.World.PresentationRandom);

		// Straight to the pan rather than through RequestHeadsDown, whose external-view gate reads state not
		// yet set up here — at launch the glance is at rest and the external view is not up anyway.
		if (staging.HeadsDown) {
			Pan.Request(headsDown: true);
			Pan.Advance(CockpitPan.DurationSeconds);
		}
	}

	/// <summary>
	/// The player's machine. A HERC has no velocity vector — the walk and run animations' root motion is what
	/// moves it — so piloting is the arrow keys on the throttle and the stick, and the machine covers whatever
	/// ground its own gait covers. See docs/retail/simulation/mech-locomotion.md.
	/// </summary>
	public MechObject? PilotMech { get; }

	/// <summary>Whether the player is in the machine rather than flying the observer camera, which only a mission with no player machine leaves them in.</summary>
	public bool Piloting { get; }

	/// <summary>Whether the player is in a machine at all — the cockpit's own per-frame work runs only then.</summary>
	public bool InMachine => Piloting && PilotMech != null;

	public CockpitViewGeometry? ViewGeometry { get; }

	public CockpitPan Pan { get; }

	/// <summary>The sideways glance to the left and right windows — see CockpitGlance.</summary>
	public CockpitGlance Glance { get; } = new();

	/// <summary>The step kick. It rides the projection centre alongside the pan, which is where the original puts it too — see CockpitViewKick.</summary>
	public CockpitViewKick Kick { get; } = new();

	public CockpitHitShake Shake { get; }

	/// <summary>
	/// The chain of views [V] and the joystick's OUTSIDE VIEW and CHASE VIEW step through, and the camera behind
	/// each — see ExternalViewChain. [Esc], [Enter], [Tab] and [N] reach it too, but only from the external view,
	/// where the cockpit's own widgets are off.
	/// </summary>
	public ExternalViewChain? Chain { get; private set; }

	/// <summary>The camera every pass this frame draws from.</summary>
	public Camera Camera { get; } = new();

	/// <summary>
	/// Builds the external view chain once the cockpit's displays are up; <c>--external</c> is [V] pressed
	/// at launch.
	/// </summary>
	public void BuildChain(bool startExternal) {
		Chain = PilotMech != null ? new ExternalViewChain(PilotMech) : null;
		if (startExternal) {
			Chain?.ToggleOutside(fromKeyboard: true);
		}
	}

	// Whether the cockpit view manager is in its view 4 — any of the external views, with no cockpit drawn: [V],
	// or the developer keys viewing an object other than the player's machine. Only meaningful while piloting —
	// the free camera already draws the whole scene with no cockpit over it.
	public bool ExternalViewActive => Chain is { ExternalViewUp: true } && Piloting && PilotMech != null;

	// Whether the outside view is the tweak's mouse orbit rather than retail's — see ExternalCamera. Never
	// once the player-death camera has the view, nor behind the preferences panel: the tweak replaces the
	// outside view the player steers, not the ones the game steers itself.
	public bool MouseOutsideView =>
		Chain is { Mode: ExternalViewMode.Outside, DeathCameraRunning: false, PanelOrbitRunning: false }
		&& TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.MouseExternalView);

	/// <summary>Whether the camera is circling the player behind the [F12] preferences panel — see <see cref="AdvancePanelOrbit"/>.</summary>
	public bool PanelOrbitUp => Chain is { PanelOrbitRunning: true };

	// Whether the cockpit's widgets are off, as CockpitView_ApplyViewState turns them off for view 4: the
	// keys CockpitWidgets_HandleCommand would have taken fall through to the dispatcher, or do nothing.
	// The tweak's outside view keeps them, being this engine's own.
	public bool CockpitWidgetsOff => ExternalViewActive && !MouseOutsideView;

	// Whether the controls drive the camera rather than the machine. Never in the tweak's outside view,
	// where the mouse does.
	public bool ControlsDriveCamera => Chain is { InputDrivesCamera: true } && !MouseOutsideView;

	// The controls go to a camera or to an electro-optical round: InputDrivesCamera or DAT_004d25aa, the
	// pair Input_BuildPlayerDevice tests together.
	public bool ControlsOnCamera => ControlsDriveCamera || _scene.World.PlayerMissile.Flown;

	// The two view changes share CockpitView_QueueViewCommand's gate on the current view: the pan down
	// starts only from the forward view, and so does a glance. Every request for either goes through
	// these two so no input path can start one while the other is out. The way back is gated only by the
	// external view — neither can be out while the other is — whose view 4 none of the commands accepts.
	public void RequestHeadsDown(bool headsDown) {
		if (ExternalViewActive) {
			return;
		}

		if (!headsDown || Glance.AtForward) {
			Pan.Request(headsDown);
		}
	}

	public void CommandGlance(GlanceSide side) {
		if (!ExternalViewActive && Pan.AtForward && !Pan.HeadsDownRequested) {
			Glance.Command(side);
		}
	}

	/// <summary>Whether a glance or the pan down is out, which [Esc] brings back rather than panning down.</summary>
	public bool AwayFromForward => !Glance.AtForward || Pan.HeadsDownRequested;

	/// <summary>
	/// The manual's [Esc] as "the way back" from the side windows and the Heads-Down Display alike — view
	/// command 6 from a glance, 1 from heads-down.
	/// </summary>
	public void ReturnToForward() {
		Glance.Return();
		RequestHeadsDown(headsDown: false);
	}

	/// <summary>
	/// [Esc] once no modal panel has taken it: out of the external view, where with the cockpit's widgets off
	/// scancode 1 falls through them to the dispatcher's own case (see <see cref="ExternalViewChain.Escape"/>), else
	/// CockpitWidgets_HandleCommand (00432bc8)'s own case 1 — view command 0 from the forward view, so it pans down
	/// to the Heads-Down Display, and <see cref="ReturnToForward"/> from a glance or heads-down. See
	/// docs/retail/key-bindings.md#displays-and-views.
	/// </summary>
	public void Escape(bool hasCockpit) {
		if (ExternalViewActive) {
			Chain?.Escape();
		} else if (hasCockpit && AwayFromForward) {
			ReturnToForward();
		} else if (hasCockpit) {
			RequestHeadsDown(headsDown: true);
		}
	}

	/// <summary>
	/// The camera's half of the input, from the controls just built for the machine: the steering and
	/// throttle axes after Backturn, and the trigger.
	/// </summary>
	public void TakeCameraAxes(MechControls built) {
		_cameraAxes = new PilotAxes(built.Turn, built.Throttle);
		_cameraTrigger = built.Fire;
	}

	/// <summary>
	/// The view chain's own keys, which reach it only while no modal panel holds the input. Each acts on every
	/// key-down event, auto-repeats included, so a held [V] goes in and out of the outside view at the repeat
	/// rate, as the original's does.
	/// </summary>
	public void ReadViewKeys(IKeyState controls) {
		if (Chain == null) {
			return;
		}

		// [V], scancode 0x2f: out to the outside view and back. The cockpit is not drawn in the
		// external view, and the machine — left out of the cockpit view because its geometry wraps the
		// eye — is.
		if (_externalViewKey.PressOrRepeat(controls, Key.V, !CtrlHeld(controls))) {
			Chain.ToggleOutside(fromKeyboard: true);
		}

		// [Enter] and [Tab] swap the controls between the camera and the machine, and [N] moves the
		// outside view on to the next squadmate. All three are cockpit keys the widgets claim first,
		// so they reach these cases only while the widgets are off.
		bool unmodified = Unmodified(controls);
		bool viewControlKey = _viewControlKey.Press(unmodified
				&& (controls.IsKeyPressed(Key.Enter) || controls.IsKeyPressed(Key.Tab)))
			| (unmodified && (controls.IsKeyRepeated(Key.Enter) || controls.IsKeyRepeated(Key.Tab)));
		if (viewControlKey && CockpitWidgetsOff) {
			Chain.ToggleCameraControl();
		}

		if (_viewNextKey.PressOrRepeat(controls, Key.N, !CtrlHeld(controls) && !AltHeld(controls))) {
			Chain.NextSquadmate(_scene.World.Objects);
		}
	}

	/// <summary>
	/// The mouse outside view's orbit: hold the left mouse button and drag to swing the eye around the
	/// machine, always aimed back at it. Gated the same way the cockpit's own clicks are — nothing to
	/// drag while the pointer is over the debug panel — and only while that view is actually up, so a
	/// drag started before switching views doesn't carry over.
	/// </summary>
	public void ReadOrbitDrag(IMouse? mouse, bool imguiWantsMouse) {
		if (MouseOutsideView && ExternalViewActive && mouse != null && !imguiWantsMouse) {
			bool dragging = mouse.IsButtonPressed(MouseButton.Left);
			var mousePosition = mouse.Position;
			if (dragging && _orbitDragging) {
				var delta = mousePosition - _orbitLastMouse;
				_orbitYaw += delta.X * ExternalCamera.OrbitSensitivity;
				_orbitPitch = Math.Clamp(
					_orbitPitch - delta.Y * ExternalCamera.OrbitSensitivity,
					-ExternalCamera.MaxOrbitPitchRadians, ExternalCamera.MaxOrbitPitchRadians);
			}
			_orbitDragging = dragging;
			_orbitLastMouse = mousePosition;
		} else {
			_orbitDragging = false;
		}
	}

	/// <summary>The pan and the glance, a frame on.</summary>
	public void Advance(double deltaSeconds, CockpitArt? art, int framebufferWidth, int framebufferHeight) {
		Pan.Advance(deltaSeconds);
		if (art != null) {
			Glance.Advance(deltaSeconds,
				CockpitScreenLayout.GlanceReachPanels(framebufferWidth, framebufferHeight, art));
		}
	}

	/// <summary>
	/// The chain's share of each tick, after the simulation's — see ExternalViewChain.Advance.
	///
	/// <para>With the player's machine destroyed, Sim_MainTick runs the death camera where it would have polled
	/// the objectives, and not under Alt+S — nor on the tick Alt+keypad + lets through, which freezes again
	/// before it gets there. When the camera's countdown runs out it raises status 2 and ends the mission
	/// whatever the answer, which the one-button panel's own table already does.</para>
	/// </summary>
	public void AdvanceChain(DeveloperKeys developerKeys) {
		bool deathCamera = PilotMech is { Destroyed: true } && !developerKeys.Frozen
			&& !developerKeys.StepPending;
		if (Chain?.Advance(_cameraAxes.Steer, _cameraAxes.Throttle, _cameraTrigger,
				ControlsDriveCamera, _scene.World.Terrain, deathCamera) == true) {
			_scene.World.Mission.PendingAlert = MissionStatus.PlayerDestroyed;
		}
	}

	/// <summary>
	/// The view behind the [F12] preferences panel, once a frame: the camera taken out to circle the player's machine
	/// as the panel goes up and given back as it comes down (<see cref="ExternalViewChain.BeginPanelOrbit"/>), and
	/// turned while <paramref name="preferencesLoopRunning"/> — not while the CONTROLS panel is up, whose own loop
	/// runs inside the preferences panel's and never reaches its draw hook. The observer camera is this engine's own,
	/// and the orbit leaves it alone.
	///
	/// <para>Retail turns the orbit once per pass of the panel's loop, which never goes through the simulator's 40 ms
	/// frame wait, so it turns as fast as the machine draws. Here it turns at the simulation's 25 steps a second, the rate retail's own frame loop
	/// is capped at (<see cref="SimWorld.TicksPerSecond"/>), however fast the host draws.</para>
	/// </summary>
	public void AdvancePanelOrbit(bool preferencesUp, bool preferencesLoopRunning, double deltaSeconds) {
		if (Chain is not { } chain) {
			return;
		}

		bool wanted = preferencesUp && InMachine;
		if (wanted && !chain.PanelOrbitRunning) {
			chain.BeginPanelOrbit(_scene.World.Terrain);
			_panelOrbitSeconds = 0;
			return;
		}

		if (!wanted) {
			if (chain.PanelOrbitRunning) {
				chain.EndPanelOrbit();
			}

			return;
		}

		if (!preferencesLoopRunning) {
			return;
		}

		// The same catch-up cap as the simulation's own accumulator, so a stall does not spin it in a burst.
		_panelOrbitSeconds = Math.Min(_panelOrbitSeconds + deltaSeconds, PanelOrbitMaxOwedSeconds);
		while (_panelOrbitSeconds >= PanelOrbitStepSeconds) {
			_panelOrbitSeconds -= PanelOrbitStepSeconds;
			chain.StepPanelOrbit(_scene.World.Terrain);
		}
	}

	private const double PanelOrbitStepSeconds = 1.0 / SimWorld.TicksPerSecond;
	private const double PanelOrbitMaxOwedSeconds = 0.25;

	/// <summary>
	/// The kick and the shake are the pilot's own, so they run only from inside the cockpit. The original's
	/// view mode 4 drops a shake in progress rather than pausing it, and ignores the hits taken meanwhile,
	/// which is what Reset does here.
	/// </summary>
	public void UpdateKickAndShake(double deltaSeconds) {
		var pilotMech = PilotMech!;
		Shake.FlashSurvivesRestart =
			TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.FlashThroughSecondHit);
		if (ExternalViewActive) {
			Kick.Reset();
			Shake.Reset(pilotMech.CockpitHits);
		} else {
			Kick.Update(deltaSeconds, pilotMech.Footfalls);
			Shake.Update(deltaSeconds, pilotMech.CockpitHits);
		}
	}

	/// <summary>The debug panel's eye-height pin, which the cockpit eye goes through.</summary>
	public SteadyEye SteadyEye { get; } = new();

	/// <summary>Puts this frame's camera where the view is: the cockpit eye, the chain's camera, the mouse orbit or the observer camera.</summary>
	public void PlaceCamera() {
		if (!InMachine) {
			_scene.Camera.ApplyTo(Camera);
			return;
		}

		var pilotMech = PilotMech!;
		if (MouseOutsideView && ExternalViewActive) {
			// The tweak's outside view: the mouse orbit round whatever the chain is viewing — see
			// ExternalCamera.
			ExternalCamera.Place(Camera, Chain!.Watched, _scene.World.Terrain,
				BinaryAngle.FromRadians(_orbitYaw), BinaryAngle.FromRadians(_orbitPitch));
		} else if (Chain is { } chain
				&& (chain.Camera.Mode != ViewCameraMode.Attached || chain.Camera.Target != pilotMech)) {
			// Every view but the player's own cockpit is the chain's camera, placed on the last tick —
			// see ViewCamera. The cockpit stays below, where the eye is pinned and read every frame.
			chain.Camera.ApplyTo(Camera);
		} else {
			// The eye rides the model node the type record names, so the walk cycle's bob comes with it —
			// see MechObject.EyePosition. Camera yaw runs opposite to a simulation heading; see
			// MissionScene.TransformOf. The debug panel's steady eye can pin its height — see SteadyEye.
			var eyeFrame = pilotMech.EyeTransform;
			var eye = SteadyEye.PinEyeHeight(
				new Vec3i(eyeFrame.X, eyeFrame.Y, eyeFrame.Z), pilotMech.Position);

			// Orientation comes off the eye node too, not off the machine's heading: the camera node
			// hangs below the two nodes the torso sequences drive, so twisting and pitching the turret
			// turns the view without anything here having to add the angles in.
			//
			// All three angles are taken, roll included, which is what the cockpit branch of
			// Cam_Update (004011a0) does: it converts the pilot node's world matrix with Transform_RotationToEuler (0047f894) and
			// stores the whole triple in the view. A walking machine's node barely rotates, but one
			// turning on the spot rolls it several degrees a step — the rock through a turn-in-place.
			var look = eyeFrame.ToEuler();
			Camera.Position = eye;
			Camera.Yaw = -look.Z & 0xffff;
			Camera.Pitch = look.X;
			Camera.Roll = look.Y;
		}

		// Keep the observer camera on the machine, so switching to it lands where the player was
		// rather than wherever it was parked at mission start.
		_scene.Camera.Position = Camera.Position;
		_scene.Camera.Heading = Camera.Yaw;
	}
}
