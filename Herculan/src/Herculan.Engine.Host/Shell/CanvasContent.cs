using Herculan.Engine.Host.Shell.Tabs;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell;

/// <summary>What is up on the canvas below the strip: the tab's screen, the dialogs over it, and which widget is under a point.</summary>
sealed class CanvasContent {
	private readonly ShellCanvas _canvas;
	private readonly ShellScreen _screen;
	private readonly ShellPointer _pointer;
	private readonly StartupScreen _startup;
	private readonly ShellDialogs _dialogs;
	private readonly ShellMovies _movies;
	private readonly MainMenuPanels _menu;
	private readonly SaveRestoreTab _save;
	private readonly HangarTabs _hangar;
	private readonly MissionTabScreens _mission;
	private readonly ShellSurface _surface = new();

	public CanvasContent(ShellCanvas canvas, ShellScreen screen, ShellPointer pointer, StartupScreen startup, ShellDialogs dialogs,
			ShellMovies movies, MainMenuPanels menu, SaveRestoreTab save, HangarTabs hangar, MissionTabScreens mission) {
		_canvas = canvas;
		_screen = screen;
		_pointer = pointer;
		_startup = startup;
		_dialogs = dialogs;
		_movies = movies;
		_menu = menu;
		_save = save;
		_hangar = hangar;
		_mission = mission;
	}

	/// <summary>
	/// The widget under a canvas point. The strip is drawn over the content and hit first; below it,
	/// whichever tab is up.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		if (_dialogs.TryHitAt(canvasX, canvasY, out var dialog)) {
			return dialog;
		}

		if (_screen.HitAt(canvasX, canvasY) is { } strip) {
			return strip;
		}

		return _screen.SelectedTab switch {
			ShellScreen.MainMenuTab => _menu.HitAt(canvasX, canvasY),
			ShellScreen.SaveTab => _save.HitAt(canvasX, canvasY),
			ShellScreen.MissionTab => _mission.HitAt(canvasX, canvasY),
			var tab => _hangar.HitAt(tab, canvasX, canvasY),
		};
	}

	/// <summary>
	/// Rasterizes the current tab's content and hands it to the renderer. Called on a state change
	/// rather than per frame: it resolves a whole canvas of palette indices and uploads a texture.
	/// The whole surface is painted each time, where the original repaints only the widgets that
	/// moved. Rows overlap by a pixel and whichever paints second owns the shared border row, so
	/// each screen paints its selected row last, as Repair_SelectHotspot's incoming repaint lands.
	/// </summary>
	public void Repaint() {
		if (!_canvas.HasRenderer) {
			return;
		}

		// Until the startup sequence has put it up, the menu is hidden and the sequence is all there is.
		if (_startup.Running) {
			_canvas.SetContent(null);
			return;
		}

		var art = _canvas.Art;
		_surface.Clear();

		// REPLAY MISSION? stands on a picture of the backdrop over the whole display, which is what the
		// renderer draws beneath an empty content, so the dialog is all there is to paint.
		if (_dialogs.Replay.IsOpen) {
			_dialogs.Replay.Paint(_surface, art.Text, art.Sprites);
			_canvas.SetContent(_surface);
			return;
		}

		// Tabs 2-7 switch palette through the scope, whose paint blacks out everything below the
		// strip; the tab's screen, if one is ported, draws over that. The lunar movie plays over the
		// same fill.
		bool filled = ShellPalette.FillsScope(_screen.SelectedTab) || _movies.ScopeFilled;
		if (filled) {
			ShellPalette.PaintScope(_surface);
		}

		bool painted = true;
		switch (_screen.SelectedTab) {
			case ShellScreen.MainMenuTab:
				_menu.Paint(_surface, art.Text, art.Sprites,
					registrationFocused: _pointer.Focused is { Kind: ShellWidgetKind.RegistrationField });
				break;
			case ShellScreen.SaveTab:
				_save.Paint(_surface, art.Text, art.Sprites,
					_pointer.Focused is { Kind: ShellWidgetKind.SaveRow } focused ? focused.Index : null);
				break;
			case ShellScreen.MissionTab:
				_mission.Paint(_surface, art.Text, art.Sprites, _pointer.Lit);
				break;
			default:
				painted = _hangar.Paint(_screen.SelectedTab, _surface, art.Text, art.Sprites);
				break;
		}

		if (!painted && !filled) {
			_canvas.SetContent(null);
			return;
		}

		_dialogs.PaintOver(_surface, art.Text, art.Sprites);
		_canvas.SetContent(_surface);
	}
}
