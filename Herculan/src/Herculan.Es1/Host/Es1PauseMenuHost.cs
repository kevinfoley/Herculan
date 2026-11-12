using Herculan.Engine.Input;
using Herculan.Engine.Menus;
using Herculan.Es1.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// <b>This engine's own:</b> <i>Metaltech: Earthsiege</i>'s mission behind HERCULAN's <see cref="PauseMenu"/>. The menu
/// holds the mission through <see cref="Es1SimulationRunner.HostHeld"/>. An item sends its panel's key straight into ES1's
/// keyboard (<see cref="Es1HostKeyboard.Tap"/>): <c>F11</c>, <c>F12</c> or <c>Q</c>, the keys of
/// docs/es1/simulation/main-loop.md#commands--sim_dispatchcommand-00059df8 and
/// docs/es1/simulation/cockpit-panel.md#keys-and-the-view-device. The hold is lifted until a tick has taken the key and
/// the work it started, the panel's loop among it, has run, then put back with the menu showing again; a key that raises
/// no panel comes back the same way.
/// </summary>
/// <param name="runner">The simulation runner, once built.</param>
/// <param name="frameLoop">The frame loop, whose work stands while a panel is up; none with no monitor, and then no item raises a panel.</param>
internal sealed class Es1PauseMenuHost(Func<Es1SimulationRunner?> runner, Func<Es1FrameLoop?> frameLoop) : IPauseMenuHost {
	// The runner's tick count when an item sent its key, until the work that key started has run.
	private long? _raisedAt;

	/// <summary>Whether a retail panel is up: <c>AlertPanel_OpenCount</c> above 0.</summary>
	private bool PanelOpen => (runner()?.Simulation.Panels?.OpenCount ?? 0) > 0;

	/// <inheritdoc/>
	public bool CanOpen => runner() != null && !PanelOpen && _raisedAt == null;

	/// <inheritdoc/>
	public bool PanelUp => _raisedAt != null || PanelOpen;

	/// <inheritdoc/>
	public void Hold() {
		if (runner() is { } held) {
			held.HostHeld = true;
		}
	}

	/// <inheritdoc/>
	public void Resume() {
		_raisedAt = null;
		if (runner() is { } held) {
			held.HostHeld = false;
		}
	}

	/// <summary>
	/// Whether the item's key would raise its panel: none with no panels or no frame loop. <c>F11</c> is the cockpit's,
	/// which answers no key while its widgets are off (<see cref="View.Es1Cockpit.WidgetsOff"/>); <c>F12</c> and <c>Q</c>
	/// are <c>Sim_DispatchCommand</c>'s own.
	/// </summary>
	public bool CanRaise(PauseMenuItem item) {
		if (runner() is not { Simulation: { Panels: not null } simulation } || frameLoop() == null) {
			return false;
		}

		return item != PauseMenuItem.Objectives || simulation.Cockpit is { WidgetsOff: false };
	}

	/// <inheritdoc/>
	public void Raise(PauseMenuItem item) {
		if (runner() is not { } raising) {
			return;
		}

		raising.HostKeyboard.Tap(ChordOf(item));
		raising.HostHeld = false;
		_raisedAt = raising.Ticks;
	}

	/// <summary>
	/// Once a host frame, after the frame loop has run: once a tick has run since an item sent its key and the frame's
	/// work no longer stands, the panel is down, or never came up, and the mission is held again under the menu.
	/// </summary>
	public void Update() {
		if (_raisedAt is { } at && runner() is { } held && held.Ticks > at && frameLoop() is { Waiting: false }) {
			_raisedAt = null;
			held.HostHeld = true;
		}
	}

	// The key ES1 gives the item's action, as a profile's binding of it sends.
	private static KeyChord ChordOf(PauseMenuItem item) => Es1CustomControls.ChordOf(item switch {
		PauseMenuItem.Objectives => KeyAction.Objectives,
		PauseMenuItem.Preferences => KeyAction.Preferences,
		_ => KeyAction.MissionStatus,
	})!.Value;
}
