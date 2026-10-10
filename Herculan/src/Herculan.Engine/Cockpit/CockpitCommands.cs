using Herculan.Engine.Audio;
using Herculan.Engine.Input;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.View;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// What a completed click on the cockpit does, and the widget presses keys share with it. The original's
/// buttons and its keyboard bindings dispatch the same calls, so the two agree here by construction rather
/// than by two parallel implementations.
/// </summary>
public sealed class CockpitCommands(CockpitDisplays displays, CockpitView view, MissionScene scene, GameAudio audio,
		ISystemButtonActions systemButtons, TapePlayback tape) {
	/// <summary>
	/// The frame's queued clicks and drags, acted on. They are drained before the pan advances and before the
	/// sim ticks, so a click and the tick that reacts to it keep a fixed order every frame — the point of
	/// queueing them in the first place. The layout is built from this frame's pan position, which is the same
	/// one the frame is drawn with, so what the player is clicking is what they are looking at.
	/// </summary>
	public void DrainClicks(CockpitInput queue, double deltaSeconds, int framebufferWidth, int framebufferHeight) {
		var cockpitArt = displays.Art!;
		var inputLayout = CockpitScreenLayout.Create(framebufferWidth, framebufferHeight, cockpitArt,
			view.Pan.OffsetRows, view.Pan.TravelRows, view.Glance.OffsetPanels);

		foreach (var click in queue.Drain(deltaSeconds, inputLayout, cockpitArt, displays.Hud)) {
			ApplyCockpitClick(click);
		}

		// The one draggable control. Dragging the slider sets the gauge, and the machine picks that up
		// on this frame's exchange unless its own input moved the throttle first.
		foreach (var drag in queue.Drags) {
			if (drag.Id.Kind == CockpitWidgetKind.Throttle && displays.ThrottleTrack is { } track) {
				displays.ThrottleGauge = track.ThrottleAt(drag.ArtY);
			}

			// A charge bar, listed only under the ChargeBarPowerLevel tweak. Retail's slider commits on
			// the release alone, so the positions on the way there do nothing.
			if (drag is { Released: true, Id.Kind: CockpitWidgetKind.WeaponChargeBar } && view.PilotMech is { } pilotMech
				&& ChargeBarSlider.For(cockpitArt, drag.Id.Index) is { } bar) {
				pilotMech.Weapons.SetPowerFromChargeBar(drag.Id.Index, bar.PositionAt(drag.ArtX));
			}
		}

		// Held buttons draw depressed, and pop back up if the pointer slides off them still held.
		displays.Hud = displays.Hud with { PressedWidget = queue.Depressed };
	}

	/// <summary>
	/// A completed click, routed to the same state changes the corresponding key already makes.
	///
	/// <para>The buttons with nothing behind them yet are deliberately silent rather than stubbed. They still
	/// hit-test and will still light on press; they simply do nothing on release.</para>
	/// </summary>
	public void ApplyCockpitClick(CockpitClick click) {
		// The click itself is audible before anything is decided by it. In the original the sound is not
		// the handler's: it is a one-line virtual (0x438e2c, the only caller of catalog id 0x11 in the
		// image) sitting in fifteen widget vtables, so a widget clicks because it is that kind of widget,
		// not because its action did something. That is why a button with nothing behind it still clicks.
		//
		// The screen-edge strips are the exception, and for the same structural reason: that virtual
		// lives in the PanelGadget mixin every gadget carries as a second base, and ScrollTrigger is one
		// of the four classes that take no mixin at all. It is silent for want of the base, not for want
		// of an action. See docs/retail/simulation/cockpit-input.md, "The second vtable".
		//
		// The system buttons are silent the other way round: SystemGadget carries the mixin, but its own
		// OnClick (SystemGadget_OnClick, 00434910) goes straight to SystemButtons_OnChildClick and never
		// calls the mixin's sound slot, which a button reaches only through its class's OnClick.
		//
		// The list regions are silent for the same reason: the command display's order column and map
		// viewport are HDDListGadgets, whose OnClick (HDDListGadget_OnClick, 0044f6ac) only queues the
		// click for HddCommandScreen_HandleListClick, and the FLASH COMM rows sit under the MFD's
		// MFDListGadget, whose OnClick (MFDListGadget_OnClick, 00447630) only calls
		// MfdFlashComm_HandleListClick. See docs/retail/simulation/audio.md, "Sounds a cockpit control makes".
		if (click.Id.Kind is not (CockpitWidgetKind.ViewEdge or CockpitWidgetKind.SystemButton
				or CockpitWidgetKind.HddOrderRow or CockpitWidgetKind.HddMapArea
				or CockpitWidgetKind.MfdFlashCommRow)) {
			audio.Director?.Play(SoundId.ButtonClick);
		}

		var pilotMech = view.PilotMech;
		var hddCommand = displays.HddCommand;
		switch (click.Id.Kind) {
			// SystemButtons_OnChildClick (004345a0), which does nothing while a tape plays back: child 0 is
			// the on-line manual, by the same two calls the [/] key makes, and child 1 the full-screen
			// toggle. Either mouse button clicks them, the click's value going unread.
			case CockpitWidgetKind.SystemButton when !tape.Playing:
				if (click.Id.AsSystemButton == SystemButton.Manual) {
					systemButtons.OpenManual();
				} else {
					systemButtons.ToggleFullScreen();
				}

				break;

			case CockpitWidgetKind.MfdButton when click.Id.Index < MfdLayout.ModeCount:
				// Button i of the F-key column dispatches SetMode(i), and picking a screen pans back up —
				// the manual's own rule for leaving the Heads-Down Display.
				displays.SetMfdMode((MfdMode)click.Id.Index);
				view.RequestHeadsDown(headsDown: false);
				break;

			// MfdButton_OnClick's XMIT case returns at once on a double-click, so double-clicking XMIT
			// transmits once. It still clicks: the sound is the button class's, not the case's.
			case CockpitWidgetKind.MfdButton when click.Id.Index == MfdLayout.TransmitButton && click.DoubleClick:
				break;

			case CockpitWidgetKind.MfdButton:
				ApplyMfdAuxClick(click.Id.Index);
				break;

			// A click on a FLASH COMM row — MfdFlashComm_HandleListClick (00447098). A double-click on the
			// row already selected presses XMIT through Widget_PressChild, which clicks and flashes it but
			// transmits nothing, its case returning on the double-click; the handler then transmits itself.
			// That is one transmission, as the key press makes. Any other click selects the row.
			case CockpitWidgetKind.MfdFlashCommRow when scene.World != null:
				int pickedRow = click.Id.Index;
				if (click.DoubleClick && pickedRow == displays.FlashComm.SelectedRow) {
					PressMfdButtonByKey(MfdLayout.TransmitButton);
				} else {
					displays.FlashComm.Select(pickedRow, flashCommIsUp: true);
				}

				break;

			case CockpitWidgetKind.HddWidget:
				ApplyHddClick(click.Id.AsHddWidget!.Value);
				break;

			// Clicking an order arms it, which is the same thing its hotkey does — HddCommandScreen_HandleListClick walks the
			// eight label rects and calls the same HddCommandScreen_SelectOrder (0044d9cc) the key dispatch does. A
			// click on the one already armed does nothing unless it is a double-click on an order ready to go,
			// which presses XMIT for you: Widget_PressChild on the button, so the click sound is XMIT's and the
			// row itself makes none.
			case CockpitWidgetKind.HddOrderRow when hddCommand != null:
				var picked = click.Id.AsHddOrder!.Value;
				if (hddCommand.SelectedOrder != picked) {
					hddCommand.SelectOrder(picked);
				} else if (click.DoubleClick && !hddCommand.AwaitingPick) {
					PressHddButtonByKey(HddLayout.Widget.Transmit);
				}

				break;

			// And a click in the map: a pick for an armed order, or the pilot selection the manual's
			// "select the pilot's marker on the map" describes.
			case CockpitWidgetKind.HddMapArea when hddCommand != null && displays.Art?.HeadsDownLayout is { } mapArea:
				PlayMapPick(hddCommand.ClickMap(click.ArtX - mapArea.MapViewport.X0, click.ArtY - mapArea.MapViewport.Y0,
					scene.World?.Objects ?? Array.Empty<SimObject>()));
				break;

			// A weapon row, dispatched by the class of gauge the row is — arm or chain on a weapon row,
			// the on/off button on an ECM or Turbo row, nothing at all on the other three pods. See
			// WeaponMounts.PressRow, which the number keys reach too.
			case CockpitWidgetKind.WeaponRow when pilotMech != null:
				pilotMech.Weapons.PressRow(click.Id.Index,
					click.Button.HasFlag(CockpitMouseButtons.Right));
				break;

			case CockpitWidgetKind.ConsoleButton when pilotMech != null:
				ApplyConsoleClick(click.Id.AsConsoleButton!.Value);
				break;

			// A shield facing: the manual's "click the respective shield symbol". Clicking the forward half
			// moves one step of balance forward and the rear half one step back — the same
			// Shield_BalanceAdjust the bracket keys reach, because in the original the key presses this
			// very widget (Mech_HandleCommand, 004157c8) rather than calling the adjust itself.
			case CockpitWidgetKind.ShieldFacing when pilotMech != null:
				pilotMech.Shields.AdjustBalance(
					towardFront: click.Id.AsShieldFacing!.Value == ShieldFacing.Front);
				break;

			// The screen-edge strip, one band of art that shows at the bottom of the forward view and
			// the top of the heads-down view. CockpitView_HandleEdgeTrigger (00433a88) picks the command
			// by current view -- 0 (pan down) from the forward view, 1 (pan up) from the heads-down one --
			// so the same widget means "down" or "up" according to where the pan already is.
			case CockpitWidgetKind.ViewEdge when click.Id.AsViewEdge == ViewEdgeStrip.Left:
				view.CommandGlance(GlanceSide.Left);
				break;

			case CockpitWidgetKind.ViewEdge when click.Id.AsViewEdge == ViewEdgeStrip.Right:
				view.CommandGlance(GlanceSide.Right);
				break;

			case CockpitWidgetKind.ViewEdge when displays.Art?.HeadsDown != null:
				view.RequestHeadsDown(headsDown: !view.Pan.AtHeadsDown);
				break;
		}
	}

	/// <summary>
	/// A key that presses an aux button — Widget_PressChild (00438d9c) on the display, which runs the button's
	/// own press slot, and so clicks as the mouse does, then flashes it. Only a button the current screen shows;
	/// false for any other, which the caller may answer some other way.
	/// </summary>
	public bool PressMfdButtonByKey(int index) {
		if (!MfdLayout.ButtonVisible(displays.Hud.Mfd, index)) {
			return false;
		}

		ApplyMfdAuxClick(index);
		audio.Director?.Play(SoundId.ButtonClick);
		displays.FlashPress(CockpitWidgetId.Mfd(index));
		return true;
	}

	/// <summary>
	/// [Enter], CockpitWidgets_HandleCommand's 0x1c case: on the scanner it presses TARGET and on TARGET STATUS
	/// SELECT, each of which steps the selection. False on any other screen, where the case steps the selection
	/// itself, with no button.
	/// </summary>
	public bool PressMfdTargetButtonByEnter() => displays.Hud.Mfd switch {
		MfdMode.Scanner => PressMfdButtonByKey(MfdLayout.TargetButton),
		MfdMode.TargetStatus => PressMfdButtonByKey(MfdLayout.SelectButton),
		_ => false,
	};

	/// <summary>
	/// A key or joystick button that presses CHAIN or LINK — ConsoleButtons_HandleCommand (004421a0), which
	/// presses the child through Widget_PressChild. Its press slot (WeaponRangeSelectGadget_OnClick, 00442dc8)
	/// clicks before it acts, as the mouse's does.
	/// </summary>
	public void PressConsoleButtonByKey(ConsoleButton button) {
		audio.Director?.Play(SoundId.ButtonClick);
		ApplyConsoleClick(button);
		displays.FlashPress(CockpitWidgetId.Console(button));
	}

	/// <summary>
	/// A key that presses a Heads-Down Display button — the display's key dispatches, which press it through
	/// Widget_PressChild, as the order column's repeat click does for XMIT. Its press slot (HddButton_OnClick,
	/// 0044be50) clicks as the mouse's does.
	/// </summary>
	public void PressHddButtonByKey(HddLayout.Widget widget) {
		ApplyHddClick(widget);
		audio.Director?.Play(SoundId.ButtonClick);
		displays.FlashPress(CockpitWidgetId.Hdd(widget));
	}

	/// <summary>
	/// [Enter] on the command display: a map click on the last picked unit or under the pointer — see
	/// <see cref="HddCommandScreen.PickByEnter"/>. The pointer is read on this frame's layout, the one the frame
	/// is drawn with.
	/// </summary>
	public void PickOnHddMapByEnter(float pointerX, float pointerY, int framebufferWidth, int framebufferHeight) {
		if (displays.HddCommand is not { } command || displays.Art is not { HeadsDownLayout: { } mapArea } cockpitArt) {
			return;
		}

		var layout = CockpitScreenLayout.Create(framebufferWidth, framebufferHeight, cockpitArt,
			view.Pan.OffsetRows, view.Pan.TravelRows, view.Glance.OffsetPanels);
		var (artX, artY) = layout.Surface(CockpitSurface.HeadsDown) is { } placed && !float.IsNaN(pointerX)
			? placed.WindowToArt(pointerX, pointerY)
			: (float.NaN, float.NaN);
		PlayMapPick(command.PickByEnter(artX - mapArea.MapViewport.X0, artY - mapArea.MapViewport.Y0,
			scene.World?.Objects ?? Array.Empty<SimObject>()));
	}

	/// <summary>[Tab] on the command display: steps the pick to the next eligible unit — see <see cref="HddCommandScreen.CycleUnit"/>.</summary>
	public void CycleHddUnitByTab() {
		if (displays.HddCommand?.CycleUnit(scene.World?.Objects ?? Array.Empty<SimObject>()) is { } found) {
			audio.Director?.Play(found ? SoundId.ScannerActive : SoundId.ScannerPassive);
		}
	}

	// HddCommandScreen_PickTarget's blip, for a unit or a gridpoint picked by a click or by [Enter].
	private void PlayMapPick(HddMapClick result) {
		if (result == HddMapClick.Picked) {
			audio.Director?.Play(SoundId.TargetSelect);
		}
	}

	/// <summary>A Heads-Down Display widget pressed, by a click or by the key the display's own dispatch maps to it.</summary>
	public void ApplyHddClick(HddLayout.Widget widget) {
		// Dark, only the page buttons act. Anything else is held, the latest press replacing any earlier
		// one, and a page button clears it by being the press that is acted on.
		bool pageButton = widget is HddLayout.Widget.PageButton0 or HddLayout.Widget.PageButton1;
		if (displays.Dropouts.HeadsDown.Dark && !pageButton) {
			displays.PendingHddPress = widget;
			return;
		}

		displays.PendingHddPress = null;
		var hddCommand = displays.HddCommand;
		switch (widget) {
			// The two page buttons dispatch HddDisplay_SetPage (0044a5e4) with their own index, and either one opens the
			// display — the same pairing F7 and F8 have.
			case HddLayout.Widget.PageButton0:
				displays.Hud = displays.Hud with { Hdd = HddPage.CommandDisplay };
				view.RequestHeadsDown(headsDown: true);
				break;

			case HddLayout.Widget.PageButton1:
				displays.Hud = displays.Hud with { Hdd = HddPage.DamageDetail };
				view.RequestHeadsDown(headsDown: true);
				break;

			// On the damage screen the up and down arrows step the component category, which is the same
			// three [S]/[I]/[W] select — up is HddDamageScreen_NextView (00450bcc), down _PrevView (00450bf0).
			// They wrap, so the pair walks the list either way without dead ends.
			case HddLayout.Widget.ArrowUp or HddLayout.Widget.ArrowDown
				when displays.Hud.Hdd == HddPage.DamageDetail:
				const int views = 3;
				int step = widget == HddLayout.Widget.ArrowUp ? 1 : views - 1;
				displays.Hud = displays.Hud with {
					HddDamage = (HddDamageView)(((int)displays.Hud.HddDamage + step) % views),
				};
				break;

			// And left and right step the herc being inspected: the player, each seated squadmate, then the
			// target, wrapping — HddDisplay_PrevSubject (0044b9e0) and _NextSubject (0044b988).
			case HddLayout.Widget.ArrowLeft or HddLayout.Widget.ArrowRight
				when displays.Hud.Hdd == HddPage.DamageDetail:
				displays.HddSubjectSlot = HddDamageSubject.Step(displays.HddSubjectSlot,
					widget == HddLayout.Widget.ArrowRight ? 1 : -1,
					slot => displays.SquadSeats[slot] != null);
				break;

			// On the command display all four arrows scroll the map instead, and the two magnifiers zoom
			// it — HddDisplay_HandleWidgetPress (0044a178)'s cases 2-7, which is one switch over the widget index for both pages.
			case HddLayout.Widget.ArrowUp when hddCommand != null:
				hddCommand.View.Pan(0, 1);
				break;

			case HddLayout.Widget.ArrowDown when hddCommand != null:
				hddCommand.View.Pan(0, -1);
				break;

			case HddLayout.Widget.ArrowLeft when hddCommand != null && displays.Hud.Hdd == HddPage.CommandDisplay:
				hddCommand.View.Pan(-1, 0);
				break;

			case HddLayout.Widget.ArrowRight when hddCommand != null && displays.Hud.Hdd == HddPage.CommandDisplay:
				hddCommand.View.Pan(1, 0);
				break;

			case HddLayout.Widget.ZoomIn when hddCommand != null:
				hddCommand.View.ZoomIn();
				break;

			case HddLayout.Widget.ZoomOut when hddCommand != null:
				hddCommand.View.ZoomOut();
				break;

			// A comm box selects its pilot, and selecting the one already selected drops it, where the
			// original's case 10-12 turns that click away and keeps the selection — a divergence recorded in
			// KNOWN_ISSUES.md; see docs/retail/simulation/heads-down-display.md#selecting-a-pilot. Selecting a
			// pilot from the damage screen also switches back to the command display, which is what that
			// case does before it selects.
			case HddLayout.Widget.PilotBox0 or HddLayout.Widget.PilotBox1 or HddLayout.Widget.PilotBox2
				when hddCommand != null:
				int slot = widget - HddLayout.Widget.PilotBox0;
				displays.Hud = displays.Hud with { Hdd = HddPage.CommandDisplay };
				hddCommand.SelectPilot(slot == hddCommand.SelectedPilot ? -1 : slot);
				break;

			// HddDisplay_HandleWidgetPress's case 13 calls no sound function; the press's click is the button's own.
			case HddLayout.Widget.Transmit when hddCommand != null:
				hddCommand.Transmit();
				break;

			case HddLayout.Widget.Cancel when hddCommand != null:
				hddCommand.Cancel();
				break;
		}
	}

	// The MFD's aux buttons, from MfdButton_OnClick's own switch (0044681c).
	//
	// SELECT (7) and TARGET (9) are one case there, not two: it branches on the current mode, stepping
	// F1's squad roster and calling TargetSelect_Cycle everywhere else. So F5's SELECT and F4's TARGET
	// are the same action, and both do what [Enter] does.
	//
	// XMIT (10) transmits the FLASH COMM page's selected row to the whole of the player's group.
	private void ApplyMfdAuxClick(int index) {
		switch (index) {
			case MfdLayout.TransmitButton when displays.Hud.Mfd == MfdMode.FlashComm && scene.World is { } xmitWorld:
				displays.FlashComm.Transmit(xmitWorld, xmitWorld.PlayerMech?.Group);
				break;

			case MfdLayout.RangeButton when displays.Hud.Mfd == MfdMode.Scanner:
				displays.CycleScannerRange();
				break;

			// Both arms then scramble the current status screen for its next refresh — see MfdStatusRefresh.
			case MfdLayout.SelectButton or MfdLayout.TargetButton when displays.Hud.Mfd == MfdMode.Status:
				displays.StatusRoster.Step();
				displays.StatusRefresh.Scramble(displays.Hud.Mfd);
				break;

			case MfdLayout.SelectButton or MfdLayout.TargetButton:
				scene.Targeting?.Cycle();
				displays.StatusRefresh.Scramble(displays.Hud.Mfd);
				break;

			case 11:
				view.PilotMech?.SetScanner(false);
				break;

			case 12:
				view.PilotMech?.SetScanner(true);
				break;
		}
	}

	// The three console buttons, from ConsoleButtons_OnChildClick's own child switch. TRACK toggles ATT
	// and nothing else: the centring that [T] does on the way off belongs to Sim_DispatchCommand's
	// scancode case, not to the button, so clicking TRACK off leaves the turret where the tracker had
	// it.
	private void ApplyConsoleClick(ConsoleButton button) {
		if (view.PilotMech is not { } pilotMech) {
			return;
		}

		switch (button) {
			case ConsoleButton.Chain:
				pilotMech.Weapons.SetGroup((pilotMech.Weapons.Group + 1) % WeaponMounts.GroupCount);
				break;

			case ConsoleButton.Link:
				pilotMech.Weapons.ToggleLink();
				break;

			case ConsoleButton.Track:
				pilotMech.ToggleAutoTrack(scene.World);
				break;
		}
	}
}
