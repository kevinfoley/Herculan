using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.View;
using Herculan.Engine.World;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The staging flags at work: the state a <c>--screenshot</c> run powers up in, the presses it makes for a
/// player who never touches a key, and the moment it waits for before it captures. One instance serves the
/// whole host run, so what a flag does once it does once per run: <c>--quit</c>'s panel goes up in the first
/// mission only, and <c>--target</c> stays armed until a pick is made, in whichever mission that is. The rest
/// is applied to every mission.
/// </summary>
/// <param name="options">The state to power up in.</param>
/// <param name="capture">What the capture waits for.</param>
/// <param name="screenshotRun">Whether this run ends in a capture at all.</param>
public sealed class SimulatorStaging(StagedStart options, StagedScreenshot capture, bool screenshotRun) {
	// How long a --screenshot run lets the scene settle before it captures. Long enough for the power-up
	// sequence and the first sensor sweep, which is what most staged flags wait on.
	private const int ScreenshotWarmupFrames = 30;

	private bool _statusAlertPending = options.StatusAlert;
	private bool _acquireTarget = options.AcquireTarget;

	// Ticks to let the sensor model run before --target takes its pick: nothing is targetable until a
	// sweep has painted it, and the sweep only runs from the world tick.
	private int _acquireTargetDelay = 5;

	// Set by --hdd-xmit, and called again once the run has ticked so a standing order can be seen to
	// survive its own reassess rather than only to have been installed.
	private Action<string>? _reportSquadOrders;

	private int _framesRendered;
	private bool _hitShakeStaged;
	private bool _screenshotTaken;

	public StagedStart Start => options;

	/// <summary>Starts a mission's own count of frames and its one capture.</summary>
	public void BeginMission() {
		_framesRendered = 0;
		_hitShakeStaged = false;
		_screenshotTaken = false;
	}

	/// <summary>
	/// <c>--hdd-pilot</c>, <c>--hdd-order</c> and <c>--hdd-xmit</c> on the command display, just built: the
	/// pilot and order selected, and the order transmitted with what the squad did with it reported.
	/// </summary>
	public void StageCommandDisplay(HddCommandScreen hddCommand, IReadOnlyList<SimObject> squad, SimWorld commandWorld,
			MissionScene scene, int mapWidth, int mapHeight) {
		if (options.HddPilot >= 0) {
			hddCommand.SelectPilot(options.HddPilot);
		}

		if (options.HddOrder is { } startOrder) {
			hddCommand.SelectOrder(startOrder);
		}

		if (!options.HddTransmit) {
			return;
		}

		if (hddCommand.AwaitingPick) {
			// The camera is not on the player until the first paint, and a map click is read through
			// it. Put it there first so the centre of the viewport is where the player is standing.
			if (scene.PlayerObject?.Object is { } commandSubject) {
				hddCommand.View.Follow(commandSubject.Position);
			}

			// The two orders that want a unit get one picked for them: the nearest of the side they
			// take, hostile for ATTACK ENEMY and friendly for DEFEND POSITION. The rest take the
			// centre of the viewport, which is where the player is standing.
			var pick = HddCommandState.NeedsUnit(options.HddOrder ?? HddOrder.Disengage)
				? commandWorld.Objects
					.Where(candidate => !candidate.Removed && !candidate.AwaitingDeployment
						&& candidate.TargetClass != TargetClass.None
						&& (candidate.Side == MissionSide.Cybrid)
							== (options.HddOrder == HddOrder.AttackEnemy)
						&& !ReferenceEquals(candidate, scene.PlayerObject?.Object))
					.OrderBy(candidate => scene.PlayerObject?.Object is { } from
						? candidate.Position.ApproxDistanceTo(from.Position)
						: 0)
					.FirstOrDefault()
				: null;

			float pickX = pick != null
				? hddCommand.View.ToScreenX(pick.Position.X)
				: mapWidth / 2f;
			float pickY = pick != null
				? hddCommand.View.ToScreenY(pick.Position.Y)
				: mapHeight / 2f;

			hddCommand.ClickMap(pickX, pickY, commandWorld.Objects);
		}

		Console.WriteLine($"Command display: XMIT {options.HddOrder} to slot {options.HddPilot} "
			+ (hddCommand.Transmit() ? "reached its recipient." : "found nobody."));

		_reportSquadOrders = when => {
			for (int slot = 0; slot < squad.Count; slot++) {
				if (squad[slot] is MechObject mate) {
					Console.WriteLine($"  {when} tick {commandWorld.TickCount}, slot {slot}: squad order {mate.SquadOrderVerb}, "
						+ $"state {mate.Behaviour.State?.Name ?? "none"}, "
						+ $"destination {mate.SquadOrderDestination}, "
						+ $"at {mate.Position}, "
						+ $"target {(mate.SquadOrderTarget == null ? "none" : mate.SquadOrderTarget.TargetClass.ToString())}, "
						+ $"reply {mate.LastSquadMessage}, radar {(mate.Scanner ? "ACTIVE" : "PASSIVE")}");
				}
			}
		};

		_reportSquadOrders("on receipt,");
	}

	/// <summary><c>--flash-comm</c> and <c>--flash-comm-xmit</c>: the FLASH COMM row selected, and transmitted.</summary>
	public void StageFlashComm(MfdFlashCommScreen flashComm, SimWorld world) {
		if (options.FlashCommRow >= 0) {
			flashComm.Select(options.FlashCommRow, flashCommIsUp: true);
		}

		if (options.FlashCommTransmit && world is { } flashCommStartWorld) {
			int verb = flashComm.SelectedVerb;
			bool taken = flashComm.Transmit(flashCommStartWorld, flashCommStartWorld.PlayerMech?.Group);
			Console.WriteLine($"FLASH COMM: XMIT row {flashComm.SelectedRow} (group 0 verb {verb}) "
				+ (taken ? "was taken." : "was refused by everyone."));
		}
	}

	/// <summary><c>--weapon</c> and <c>--link</c>: a weapon row armed, and linked.</summary>
	public void StageWeapons(MechObject armedMech) {
		if (options.WeaponRow is { } startRow) {
			armedMech.Weapons.SelectBySlot(startRow);
			if (options.Link) {
				armedMech.Weapons.ToggleLink();
			}
		}
	}

	/// <summary>
	/// <c>--heading</c>, <c>--throttle</c>, <c>--target</c>'s scanner and <c>--track</c> on the machine. The scanner
	/// goes on right away; the selection itself has to wait for the sensor model to have run, since nothing is
	/// targetable until a sweep has painted it. It is taken on the first tick after that, by
	/// <see cref="AcquireTarget"/>.
	/// </summary>
	public void StageMachine(MechObject? pilotMech, SimWorld world) {
		if (pilotMech != null && options.Heading is { } stagedHeading) {
			pilotMech.Heading = (ushort)stagedHeading;
		}

		if (pilotMech != null && options.Throttle != 0) {
			pilotMech.Throttle = options.Throttle;
		}

		if (_acquireTarget && pilotMech != null) {
			pilotMech.ToggleScanner(world);
		}

		if (options.AutoTrack && pilotMech != null) {
			pilotMech.Weapons.AutoTrack = true;
		}
	}

	/// <summary>
	/// <c>--quit</c> stages the [Q] panel, which needs a ticked world to evaluate against, so it is raised on the
	/// first update rather than at load.
	/// </summary>
	public void RaiseStatusAlert(ModalPanels panels, SimWorld world) {
		if (!_statusAlertPending) {
			return;
		}

		_statusAlertPending = false;
		if (options.StatusAlertStatus >= 0) {
			panels.OpenStatusAlert((MissionStatus)options.StatusAlertStatus, world.Mission.Objectives);
		} else {
			panels.RaiseStatusAlertForQuit();
		}
	}

	/// <summary><c>--target</c>'s pick, once the sensor model has had its ticks.</summary>
	public void AcquireTarget(MechObject? pilotMech, TargetSelection? targeting) {
		if (_acquireTarget && pilotMech != null && targeting is { } startupTargeting
				&& --_acquireTargetDelay <= 0) {
			if (startupTargeting.SelectNearest() is { } acquired) {
				_acquireTarget = false;
				Console.WriteLine($"Target acquired: {acquired.GetType().Name} at "
					+ $"{pilotMech.Position.ApproxDistanceTo(acquired.Position)} world units.");
			}
		}
	}

	/// <summary>
	/// <c>--hit-shake</c>'s hit, staged once, at the earliest frame the capture could fire: a shake lasts under a
	/// second and restarting it would stop the view half — see CockpitHitShake.
	/// </summary>
	public void StageHitShake(CockpitHitShake shake) {
		if (options.HitShake && !_hitShakeStaged && _framesRendered >= ScreenshotWarmupFrames) {
			_hitShakeStaged = true;
			shake.Start();
		}
	}

	/// <summary>
	/// The end of a rendered frame: whether a <c>--screenshot</c> run captures it, and closes the window, which it
	/// does once, once the scene has settled and whatever its staged flags wait on is on screen.
	/// </summary>
	public bool AfterFrame(SimWorld world, bool drawsBeams, SquadCommChannel squadComm, CockpitHitShake shake) {
		_framesRendered++;

		// A tracer is on screen for one tick out of every refire period and a travelling shot for as long
		// as its flight lasts, so either one counts as "the trigger produced something visible".
		bool shotWanted = options.HeldFire && (drawsBeams || world.Bullets != null);

		// --target waits the same way --fire does: the sensor model has to run before anything is
		// targetable, so the capture holds until a selection exists rather than photographing a blank HUD.
		// --impact waits for a slot of the effect light field to be lit, which is the one moment the
		// dynamic lights are on screen at all. See EffectLightSelection.
		bool lightWanted = capture.WaitForEffectLight
			&& !world.Effects.Lights.Slots.Any(slot => slot.IsLive);

		bool transmissionWanted = capture.WaitForTransmission && squadComm.Transmission is not { ShowName: true };

		// --hit-shake waits for the palette half to be up. It alternates on its own 0-9 tick timer, so
		// without this the capture would land on whichever side of the flash the frame count happened to
		// fall on.
		bool flashWanted = options.HitShake && !shake.FlashActive;

		if (screenshotRun && !_screenshotTaken && _framesRendered >= ScreenshotWarmupFrames
				&& !flashWanted && !_acquireTarget
				&& !lightWanted && !transmissionWanted
				&& (!shotWanted || world.Tracers.Count > 0 || world.Projectiles.Count > 0
					|| world.RocketsInFlight.Count > 0)) {
			_screenshotTaken = true;
			_reportSquadOrders?.Invoke($"after {_framesRendered} frames,");
			return true;
		}

		return false;
	}
}
