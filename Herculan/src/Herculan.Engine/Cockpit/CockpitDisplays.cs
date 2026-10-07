using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.World;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The cockpit's displays and what they show: the HUD state every widget draws from, the MFD's screens, the
/// Heads-Down Display's command and damage screens, the squad's comm boxes, and the instruments' own clocks.
/// All of it is per-mission and built once here; <see cref="PlayerCockpitUpdate"/> refreshes it each frame and
/// <see cref="CockpitCommands"/> is what the player's keys, clicks and buttons change it through.
/// </summary>
public sealed class CockpitDisplays {
	private readonly CockpitView _view;
	private readonly GameAudio _audio;

	public CockpitDisplays(SimulatorStart start, CockpitArt? art, CockpitView view, SimulatorStaging staging) {
		_view = view;
		_audio = start.Audio;
		Art = art;
		var scene = start.Scene;
		var squadPlacements = start.SquadPlacements;

		// The Heads-Down Display's command display: the map camera, the mission's terrain raster, and the
		// three squad comm boxes. All three are per-mission, so they are built once here.
		if (art?.HeadsDownLayout is { } commandLayout && scene.World is { } commandWorld) {
			var mapBounds = MissionBox.Of(scene.Mission.Coordinates);
			var mapViewport = commandLayout.MapViewport;
			var squad = squadPlacements.Select(o => o.Object).ToList();

			// The same relief through the damage-flash palette. Rasterized here beside the ordinary one
			// rather than on each toggle: it is one texel per terrain cell, so a second copy is small, and
			// rebuilding it inside a flash would stutter.
			HddMapFlashRaster = HddMapRaster.Build(commandWorld.Terrain, mapBounds, art.FlashPaletteEntry);

			HddCommand = new HddCommandScreen(
				new HddMapView(mapBounds, mapViewport.Width, mapViewport.Height),
				HddMapRaster.Build(commandWorld.Terrain, mapBounds, art.PaletteEntry),
				squad,
				commandWorld);

			staging.StageCommandDisplay(HddCommand, squad, commandWorld, scene, mapViewport.Width, mapViewport.Height);
		}

		// The MFD's FLASH COMM page and the comm channel behind it. The page is six order rows and a
		// selection; the channel is the three video boxes and the queue in front of them, which is what turns
		// a squadmate's reply into static, a talking portrait and a recorded line. Both are per-mission: the
		// boxes have to know who is in them, and that comes off each machine's own pilot index.
		var pilotRoster = PilotRoster.Load(start.Content);
		SquadComm = new SquadCommChannel(start.Content, pilotRoster, scene.World.PresentationRandom,
			start.Mission.Header.TrainingMissionNumber);

		for (int slot = 0; slot < squadPlacements.Count; slot++) {
			SquadComm.Seat(slot, squadPlacements[slot].Placement.PilotIndex, squadPlacements[slot].Object);
			SquadSeats[slot] = squadPlacements[slot].Object;
		}

		SquadCount = squadPlacements.Count;

		// A training mission's instructor speaks from loose files on the disc, in the language's voice folder the
		// cockpit computer's speech is read from too.
		string voiceFolder = ComputerVoice.VoiceFolder(start.Content.Language);
		start.Audio.InstructorClip = (trainingMission, messageId) =>
			InstructorVoice.ReadClip(start.InstallRoot, start.Disc, voiceFolder, trainingMission, messageId);
		start.Audio.AttachSquad(SquadComm);

		// A comm box captions itself with its pilot's roster name, the same one the MFD's transmission plate
		// carries — both are the gauge's own +0x137.
		if (HddCommand != null) {
			HddCommand.PilotNameOf = SquadComm.Name;
		}

		staging.StageFlashComm(FlashComm, scene.World);
		if (pilotRoster == null) {
			Console.Error.WriteLine($"No {PilotRoster.ResourceName} — the comm boxes have no names and no portraits.");
		}

		// Whose herc the damage detail is inspecting — the display's subject selector, which starts on the
		// player and which the left and right arrows step. See HddDamageSubject. A --hdd-subject naming an
		// empty squad slot, which no step can land on, starts on the player instead.
		var stagingOptions = staging.Start;
		HddSubjectSlot = stagingOptions.HddSubject is > HddDamageSubject.PlayerSlot and < HddDamageSubject.TargetSlot
			&& SquadSeats[stagingOptions.HddSubject - 1] == null
			? HddDamageSubject.PlayerSlot
			: stagingOptions.HddSubject;

		// The cockpit readouts' live values. The hardpoint names come from the shell weapon catalog keyed by
		// player.mec's own hardpoint ids; speed, throttle, turret, the shield numbers and the energy bar are
		// taken off the piloted machine each frame. What is left sits at the power-up defaults in
		// CockpitHudState.Default until the sim carries the state behind it.
		Hud = CockpitHudState.Default with { Hdd = stagingOptions.HddPage, HddDamage = stagingOptions.HddDamageView };
		if (stagingOptions.Mfd is { } startMfdMode) {
			Hud = Hud with { Mfd = startMfdMode };
		}

		// The weapon panel, off the piloted machine's own mounts, which are already built, in cockpit-row
		// order. See WeaponRowState.
		if (art?.Gau is { } weaponGau && scene.PlayerMech is { } armedMech) {
			Hud = Hud with {
				Weapons = WeaponRowState.Build(armedMech.Weapons, weaponGau.WeaponListTotal, art.Strings),
			};

			staging.StageWeapons(armedMech);
		}

		// What F1's SELECT walks: the machine being flown, then the squadmates in comm-box order, taken
		// once as the mission starts.
		StatusRoster = new MfdStatusRoster(view.PilotMech, SquadSeats.Take(SquadCount));

		// The console's throttle slider and the machine's throttle setting are two-way bound, so the gauge's
		// own value is state in its own right: it is what the machine reads on any frame the machine did not
		// itself move the throttle. See MechObject.ExchangeCockpitThrottle.
		ThrottleTrack = art != null ? Engine.Cockpit.ThrottleTrack.From(art) : null;
		ThrottleGauge = stagingOptions.Throttle;
	}

	public CockpitArt? Art { get; }

	/// <summary>What every cockpit widget draws from this frame.</summary>
	public CockpitHudState Hud { get; set; }

	public HddCommandScreen? HddCommand { get; }

	/// <summary>The heads-down map's relief through the damage-flash palette, built beside the ordinary one.</summary>
	public HddMapRaster? HddMapFlashRaster { get; }

	public MfdFlashCommScreen FlashComm { get; } = new();

	public SquadCommChannel SquadComm { get; }

	/// <summary>Who sits in each comm box, in comm-box order; empty boxes are null.</summary>
	public SimObject?[] SquadSeats { get; } = new SimObject?[SquadCommChannel.SlotCount];

	/// <summary>Each comm box's own picture this frame.</summary>
	public SquadTransmission?[] PilotVideos { get; } = new SquadTransmission?[SquadCommChannel.SlotCount];

	public int SquadCount { get; }

	/// <summary>Which selector slot the damage detail is inspecting.</summary>
	public int HddSubjectSlot { get; set; }

	/// <summary>
	/// A Heads-Down Display button pressed while the sensor dropout has the display dark. The original's
	/// press handler (HddDisplay_HandleWidgetPress, 0044a178) returns before it acts and before it clears
	/// the press for anything but the two page buttons, so the last such press waits and fires the first
	/// frame the display is back — see <see cref="CockpitCommands.ApplyHddClick"/>.
	/// </summary>
	public HddLayout.Widget? PendingHddPress { get; set; }

	/// <summary>
	/// The MFD switching itself to the missile camera, and the camera screen itself — see MfdMissileCam.
	/// Every change of screen goes through <see cref="SetMfdMode"/>, which is how the switch learns of one
	/// made by hand.
	/// </summary>
	public MfdMissileCamSwitch MissileCamSwitch { get; } = new();
	public MfdMissileCamScreen MissileCamScreen { get; } = new();
	public bool MissileCamUpdatedLastFrame { get; set; }

	public MfdStatusRoster StatusRoster { get; }

	/// <summary>What F1 and F5 show between their repaints, which the original makes only every 30 coarse ticks.</summary>
	public MfdStatusRefresh StatusRefresh { get; } = new();

	/// <summary>
	/// The front window's TIME: readout. It is driven from the frames the gunsight is painted on, which is what
	/// stalls it in the external view, exactly as the original's does.
	/// </summary>
	public MissionClock Clock { get; } = new();

	/// <summary>
	/// The nav marker the player drops on themselves with [Alt+D], and the second subject the front window's
	/// waypoint indicators can be pointing at. Cockpit-view state, not the machine's, so it lives here beside
	/// the clock.
	/// </summary>
	public NavMarker NavMarker { get; } = new();

	public ThrottleTrack? ThrottleTrack { get; }
	public short ThrottleGauge { get; set; }

	/// <summary>
	/// The compass winding up from north over the power-up, on a walking machine only — the sweep decides that
	/// for itself off the same FlyerFlag the engine hum is gated on.
	/// </summary>
	public HeadingTapeSweep? HeadingSweep { get; private set; }

	/// <summary>
	/// The weapon rows winking on and the shield rings filling, the same sequence's. A --screenshot run starts
	/// with them finished: its warm-up is half a second, and the last row arms at 3.2 s.
	/// </summary>
	public CockpitPowerUp PowerUp { get; private set; } = CockpitPowerUp.Finished;

	/// <summary>The displays' sensor dropouts, one per display, for the whole mission.</summary>
	public CockpitDropouts Dropouts { get; } = new();

	/// <summary>The buttons a press made for the player holds down for a moment — see <see cref="FlashPress"/>.</summary>
	public PressFlashes<CockpitWidgetId> PressFlashes { get; } = new();

	/// <summary>
	/// Shows <paramref name="id"/> pressed for the next <see cref="PressFlashes{TId}.FlashTicks"/> coarse ticks,
	/// as Widget_PressChild (00438d9c) does for every button it presses — a key, a joystick button, or a click on
	/// a list that presses the display's XMIT.
	///
	/// <para>A flash only shows on a button whose paint reads the press byte — see
	/// <see cref="CockpitHudState.ShowsPressed"/>. The original flashes every button it presses for the player;
	/// the callers here leave out the ones that draw nothing from it — the latching MFD and Heads-Down Display
	/// buttons, the weapon rows and the shield facings.</para>
	/// </summary>
	public void FlashPress(CockpitWidgetId id) => PressFlashes.Flash(id, _audio.CoarseTicks);

	/// <summary>
	/// The cockpit powers up the moment the player has a machine — the start-up sequence and, for a flyer, the
	/// engine hum that runs for the rest of the mission. See GameAudio.PowerUp.
	///
	/// <para>The listener has to be placed first. The hum is positional and PowerUp is a one-shot: SoundDirector
	/// refuses to start a source past its catalog row's cutoff range, so with the listener still at its default
	/// origin and the machine tens of thousands of units away at its mission spawn, the hum would be judged
	/// inaudible, never started, and never retried. The camera is not positioned yet at this point in setup, so
	/// the machine's own eye stands in — which is where the camera opens anyway. The heading sweep and the
	/// power-up need the tick the power-up began on, which is why they are built here rather than with the
	/// cockpit.</para>
	/// </summary>
	public void PowerUpCockpit(GameAudio audio, bool screenshotRun) {
		if (_view.PilotMech is not { } pilotMech) {
			return;
		}

		audio.SetListener(pilotMech.EyePosition, pilotMech.Heading);
		audio.PowerUp(pilotMech);
		HeadingSweep = HeadingTapeSweep.ForPowerUp(pilotMech, audio.CoarseTicks);
		if (!screenshotRun) {
			PowerUp = CockpitPowerUp.ForPowerUp(pilotMech, audio.CoarseTicks);
		}
	}

	public void SetMfdMode(MfdMode requested) => Hud = Hud with {
		Mfd = MissileCamSwitch.SetMode(Hud.Mfd, requested),
	};

	/// <summary>MfdDisplay_CycleScannerRange (00446fc8): the next of the three scanner ranges, wrapping.</summary>
	public void CycleScannerRange() {
		Hud = Hud with {
			Scanner = Hud.Scanner with {
				RangeIndex = MfdScanner.NextRangeIndex(Hud.Scanner.RangeIndex),
			},
		};
	}

	/// <summary>
	/// A destroyed squadmate's box goes to static. The original's idle paint reads the machine's own destroyed
	/// flag, so the channel is handed it fresh each frame.
	/// </summary>
	public void UpdateSquadVideos() {
		for (int slot = 0; slot < SquadCommChannel.SlotCount; slot++) {
			SquadComm.SetDestroyed(slot, SquadSeats[slot] is { Destroyed: true });
			PilotVideos[slot] = SquadComm.Video(slot);
		}
	}

	// Whether the command display is down and holding the letter keys. Both the split that leaves the
	// arrows scrolling its map and the one that leaves [T] as an order hotkey rather than the ATT toggle
	// read it; the original has no such clash, its own screens owning the keyboard outright while up.
	// Neither display holds anything from the external view, which leaves the pan where it was but takes
	// the cockpit's widgets, and the keys they would have taken, off.
	public bool HddCommandHasKeyboard =>
		HddCommand != null && _view.Pan.AtHeadsDown && Hud.Hdd == HddPage.CommandDisplay
		&& !_view.ExternalViewActive;

	// Whether the display is down and holding the four arrows: the command display's map scroll, or the
	// damage detail's category and herc steps.
	public bool HddHasArrows =>
		_view.Pan.AtHeadsDown && !_view.ExternalViewActive
		&& (HddCommandHasKeyboard || Hud.Hdd == HddPage.DamageDetail);

	// The same split for FLASH COMM's own letters. In the original both of the page's key dispatches are
	// mode-gated the same way — MfdDisplay_KeyDispatch (004469c0) takes the letters only while the MFD is on mode 1 — and none
	// of the seven letters means anything else anywhere in the cockpit. Here they collide with this
	// host's camera and view keys, so the page only takes them while it is the screen showing and the
	// Heads-Down Display is not down over it.
	public bool FlashCommHasKeyboard =>
		Hud.Mfd == MfdMode.FlashComm && !_view.Pan.AtHeadsDown && !_view.ExternalViewActive;
}
