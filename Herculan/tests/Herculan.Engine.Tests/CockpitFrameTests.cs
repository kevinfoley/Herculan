using Herculan.Engine.Cockpit;
using Herculan.Engine.Input;
using Herculan.Engine.Sim;
using Silk.NET.Input;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The cockpit's input paths through a whole frame (<see cref="SimulatorRig"/>), one behaviour each: what a key,
/// a click or a panel does to the machine, the displays and the simulation's clock. <see cref="CockpitSessionTests"/>
/// holds everything still at once; these say what each piece is for.
///
/// <para>Skips silently when no Earthsiege 2 install with its demo tapes can be found.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public class CockpitFrameTests {
	/// <summary>
	/// [P] raises the pause panel, and every modal freezes the simulation behind it: its own loop never reaches
	/// Sim_MainTick. [Esc] takes it down and the ticks carry on from where they stopped.
	/// </summary>
	[Fact]
	public void PauseFreezesTheSimulationUntilItIsDismissed() {
		if (SimulatorRig.Load() is not { } rig) {
			return;
		}

		rig.Run(10);
		rig.Tap(Key.P);
		Assert.Equal(StatusAlertPanel.PauseStatus, rig.Panels.StatusAlert!.Status);
		Assert.True(rig.Panels.AnyOpen);

		long frozenAt = rig.World.TickCount;
		rig.Run(30);
		Assert.Equal(frozenAt, rig.World.TickCount);

		rig.Tap(Key.Escape);
		rig.Run(10);
		Assert.False(rig.Panels.AnyOpen);
		Assert.True(rig.World.TickCount > frozenAt);
	}

	/// <summary>
	/// A panel going up puts the pointer on the widget it focuses, and coming down puts it back where it was —
	/// AlertPanel_SetFocus and AlertPanel_Leave, through the pointer device rather than the window.
	/// </summary>
	[Fact]
	public void APanelPutsThePointerOnItsFocusAndBackAsItCloses() {
		if (SimulatorRig.Load() is not { } rig) {
			return;
		}

		rig.Pointer.Set(100, 100, CockpitMouseButtons.None);
		rig.Run(5);
		rig.Tap(Key.F11);
		Assert.True(rig.Panels.Objectives!.IsOpen);
		Assert.True(rig.Pointer.Warps > 0);
		Assert.NotEqual((100f, 100f), (rig.Pointer.Pointer().X, rig.Pointer.Pointer().Y));

		rig.Tap(Key.Enter);
		rig.Run(5);
		Assert.False(rig.Panels.Objectives.IsOpen);
		Assert.Equal((100f, 100f), (rig.Pointer.Pointer().X, rig.Pointer.Pointer().Y));
	}

	/// <summary>
	/// The arrows are the keyboard's stick: [Up] opens the throttle and [Right] steers right, at half a stick's
	/// travel. A modal panel takes the controls away entirely, so the machine sees them centred while it is up.
	/// </summary>
	[Fact]
	public void TheArrowsDriveTheMachineAndAPanelTakesThemAway() {
		if (SimulatorRig.Load() is not { } rig || rig.Player is not { } mech) {
			return;
		}

		rig.Keys.Hold(Key.Up, Key.Right);
		rig.Run(5);
		Assert.Equal(-MechControls.KeyboardAxis, mech.Controls.Throttle);
		Assert.Equal(MechControls.KeyboardAxis, mech.Controls.Turn);

		rig.Keys.Hold(Key.P);
		rig.Run(2);
		Assert.True(rig.Panels.AnyOpen);
		Assert.Equal(MechControls.Neutral, mech.Controls);
	}

	/// <summary>
	/// The cockpit update copies the machine's readouts onto the HUD each frame: the throttle the arrows have
	/// opened is the throttle the gauge shows.
	/// </summary>
	[Fact]
	public void TheHudShowsTheMachinesThrottle() {
		if (SimulatorRig.Load() is not { } rig || rig.Player is not { } mech) {
			return;
		}

		rig.Keys.Hold(Key.Up);
		rig.Run(30);
		Assert.NotEqual(0, mech.Throttle);
		Assert.Equal(mech.Throttle, rig.Displays.Hud.Throttle);
	}

	/// <summary>
	/// []] moves the shield balance one step forward per press: held, it does not repeat, because the
	/// command is dispatched once per key-down.
	/// </summary>
	[Fact]
	public void AShieldKeyMovesTheBalanceOncePerPress() {
		if (SimulatorRig.Load() is not { } rig || rig.Player is not { } mech) {
			return;
		}

		rig.Run(5);
		short before = mech.Shields.Balance;
		rig.Keys.Hold(Key.RightBracket);
		rig.Run(20);
		Assert.Equal(before + ShieldCharge.BalanceStep, mech.Shields.Balance);
	}

	/// <summary>
	/// [F1]-[F6] pick the MFD's screen, and [F8] opens the Heads-Down Display on its damage page, which asks
	/// the pan to go down.
	/// </summary>
	[Fact]
	public void TheFunctionKeysPickTheMfdScreenAndTheHeadsDownPage() {
		if (SimulatorRig.Load() is not { } rig) {
			return;
		}

		rig.Tap(Key.F3);
		Assert.Equal((MfdMode)2, rig.Displays.Hud.Mfd);

		rig.Tap(Key.F8);
		Assert.Equal(HddPage.DamageDetail, rig.Displays.Hud.Hdd);
		Assert.True(rig.View.Pan.HeadsDownRequested);
	}

	/// <summary>
	/// On the command display, [1] selects the first squadmate and [A] arms ATTACK ENEMY, which then waits for a
	/// pick on the map.
	/// </summary>
	[Fact]
	public void TheCommandDisplaysKeysSelectAPilotAndArmAnOrder() {
		if (SimulatorRig.Load() is not { } rig || rig.Displays.HddCommand is not { } command) {
			return;
		}

		rig.Tap(Key.F7);
		rig.Run(40);
		Assert.True(rig.View.Pan.AtHeadsDown);

		rig.Tap(Key.Number1);
		rig.Tap(Key.A);
		Assert.Equal(0, command.SelectedPilot);
		Assert.Equal(HddOrder.AttackEnemy, command.SelectedOrder);
		Assert.True(command.AwaitingPick);
	}

	/// <summary>The two system buttons ask the window for the manual and for full screen, one call per click.</summary>
	[Fact]
	public void TheSystemButtonsReachTheWindow() {
		if (SimulatorRig.Load() is not { } rig) {
			return;
		}

		rig.Run(5);
		rig.ClickSystemButton(SystemButton.Manual);
		Assert.Equal((1, 0), (rig.SystemButtonPresses.Manual, rig.SystemButtonPresses.FullScreen));

		rig.ClickSystemButton(SystemButton.FullScreen);
		Assert.Equal((1, 1), (rig.SystemButtonPresses.Manual, rig.SystemButtonPresses.FullScreen));
	}

	/// <summary>
	/// [Ctrl+Q] raises EXIT EARTHSIEGE?, and QUIT — its second button — ends the mission with the game's quit
	/// flag set. The panel says so; the host is what closes the window.
	/// </summary>
	[Fact]
	public void QuitOnTheExitPanelEndsTheMissionAndTheGame() {
		if (SimulatorRig.Load() is not { } rig) {
			return;
		}

		rig.Run(5);
		rig.Tap(Key.ControlLeft, Key.Q);
		Assert.Equal(StatusAlertPanel.ExitGameStatus, rig.Panels.StatusAlert!.Status);
		Assert.False(rig.MissionOver);

		rig.Tap(Key.Tab);
		rig.Tap(Key.Enter);
		rig.Run(5);
		Assert.True(rig.MissionOver);
		Assert.True(rig.QuitGame);
	}

	/// <summary>
	/// [Alt+S] freezes the simulation only under the developer flag, and a second press lets it go.
	/// </summary>
	[Fact]
	public void TheDeveloperFreezeNeedsTheDeveloperFlag() {
		if (SimulatorRig.Load() is not { } plain || SimulatorRig.Load(developer: true) is not { } developer) {
			return;
		}

		plain.Tap(Key.AltLeft, Key.S);
		Assert.False(plain.DeveloperKeys.Frozen);

		developer.Tap(Key.AltLeft, Key.S);
		Assert.True(developer.DeveloperKeys.Frozen);
		developer.Tap(Key.AltLeft, Key.S);
		Assert.False(developer.DeveloperKeys.Frozen);
	}
}
