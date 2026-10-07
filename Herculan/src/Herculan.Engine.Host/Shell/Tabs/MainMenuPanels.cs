using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell.Tabs;

/// <summary>
/// The main menu and the screens that stand in for it while they are up — practice missions, preferences and
/// the registration screen START NEW GAME opens — with the menu buttons' handlers.
/// </summary>
sealed class MainMenuPanels {
	/// <summary>
	/// <c>prefs.cfg</c> option 46, which of the three demo missions the next <c>INSTANT ACTION</c> plays,
	/// stepped modulo <see cref="InstantActionMissionCount"/> after each.
	/// </summary>
	private const int InstantActionOption = 46;
	private const int InstantActionMissionCount = 3;

	/// <summary>
	/// The game state (<c>0048260e</c>) a game goes on from — the debrief's; the others are why it ended
	/// (docs/retail/shell/campaign-loop.md#where-the-debrief-goes-next).
	/// </summary>
	private const int ContinuingGameState = 2;

	private readonly string _installRoot;
	private readonly GameDisc? _disc;
	private readonly GameContent _content;
	private readonly FrontEndWindow _window;
	private readonly ShellScreen _screen;
	private readonly WidgetEvents _widgets;
	private readonly GameInProgress _game;
	private readonly ShellSaveScreen _saveScreen;
	private readonly ShellMainMenu _mainMenu;
	private readonly ShellDialogs _dialogs;
	private readonly ShellMovies _movies;
	private readonly ShellAudio _audio;
	private readonly ShellOutcome _outcome;
	private readonly CampaignScreens _loop;
	private readonly TabNavigation _navigation;
	private readonly Action _repaint;

	// The practice screen's five parameters are options 37-41 of the same array, stepped in memory;
	// with no prefs.cfg they start where the memset leaves them, at 0. The screen stands in for the
	// main menu while it is up, and is built on first use and kept.
	private ShellPracticeScreen? _practiceScreen;
	private bool _practiceUp;

	// The preferences screen shows six options of the same array. It stands in for the main menu while
	// it is up, as the practice screen does, and is built on first use and kept.
	private ShellPreferencesScreen? _preferencesScreen;

	// The registration screen START NEW GAME opens, built once at startup as the original's is, so the name
	// and the skill it holds survive a CANCEL. It stands in for the main menu while it is up.
	private readonly ShellRegistrationScreen _registration = new();

	public MainMenuPanels(string installRoot, GameDisc? disc, GameContent content, FrontEndWindow window, ShellScreen screen,
			WidgetEvents widgets, GameInProgress game, ShellSaveScreen saveScreen, ShellMainMenu mainMenu, ShellDialogs dialogs,
			ShellMovies movies, ShellAudio audio, ShellOutcome outcome, CampaignScreens loop, TabNavigation navigation,
			Action repaint) {
		_installRoot = installRoot;
		_disc = disc;
		_content = content;
		_window = window;
		_screen = screen;
		_widgets = widgets;
		_game = game;
		_saveScreen = saveScreen;
		_mainMenu = mainMenu;
		_dialogs = dialogs;
		_movies = movies;
		_audio = audio;
		_outcome = outcome;
		_loop = loop;
		_navigation = navigation;
		_repaint = repaint;

		widgets.Handle(ShellWidgetKind.MainMenuButton, widget => ClickMainMenuButton((ShellMainMenuButton)widget.Index));
		widgets.Handle(ShellWidgetKind.PracticeRow, widget => SelectPracticeRow(widget.Index));
		widgets.Handle(ShellWidgetKind.PracticeButton, widget => ClickPracticeButton((ShellPracticeButton)widget.Index));
		widgets.Handle(ShellWidgetKind.PreferencesWidget, widget => ClickPreferences((ShellPreferencesWidget)widget.Index));
		widgets.Handle(ShellWidgetKind.EndOfGameOkay, _ => {
			_dialogs.EndOfGame.Close();
			_repaint();
		});

		// Registration_OnNameEvent (0043bdee) acts on keys alone, which EditFields.DeliverKey delivers; a press
		// only gives the field the focus.
		widgets.Handle(ShellWidgetKind.RegistrationField, _ => _repaint());
		widgets.Handle(ShellWidgetKind.RegistrationButton, widget => ClickRegistration((ShellRegistrationButton)widget.Index));
	}

	public bool PreferencesUp { get; private set; }

	public bool RegistrationUp { get; private set; }

	public ShellRegistrationScreen Registration => _registration;

	public ShellHit? HitAt(float canvasX, float canvasY) {
		if (_practiceUp) {
			return _practiceScreen!.HitAt(canvasX, canvasY);
		}

		if (PreferencesUp) {
			return _preferencesScreen!.HitAt(canvasX, canvasY);
		}

		return RegistrationUp ? _registration.HitAt(canvasX, canvasY) : _mainMenu.HitAt(canvasX, canvasY);
	}

	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, bool registrationFocused) {
		if (_practiceUp) {
			_practiceScreen!.Paint(surface, text, sprites);
		} else if (PreferencesUp) {
			_preferencesScreen!.Paint(surface, text, sprites);
		} else if (RegistrationUp) {
			_registration.Paint(surface, text, sprites, focused: registrationFocused);
		} else {
			_mainMenu.Paint(surface, text, sprites);
		}
	}

	/// <summary>PRACTICE MISSIONS, 004318ab: MainMenu_Hide, PracticeScreen_Show (0044bc92), then the mode to 0.</summary>
	public void OpenPractice() {
		_practiceScreen ??= new ShellPracticeScreen(_game.Options);
		_practiceScreen.Show();
		_practiceUp = true;
		_game.SetMode(ShellCampaignMode.Training);
		_repaint();
	}

	// A main-menu button's handler.
	private void ClickMainMenuButton(ShellMainMenuButton button) {
		switch (button) {
			case ShellMainMenuButton.OnlineManual:
				OpenOnlineManual();
				break;
			case ShellMainMenuButton.StartNewGame:
				StartNewGame();
				break;
			case ShellMainMenuButton.Credits:
				_movies.Credits();
				break;
			case ShellMainMenuButton.Quit:
				Quit();
				break;
			case ShellMainMenuButton.PracticeMissions:
				OpenPractice();
				break;
			case ShellMainMenuButton.Preferences:
				OpenPreferences();
				break;
			case ShellMainMenuButton.SaveRestore:
				OpenSaveRestore();
				break;
			case ShellMainMenuButton.InstantAction:
				InstantAction();
				break;
			case ShellMainMenuButton.ContinueGame:
				ContinueGame();
				break;
			case ShellMainMenuButton.ViewDemo:
				ViewDemo();
				break;
			default:
				Console.WriteLine($"{button} — the button is live and its action is not ported yet.");
				break;
		}
	}

	// ONLINE MANUAL, 004317ea: out of full screen with option 6 written to match, the options committed
	// and all 54 saved, the shell's sound stopped, then the help file — here through OnlineManual,
	// since there is no WinHelp to hand it to.
	private void OpenOnlineManual() {
		if (_window.FullScreen) {
			_window.ToggleFullScreen();
		}

		_game.Options.Set(Prefs.DisplayModeOption, 0);
		_game.Options.Commit();
		_game.Options.Save(Enumerable.Range(0, Prefs.Length).ToArray());
		_audio.Sound?.Stop();
		OnlineManual.Open(_installRoot, _disc);
	}

	// INSTANT ACTION, 004312a6: InstantAction_Active (0047363c) set, training mode, then InstantAction_SelectDemo (0044befb) —
	// row 8 + option 46 selected, option 46 stepped modulo 3, the options committed and all saved — and
	// Begin Mission's path from the new career on. The original blanks the screen first, full screen,
	// or shows and hides the palette scope in a window, whose black fill the window closing straight
	// after never shows.
	private void InstantAction() {
		_loop.SetInstantAction();
		_game.SetMode(ShellCampaignMode.Training);
		_practiceScreen ??= new ShellPracticeScreen(_game.Options);
		_practiceScreen.SelectRow(ShellPracticeScreen.RowCount + _game.Options[InstantActionOption]);
		_game.Options.Step(InstantActionOption, InstantActionMissionCount);
		_game.Options.Commit();
		_game.Options.Save(Enumerable.Range(0, Prefs.Length).ToArray());

		if (_loop.LaunchTraining(_practiceScreen.SelectedRow, "Instant Action")) {
			_outcome.Blanked = _window.FullScreen;
		}
	}

	// CONTINUE GAME, MainMenu_OnContinue (004313e4): campaign mode, slot 10 loaded and selected on the save
	// screen, then the bare frame when the game goes on (state 2) and the END OF GAME alert over the menu
	// otherwise (EndOfGame_Show (0044cecf)). Unlike RESTORE it neither autosaves nor clears the campaign map's
	// first-show flag. The original wraps the load in the hourglass, which a load inside one update,
	// with no message pumped, would never show.
	private void ContinueGame() {
		_game.SetMode(ShellCampaignMode.Campaign);
		if (!_loop.LoadSlot(GameInProgress.CurrentGameSlot) || _game.LoadedGame == null) {
			Console.WriteLine("The current game could not be read — nothing continued.");
			return;
		}

		_saveScreen.SelectSlot(GameInProgress.CurrentGameSlot);
		if (_game.LoadedGame.GameState == ContinuingGameState) {
			_navigation.ReturnToFrame();
			return;
		}

		_dialogs.EndOfGame.Open(_game.LoadedGame.GameState);
		_repaint();
	}

	// START NEW GAME, MainMenu_OnStartNewGame (00431379): MainMenu_Hide, the mode to 1 (Shell_SetCampaignMode (0040e69e)), then
	// Registration_Show (0043bc0a) — the screen up and a left press posted at its name field with the pointer
	// locked on it, as SAVE takes a save row, so keys reach the field at once.
	private void StartNewGame() {
		_game.SetMode(ShellCampaignMode.Campaign);
		RegistrationUp = true;
		_widgets.Grab(ShellRegistrationScreen.FieldHit);
		_repaint();
	}

	// SKILL LEVEL (0043c01d) steps the skill; CANCEL (0043c098) is Registration_Hide then MainMenu_Show;
	// ACCEPT (0043c0fb) hides the screen and starts the career (CampaignScreens.StartCampaign).
	private void ClickRegistration(ShellRegistrationButton button) {
		switch (button) {
			case ShellRegistrationButton.SkillLevel:
				_registration.StepSkill();
				break;
			case ShellRegistrationButton.Cancel:
				RegistrationUp = false;
				break;
			default:
				RegistrationUp = false;
				_loop.StartCampaign(_registration.Name, _registration.Skill);
				return;
		}

		_repaint();
	}

	// VIEW DEMO, 0043156f: the screen blanked, full screen only, then exit code 5 and the loop's end. The
	// launcher answers 5 by starting the simulator with -D, which the caller does here.
	private void ViewDemo() {
		_outcome.Blanked = _window.FullScreen;
		_outcome.ExitCode = ShellHost.DemoExitCode;
		_window.Close();
	}

	// SAVE/RESTORE, 00431498: hide the menu, set the campaign mode to 1 (Shell_SetCampaignMode (0040e69e)), point the
	// save screen's EXIT back here, and enter it.
	private void OpenSaveRestore() {
		_game.SetMode(ShellCampaignMode.Campaign);
		_saveScreen.ExitTarget = ShellSaveExitTarget.MainMenu;
		_screen.SelectTab(ShellScreen.SaveTab);
		_saveScreen.Enter();
		_repaint();
	}

	// QUIT, 00431727: blank the screen (Shell_BlankScreen, 0040723d) and end the main loop, with no prompt
	// and no exit code of its own, so the shell returns the 0 its startup left and the launcher stops
	// (docs/retail/shell/screen-layout.md#quit). The loop's common exit autosaves after the window's run returns.
	private void Quit() {
		_outcome.Blanked = true;
		_window.Close();
	}

	// PREFERENCES, 0043150c: MainMenu_Hide, then PreferencesScreen_Enter (004366b5), which seeds the
	// checkboxes from the options and shows the screen.
	private void OpenPreferences() {
		_preferencesScreen ??= new ShellPreferencesScreen(_game.Options,
			ShellArt.ReadBankFrames(_content, ShellPreferencesScreen.CheckBoxBank),
			isFullScreen: () => _window.FullScreen, toggleFullScreen: _window.ToggleFullScreen);
		PreferencesUp = true;
		_repaint();
	}

	// A widget's handler. Cancel (PreferencesScreen_OnCancel, 00436b90) and Accept (PreferencesScreen_OnAccept, 00436c51) end in PreferencesScreen_Hide (00436717) and
	// MainMenu_Show, which take the screen down and put the menu back.
	private void ClickPreferences(ShellPreferencesWidget widget) {
		if (_preferencesScreen == null) {
			return;
		}

		if (_preferencesScreen.Click(widget, _audio.Sound)) {
			PreferencesUp = false;
			_screen.SelectTab(ShellScreen.MainMenuTab);
		}

		_repaint();
	}

	// A practice row's handler, PracticeScreen_OnRow0-7 (0044c413-0044c6ba): PracticeScreen_SelectRow
	// (0044bd7c), a no-op on the row already lit.
	private void SelectPracticeRow(int row) {
		if (_practiceScreen?.SelectRow(row) == true) {
			_repaint();
		}
	}

	// The five parameter labels step their option, forward on the left release and back on the right
	// (0044bf29-0044c21d). Main Menu (0044c2da) hides the screen and shows the menu. Begin Mission
	// (0044c396) is BeginPractice.
	private void ClickPracticeButton(ShellPracticeButton button) {
		if (_practiceScreen == null) {
			return;
		}

		switch (button) {
			case ShellPracticeButton.MainMenu:
				_practiceUp = false;
				_screen.SelectTab(ShellScreen.MainMenuTab);
				break;
			case ShellPracticeButton.BeginMission:
				BeginPractice();
				return;
			default:
				_practiceScreen.Step(button, _widgets.EventButton == ShellMouseButton.Left);
				break;
		}

		_repaint();
	}

	// Begin Mission (0044c396): the options committed and saved to prefs.cfg, then LaunchTraining on the
	// lit row.
	private void BeginPractice() {
		_game.Options.Commit();
		_game.Options.Save(Enumerable.Range(0, Prefs.Length).ToArray());
		_loop.LaunchTraining(_practiceScreen!.SelectedRow, "Begin Mission");
	}
}
