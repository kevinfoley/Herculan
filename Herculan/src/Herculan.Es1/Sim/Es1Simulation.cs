using System.Runtime.CompilerServices;
using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Sim;

/// <summary>
/// An element of an effect pool the tick walks and destroys at once when its tick answers non-zero
/// (docs/es1/simulation/main-loop.md#the-tick--sim_maintick-00059700). Destroying it, slot <c>+0xc</c> with 3, takes it
/// off its pool.
/// </summary>
public interface IEs1PoolTicked {
	/// <summary>One tick; non-zero to be destroyed.</summary>
	short Tick();
}

/// <summary>
/// The simulator's tick on a world: <c>Sim_MainTick</c> (<c>ES1DBSIM 00059700</c>), <c>Sim_DispatchCommand</c>
/// (<c>00059df8</c>) and <c>Sim_PollPlayerInput</c> (<c>0005a0c0</c>) (docs/es1/simulation/main-loop.md#the-tick--sim_maintick-00059700,
/// docs/es1/simulation/main-loop.md#commands--sim_dispatchcommand-00059df8,
/// docs/es1/simulation/main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0), with the flags of
/// <c>Main_StaticInit</c>'s block it reads and writes. The host runs <see cref="MainTick"/> once a tick the clock
/// (<see cref="Es1SimClock"/>) answers.
/// </summary>
public sealed class Es1Simulation {
	/// <summary>The commands' codes the tick acts on or takes: <c>Alt+S</c>, <c>Backspace</c>, <c>Ctrl+1</c> and keypad 0.</summary>
	public const ushort AltS = 0x21f, Backspace = 0x0e, Ctrl1 = 0x402, Keypad0 = 0x52;

	/// <summary>The view commands.</summary>
	public const ushort V = 0x2f, N = 0x31, Tab = 0x0f, Enter = 0x1c, Esc = 0x01;

	/// <summary>The status-panel and preferences commands.</summary>
	public const ushort Q = 0x10, CtrlEsc = 0x401, CtrlQ = 0x410, P = 0x19, AltP = 0x219, F12 = 0x58;

	/// <summary><c>ViewChain_Viewed</c>'s target class that is a flyer.</summary>
	private const short FlyerClass = 2;

	private readonly IReadOnlyList<Es1MissionGroup> _groups;
	private readonly Es1Mech _playerMech;

	// 000971f8: the gunsight drag armed; 000b5df0 and 000b5df4, the pointer where it armed; 000b5de4 and 000b5de8,
	// what it adds to the twist and the pitch.
	private bool _dragArmed;
	private int _dragStartX, _dragStartY;
	private int _dragTwist, _dragPitch;

	// 000971fc: button 2 has cycled the target and not been let go.
	private bool _cycleHeld;

	/// <summary>The simulation of <paramref name="world"/>, whose <c>LocalPlayerMech</c> it requires.</summary>
	/// <param name="viewChain">The views and cameras the mission start built on the player's machine; with none, a chain with no view manager.</param>
	/// <exception cref="ArgumentException">The world has no player's machine.</exception>
	public Es1Simulation(Es1World world, Es1InputBuild input, Es1ViewChain? viewChain = null)
		: this(world.Globals, world.Groups,
			world.LocalPlayer ?? throw new ArgumentException("The world has no player's machine.", nameof(world)),
			world.PlayerMech, input, viewChain) {
	}

	/// <param name="playerMech"><c>PlayerMech</c>, the all-stop's machine; <paramref name="localPlayer"/> when null.</param>
	internal Es1Simulation(Es1SimGlobals globals, IReadOnlyList<Es1MissionGroup> groups,
			Es1Mech localPlayer, Es1Mech? playerMech, Es1InputBuild input, Es1ViewChain? viewChain = null) {
		Globals = globals;
		_groups = groups;
		LocalPlayer = localPlayer;
		_playerMech = playerMech ?? localPlayer;
		ViewChain = viewChain ?? new Es1ViewChain(localPlayer, null);
		Input = input;
	}

	/// <summary>The chain of views, its cameras and the view manager its keys queue commands on.</summary>
	public Es1ViewChain ViewChain { get; }

	/// <summary><c>CockpitViewInstance</c> (<c>00092d1c</c>), the view manager's cockpit; null with no cockpit loaded.</summary>
	public Es1Cockpit? Cockpit => ViewChain.Manager?.Cockpit;

	public Es1SimGlobals Globals { get; }

	/// <summary>The input build the tick runs first.</summary>
	public Es1InputBuild Input { get; }

	/// <inheritdoc cref="Es1InputBuild.Controls"/>
	public Es1ControlsState Controls => Input.Controls;

	/// <summary><c>LocalPlayerMech</c> (<c>000b5b86</c>).</summary>
	public Es1Mech LocalPlayer { get; }

	/// <inheritdoc cref="Es1ViewChain.Viewed"/>
	public Es1Mech Viewed {
		get => ViewChain.Viewed;
		set => ViewChain.Viewed = value;
	}

	/// <summary><c>Sim_Frozen</c> (<c>000b5b92</c>): the pools, the groups, the sensor model and the systems tick do not run.</summary>
	public bool Frozen { get; set; }

	/// <summary><c>Sim_StepOnce</c> (<c>000b5b9c</c>): the tick that finds it set clears it and sets the freeze. No key sets it.</summary>
	public bool StepOnce { get; set; }

	/// <inheritdoc cref="Es1SimGlobals.CenterTurretMode"/>
	public bool CenterTurretMode {
		get => Globals.CenterTurretMode;
		set => Globals.CenterTurretMode = value;
	}

	/// <summary>
	/// <c>TapePlayback</c> and <c>TapeRecording</c> (<c>000b5b76</c>, <c>000b5b78</c>), which <c>Alt+S</c>'s freeze
	/// tests. PLACEHOLDER: always clear: <c>-p</c>, <c>-r</c> and <c>-D</c> are not taken, and no plan step ports tapes.
	/// </summary>
	public bool TapePlayback { get; init; }

	/// <inheritdoc cref="TapePlayback"/>
	public bool TapeRecording { get; init; }

	/// <summary>The developer keys' steps <see cref="Es1MechCommands"/> reads and sets.</summary>
	public Es1DevKeySteps DevKeys { get; } = new();

	/// <summary>
	/// The explosions (<c>g_ExplosionPool</c>, their tick <c>00005d74</c>), the pools <c>000aef68</c> (<c>0000693c</c>)
	/// and <c>000aefd4</c> (<c>00006b28</c>) and the debris (<c>000aeef8</c>, <c>000064b8</c>), each in build order.
	/// PLACEHOLDER: empty: plan step 7e (effects) spawns them. The projectiles are <see cref="Es1SimGlobals.Projectiles"/>.
	/// </summary>
	public List<IEs1PoolTicked> Explosions { get; } = new();

	/// <inheritdoc cref="Explosions"/>
	public List<IEs1PoolTicked> PoolAef68 { get; } = new();

	/// <inheritdoc cref="Explosions"/>
	public List<IEs1PoolTicked> PoolAefd4 { get; } = new();

	/// <inheritdoc cref="Explosions"/>
	public List<IEs1PoolTicked> Debris { get; } = new();

	/// <summary>
	/// The in-mission panels' shared state, which <c>Sim_StartMission</c>'s display and mouse give; none raises no panel,
	/// as with no display, which retail always has, an engine guard.
	/// </summary>
	public Es1AlertGlobals? Panels { get; set; }

	/// <summary>
	/// The frame loop's frame, which <c>PreferencesPanel_Raise</c> draws frames of its own with inside the tick; the loop
	/// sets it when it is wired to the runner. None raises no preferences panel, as with no display, an engine guard.
	/// </summary>
	public IEs1Frame? Frame { get; set; }

	/// <summary><c>Sim_QuitFlag</c> (<c>000b5b9e</c>): set by a non-zero answer of the quit prompt, which ends the mission.</summary>
	public bool QuitFlag { get; private set; }

	/// <summary>
	/// <c>Sim_MainTick</c>: one tick, in retail's order, yielding a wait at each pass of a panel's loop the commands
	/// raise, from which the caller goes on (plan decision 11). <paramref name="ended"/> is set when the tick ends the
	/// mission, which in the slice only the quit prompt's QUIT does.
	/// </summary>
	public IEnumerable<Es1Wait> MainTick(StrongBox<bool> ended) {
		ended.Value = false;

		// 1. The input block. During playback the block's +0x29 would be the tick's length; tapes are not taken.
		var block = Input.Build();

		// 2. The pools, last to first, and the groups.
		if (!Frozen) {
			for (int i = Globals.Structures.Count - 1; i >= 0; i--) {
				// PLACEHOLDER: slot +0x1c, the structure's tick, unread with its constructor 00003cbc; the plan's
				// structures step (after step 8) spawns and ticks them.
			}

			TickPool(Explosions);
			TickPool(PoolAef68);
			TickPool(PoolAefd4);
			TickPool(Debris);
			Controls.MissileFlown = false;
			Globals.Projectiles.Tick(new Projectiles.Es1ProjectileTick(Globals, block, Controls));

			foreach (var group in _groups) {
				if (group.Action == null) {
					TickMembers(group);
				} else if (group.Action.Activated) {
					group.Action = null;
				}
			}
		}

		// 3. A single step freezes the simulation after this one.
		if (StepOnce) {
			StepOnce = false;
			Frozen = true;
		}

		// 4. The player's controls.
		var polled = new StrongBox<bool>();
		foreach (var wait in PollPlayerInput(block, polled)) {
			yield return wait;
		}

		if (polled.Value) {
			ended.Value = true;
			yield break;
		}

		// 5. PLACEHOLDER: Actions_EvaluateTriggers (0001f87c)(LocalPlayerMech), plan step 8. Then the views.
		ViewChain.Apply(Controls);

		// 6. Unless frozen, the sensor model, then the systems tick.
		if (!Frozen) {
			Es1SensorModel.Tick(Globals, _playerMech);

			// PLACEHOLDER: Mech_PerTickSystemsUpdate (00014ee0) for each machine of g_MechPool, last to first, whose group
			// holds no action and that is not destroyed, plan step 7.
		}

		// 7. PLACEHOLDER: Mission_PollStatus (0000f454)(LocalPlayerMech) and the status panel 00046efc it puts up, which
		// end the mission; the mission's end, after plan step 8.
	}

	/// <summary>
	/// <c>Sim_DispatchCommand</c>(<paramref name="block"/>): the command word to the cockpit, then the window keys, then
	/// the fifteen cases at <c>00097180</c>, and any other code to <see cref="Viewed"/>'s <c>Mech_HandleCommand</c>,
	/// whose answer it gives in <paramref name="answer"/>. A case answers 1 plus its panel's term, so 2 ends the mission.
	/// A panel's case, the cockpit's <c>F11</c> among them, yields a wait at each pass of the panel's loop.
	/// </summary>
	internal IEnumerable<Es1Wait> DispatchCommand(Es1PlayerInputBlock block, StrongBox<short> answer) {
		ushort code = block.CommandWord;

		// With no cockpit loaded retail would read through a null CockpitViewInstance; here the cockpit takes nothing.
		var cockpitTook = new StrongBox<short>();
		if (Cockpit is { } cockpit) {
			foreach (var wait in cockpit.HandleCommand(code, Panels, cockpitTook)) {
				yield return wait;
			}
		}

		if (cockpitTook.Value != 0) {
			answer.Value = cockpitTook.Value;
			yield break;
		}

		// Sim_HandleWindowKey (00059dec) answers 0 for every code.

		var chain = ViewChain;
		var views = chain.Manager;
		short second = 0;
		switch (code) {
			case AltS:
				if (TapePlayback || TapeRecording) {
					Frozen = !Frozen;
				}

				break;
			case V:
				if (chain.View is Es1ViewChain.FreeView or Es1ViewChain.OutsideView) {
					views?.QueueViewCommand(3);
					chain.PendingView = Es1ViewChain.CockpitView;
				} else if (chain.View == Es1ViewChain.CockpitView) {
					views?.QueueViewCommand(2);
					chain.PendingView = Es1ViewChain.OutsideView;
				}

				break;
			case N:
				if (views?.CurrentView == Es1CockpitViews.ExternalView && chain.View == Es1ViewChain.OutsideView
						&& NextSquadObject() is { } next && !ReferenceEquals(next, chain.Viewed)) {
					chain.ViewObject(next, Controls);
					chain.Camera.Mode = Es1Camera.Orbit;
					chain.CaptionFrames = Es1ViewChain.CaptionFrameCount;
					chain.Chosen = next;
				}

				break;
			case Tab or Enter:
				if (views?.CurrentView == Es1CockpitViews.ExternalView) {
					chain.ToggleCameraControl();
				}

				break;
			case Esc:
				views?.QueueViewCommand(3);
				// PLACEHOLDER: Sound_Play(0x2d), the engine hum; no plan step ports ES1's sound yet.
				chain.PendingView = Es1ViewChain.CockpitView;
				break;
			case Backspace:
				CenterTurretMode = true;
				break;
			case Q:
				// PLACEHOLDER: the status panel with Mission_StatusForAlert(1, LocalPlayerMech)'s status, whose answer is
				// the second term when it ends the mission, and MissionAlertArmed cleared: Mission_Status needs plan step
				// 8's sensor model and routes, and the mission's end, after plan step 8, ports it.
				break;
			case CtrlEsc or CtrlQ: {
				var term = new StrongBox<int>();
				foreach (var wait in RunStatusPanel(Es1StatusAlertPanel.QuitStatus, term)) {
					yield return wait;
				}

				second = (short)term.Value;
				if (term.Value != 0) {
					QuitFlag = true;
				}

				break;
			}
			case P: {
				var term = new StrongBox<int>();
				foreach (var wait in RunStatusPanel(Es1StatusAlertPanel.PauseStatus, term)) {
					yield return wait;
				}

				second = (short)term.Value;
				break;
			}
			case AltP or F12:
				foreach (var wait in Es1PreferencesRaise.Run(this)) {
					yield return wait;
				}

				break;
			case Ctrl1:
				Controls.ThrottleLeverMode = (short)(Controls.ThrottleLeverMode > 0 ? -1 : 1);
				break;
			case Keypad0:
				Controls.ToggleStickMode();
				break;
			default:
				answer.Value = Es1MechCommands.HandleCommand(Viewed, _playerMech, code, DevKeys);
				yield break;
		}

		answer.Value = (short)(1 + second);
	}

	/// <summary>
	/// A status panel case: <c>StatusAlertPanel_Ctor</c>(0, 0, 1, <paramref name="status"/>), run through its slot
	/// <c>+0xc</c>, its answer in <paramref name="answer"/>, then destroyed with <c>Alert_Dtor</c>(panel, 3). With no
	/// <see cref="Panels"/> no panel is raised and the answer is 0.
	/// </summary>
	private IEnumerable<Es1Wait> RunStatusPanel(int status, StrongBox<int> answer) {
		answer.Value = 0;
		if (Panels == null) {
			yield break;
		}

		var panel = new Es1StatusAlertPanel(Panels, status);
		foreach (var wait in panel.Run(answer)) {
			yield return wait;
		}

		panel.Destroy();
	}

	/// <summary>
	/// <c>Sim_PollPlayerInput</c>(<paramref name="block"/>): the commands, the axes to the machine or the camera, the
	/// gunsight drag, the camera's steer, the throttle and the turret, the trigger and buttons 2 and 3.
	/// <paramref name="ended"/> is set when the dispatch answered 2. The dispatch's panels yield their waits.
	/// </summary>
	internal IEnumerable<Es1Wait> PollPlayerInput(Es1PlayerInputBlock block, StrongBox<bool> ended) {
		var answer = new StrongBox<short>();
		foreach (var wait in DispatchCommand(block, answer)) {
			yield return wait;
		}

		Cockpit?.PollViewDevice(block);

		// The axes: the machine's, or with the camera driven the camera's two and the trigger.
		bool cameraDriven = Controls.InputDrivesCamera;
		short steer = 0, throttle = 0, twist = 0, pitch = 0, cameraX = 0, cameraY = 0;
		byte trigger = 0;
		if (!cameraDriven) {
			steer = block.SteeringAxis;
			throttle = block.ThrottleAxis;
			twist = block.TwistAxis;
			pitch = block.PitchAxis;
		} else {
			(cameraX, cameraY) = block.CameraAxisValues;
			trigger = block.Buttons[0];
		}

		(twist, pitch) = GunsightDrag(block, twist, pitch);

		// Cam_Steer (00000a04)(ViewObjectPtr, trigger, throttle, steer): the axis +0x21 points at as the throttle, +0x1d's as
		// the steering.
		ViewChain.Camera.Steer(trigger, cameraY, cameraX);

		if (Viewed.TargetClass == FlyerClass && !cameraDriven) {
			// PLACEHOLDER: 0001b794(viewed, throttle, steering) and the viewed object's +0xa9 set, the flyer's controls;
			// no flyer is spawned until the plan's flyers step.
		} else if (LocalPlayer.IsAnimated) {
			TickPlayersMachine(steer, throttle, twist, pitch);
		}

		// PLACEHOLDER: with the camera not driven, WeaponMounts_FireTrigger (mounts, player, block), plan step 7.

		// Button 2: the target's cycle once a press.
		if (block.Buttons[2] == 0) {
			_cycleHeld = false;
		} else if (!_cycleHeld) {
			// With no cockpit loaded retail would cycle through a null CockpitViewInstance; here nothing is cycled.
			Cockpit?.Targets.Cycle();
			_cycleHeld = true;
		}

		// Button 3: the all-stop in stick mode 1, the centring in mode 2.
		if (block.Buttons[3] != 0) {
			if (Controls.StickMode == 1) {
				LocalPlayer.Throttle = 0;
				LocalPlayer.ThrottleChanged = true;
			} else if (Controls.StickMode == 2) {
				CenterTurretMode = true;
			}
		}

		ended.Value = answer.Value == 2;
	}

	/// <summary>
	/// The player's machine: <c>Mech_ApplyThrottleInput</c> with <c>ThrottleLeverMode</c>, then the turret, with the range
	/// to the target <c>+0x15e</c> by <c>Math_DistanceBetweenPoints</c>, 0 with none: disabled it centres; with no twist or
	/// pitch input, TRACK's latch and a target not destroyed, it tracks the target's position plus its slot <c>+0x34</c>
	/// offset (<see cref="Es1Turret.TrackPoint"/>) and the centring stops; with input the centring stops; then the centring
	/// tick, or unless tracking the two axis ticks. TRACK's latch is <see cref="Es1WeaponMounts.TrackLatch"/>, which the
	/// console's <c>TRACK</c> sets through the weapon manager's per-frame pass.
	/// </summary>
	private void TickPlayersMachine(short steer, short throttle, short twist, short pitch) {
		var player = LocalPlayer;
		var clock = Globals.Clock;
		player.Locomotion.ApplyThrottleInput(clock, throttle, steer, Controls.ThrottleLeverMode);

		var target = player.Target;
		int range = target?.Position.ApproxDistanceTo(player.Position) ?? 0;
		bool tracking = false;
		if (player.Immobilized) {
			CenterTurretMode = true;
		} else if (twist == 0 && pitch == 0) {
			// Retail's machine always holds a manager; this one has none until Mech_ConfigureLoadout has run.
			if (player.Mounts?.TrackLatch == true && target is { Destroyed: false }) {
				player.Turret.TrackPoint(clock, Globals.TurretServos, target.Position + target.AimOffset);
				CenterTurretMode = false;
				tracking = true;
			}
		} else {
			CenterTurretMode = false;
		}

		if (CenterTurretMode) {
			player.Turret.CenterTorsoTick(clock, Globals.TurretServos, range);
		} else if (!tracking) {
			player.Turret.TwistTick(clock, Globals.TurretServos, twist);
			player.Turret.PitchTick(clock, Globals.TurretServos, range, pitch);
		}
	}

	/// <summary>
	/// The gunsight drag on the mouse's bytes <c>+0xb</c> and <c>+0xa</c> (docs/es1/simulation/main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0):
	/// <c>+0xb</c> clear disarms it and zeroes what it adds; <c>+0xb</c> with <c>+0xa</c> zeroes the player's twist and
	/// pitch and disarms it; <c>+0xb</c> alone, once armed, adds −sign(<i>dx</i>)·<i>dx</i>²/64 to the twist and
	/// sign(<i>dy</i>)·<i>dy</i>²/32 to the pitch, <i>dx</i> and <i>dy</i> the arming point less the pointer.
	/// </summary>
	private (short Twist, short Pitch) GunsightDrag(Es1PlayerInputBlock block, short twist, short pitch) {
		if (block.MouseButtons[1] == 0) {
			_dragArmed = false;
			_dragTwist = 0;
			_dragPitch = 0;
		} else if (block.MouseButtons[0] == 0) {
			if (!_dragArmed) {
				if (InsideGunsightGadget(block.PointerX, block.PointerY)) {
					_dragArmed = true;
					_dragStartX = block.PointerX;
					_dragStartY = block.PointerY;
				}
			} else {
				int dx = _dragStartX - block.PointerX;
				int dy = _dragStartY - block.PointerY;
				_dragTwist = dx * dx >> 6;
				if (dx > 0) {
					_dragTwist = -_dragTwist;
				}

				_dragPitch = dy * dy >> 5;
				if (dy < 0) {
					_dragPitch = -_dragPitch;
				}
			}
		} else {
			LocalPlayer.Twist = 0;
			LocalPlayer.Pitch = 0;
			_dragArmed = false;
		}

		return _dragTwist != 0 || _dragPitch != 0
			? ((short)(twist + _dragTwist), (short)(pitch + _dragPitch))
			: (twist, pitch);
	}

	/// <summary>
	/// <c>N</c>'s walk: from <see cref="Viewed"/> on through <c>LiveObjectList</c> with <c>ObjectList_Next</c>
	/// (<c>0000dba4</c>), which wraps, to the first object whose height (<c>+0x28</c>) is not negative and whose group is
	/// the player's, the player included. Retail's walk has no end and spins forever when no object passes; this one gives
	/// up after a full turn of the list and answers null, an engine guard. An object that is not a HERC, which the slice
	/// never lists (blocks 7 and 8 are not spawned), is passed over.
	/// </summary>
	private Es1Mech? NextSquadObject() {
		var live = Globals.LiveObjects;
		int at = live.IndexOf(Viewed);
		var group = LocalPlayer.Group;
		for (int step = 1; step <= live.Count; step++) {
			var candidate = live[(at + step) % live.Count];
			if (candidate.Position.Z >= 0 && ReferenceEquals(candidate.Group, group) && candidate is Es1Mech mech) {
				return mech;
			}
		}

		return null;
	}

	/// <summary>
	/// Whether the pointer lies inside the rect <c>+0</c>–<c>+0xc</c> of the roving gunsight (<c>CockpitViewInstance</c>
	/// <c>+0x1f1</c>), edges included. With no cockpit, where retail would read through a null one, never.
	/// </summary>
	private bool InsideGunsightGadget(int x, int y) => Cockpit?.GunsightRect.Contains(x, y) == true;

	/// <summary>
	/// <c>Group_TickMembers</c> (<c>0001cf60</c>): <c>Mech_AiTick</c> on each member
	/// (docs/es1/simulation/mission-groups.md#the-tick). A member the pool had no object for, or a flyer or base, which
	/// no group holds until the plan's flyers-and-bases step, is skipped.
	/// </summary>
	private void TickMembers(Es1MissionGroup group) {
		foreach (var member in group.Members) {
			if (member is Es1Mech mech) {
				Es1MechBehaviour.AiTick(mech, Globals);
			}
		}
	}

	/// <summary>A pool walked last to first with <c>Pool_Prev</c>, each element whose tick answers non-zero destroyed at once.</summary>
	private static void TickPool(List<IEs1PoolTicked> pool) {
		for (int i = pool.Count - 1; i >= 0; i--) {
			if (pool[i].Tick() != 0) {
				pool.RemoveAt(i);
			}
		}
	}
}
