using Herculan.Engine.Audio;
using Herculan.Engine.Video;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Shell;

/// <summary>
/// What playing the queue out does to the rest of the shell, which the host owns.
/// </summary>
public sealed class ShellMovieHooks {
	/// <summary>The bytes of an <c>AVI</c> file by its path under the disc, <see cref="ShellMovieQueue.MoviePath"/>, or null.</summary>
	public required Func<string, byte[]?> ReadMovie { get; init; }

	/// <summary><c>Shell_InstallPalette</c> (<c>004075b2</c>) and its commit, by index.</summary>
	public required Action<int> InstallPalette { get; init; }

	/// <summary><c>Shell_SetPaletteScope</c> (<c>00439da0</c>): the scope's black fill below the strip, then the palette.</summary>
	public required Action<int> SetPaletteScope { get; init; }

	/// <summary>The top-level window <c>Shell_TopWindow</c> (<c>004810e4</c>), which holds the frame's root, repainted: the root's backdrop over the scope's fill.</summary>
	public required Action RepaintRoot { get; init; }

	/// <summary>Whether the frame's full-screen panel (<c>ShellPanelWidget</c>) is up — the strip is showing.</summary>
	public required Func<bool> FrameUp { get; init; }

	/// <summary>Every tab unlit but MISSION, which is lit, and the mission screen's view shown again.</summary>
	public required Action LightMissionTab { get; init; }

	/// <summary>
	/// <c>Mission_Leave</c> (<c>00444a05</c>), the MISSION tab unlit and no tab current (<c>0xffff</c>), with
	/// nothing repainted: the screen keeps the mission screen until the location picture goes up
	/// (docs/retail/shell/movies-and-sound.md#the-shells-movies).
	/// </summary>
	public required Action LeaveMissionTab { get; init; }

	/// <summary>The campaign stage, 1-5.</summary>
	public required Func<int> CampaignStage { get; init; }

	/// <summary>
	/// The location picture, frame 0 of a <c>dba\</c> bank, put up over the whole window through a
	/// palette — the stage's theater palette, as <c>maybe_Mission_UpdateLocationTab</c>
	/// (<c>0044409f</c>) installs it. The bank is null for a stage outside the table.
	/// </summary>
	public required Action<int, string?> ShowLocationPicture { get; init; }

	/// <summary>
	/// <c>Mission_HideLocationPicture</c> (<c>004441ae</c>) taking the location picture down, the frame's root repainted and palette 1
	/// installed. Called only while the picture is up.
	/// </summary>
	public required Action HideLocationPicture { get; init; }

	/// <summary>A movie that could not be opened, or stopped on a frame it could not decode: its path and why.</summary>
	public Action<string, string>? Report { get; init; }

	/// <summary>
	/// <c>Shell_HasFocus</c> (<c>0046c094</c>): whether the shell's window has the focus. After each movie the run waits
	/// until it does — docs/retail/shell/startup.md#the-main-loop. Null counts as having it.
	/// </summary>
	public Func<bool>? HasFocus { get; init; }
}

/// <summary>
/// <c>Movie_PlayQueue</c> (<c>0041e368</c>), playing <see cref="ShellMovieQueue"/> out one movie at a
/// time through <c>Avi_Play</c> (<c>0041e01c</c>). See docs/retail/shell/movies-and-sound.md#the-shells-movies.
///
/// <para>The original is one blocking loop, with each fade and the location picture's two seconds
/// blocking inside it. This steps once per <see cref="Update"/> under the same order, so the window
/// keeps drawing, and <see cref="Active"/> is what the host holds the widgets on meanwhile — the span
/// in which the original's widgets see no event.</para>
///
/// <para>The hourglass <c>Movie_PlayQueue</c> raises before each movie is left out: it shows only
/// while nothing pumps messages, and a movie here opens inside one update. A movie that will not
/// open is skipped (see <see cref="Begin"/>).</para>
/// </summary>
public sealed class ShellMovieRun : IDisposable {
	/// <summary>How long the location picture stays up: <c>Shell_BusyWaitSeconds(2)</c> (<c>00401d53</c>)'s busy wait, in milliseconds.</summary>
	public const long LocationHoldMilliseconds = 2000;

	/// <summary>
	/// <c>MissionLocationDbaTable</c> (<c>00477fcc</c>): the location picture's bank for stages 1-4.
	/// The original indexes it by <c>stage - 1</c> unchecked.
	/// </summary>
	public static readonly string[] LocationBanks = { "ALPH2", "DELT1", "OMIC1", "BRAV1" };

	private enum Stage {
		Idle,
		FadingOut,
		Playing,
		AwaitingFocus,
		LocationFadeIn,
		LocationFadeOut,
		LocationHold,
	}

	private readonly ShellMovieQueue _queue;
	private readonly ShellMovieHooks _hooks;
	private readonly ShellSound? _sound;
	private readonly IAudioBackend? _audio;
	private Stage _stage;
	private ShellMovieEntry? _entry;
	private MoviePlayer? _player;

	// The run's bVar1: the music was started again at a location picture, so the end of the run does not.
	private bool _musicRestarted;

	// MovieQueue_IntroSkipped (00470fe0): a click or Esc or Space during an intro movie skips the intro's other part. No store
	// clearing it is known.
	private bool _introSkipped;
	private long _locationShownAt;

	public ShellMovieRun(ShellMovieQueue queue, ShellMovieHooks hooks, ShellSound? sound, IAudioBackend? audio) {
		_queue = queue;
		_hooks = hooks;
		_sound = sound;
		_audio = audio;
	}

	/// <summary>Whether the queue is being played out, from the fade before the first movie to the last entry's end.</summary>
	public bool Active => _stage != Stage.Idle;

	/// <summary>
	/// <c>Avi_Playing</c> (<c>00470d70</c>): a movie is on screen. A mouse button going down, or Esc
	/// or Space, ends it and does nothing else.
	/// </summary>
	public bool Playing => _stage == Stage.Playing;

	/// <summary>The movie on screen, or null.</summary>
	public MoviePlayer? Movie => Playing ? _player : null;

	/// <summary>Where the movie on screen plays.</summary>
	public ShellMovieRect Rect => _entry?.Rect ?? default;

	/// <summary>
	/// The call that starts a run — the main loop's once a pass, or a caller's straight after it
	/// enqueues. Nothing happens with movies off, with the ring empty, or with a run already going.
	/// </summary>
	public void Start() {
		if (!_queue.Enabled || Active) {
			return;
		}

		if (_queue.Next == null) {
			_queue.Running = false;
			return;
		}

		// The music faded out and stopped before the first movie.
		_queue.Running = true;
		_musicRestarted = false;
		_sound?.FadeOut();
		_stage = Stage.FadingOut;
	}

	/// <summary>
	/// One step of the run. <paramref name="stopPressed"/> is a mouse button, Esc or Space going down
	/// since the last step, which ends the movie on screen.
	/// </summary>
	public void Update(GL gl, TimeSpan delta, bool stopPressed) {
		switch (_stage) {
			case Stage.FadingOut:
				if (_sound?.Fading == true) {
					return;
				}

				_sound?.Stop();
				NextEntry();
				return;
			case Stage.Playing:
				if (_player == null) {
					return;
				}

				if (stopPressed && IsIntro(_entry!.Id)) {
					_introSkipped = true;
				}

				_player.Update(gl, delta);
				if (_player.HasFailed) {
					int id = _entry!.Id;
					string name = _queue.MoviePath(id) ?? $"movie 0x{id:x}";
					_hooks.Report?.Invoke(name, _player.DecodeException is { } ex
						? $"stopped on a decoder fault ({ex.GetType().Name}: {ex.Message})"
						: "stopped on a frame it could not decode");
				}

				if (stopPressed || _player.IsFinished || _player.HasFailed) {
					EndMovie();
					if (!AwaitFocus() && AfterMovie()) {
						FinishEntry();
					}
				}

				return;
			case Stage.AwaitingFocus:
				if (!HasFocus()) {
					return;
				}

				if (AfterMovie()) {
					FinishEntry();
				}

				return;
			case Stage.LocationFadeIn:
				if (_sound?.Fading == true) {
					return;
				}

				UpdateLocationTab();
				return;
			case Stage.LocationFadeOut:
				if (_sound?.Fading == true) {
					return;
				}

				_sound?.Stop();
				FinishEntry();
				return;
			case Stage.LocationHold:
				if (Environment.TickCount64 - _locationShownAt < LocationHoldMilliseconds) {
					return;
				}

				_hooks.HideLocationPicture();
				FinishEntry();
				return;
		}
	}

	// The top of the loop for the entry at the read index, through to the run's end once the ring is
	// empty. Returns as soon as an entry has to wait — on its movie, a fade or the location picture.
	private void NextEntry() {
		while (_queue.Next is { } entry) {
			_entry = entry;
			if (entry.Id == ShellMovieQueue.Dropship) {
				_musicRestarted = false;
			}

			bool intro = IsIntro(entry.Id);
			if (entry.Palette is { } palette && !intro && entry.Id != ShellMovieQueue.Credits) {
				_hooks.InstallPalette(palette);
			}

			if (entry.Id != ShellMovieQueue.Dropship && _hooks.FrameUp()) {
				_hooks.LightMissionTab();
			}

			if ((!intro || !_introSkipped) && Begin(entry)) {
				return;
			}

			if (AwaitFocus() || !AfterMovie()) {
				return;
			}

			_queue.Remove();
		}

		_entry = null;
		_stage = Stage.Idle;
		_queue.Running = false;
		if (!_musicRestarted) {
			_sound?.Start();
			_sound?.FadeIn();
		}
	}

	// Avi_Play's open. PLACEHOLDER: a movie that will not open is skipped. The original stops the shell
	// with "Please insert ESII CD and restart" for the intro, and for any other movie puts the insert-CD
	// panel (InsertCdPanel (0048d108)) up and tries again on its Continue or ends the shell on its Quit;
	// neither is ported.
	private bool Begin(ShellMovieEntry entry) {
		string? name = _queue.MoviePath(entry.Id);
		byte[]? bytes = name == null ? null : _hooks.ReadMovie(name);
		if (name == null || bytes == null || MoviePlayer.Open(bytes) is not { } player) {
			_hooks.Report?.Invoke(name ?? $"movie 0x{entry.Id:x}", "could not be opened, and is skipped");
			return false;
		}

		_player = player;
		if (_audio != null) {
			player.StartAudio(_audio);
		}

		_stage = Stage.Playing;
		return true;
	}

	private void EndMovie() {
		_player?.Stop(_audio);
		_player?.Dispose();
		_player = null;
	}

	private bool HasFocus() => _hooks.HasFocus?.Invoke() ?? true;

	// Once Avi_Play has returned, or an entry's movie was skipped, the queue pumps messages until the shell has the
	// focus. Returns whether the run now waits for it.
	private bool AwaitFocus() {
		if (HasFocus()) {
			return false;
		}

		_stage = Stage.AwaitingFocus;
		return true;
	}

	// Everything after Avi_Play returns, up to the location picture. Returns whether the entry is done;
	// false when the run now waits on the location picture's fade.
	private bool AfterMovie() {
		var entry = _entry!;
		if (entry.Id == ShellMovieQueue.Dropship && _hooks.FrameUp()) {
			_hooks.SetPaletteScope(ShellPalette.ServiceBay);
			_hooks.RepaintRoot();
		}

		if (entry.Id == ShellMovieQueue.Credits) {
			_hooks.InstallPalette(ShellPalette.ServiceBay);
		}

		if (entry.ShowsLocation) {
			_hooks.LeaveMissionTab();
			_sound?.Start();
			_sound?.FadeIn();
			_musicRestarted = true;
			_stage = Stage.LocationFadeIn;
			return false;
		}

		return true;
	}

	// maybe_Mission_UpdateLocationTab (0044409f): at stage 5 the dropship movie queued in the picture's
	// place, the arming palette through the scope and the music faded out and stopped; below it, the
	// picture, held for two seconds.
	private void UpdateLocationTab() {
		int stage = _hooks.CampaignStage();
		if (stage == ShellPalette.LunarStage) {
			_queue.Enqueue(ShellMovieQueue.Dropship, ShellMovieQueue.FullRect);
			_hooks.SetPaletteScope(ShellPalette.Arming);
			_sound?.FadeOut();
			_stage = Stage.LocationFadeOut;
			return;
		}

		_hooks.ShowLocationPicture(ShellPalette.ForStage(stage),
			stage >= 1 && stage <= LocationBanks.Length ? LocationBanks[stage - 1] : null);
		_locationShownAt = Environment.TickCount64;
		_stage = Stage.LocationHold;
	}

	// The entry freed and the read index moved on, then the loop's next turn.
	private void FinishEntry() {
		_queue.Remove();
		NextEntry();
	}

	private static bool IsIntro(int id) => id is ShellMovieQueue.IntroPart1 or ShellMovieQueue.IntroPart2;

	public void Dispose() {
		EndMovie();
		_stage = Stage.Idle;
	}
}
