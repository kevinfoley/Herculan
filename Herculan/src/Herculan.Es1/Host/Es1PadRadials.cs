using Herculan.Engine.Input;
using Herculan.Engine.Menus;
using Herculan.Es1.Hdd;
using Herculan.Es1.Input;
using Herculan.Es1.Mfd;
using Herculan.Es1.Sim;
using ImGuiNET;
using Silk.NET.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// <b>This engine's own:</b> the two <see cref="PadRadial"/>s over a <i>Metaltech: Earthsiege</i> mission, opened by the
/// inputs the controller's Custom profile binds to <see cref="KeyAction.SquadOrdersWheel"/> and
/// <see cref="KeyAction.ScreensWheel"/> (the stick clicks by default) and aimed by the gamepad's left and right sticks
/// (docs/herculan/herculan-key-bindings.md, "Squad orders on a gamepad" and "Screens on a gamepad"). A choice goes into
/// ES1's keyboard as its key (<see cref="Es1HostKeyboard.Tap"/>), so the cockpit takes it through its own keys and a
/// recording takes the key: an order as FLASH COMM's [Alt] key for the order its row shows
/// (docs/es1/simulation/mfd-flash-comm.md#the-order-helpers), a screen as its function key.
/// </summary>
/// <param name="runner">The simulation runner, once built.</param>
/// <param name="port">The game port over the host's controller, once built.</param>
internal sealed class Es1PadRadials(Func<Es1SimulationRunner?> runner, Func<Es1ControllerPort?> port) {
	// The screen radial's eight, in its order: the MFD's six, then the Heads-Down Display's two pages.
	private static readonly KeyAction[] ScreenActions = [
		KeyAction.MfdStatus, KeyAction.MfdFlashComm, KeyAction.MfdNavMap, KeyAction.MfdScanner, KeyAction.MfdTarget,
		KeyAction.MfdMissileCam, KeyAction.CommandDisplay, KeyAction.DamageDetail,
	];

	private const int MfdScreenCount = Es1MfdDisplay.ModeCount;

	// Each FLASH COMM row's [Alt] action; row 4's is the first order's, its second order's being SendEmcon.
	private static readonly KeyAction[] RowActions = [
		KeyAction.SendAttackMyTarget, KeyAction.SendIgnoreMyTarget, KeyAction.SendHelpMeOut, KeyAction.SendJoinOnMe,
		KeyAction.SendScanForHostiles, KeyAction.SendFireAtWill,
	];

	// Each label radius keeps its ring's neighbouring labels apart, as Earthsiege 2's do.
	private readonly PadRadial _orders = new(ControllerDevice.GamepadLeftStick, 0, Es1MfdFlashCommScreen.RowCount, 200f);
	private readonly PadRadial _screens = new(ControllerDevice.GamepadRightStick, 2, ScreenActions.Length, 260f);

	/// <summary>Whether either radial mutes the port (<see cref="PadRadial.MutesStick"/>).</summary>
	public bool MutesStick => _orders.MutesStick || _screens.MutesStick;

	/// <summary>
	/// Once a host frame, before the frame's ticks: reads the radials, each opening only while <paramref name="allowed"/>
	/// and the other is down, and taps a choice's key. With no Custom profile, as a joystick in Original mode has, neither
	/// opens.
	/// </summary>
	public void Update(IInputContext input, bool allowed) {
		var read = port();
		var mfd = runner()?.Simulation.Cockpit?.Mfd;
		allowed &= read != null && mfd != null;
		if (_orders.Update(input, allowed && !_screens.IsOpen, Click(read, KeyAction.SquadOrdersWheel)) is { } row && mfd != null) {
			Tap(OrderAction(mfd.FlashCommScreen, row));
		}

		if (_screens.Update(input, allowed && !_orders.IsOpen, Click(read, KeyAction.ScreensWheel)) is { } screen) {
			Tap(ScreenActions[screen]);
		}
	}

	/// <summary>Takes both radials down taking nothing.</summary>
	public void Cancel() {
		_orders.Cancel();
		_screens.Cancel();
	}

	/// <summary>Draws whichever radial is up, in <paramref name="font"/>. Call once per frame inside the ImGui frame.</summary>
	public void Draw(ImFontPtr font) {
		_orders.Draw(OrderLabel, font);
		_screens.Draw(ScreenLabel, font);
	}

	private static Func<ControllerDevice, bool> Click(Es1ControllerPort? read, KeyAction wheel) =>
		device => read?.HostActionHeldOn(device, wheel) == true;

	// The [Alt] key that transmits the order the row shows: row 4's second order, EMCON, has its own.
	private static KeyAction OrderAction(Es1MfdFlashCommScreen flashComm, int row) =>
		flashComm.OrderOf(row) == Es1SquadOrder.RadarOffVerb ? KeyAction.SendEmcon : RowActions[row];

	private void Tap(KeyAction action) {
		if (runner() is { } tapping && Es1CustomControls.ChordOf(action) is { } chord) {
			tapping.HostKeyboard.Tap(chord);
		}
	}

	// An order's label: the order FLASH COMM's row shows, in STRINGS0.STR's own words.
	private string OrderLabel(int row) {
		var cockpit = runner()?.Simulation.Cockpit;
		if (cockpit == null) {
			return string.Empty;
		}

		int order = cockpit.Mfd.FlashCommScreen.OrderOf(row);
		return cockpit.Hdd.Text(Es1MfdFlashCommScreen.OrderGroup, order) ?? string.Empty;
	}

	// A screen's label: which display, its function key, and its title in STRINGS0.STR's own words, the MFD's from its
	// title captions and the Heads-Down Display's from its page titles.
	private string ScreenLabel(int screen) {
		var cockpit = runner()?.Simulation.Cockpit;
		string? title = screen < MfdScreenCount
			? cockpit?.Mfd.Caption(screen)
			: cockpit?.Hdd.Text(Es1HddDisplay.MapTitleGroup, screen - MfdScreenCount);
		return $"{(screen < MfdScreenCount ? "MFD" : "HDD")} {Es1CustomControls.ChordOf(ScreenActions[screen])} {title}".TrimEnd();
	}
}
