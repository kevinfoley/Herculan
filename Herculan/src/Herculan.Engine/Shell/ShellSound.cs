using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using HercWorks.Core.Data.File.Cfg;

namespace Herculan.Engine.Shell;

/// <summary>
/// VSHELL's sound: the shell music, the two click sounds and the startup switch, out of <c>SHLSOUND.VOL</c>, with the
/// gates, the fade and the focus stop the original puts round them
/// (docs/retail/shell/movies-and-sound.md#sound).
///
/// <para>The original's fades are loops that hold the shell until they finish. This one steps once per
/// <see cref="Update"/> under the same rule, so the window keeps drawing, and <see cref="Fading"/> is
/// what the host holds the widgets on meanwhile.</para>
/// </summary>
public sealed class ShellSound {
	/// <summary>The archive the shell's sounds are in.</summary>
	public const string ArchiveName = "SHLSOUND.VOL";

	/// <summary><c>prefs.cfg</c> option 5, which of the two music tracks the next startup loads.</summary>
	public const int MusicTrackOption = 5;

	private const string Folder = "hmi";

	/// <summary>The volume the fade in stops at, and the one a click is played at.</summary>
	private const int FullVolume = 100;

	/// <summary>The fade out stops once the volume is below this.</summary>
	private const int MinimumFadeVolume = 2;

	/// <summary>
	/// <c>GetTickCount</c> milliseconds that must pass between two steps of a fade — strictly more than
	/// this.
	/// </summary>
	private const long FadeStepMilliseconds = 10;

	private readonly IAudioBackend _backend;
	private readonly SimulatorPreferences _options;
	private readonly int _press;
	private readonly int _tabClick;
	private readonly int _switch;
	private readonly int _music;
	private int _musicPlay = -1;

	// The clicks' playbacks, which Stop ends along with the music.
	private readonly List<int> _clickPlays = new();

	// ShellSound_MusicVolume (004731fc), the music's volume: 0 in the image, and moved only by the fades.
	private int _musicVolume;
	private bool _fadingIn;
	private bool _fadingOut;
	private long _lastFadeStep;

	private ShellSound(IAudioBackend backend, SimulatorPreferences options, int press, int tabClick, int @switch,
			int music) {
		_backend = backend;
		_options = options;
		_press = press;
		_tabClick = tabClick;
		_switch = @switch;
		_music = music;
	}

	/// <summary>The music track's file name, which <see cref="MusicTrackOption"/> picks.</summary>
	public string MusicName { get; private init; } = string.Empty;

	/// <summary>
	/// Whether the music is loaded — <c>ShellSound_MusicSample</c> (<c>0048cdd0</c>) not being 0, which
	/// the original answers with <c>Cannot load sound.</c> and carries on.
	/// </summary>
	public bool HasMusic => _music >= 0;

	/// <summary>
	/// <c>ShellSound_Running</c> (<c>00473200</c>): the sounds are running. <see cref="Start"/> raises it
	/// and <see cref="Stop"/> drops it, and no click plays while it is down.
	/// </summary>
	public bool Active { get; private set; }

	/// <summary>
	/// Whether a fade is under way — the span the original spends inside its fade loop, where no widget
	/// takes an event.
	/// </summary>
	public bool Fading => _fadingIn || _fadingOut;

	/// <summary>
	/// The sound manager's setup, <c>ShellSound_Init</c> (<c>0042ec7c</c>): <c>hmi\gm_69.wav</c> the
	/// press sound, <c>hmi\bptlt2.wav</c> the tab click, <c>hmi\lswitch2.wav</c> the startup switch, and
	/// <c>hmi\shell1.wav</c> while option 5 is non-zero or
	/// <c>hmi\shell2.wav</c> while it is 0 for the music, looping forever at volume 0. A missing sample
	/// plays nothing. Flipping option 5 afterwards is the caller's, as it is the setup's in the original.
	/// </summary>
	public static ShellSound Load(GameContent content, IAudioBackend backend, SimulatorPreferences options) {
		ArgumentNullException.ThrowIfNull(content);
		ArgumentNullException.ThrowIfNull(backend);
		ArgumentNullException.ThrowIfNull(options);

		string musicName = options[MusicTrackOption] != 0 ? "shell1.wav" : "shell2.wav";
		return new ShellSound(backend, options, Sample("gm_69.wav"), Sample("bptlt2.wav"),
			Sample("lswitch2.wav"), Sample(musicName)) {
			MusicName = musicName,
		};

		int Sample(string name) =>
			content.Read(Folder, name) is { } bytes && WaveSample.Decode(bytes) is { } sample
				? backend.CreateSample(sample)
				: -1;
	}

	/// <summary>
	/// <c>ShellSound_Start</c> (<c>0042ef5b</c>), run at startup and when the window takes the focus:
	/// unless the sounds are already running, raises <see cref="Active"/> and starts the music from its
	/// top at whatever volume
	/// it was left on.
	///
	/// <para>The original also plays the press sound at volume 0 here, which is inaudible and is not
	/// reproduced.</para>
	/// </summary>
	public void Start() {
		if (Active) {
			return;
		}

		Active = true;
		_musicPlay = _backend.Start(_music, Gain(_musicVolume), 0f, 1f, looping: true);
	}

	/// <summary>
	/// <c>ShellSound_Stop</c> (<c>0042f030</c>), run when the window loses the focus: stops every sound and drops
	/// <see cref="Active"/>. The music's volume is left where it was, and a fade under way goes on
	/// stepping it.
	///
	/// <para>Every sound is this class's own playbacks, not the device's: the movies play on the same device here,
	/// where retail's soundtrack is outside the sound manager (<see cref="SoundCfgBackend"/>), so it plays on through this.</para>
	/// </summary>
	public void Stop() {
		if (!Active) {
			return;
		}

		_backend.Stop(_musicPlay);
		foreach (int play in _clickPlays) {
			_backend.Stop(play);
		}
		_clickPlays.Clear();
		_musicPlay = -1;
		Active = false;
	}

	/// <summary>
	/// <c>ShellSound_FadeIn</c> (<c>0042f21c</c>): while MUSIC (option 0) is on, raises the music's
	/// volume one step at a time to 100. With MUSIC off it does nothing, so the music goes on at the volume it has, which from
	/// startup is 0.
	/// </summary>
	public void FadeIn() {
		if (_options[Prefs.MusicOption] == 0) {
			return;
		}

		_fadingIn = true;
		_fadingOut = false;
		_lastFadeStep = Environment.TickCount64;
	}

	/// <summary>
	/// <c>ShellSound_FadeOut</c> (<c>0042f178</c>): while MUSIC is on, lowers the music's volume one step
	/// at a time until it is below 2, so it stops at 1. MUSIC is tested once, as the fade starts, so a
	/// caller that turns it off straight after still gets the whole fade — which is what the original's
	/// callers do once its blocking loop has returned.
	/// </summary>
	public void FadeOut() {
		if (_options[Prefs.MusicOption] == 0) {
			return;
		}

		_fadingOut = true;
		_fadingIn = false;
		_lastFadeStep = Environment.TickCount64;
	}

	/// <summary>
	/// One pass of the fade's loop: a step whenever more than 10 ms of <c>GetTickCount</c> have passed
	/// since the last — up until the volume is past 99, or down until it is below 2.
	/// <see cref="Environment.TickCount64"/> is that same clock, at its same granularity.
	///
	/// <para>It services the backend's device first (<see cref="IAudioBackend.Update"/>), which is this
	/// engine's and has no counterpart in the original's loop.</para>
	/// </summary>
	public void Update() {
		_backend.Update();

		if (!Fading) {
			return;
		}

		if (_fadingIn ? _musicVolume > FullVolume - 1 : _musicVolume < MinimumFadeVolume) {
			_fadingIn = false;
			_fadingOut = false;
			return;
		}

		long now = Environment.TickCount64;
		if (now - _lastFadeStep <= FadeStepMilliseconds) {
			return;
		}

		_lastFadeStep = now;
		_musicVolume += _fadingIn ? 1 : -1;
		if (_musicPlay >= 0) {
			_backend.SetGain(_musicPlay, Gain(_musicVolume));
		}
	}

	/// <summary>
	/// <c>ShellSound_PlayPress</c> (<c>0042eecf</c>), the press sound a button makes as it goes down, at
	/// volume 100 while the sounds are running and SOUNDS (option 1) is on.
	/// </summary>
	public void PlayPress() => Play(_press);

	/// <summary>
	/// <c>ShellSound_PlayTabClick</c> (<c>0042ee89</c>), the click at the end of every tab switch, gated
	/// as <see cref="PlayPress"/> is.
	/// </summary>
	public void PlayTabClick() => Play(_tabClick);

	/// <summary>
	/// <c>ShellSound_PlaySwitch</c> (<c>0042ef15</c>), the switch the startup sequence opens with, gated as
	/// <see cref="PlayPress"/> is.
	/// </summary>
	public void PlaySwitch() => Play(_switch);

	private void Play(int sample) {
		if (Active && _options[Prefs.SoundsOption] != 0) {
			_clickPlays.RemoveAll(click => !_backend.IsPlaying(click));
			int play = _backend.Start(sample, Gain(FullVolume), 0f, 1f, looping: false);
			if (play >= 0) {
				_clickPlays.Add(play);
			}
		}
	}

	/// <summary>
	/// The SOS volume, 0-100, as a gain. The driver is handed <c>volume * master * 0x7fff / 10000</c>
	/// with the master <c>Sos_MasterVolume</c> (<c>00473160</c>) on the 100 it holds in the image, which is linear in the volume.
	/// </summary>
	private static float Gain(int volume) => Math.Clamp(volume / (float)FullVolume, 0f, 1f);
}
