using Herculan.Engine.Content;
using Herculan.Engine.Host.Shell.Tabs;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// What the shell's screens do at each step of the campaign loop (docs/retail/shell/campaign-loop.md): the dialog,
/// movies, tab and window that follow a load, a debrief, a new career or a launch. The steps themselves, and where
/// each leads, are <see cref="ShellCampaignLoop"/>'s.
/// </summary>
sealed class CampaignScreens {
	private readonly GameInProgress _game;
	private readonly ShellCampaignLoop _campaign;
	private readonly HangarTabs _hangar;
	private readonly MissionTabScreens _mission;
	private readonly TabNavigation _navigation;
	private readonly StartupScreen _startup;
	private readonly ShellMovies _movies;
	private readonly ShellCanvas _canvas;
	private readonly ShellDialogs _dialogs;
	private readonly ShellScreen _screen;
	private readonly ShellOutcome _outcome;
	private readonly FrontEndWindow _window;
	private readonly Action _repaint;

	// Shell_MissionPickerEnabled (00482284), -@: whether the mission-name dialog is left up for a click rather than
	// clicked at once.
	private readonly bool _missionPicker;

	// What a mission load that fails goes back to, and the training launch's name for its log, both set by whichever
	// step reached Career_StartMissionLoad.
	private Action _loadFailed = () => { };
	private string _trainingLabel = string.Empty;

	public CampaignScreens(string installRoot, GameContent content, GameInProgress game, ShellSaveScreen saveScreen, HangarTabs hangar,
			MissionTabScreens mission, TabNavigation navigation, StartupScreen startup, ShellMovies movies, ShellCanvas canvas,
			ShellDialogs dialogs, ShellScreen screen, ShellOutcome outcome, FrontEndWindow window, WidgetEvents widgets,
			bool missionPicker, Action repaint) {
		_game = game;
		_campaign = new ShellCampaignLoop(installRoot, content, game, saveScreen);
		_hangar = hangar;
		_mission = mission;
		_navigation = navigation;
		_startup = startup;
		_movies = movies;
		_canvas = canvas;
		_dialogs = dialogs;
		_screen = screen;
		_outcome = outcome;
		_window = window;
		_missionPicker = missionPicker;
		_repaint = repaint;

		// The mission map rebuilt on the briefing's next visit, and the repair screen on the adopted hangar.
		_campaign.Adopted += (adopted, files) => {
			_mission.OnAdopted(files, adopted);
			_hangar.OnAdopted();
		};

		_campaign.SlotLoaded += entry => Console.WriteLine($"Loaded {entry.FileName}: "
			+ (ShellSaveSummary.From(_game.LoadedGame!) is { } summary
				? $"{summary.PilotName}, sector {summary.Sector}, mission {summary.Mission + 1}."
				: "no pilot record.")
			+ (_hangar.RepairBay >= 0
				? $" Repair opens on bay {_hangar.RepairBay}."
				: " No built machine in any hangar bay."));

		widgets.Handle(ShellWidgetKind.ReplayButton, widget => ClickReplay((ShellReplayButton)widget.Index));
		widgets.Handle(ShellWidgetKind.MissionNameButton, widget => ClickMissionName((ShellMissionNameButton)widget.Index));
	}

	/// <inheritdoc cref="ShellCampaignLoop.SetInstantAction"/>
	public void SetInstantAction() => _campaign.SetInstantAction();

	/// <inheritdoc cref="ShellCampaignLoop.LoadSlot"/>
	public bool LoadSlot(int slot) => _campaign.LoadSlot(slot);

	/// <summary>
	/// The simulator's return (<see cref="ShellCampaignLoop.ReturnFromMission"/>), then wherever the debrief goes
	/// next (docs/retail/shell/campaign-loop.md#where-the-debrief-goes-next): the menu when there is nothing to
	/// debrief.
	/// </summary>
	public void ReturnFromMission() {
		if (_campaign.ReturnFromMission() is not { } result) {
			_startup.Begin();
			return;
		}

		if (result.Report is { } report) {
			_mission.WriteReport(report, _canvas.Art.Text);
		}

		switch (result.State) {
			case ShellDebrief.CampaignOverState or ShellDebrief.ShellState:
				// ReplayDialog_Show(state) (0044ca57).
				_dialogs.Replay.Open(result.State.Value);
				break;
			case ShellDebrief.CampaignWonState:
				// Game_SaveSlot(10), the two ending movies, palette 1 and the startup sequence.
				_game.AutoSave();
				_movies.Queue.Enqueue(ShellMovieQueue.Victory, ShellMovieQueue.FullRect);
				_movies.Queue.Enqueue(ShellMovieQueue.Credits, ShellMovieQueue.FullRect);
				_movies.Start();
				_canvas.InstallPalette(ShellPalette.ServiceBay);
				_startup.Begin();
				break;
			case ShellDebrief.NextMissionState:
				// MissionScreenView = 4, then Career_StartMissionLoad: the next mission's load, whose campaign end
				// puts the frame up and the mission tab in that view, or the menu when it will not load.
				_mission.ShowDebrief(result.Debrief is { } text ? ShellMissionTexts.AssembleDebrief(text) : null,
					result.Debrief?.Movie);
				if (_game.LoadedGame != null) {
					StartMissionLoad(_startup.Begin);
				}

				break;
			default:
				// A training debrief leaves the state alone and shows the startup sequence.
				_startup.Begin();
				break;
		}
	}

	/// <summary>
	/// ACCEPT, Registration_OnAccept (0043c0fb): the screen hidden, the campaign map's first-show flag
	/// (DAT_004778aa) cleared, and the career started (<see cref="ShellCampaignLoop.StartCampaign"/>), its position
	/// step ending in the mission load; the menu stays up when either fails.
	/// </summary>
	public void StartCampaign(string name, int skill) {
		_mission.ClearMapShown();
		if (!_campaign.StartCampaign(name, skill)) {
			_repaint();
			return;
		}

		StartMissionLoad(_repaint);
	}

	/// <summary>
	/// A training career started on <paramref name="row"/> (<see cref="ShellCampaignLoop.StartTraining"/>), its position
	/// step ending in the mission load. Returns whether that launched the mission, closing the shell on exit code 2: not
	/// when it failed, nor when the mission-name dialog is left up for the launch to come from its button.
	/// </summary>
	public bool LaunchTraining(int row, string label) {
		_trainingLabel = label;
		return _campaign.StartTraining(row, label) && StartMissionLoad(_repaint);
	}

	/// <summary>
	/// Career_StartMissionLoad (00412ce1), the end of a career's position step: the mission-name dialog shown with the
	/// position's mission as its default (MissionNameDialog_Show, 0044db25), and then, without -@, a press and a release
	/// posted at its Use Default, which the original delivers once the handler that got here has returned. Here that
	/// click is the load itself, run at once. <paramref name="loadFailed"/> is where a load that fails goes back to.
	/// Returns whether the load ran and succeeded.
	/// </summary>
	private bool StartMissionLoad(Action loadFailed) {
		_loadFailed = loadFailed;
		if (!_missionPicker) {
			return LoadCurrentMission(null);
		}

		_dialogs.MissionName.Open(_campaign.CareerMissionPath());
		_repaint();
		return false;
	}

	/// <summary>
	/// Career_LoadCurrentMission (0044d4cc) for a null <paramref name="missionPath"/>, MissionNameDialog_OnLoad (0044d5bd)
	/// for a typed one. A campaign's puts the frame up on the mission tab in the view MissionScreenView holds; a training
	/// one's launches, closing the shell on exit code 2. Returns whether it loaded.
	/// </summary>
	private bool LoadCurrentMission(string? missionPath) {
		if (_game.Mode == ShellCampaignMode.Campaign) {
			if (!_campaign.LoadCareerMission(missionPath)) {
				_mission.DropDebrief();
				_loadFailed();
				return false;
			}

			_screen.ReturnToFrame(_game.Mode);
			_navigation.ShowMissionView();
			return true;
		}

		if (_campaign.LaunchTraining(_trainingLabel, missionPath) is not { } launch) {
			_loadFailed();
			return false;
		}

		_outcome.Launch = launch;
		_window.Close();
		return true;
	}

	// A DEBUG dialog button. Both hide the dialog first (MissionNameDialog_Hide, 0044db87): Use Default
	// (MissionNameDialog_OnUseDefault, 0044d55a) loads the career position's mission, the other
	// (MissionNameDialog_OnLoad, 0044d5bd) the typed one.
	private void ClickMissionName(ShellMissionNameButton button) {
		_dialogs.MissionName.Close();
		LoadCurrentMission(button == ShellMissionNameButton.UseDefault ? null : _dialogs.MissionName.LoadPath);
	}

	// A REPLAY MISSION? button. No (ReplayDialog_OnNo, 0044cbbd) saves slot 10, takes the dialog down and
	// shows the main menu. Yes (ReplayDialog_OnYes, 0044cb44) takes it down and replays slot 10's mission
	// (ShellCampaignLoop.Replay), ending the shell on exit code 2.
	private void ClickReplay(ShellReplayButton button) {
		_dialogs.Replay.Close();
		if (button == ShellReplayButton.No) {
			_game.AutoSave();
			_repaint();
			return;
		}

		if (_campaign.Replay() is not { } launch) {
			_repaint();
			return;
		}

		_outcome.Launch = launch;
		_window.Close();
	}
}
