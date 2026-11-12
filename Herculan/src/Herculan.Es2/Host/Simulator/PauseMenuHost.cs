using Herculan.Engine.Menus;
using Herculan.Es2.Cockpit;
using Herculan.Es2.Input;

namespace Herculan.Es2.Host.Simulator;

/// <summary>
/// Earthsiege 2's mission behind the <see cref="PauseMenu"/>. This engine's own. The menu holds the mission through
/// <see cref="SimulatorFrame.Pause"/>, and its items open retail's panels directly, as [F11], [F12] and [Q] do.
/// </summary>
sealed class PauseMenuHost(SimulatorFrame frame, ModalPanels panels, MissionOutcome outcome, CockpitInput cockpit)
		: IPauseMenuHost {
	/// <inheritdoc/>
	public bool CanOpen => !panels.AnyOpen && !outcome.Over;

	/// <inheritdoc/>
	public bool PanelUp => panels.AnyOpen;

	/// <inheritdoc/>
	public void Hold() => frame.Pause();

	/// <summary>
	/// Mouse events go on being queued for the cockpit while the frame is held, so a click on the menu would land on the
	/// console behind it once the mission runs on; they are dropped as it resumes.
	/// </summary>
	public void Resume() {
		frame.Unpause();
		cockpit.Reset();
	}

	/// <inheritdoc/>
	public bool CanRaise(PauseMenuItem item) => item switch {
		PauseMenuItem.Objectives => panels.CanRaiseObjectives,
		PauseMenuItem.Preferences => panels.CanRaisePreferences,
		PauseMenuItem.Quit => panels.CanRaiseStatusAlert(outcome.Over),
		_ => false,
	};

	/// <inheritdoc/>
	public void Raise(PauseMenuItem item) {
		switch (item) {
			case PauseMenuItem.Objectives:
				panels.Objectives?.Open();
				break;
			case PauseMenuItem.Preferences:
				panels.Preferences?.Open();
				break;
			case PauseMenuItem.Quit:
				panels.RaiseStatusAlertForQuit();
				break;
		}
	}
}
