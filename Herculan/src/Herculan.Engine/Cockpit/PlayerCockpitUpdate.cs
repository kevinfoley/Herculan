using System.Numerics;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Settings;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// Player_PerFrameCockpitUpdate's share of each frame: the weapon manager's pass, the instruments' clocks, the
/// sensor dropout, and the HUD state rebuilt from the machine for the widgets to draw.
/// </summary>
public sealed class PlayerCockpitUpdate(CockpitDisplays displays, CockpitView view, CockpitCommands commands,
		MissionScene scene, GameAudio audio) {
	/// <summary>Runs the frame's cockpit update, when there is a cockpit and a machine to read it off.</summary>
	public void Update(double deltaSeconds) {
		if (displays.Art is not { } cockpitArt || view.PilotMech is not { } pilotMech) {
			return;
		}

		// Player_PerFrameCockpitUpdate's own order: the range to the selected target, then the weapon
		// manager's pass, then the gauge and the machine settle which of them moved this frame, then
		// the readouts are taken from the machine. The range is zero when nothing is selected, which
		// is what turns the manager's range gate off.
		int targetRange = pilotMech.Target is { } weaponTarget
			? pilotMech.Position.ApproxDistanceTo(weaponTarget.Position)
			: 0;
		pilotMech.Weapons.PerFrameUpdate(targetRange, scene.World.MissileFlown);

		// The pods' own tick runs from inside that pass in the original, and only ever for the machine
		// the cockpit belongs to. It is what carries the ECM and Turbo rows' buttons into the sim.
		pilotMech.PodTick(scene.World);
		displays.ThrottleGauge = pilotMech.ExchangeCockpitThrottle(displays.ThrottleGauge);

		// HudClock_Tick (0043dcac) runs inside the gunsight's paint, so a view with no gunsight in it does not
		// advance the clock at all.
		if (!view.ExternalViewActive) {
			displays.Clock.Advance(deltaSeconds);
		}

		// NavMarker_Tick (004349ac) runs from the cockpit's own paint, one frame apart, and is what arms the marker
		// on leaving it and clears it — announcing WAYPOINT REACHED — on coming back.
		displays.NavMarker.Tick(pilotMech.Position, audio.Messages);

		// Player_ResolveTargetAimPoint, once a frame and once only: it runs the Targeting Pod's decay
		// countdown as a side effect, so asking twice would halve how long a damaged pod holds a
		// component. Both consumers — the front-window target box and the MFD's F5 doll — read this.
		var targetAim = pilotMech.ResolveTargetAimPoint();

		// Cockpit_PowerUpTick's arming pass, which runs from the cockpit's own per-frame update.
		var cockpitPowerUp = displays.PowerUp;
		cockpitPowerUp.Tick(audio.CoarseTicks);

		// The dish's power-up animation, on whichever screen the display is showing this frame. Asked
		// before the dropout, because it is what decides whether the MFD's update gets that far.
		var hudState = displays.Hud;
		bool scannerShowing = hudState.Mfd == MfdMode.Scanner;
		int? mfdPowerUpFrame = cockpitPowerUp.MfdFrame(scannerShowing, audio.CoarseTicks);

		// A Heads-Down Display press held while it was dark, acted on by the first update that finds it
		// back. The display's update handles its pending press before it ticks the dropout, so this goes
		// first too, and a press waits one frame past the flip.
		var cockpitDropouts = displays.Dropouts;
		if (displays.PendingHddPress is { } heldPress && !cockpitDropouts.HeadsDown.Dark && !view.ExternalViewActive) {
			commands.ApplyHddClick(heldPress);
		}

		// The sensor dropout, from each display's own update. The forward console's rows and the MFD only
		// update while they are on screen, which a fully panned heads-down view is not.
		if (scene.World is { } dropoutWorld) {
			var mounts = pilotMech.Weapons;
			cockpitDropouts.Tick(SensorDropout.SensorCondition(pilotMech), audio.CoarseTicks,
				dropoutWorld.PresentationRandom,
				cockpitUp: !view.ExternalViewActive,
				consoleOnScreen: !view.Pan.AtHeadsDown,
				mfdUpdating: cockpitPowerUp.MfdReachesDropout(scannerShowing),
				rowTicking: row => mounts.BySlot(row) != null && cockpitPowerUp.RowPowered(row));
		}

		var (shieldFront, shieldRear) = pilotMech.Shields.Readout(
			rounded: TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.RoundShieldBalance));

		var squadComm = displays.SquadComm;
		var hddCommand = displays.HddCommand;
		hudState = displays.Hud;
		hudState = hudState with {
			MissionTime = displays.Clock.Text,
			SpeedKph = TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.ShowAccurateSpeed)
				? pilotMech.GroundSpeedKph
				: pilotMech.DisplaySpeedKph,
			Throttle = displays.ThrottleGauge,
			TorsoTwist = pilotMech.TorsoTwistAngle,

			// What the gunsight hands the heading tape: the machine's own heading out of mech+0x10, except
			// while the cockpit's power-up wind-up is still running, when it is that ramp instead. The
			// sweep latches itself as it goes, so this is the once-a-frame call it expects.
			Heading = displays.HeadingSweep?.Angle((short)pilotMech.Heading, audio.CoarseTicks)
				?? (short)pilotMech.Heading,
			ShieldFront = shieldFront,
			ShieldRear = shieldRear,
			EnergyFraction = pilotMech.EnergyPoolFraction,
			Weapons = WeaponRowState.Build(pilotMech.Weapons,
				cockpitArt.Gau.WeaponListTotal, cockpitArt.Strings, cockpitPowerUp, audio.CoarseTicks,
				cockpitDropouts),
			Dropout = cockpitDropouts.Snapshot,
			ChargeBarsDraggable = TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.ChargeBarPowerLevel),
			ChainGroup = pilotMech.Weapons.Group,
			AutoTrack = pilotMech.Weapons.AutoTrack,
			Target = ResolveTargetIndicator(pilotMech, targetAim),

			// The damage detail's subject, re-read every frame: on the target slot it follows the
			// selection, as HddDisplay_Update (00449bd0) re-points it whenever the selection changes.
			HddSubject = HddDamageSubject.For(displays.HddSubjectSlot, pilotMech, displays.SquadSeats, squadComm.Name,
				scene.Targeting?.Selected, cockpitArt.Strings),

			// Rebuilt every frame, whichever of the two scanners is up. The MFD screen's own update
			// slot runs while F4 is showing (mode 3's dirty flag is the one MfdDisplay_Update never
			// clears); the floating repeater calls that same slot itself while F4 is not. Exactly one
			// of them runs per frame in the original, so the list is always a frame old at most.
			Scanner = MfdScanner.Build(pilotMech, scene.World?.Objects,
				scene.Targeting?.Selected, hudState.Scanner),

			// The NAV MAP's paint re-centres on the machine every frame it runs, and blits the same
			// raster the command display does.
			NavMap = MfdNavMap.Build(pilotMech, hddCommand?.Raster),

			// The message port has already run for this frame inside audio.Update, before this.
			Message = audio.Messages.Ticker,

			// The command display, rebuilt every frame whether or not it is the page showing: its map
			// follows the machine, so the camera has to keep up even while the damage screen is up.
			Command = hddCommand is { } commandScreen
				? commandScreen.Build(pilotMech, scene.World?.Objects ?? Array.Empty<SimObject>(),
					scene.Mission.PlayerRoute, cockpitArt.Strings)
				: hudState.Command,

			// MfdFlashCommScreen_Paint (0043f7a4)'s first act is to copy the display's row onto the screen, so the page's own
			// row only survives between repaints — which is what lets an [Alt] hotkey pressed from
			// another screen transmit a row the cursor never moved to.
			FlashComm = displays.FlashComm.Snapshot(),

			// The comm channel has already run for this frame inside audio.Update, on the same clock as
			// the computer's port.
			Transmission = squadComm.Transmission,

			// And the line that goes with it, over the canopy. Composed here rather than in the port
			// because the name in front of it is the comm box's, not the message's — PilotMessagePort_ComposeLine (00435d0c)
			// asks the box for it through Squad_IndexOf.
			PilotMessage = ComposePilotMessage(squadComm),

			MfdPowerUpFrame = mfdPowerUpFrame,

			// A training mission's port draws its own wrapped block instead of that line.
			TrainingMessage = ComposeTrainingMessage(squadComm),

			// Each box's own picture. The MFD shows one box's, full screen; the display shows all
			// three in place, and a destroyed squadmate's sits on static there without ever having
			// had a message to open it.
			PilotVideos = displays.PilotVideos,

			// The two waypoint indicators over the compass. The route one follows the player group's
			// cursor, which the player's own think steps on arrival; the marker one is only there
			// while the player has dropped one.
			RouteWaypoint = WaypointMark.ForRoute(pilotMech),
			NavMarker = WaypointMark.ForNavMarker(pilotMech, displays.NavMarker),

			// The RAZOR's altitude scale, read off the world the paint reads it off.
			Altitude = scene.World is { } altitudeWorld ? AltitudeReading.For(pilotMech, altitudeWorld.Terrain) : null,
		};

		// MfdDisplay_Update's share of the missile camera, run only while the display updates at all —
		// up on the cockpit view, not panned away, lit and powered up. The switch goes first; then the
		// screen's own update, unless a transmission has the display, which it can only while the
		// switch does not hold. The first frame the screen updates after not doing so stands for the
		// display's full repaint.
		var missileCamSwitch = displays.MissileCamSwitch;
		int weaponRows = cockpitArt.Gau.WeaponListTotal;
		bool mfdUpdating = !view.ExternalViewActive && !view.Pan.AtHeadsDown
			&& !hudState.Dropout.MfdHidden && hudState.MfdPowerUpFrame is null;
		if (mfdUpdating) {
			hudState = hudState with {
				Mfd = missileCamSwitch.Sync(hudState.Mfd,
					MfdMissileCamSwitch.Condition(pilotMech.Weapons, weaponRows)),
			};
		}

		bool missileCamUpdating = mfdUpdating && hudState.Mfd == MfdMode.MissileCam
			&& (missileCamSwitch.Holding || squadComm.Transmission is null);
		var missileCam = hudState.MissileCam;
		if (missileCamUpdating && scene.World is { } camWorld) {
			missileCam = displays.MissileCamScreen.Update(camWorld, pilotMech.LockAcquired,
				MfdMissileCam.LauncherRounds(pilotMech.Weapons, weaponRows), audio.CoarseTicks,
				repaint: !displays.MissileCamUpdatedLastFrame);
		}

		displays.MissileCamUpdatedLastFrame = missileCamUpdating;
		hudState = hudState with { MissileCam = missileCam, MissileCamHolding = missileCamSwitch.Holding };

		// The status screens, under the same gate as the missile camera's update plus the display's arming,
		// which returns first on any screen but the scanner. A paint carries the Targeting Pod's component
		// on top of what the subject itself says — the pod belongs to the machine looking, not to what it is
		// looking at, and only the id the pod's own present flag vouches for reaches it.
		bool statusUpdating = mfdUpdating && cockpitPowerUp.MfdReachesDropout(hudState.Mfd == MfdMode.Scanner)
			&& (missileCamSwitch.Holding || squadComm.Transmission is null);
		var statusRefresh = displays.StatusRefresh;
		statusRefresh.Update(hudState.Mfd, statusUpdating, view.Pan.IsPanning || view.Glance.Sliding,
			audio.CoarseTicks, displays.StatusRoster.Subject, scene.Targeting?.Selected,
			subject => MfdStatusSubject.For(subject, pilotMech, cockpitArt.Strings, squadComm)
				with { HighlightComponent = targetAim.ComponentTargeted ? targetAim.Component : -1 });
		displays.Hud = hudState with { StatusSubject = statusRefresh.Status, TargetSubject = statusRefresh.Target };
	}

	// The pilot and squad channel's line, composed the way PilotMessagePort_ComposeLine (00435d0c) composes it: the speaker's name,
	// ": ", then the message, capped at the composer's own strncat length. The name comes from the comm
	// box rather than from the queued record — PilotMessagePort_ComposeLine resolves the record's speaker to a slot with
	// Squad_IndexOf and asks the display for that box's name.
	//
	// Nothing is drawn while the channel is on VoiceOnly: the paint's first test is PilotMessageMode != 1.
	private static PilotMessageLine? ComposePilotMessage(SquadCommChannel channel) {
		if (channel.Port.Training
			|| channel.Port.Mode == MessageChannelMode.VoiceOnly
			|| channel.Port.Current is not { Text.Length: > 0 } current) {
			return null;
		}

		string text = current.Text.Length > PilotMessageBoxLayout.MaxTextLength
			? current.Text[..PilotMessageBoxLayout.MaxTextLength]
			: current.Text;
		string name = channel.Name(current.Slot);
		return new PilotMessageLine(
			name.Length > 0 ? name + PilotMessageBoxLayout.NameSeparator + text : text,
			current.Slot);
	}

	// The training port's block: the instruction's sentences through PilotMessagePort_WrapText's own
	// wrap. The same VoiceOnly test as the ordinary port's paint opens with.
	private static TrainingMessageBox? ComposeTrainingMessage(SquadCommChannel channel) {
		if (!channel.Port.Training
			|| channel.Port.Mode == MessageChannelMode.VoiceOnly
			|| channel.Port.Current is not { Sentences.Count: > 0 } current) {
			return null;
		}

		var (lines, widest) = TrainingMessageLayout.Wrap(current.Sentences);
		return new TrainingMessageBox(lines, widest);
	}

	// Where the selection lands on the canopy, for the front-window target box and arrow — the original's
	// own projection rather than the GL one: view-space offsets scaled by the focal length about the
	// herc's .VUE projection centre, which is what Gunsight_TargetIndicatorPaint (0043b950) does with Raster_ProjectToScreen. It
	// agrees with the GL projection because the camera's field of view is derived from the same focal
	// length and the cockpit renderer installs the same centre, including the step kick.
	private TargetIndicator? ResolveTargetIndicator(MechObject pilot,
			(Vec3i Point, bool ComponentTargeted, short Component) aimPoint) {
		if (scene.Targeting is not { IndicatorArmed: true, Selected: { } target }) {
			return null;
		}

		var (centerX, centerY) = view.ViewGeometry?.ProjectionCenter(CockpitViewGeometry.ForwardViewIndex)
			?? (CockpitViewGeometry.DefaultProjectionCenterX, CockpitViewGeometry.DefaultProjectionCenterY);

		// With a Targeting Pod fitted and the target close enough, the aim point is a component of it
		// rather than its aim node, and the box reduces to its pip.
		var camera = view.Camera;
		var aim = aimPoint.Point;
		var offset = WorldScale.ToRender(aim) - WorldScale.ToRender(camera.Position);
		var forward = camera.Forward;
		var up = camera.Up;
		var right = Vector3.Cross(forward, up);

		float depth = Vector3.Dot(offset, forward);
		float across = Vector3.Dot(offset, right);
		bool inFront = depth >= camera.NearPlane;

		return new TargetIndicator(
			ScreenX: centerX + across * Camera.FocalLengthPixels / MathF.Max(depth, camera.NearPlane),
			ScreenY: centerY - view.Kick.OffsetPixels + view.Shake.OffsetPixels
				- Vector3.Dot(offset, up) * Camera.FocalLengthPixels / MathF.Max(depth, camera.NearPlane),
			InFront: inFront,
			BehindToLeft: across < 0f,
			ShapeRadius: target.ShapeRadius,
			Distance: pilot.Position.ApproxDistanceTo(aim),
			Locked: pilot.LockAcquired,
			ComponentTargeted: aimPoint.ComponentTargeted);
	}
}
