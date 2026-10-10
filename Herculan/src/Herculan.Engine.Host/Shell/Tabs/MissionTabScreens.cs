using Herculan.Engine.Content;
using Herculan.Engine.Shell;
using Herculan.Engine.World;

namespace Herculan.Engine.Host.Shell.Tabs;

/// <summary>
/// The MISSION tab: the campaign map, the briefing with its Mission Map panel, and the debrief, and
/// <c>Rock &amp; Roll &gt;</c>, which hands the mission over.
/// </summary>
sealed class MissionTabScreens {
	private readonly string _installRoot;
	private readonly GameContent _content;
	private readonly ShellCanvas _canvas;
	private readonly ShellMovies _movies;
	private readonly GameInProgress _game;
	private readonly ShellScreen _screen;
	private readonly ShellDialogs _dialogs;
	private readonly ShellOutcome _outcome;
	private readonly FrontEndWindow _window;
	private readonly WidgetEvents _widgets;
	private readonly Action _repaint;

	// The arrows' auto-repeat alarm. Each arrow installs its own as it is shown and removes it as it is
	// hidden; Mission_Leave (00444a05) hides them all and Mission_Show (004441e3) shows a view's arrows
	// together, so the alarms of the arrows up are installed in one pass and tick together, as this one does.
	private readonly ShellAlarm _arrowAlarm = new(Environment.TickCount64);

	// The mission tab's briefing, objectives and intelligence report, assembled from the loaded slot's
	// career block and its own missn%d.str, and the screen that shows them, built once and kept.
	private readonly ShellMissionScreen _missionScreen;
	private ShellMissionTexts _missionTexts;
	private ShellMissionView _viewUp = ShellMissionView.Map;

	// The map inside the briefing's Mission Map panel, built for the loaded slot's mission the first
	// time the briefing comes up after a load, as the original builds it when a mission is loaded,
	// and kept until the next load. Its intro runs the first time it is shown.
	private readonly ShellMapArt? _mapArt;
	private ShellMap? _missionMap;

	// The mission tab's palette depends on the campaign stage, the career's own 1-5, and on which of its
	// views the tab opens: the map while the campaign map's first-show flag (DAT_004778aa) is
	// clear and the mission-within-stage counter is zero, the briefing otherwise
	// (TabHandler_Mission, 0043a6ca). The stage and the counter come from the loaded game.
	private bool _mapShown;

	// MissionScreenView (0048106c) at 4, which only the debrief writes, and the tab handler's own map
	// or briefing overwrites; with it, the debrief's text, its movie, and that movie's once-per-load flag
	// (DAT_004778ac), which a load and a new career clear.
	private bool _debriefUp;
	private string? _debriefText;
	private short? _debriefMovie;
	private bool _debriefMovieQueued;

	// The briefing movie plays once per load (DAT_004778ab).
	private bool _briefingMovieQueued;

	public MissionTabScreens(string installRoot, GameContent content, ShellCanvas canvas, ShellMissionScreen missionScreen,
			ShellMovies movies, GameInProgress game, ShellScreen screen, ShellDialogs dialogs, ShellOutcome outcome,
			FrontEndWindow window, WidgetEvents widgets, Action repaint) {
		_installRoot = installRoot;
		_content = content;
		_canvas = canvas;
		_missionScreen = missionScreen;
		_movies = movies;
		_game = game;
		_screen = screen;
		_dialogs = dialogs;
		_outcome = outcome;
		_window = window;
		_widgets = widgets;
		_repaint = repaint;

		_missionTexts = ShellMissionTexts.Load(game.WorkingFiles, game.LoadedGame);
		_mapArt = ShellMapArt.Load(content);

		widgets.Handle(ShellWidgetKind.MissionButton, widget => ClickMissionButton((ShellMissionButton)widget.Index));
		widgets.Handle(ShellWidgetKind.MissionArrow, ClickMissionArrow);
		widgets.Handle(ShellWidgetKind.LaunchRefusalOkay, _ => {
			_dialogs.LaunchRefusal.Close();
			_repaint();
		});
	}

	/// <summary>The map's timer, <c>Shell_TimerTicks</c> (<c>00465a1c</c>): <c>GetTickCount()</c> in units of 16 ms.</summary>
	public static uint MapClock() => (uint)(Environment.TickCount64 >> 4);

	/// <summary>The view the tab opens in (see <see cref="ViewFor"/>), from this turn's state.</summary>
	public ShellMissionView View() => ViewFor(_debriefUp, _mapShown, _game.MissionInStage);

	/// <summary>
	/// The debrief's view while MissionScreenView holds it; otherwise the map while the campaign map's first-show flag is
	/// clear and the mission-within-stage counter is zero, the briefing otherwise.
	/// </summary>
	public static ShellMissionView ViewFor(bool debriefUp, bool mapShown, int missionInStage) =>
		debriefUp ? ShellMissionView.Debriefing
		: !mapShown && missionInStage == 0 ? ShellMissionView.Map : ShellMissionView.Briefing;

	/// <summary>The map, while its intro is still running on the briefing that is up.</summary>
	public ShellMap? IntroUp() =>
		_screen.SelectedTab == ShellScreen.MissionTab && _viewUp == ShellMissionView.Briefing
			&& _missionMap is { IntroRunning: true } map ? map : null;

	/// <summary>
	/// Tab 7's entry, Mission_Show (004441e3), in the view the tab handler picks, or the debrief's. The map
	/// view queues the stage's two movies and sets the map's first-show flag whether or not they play; the
	/// briefing queues the career's briefing movie once per load, and the debrief its debrief movie, both
	/// into the Telecomm picture through the view's palette. The main loop's pass plays them.
	/// </summary>
	public void Enter() {
		_viewUp = View();

		// The briefing shows all eight arrows and the debrief the two page buttons; the map view none.
		if (_viewUp == ShellMissionView.Map) {
			_arrowAlarm.Remove();
		} else {
			_arrowAlarm.Install(ShellPointer.RepeatAlarmMilliseconds, ShellPointer.RepeatAlarmMilliseconds);
		}

		if (_viewUp == ShellMissionView.Debriefing) {
			_missionScreen.EnterDebrief(_debriefText, _canvas.Art.Sprites?.Font(ShellArt.ScreenFont));
			if (!_debriefMovieQueued && _debriefMovie is { } movie) {
				_movies.Queue.Enqueue(movie, ShellMovieQueue.TelecommRect, ShellPalette.FirstDebriefing - 1 + _game.CampaignStage);
				_debriefMovieQueued = true;
			}

			return;
		}

		if (_viewUp == ShellMissionView.Map) {
			int mapPalette = _game.CampaignStage - 1 > 3 ? ShellPalette.CampaignMapMoon : ShellPalette.CampaignMapEarth;
			_movies.Queue.Enqueue(ShellMovieQueue.StageMovieBase + _game.CampaignStage, ShellMovieQueue.TelecommRect, mapPalette);
			_movies.Queue.Enqueue(ShellMovieQueue.StageThumbnailBase + _game.CampaignStage, ShellMovieQueue.MapPanelRect,
				showsLocation: true);
			_mapShown = true;
			string? campaignText = ShellCampaignText.Load(_content, _game.CampaignStage);
			_missionScreen.EnterMap(_game.CampaignStage, campaignText, _canvas.Art.Text, _canvas.Art.Sprites?.Font(ShellArt.ScreenFont));
			return;
		}

		_missionScreen.EnterBriefing(_missionTexts, _canvas.Art.Sprites?.Font(ShellArt.ScreenFont));
		_missionMap ??= _game.LoadedGame != null ? ShellMap.Load(_installRoot, _game.WorkingFiles, _content) : null;
		if (!_briefingMovieQueued && _game.LoadedGame != null) {
			_movies.Queue.Enqueue(_game.LoadedGame.BriefingMovie, ShellMovieQueue.TelecommRect,
				ShellPalette.FirstBriefing - 1 + _game.CampaignStage);
			_briefingMovieQueued = true;
		}

		// The intro's first pass puts the camera on the full view before anything is painted. The main
		// loop runs it after the pass's movies (ShellMap_RunIntro after Movie_PlayQueue), so behind a
		// briefing movie it waits for the movie.
		if (_missionMap is { IntroRunning: true } intro && !BriefingMoviePending()) {
			intro.Advance(MapClock());
		}
		if (_missionMap == null) {
			Console.Error.WriteLine($"Mission map: no working script.dat ({_game.WorkingFiles.Script}) — the panel stays black.");
		}
	}

	/// <summary>
	/// A tab click, before the strip moves: the teardown's mission arm, Mission_Leave (00444a05), takes the report
	/// texts down for good; and the mission tab's own handler writes the map or the briefing over the debrief's view.
	/// </summary>
	public void TabClicked() {
		if (_screen.SelectedTab == ShellScreen.MissionTab) {
			_missionScreen.Leave();
			_arrowAlarm.Remove();
		}

		_debriefUp = false;
	}

	/// <summary>
	/// <c>Timer_Tick</c>'s pass over the arrows' alarm, which <c>Shell_PumpEvents</c> (<c>0046814c</c>) runs before
	/// <c>EventQueue_Pump</c>: whether it ticks this pass. The tick is delivered after the pass's clicks, by
	/// <see cref="RepeatArrows"/>.
	/// </summary>
	public bool TickArrowAlarm(long now) => _arrowAlarm.Tick(now);

	/// <summary>The alarm's tick, delivered to every arrow up; the one that is lit fires again.</summary>
	public void RepeatArrows() {
		if (_screen.SelectedTab != ShellScreen.MissionTab || _viewUp == ShellMissionView.Map) {
			return;
		}

		var first = _viewUp == ShellMissionView.Debriefing ? ShellMissionArrow.PageUp : ShellMissionArrow.MapUp;
		foreach (var arrow in Enum.GetValues<ShellMissionArrow>()) {
			if (arrow >= first) {
				_widgets.RepeatTick(new ShellWidget(ShellWidgetKind.MissionArrow, (int)arrow));
			}
		}
	}

	/// <summary>
	/// A new game in progress: the briefing's and debrief's movies to play again (DAT_004778ab and DAT_004778ac
	/// cleared), the texts assembled from it, and the mission map rebuilt on the briefing's next visit.
	/// </summary>
	public void OnAdopted(ShellWorkingFiles files, HercWorks.Core.Data.File.Sav.PlayerSave game) {
		_missionMap = null;
		_briefingMovieQueued = false;
		_debriefMovieQueued = false;
		_missionTexts = ShellMissionTexts.Load(files, game);
	}

	/// <summary>Clears the campaign map's first-show flag (DAT_004778aa), so the tab next opens on the map.</summary>
	public void ClearMapShown() => _mapShown = false;

	/// <summary>MissionScreenView = 4, with the debrief's text and movie.</summary>
	public void ShowDebrief(string? text, short? movie) {
		_debriefUp = true;
		_debriefText = text;
		_debriefMovie = movie;
	}

	public void DropDebrief() => _debriefUp = false;

	public void WriteReport(ShellDebriefReport report, ShellText? text) => _missionScreen.WriteReport(report, text);

	public ShellHit? HitAt(float canvasX, float canvasY) => _missionScreen.HitAt(canvasX, canvasY);

	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, ShellWidget? lit) {
		_missionScreen.Paint(surface, text, sprites, lit);

		// PLACEHOLDER: the map panel is left bare while the briefing movie is queued or playing,
		// its intro not yet begun; what the original's panel shows then is not known.
		if (_viewUp == ShellMissionView.Briefing && !BriefingMoviePending()) {
			_missionMap?.Paint(surface, _mapArt, MapClock());
		}
	}

	// Whether a movie is queued or playing out, which on the briefing is its movie.
	private bool BriefingMoviePending() => _movies.Active || _movies.Queue.Next != null;

	// A text button shows its text; Rock & Roll launches the mission.
	private void ClickMissionButton(ShellMissionButton button) {
		if (button == ShellMissionButton.RockAndRoll) {
			RockAndRoll();
			return;
		}

		_missionScreen.ShowText(button);
		_repaint();
	}

	// The page arrows page the text that is up (Mission_OnPageUp (004455e9), Mission_OnPageDown (0044569a)). The
	// map's six (Mission_OnMapUp (00444ee7) to Mission_OnMapZoomOut (004452f2)) call the map's method and repaint
	// it 1-4 times by the arrow's repeat count (docs/retail/shell/mission-screen.md#the-three-views). The original
	// paints and presents after every step, back to back within the one handler, so only the last stays on screen
	// and one repaint after the steps shows the same.
	private void ClickMissionArrow(ShellWidget widget) {
		var arrow = (ShellMissionArrow)widget.Index;
		if (arrow is not (ShellMissionArrow.PageUp or ShellMissionArrow.PageDown)) {
			if (_missionMap != null) {
				int repeats = _widgets.RepeatCount(widget);
				int steps = repeats < 3 ? 1 : repeats < 6 ? 2 : repeats < 9 ? 3 : 4;
				for (int i = 0; i < steps; i++) {
					_missionMap.Press(arrow);
				}

				_repaint();
			}

			return;
		}

		if (_missionScreen.Page(arrow == ShellMissionArrow.PageDown)) {
			_repaint();
		}
	}

	// Mission_OnRockAndRoll (00445509): the first test to fail puts the refusal up; otherwise the handoff
	// is written and the shell's window closes, and the host runs the mission it names.
	private void RockAndRoll() {
		if (ShellMissionLaunch.Check(_game.Hangar) is { } refusal) {
			_dialogs.LaunchRefusal.Open(refusal);
			Console.WriteLine($"Rock & Roll refused: {refusal}.");
			_repaint();
			return;
		}

		string dataDirectory = ShellWorkingFiles.DataDirectory(_installRoot);
		string? scriptPath = _game.LoadedGame == null ? null
			: ShellMissionLaunch.WriteHandoff(dataDirectory, _game.WorkingFiles, _game.LoadedGame, _game.Hangar);
		if (scriptPath == null) {
			Console.WriteLine($"Rock & Roll: no working script.dat ({_game.WorkingFiles.Script}) to launch.");
			return;
		}

		// The export rewrote data\player.mec, which the loop exit's autosave copies out with the other two.
		_game.WorkingFiles = ShellWorkingFiles.In(dataDirectory);
		_outcome.Launch = new ShellLaunch(scriptPath, dataDirectory);
		Console.WriteLine($"Rock & Roll — handoff written to {dataDirectory}; launching the mission.");
		_window.Close();
	}
}
