using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// The movie queue, played out by the run built with the window, and what playing it leaves on the screen: the
/// location picture a map movie leaves up for two seconds, the scope's black fill, and CREDITS' blank.
/// </summary>
sealed class ShellMovies : IDisposable {
	private readonly string _installRoot;
	private readonly GameDisc? _disc;
	private readonly GameContent _content;
	private readonly ShellCanvas _canvas;
	private readonly ShellScreen _screen;
	private readonly ShellMissionScreen _missionScreen;
	private readonly GameInProgress _game;
	private readonly FrontEndWindow _window;
	private readonly PolledButtons _buttons;
	private readonly Action _repaint;
	private GL? _gl;
	private ShellMovieRun? _run;

	// The location picture a map movie leaves up for two seconds.
	private ShellImage? _locationPicture;
	private GpuTexture? _locationTexture;

	public ShellMovies(string installRoot, GameDisc? disc, GameContent content, ShellCanvas canvas, ShellScreen screen,
			ShellMissionScreen missionScreen, GameInProgress game, FrontEndWindow window, PolledButtons buttons,
			bool moviesEnabled, Action repaint) {
		_installRoot = installRoot;
		_disc = disc;
		_content = content;
		_canvas = canvas;
		_screen = screen;
		_missionScreen = missionScreen;
		_game = game;
		_window = window;
		_buttons = buttons;
		_repaint = repaint;
		Queue = new ShellMovieQueue(moviesEnabled, content.IsV110 ? content.Language : GameLanguage.English);
	}

	public ShellMovieQueue Queue { get; }

	/// <summary>Whether the queue is playing out, which holds the shell as Movie_PlayQueue's loop does.</summary>
	public bool Active => _run?.Active == true;

	public bool Playing => _run?.Playing == true;

	/// <summary>Whether CREDITS has the screen blanked round its movie.</summary>
	public bool CreditsUp { get; private set; }

	/// <summary>The scope's black fill below the strip, which the lunar movie plays over.</summary>
	public bool ScopeFilled { get; private set; }

	/// <summary>Builds the run once the window has a GL context and the shell its sound.</summary>
	public void Attach(GL gl, ShellSound? sound, IAudioBackend? audio) {
		_gl = gl;
		_run = new ShellMovieRun(Queue, Hooks(), sound, audio);
	}

	/// <summary>Movie_PlayQueue: starts what has been queued, if nothing is playing.</summary>
	public void Start() => _run?.Start();

	/// <summary>
	/// One update while the queue plays out. A mouse button or Esc or Space going down ends the movie on
	/// screen and reaches nothing else: MainWndProc drops the button's messages while one plays, and
	/// WinButton_HandleEvent and ESButtonBitmap_HandleEvent every mouse event while the queue runs. The buttons'
	/// state is still taken, so a press made meanwhile is spent rather than delivered afterwards.
	/// </summary>
	public void Update(double delta) {
		bool stop = _buttons.SkipPressed(_window.Mouse, _window.Keyboard);

		if (_gl != null) {
			_run!.Update(_gl, TimeSpan.FromSeconds(delta), stop);
		}
	}

	/// <summary>
	/// CREDITS, MainMenu_OnCredits (004315ec): a bare window shown and the screen blanked, the credits queued
	/// and played straight away, then the screen blanked again, the window hidden and the menu repainted
	/// under it (<see cref="EndCredits"/>).
	/// </summary>
	public void Credits() {
		CreditsUp = true;
		Queue.Enqueue(ShellMovieQueue.Credits, ShellMovieQueue.FullRect);
		Start();
	}

	public void EndCredits() {
		CreditsUp = false;
		_repaint();
	}

	public void DrawLocationPicture(ShellScreenLayout layout) {
		if (_locationTexture != null && _locationPicture != null) {
			_canvas.DrawTexture(layout, _locationTexture, 0, 0, _locationPicture.Width, _locationPicture.Height);
		}
	}

	public void DrawMovie(ShellScreenLayout layout) {
		if (_run?.Movie is { Texture: { } texture } && _canvas.HasRenderer) {
			var rect = _run.Rect;
			_canvas.DrawTexture(layout, texture, rect.X, rect.Y, rect.Width, rect.Height);
		}
	}

	/// <summary>The movie and the location picture hold GL textures, released while the context is current.</summary>
	public void Dispose() {
		_run?.Dispose();
		_locationTexture?.Dispose();
		_locationTexture = null;
	}

	// What playing the queue does to the rest of the shell.
	private ShellMovieHooks Hooks() => new() {
		ReadMovie = path => {
			using var stream = GameInstall.OpenDiscFile(_installRoot, _disc, path);
			return stream != null ? MovieHost.ReadMovie(stream) : null;
		},
		InstallPalette = index => {
			_canvas.InstallPalette(index);
			_repaint();
		},
		SetPaletteScope = index => {
			ScopeFilled = true;
			_canvas.InstallPalette(index);
			_repaint();
		},
		RepaintRoot = () => {
			ScopeFilled = false;
			_repaint();
		},
		FrameUp = () => _screen.StripVisible,
		LightMissionTab = () => {
			_screen.LightOnly(ShellScreen.MissionTab);
			_repaint();
		},
		LeaveMissionTab = () => {
			_missionScreen.Leave();
			_screen.LeaveTab();
		},
		CampaignStage = () => _game.CampaignStage,
		ShowLocationPicture = (palette, bank) => {
			_canvas.InstallPalette(palette);
			_locationPicture = bank == null ? null : _canvas.Art.LoadBankFrame(_content, bank, 0);
			_locationTexture?.Dispose();
			_locationTexture = _locationPicture == null || _gl == null ? null
				: new GpuTexture(_gl, _locationPicture.Pixels, _locationPicture.Width, _locationPicture.Height);
		},
		HideLocationPicture = () => {
			_locationTexture?.Dispose();
			_locationTexture = null;
			_locationPicture = null;
			_canvas.InstallPalette(ShellPalette.ServiceBay);
			_repaint();
		},
		Report = (path, what) => Console.WriteLine($"Movie {path} {what}."),
		HasFocus = () => _window.HasFocus,
	};
}
