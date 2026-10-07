using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// The shell's sound, created with the window since it needs the device. With --no-sound there is
/// none at all, as with the original's -s, which skips the sound manager's setup and so leaves
/// option 5 alone too.
/// </summary>
sealed class ShellAudio : IDisposable {
	private readonly GameContent _content;
	private readonly SimulatorPreferences _options;
	private readonly bool _silent;
	private readonly SoundCfg _soundCfg;

	public ShellAudio(GameContent content, SimulatorPreferences options, bool silent, SoundCfg soundCfg) {
		_content = content;
		_options = options;
		_silent = silent;
		_soundCfg = soundCfg;
	}

	/// <summary>
	/// The device as it is, which the movies play through. The sound manager's samples go through a
	/// <see cref="SoundCfgBackend"/> on it instead, which VSHELL's <c>sound.cfg</c> reading, <c>Sfx_Construct</c>
	/// (<c>0042bf3d</c>), configures.
	/// </summary>
	public IAudioBackend? Backend { get; private set; }

	public ShellSound? Sound { get; private set; }

	/// <summary>Whether a fade is running, which holds the shell until it is done.</summary>
	public bool Fading => Sound?.Fading == true;

	/// <summary>
	/// The sound manager's setup (ShellSound_Init, 0042ec7c): the samples, the music track option 5 picks, and then
	/// option 5 flipped, committed and all 54 options saved, so the next run plays the other track. The
	/// startup then starts the music at volume 0 (Shell_BuildScreensAndStart's ShellSound_Start), and the movie queue
	/// fades it in after the intro — or, with movies off, leaves it there. A run staged on another screen
	/// has no intro, and fades it in here: <paramref name="fadeIn"/>.
	/// </summary>
	public void Start(bool fadeIn) {
		if (_silent) {
			return;
		}

		Backend = OpenAlBackend.TryCreate(out string? failure) as IAudioBackend ?? new NullAudioBackend();
		if (failure != null) {
			Console.Error.WriteLine($"Audio unavailable ({failure}) — the shell runs silent.");
		}

		// The wrapper is not disposed: it owns Backend, which Dispose disposes directly.
		Sound = ShellSound.Load(_content, new SoundCfgBackend(Backend, _soundCfg), _options);
		if (!Sound.HasMusic) {
			Console.Error.WriteLine($"No hmi\\{Sound.MusicName} in {ShellSound.ArchiveName} — the shell has no music.");
		}
		_options.Set(ShellSound.MusicTrackOption, (byte)(_options[ShellSound.MusicTrackOption] ^ 1),
			apply: false);
		_options.Commit();
		_options.Save(Enumerable.Range(0, Prefs.Length).ToArray());

		Sound.Start();
		if (fadeIn) {
			Sound.FadeIn();
		}
	}

	public void Update() => Sound?.Update();

	/// <summary>
	/// WM_SETFOCUS starts the sounds again, unless a movie is playing, and WM_KILLFOCUS stops them
	/// (MainWndProc, 00404a2c). The stop reaches a movie's soundtrack too, the two sharing one backend
	/// here where retail's MCI sound is not the sound manager's.
	/// </summary>
	public void FocusChanged(bool focused, bool moviePlaying) {
		if (focused) {
			if (!moviePlaying) {
				Sound?.Start();
			}
		} else {
			Sound?.Stop();
		}
	}

	public void Dispose() {
		Sound?.Stop();
		Sound = null;
		Backend?.Dispose();
		Backend = null;
	}
}
