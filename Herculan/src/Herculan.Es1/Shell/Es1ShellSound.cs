using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io;
using Herculan.Engine.Audio;
using Herculan.Engine.Settings;
using Herculan.Es1.Settings;
using Herculan.Es1.Sim;
using Herculan.Es1.Sound;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's sound manager (docs/es1/formats/sound-samples.md#the-shells-sound-manager): <c>ShellSound_Manager</c>
/// (<c>ES1GO 00082768</c>), which <see cref="Init"/> makes from <c>resource.cfg</c> at start-up with the 20 names of
/// <see cref="Names"/> and no catalog, and the one sound it makes, the buttons' click <c>ShellSound_ButtonSound</c>
/// (<c>0007fd00</c>), which the menus' buttons (<see cref="Menus.Es1MTButton"/>) and the shell's panels
/// (<see cref="PlayClick"/>) play, and the song of the screen, <c>ShellMusic_Object</c> (<c>0007fcfc</c>)
/// (docs/es1/shell/menus.md#the-backdrop), which <see cref="PlayMusic"/> makes. The manager is
/// <see cref="Es1SoundManager"/>, the simulator's, as the shell's copies hold the simulator's statements.
/// </summary>
public sealed class Es1ShellSound {
	/// <summary>The tracks the screens start themselves, name indexes of <see cref="Names"/>: <c>res32\death.hmp</c>,
	/// <c>res32\intro1.hmp</c>, <c>res32\intro2.hmp</c> and <c>res32\losing.hmp</c>.</summary>
	public const short DeathTrack = 7, Intro1Track = 0xc, Intro2Track = 0xd, LosingTrack = 0x13;

	private readonly Func<short, IEnumerable<Es1ShellWait>> _waitTicks;

	/// <param name="waitTicks"><c>Shell_WaitTicks</c> (<see cref="Es1ShellDevices.WaitTicks"/>), which the song's fade waits through.</param>
	public Es1ShellSound(Func<short, IEnumerable<Es1ShellWait>> waitTicks) {
		_waitTicks = waitTicks;
	}

	/// <summary>
	/// <c>Shell_MusicOption</c> (<see cref="Es1ShellData.MusicOption"/>), which the host wires once the shared data
	/// exists; 0 before, the byte's value in the image.
	/// </summary>
	public Func<byte> MusicOption { get; set; } = () => 0;

	/// <summary><c>ShellMusic_Object</c> (<c>0007fcfc</c>): the song <see cref="PlayMusic"/> made, none until then and after <see cref="StopMusic"/>.</summary>
	public Es1SoundObject? MusicObject { get; private set; }

	/// <summary>The master volume <c>ShellSound_Init</c> sets, where the simulator's stays 90.</summary>
	public const sbyte MasterVolume = 95;

	/// <summary>The memory limit <c>ShellSound_Init</c> sets.</summary>
	public const uint MemoryLimit = 200_000;

	/// <summary>The click's name index, <c>res32\button.sfx</c>.</summary>
	public const short ButtonIndex = 9;

	/// <summary>The row <c>ShellSound_PlayClick</c> plays the click for, the simulator's <see cref="Es1Sound.ClickRow"/>.</summary>
	public const short ClickRow = Es1Sound.ClickRow;

	/// <summary>The line <c>ShellSound_Init</c>'s assert names in <c>src\system.cpp</c>.</summary>
	private const int SoundsetAssertLine = 0x151;

	/// <summary>
	/// <c>ShellSound_Names</c> (<c>000828c0</c>): the 20 names the manager is given, each name's index its position
	/// (docs/es1/formats/sound-samples.md#the-shells-names).
	/// </summary>
	public static readonly string[] Names = [
		@"res32\xxxxxxxx.sfx", @"res32\menu.hmp", @"res32\menu.hmp", @"res32\shella.hmp", @"res32\shellb.hmp",
		@"res32\shellc.hmp", @"res32\shella.hmp", @"res32\death.hmp", @"res32\win2.hmp", @"res32\button.sfx",
		@"res32\menuclsa.raw", @"res32\menuopn2.raw", @"res32\intro1.hmp", @"res32\intro2.hmp", @"res32\xxxxxxxx.raw",
		@"res32\rubble.hmp", @"res32\ice.hmp", @"res32\desert.hmp", @"res32\volcano.hmp", @"res32\losing.hmp",
	];

	/// <summary><c>ShellSound_Manager</c> (<c>00082768</c>): none until <see cref="Init"/> makes one, under <c>-X</c>, and with a digital device 0.</summary>
	public Es1SoundManager? Manager { get; private set; }

	/// <summary><c>ShellSound_ButtonSound</c> (<c>0007fd00</c>): the click, none with no manager.</summary>
	public Es1SoundObject? ButtonSound { get; private set; }

	/// <summary>
	/// <c>ShellSound_Init</c> (<c>00046235</c>): nothing under <paramref name="debugMode"/> (<c>Shell_DebugMode</c>,
	/// <c>-X</c>); a fatal assert when the install's <c>resource.cfg</c> does not open; the six values read
	/// (<see cref="Es1Sound.ReadDevices"/>), and with a digital device 0 no manager. Otherwise the manager over
	/// <paramref name="backend"/> with the six values stored, <see cref="Es1SoundManager.LoadDrivers"/>, whose answer is
	/// not read, <see cref="MasterVolume"/>, <see cref="MemoryLimit"/>, <see cref="Names"/>, and the click made with
	/// <see cref="Es1SoundManager.CreateObject"/>(2, 0, <see cref="ButtonIndex"/>) and loaded. It sets no hearing range.
	/// A click not made is not loaded, an engine guard where retail loads through a null object.
	/// </summary>
	/// <param name="read">The shell's volumes' reader, (folder, file name) to a file's bytes, which the manager reads through.</param>
	/// <param name="timer">The shell's PC timer, which the manager's drivers put the songs' tick on.</param>
	/// <exception cref="Es1FatalAssertException"><c>resource.cfg</c> does not open.</exception>
	public void Init(string installRoot, bool debugMode, Func<string, string, byte[]?> read, IAudioBackend backend,
			Es1SimClock timer) {
		if (debugMode) {
			return;
		}

		if (!File.Exists(CaseInsensitivePath.Combine(installRoot, Es1ResourceCfg.FileName))) {
			throw new Es1FatalAssertException(
				$"ERROR: Run soundset first. ({Es1ResourceCfg.FileName} does not open in {installRoot}; src\\system.cpp line 0x{SoundsetAssertLine:x} asserts here.)");
		}

		var devices = Es1Sound.ReadDevices(installRoot);
		if (devices.DigitalDevice == 0) {
			return;
		}

		var manager = new Es1SoundManager(read, new Es1DigitalDriver(backend), timer: timer) { Devices = devices };
		Manager = manager;
		manager.LoadDrivers();
		manager.SetMasterVolume(MasterVolume);
		manager.SetMemoryLimit(MemoryLimit);
		manager.SetSampleNames(Names);
		ButtonSound = manager.CreateObject(2, 0, ButtonIndex);
		ButtonSound?.LoadSample();
	}

	/// <summary>
	/// A menu button's click, <c>MTButton_Draw</c>'s in state 1 (docs/es1/shell/menu-controls.md#drawing-a-control): while
	/// <paramref name="soundOption"/> (<c>Shell_SoundOption</c>) and <see cref="ButtonSound"/> are set, the click stopped
	/// when its slot <c>+0x30</c> answers that it plays (slot <c>+0x14</c>), and played (slot <c>+0x10</c>).
	/// </summary>
	public void ClickButton(byte soundOption) {
		if (soundOption == 0 || ButtonSound is not { } click) {
			return;
		}

		if (click.IsPlaying()) {
			click.Stop();
		}

		click.Play();
	}

	/// <summary>
	/// <c>ShellSound_PlayClick</c> (<c>0004dde4</c>): for <see cref="ClickRow"/>, with
	/// <see cref="ButtonSound"/> set, the click played through its slot <c>+0x10</c>, with no stop first; nothing for any
	/// other row.
	/// </summary>
	/// <param name="row">The row of the click.</param>
	public void PlayClick(short row) {
		if (row == ClickRow) {
			ButtonSound?.Play();
		}
	}

	/// <summary>
	/// <c>ShellMusic_Play</c> (<c>00020a70</c>): nothing with no manager; otherwise
	/// <see cref="StopMusic"/>, then <see cref="MusicObject"/> made with <see cref="Es1SoundManager.CreateObject"/>(2, 0,
	/// track), loaded, its slot <c>+0x28</c> called with (−1, −1, <c>0xffff</c>), the endless repeat, and played through
	/// slot <c>+0x10</c> while <see cref="MusicOption"/> is set. The object is made and loaded with the option off, so
	/// that the MUSIC box (<see cref="SetMusicPlaying"/>) has one to play. An object not made is not loaded or played, an
	/// engine guard where retail calls through a null object.
	/// <para>With <see cref="SkipPause"/> nothing is waited for: <see cref="PlayMusicAfterFade"/>.</para>
	/// </summary>
	/// <param name="track">The track number.</param>
	public IEnumerable<Es1ShellWait> PlayMusic(short track) {
		if (Manager is not { } manager) {
			yield break;
		}

		if (SkipPause) {
			PlayMusicAfterFade(track);
			yield break;
		}

		foreach (var wait in StopMusic()) {
			yield return wait;
		}

		CreateMusic(manager, track);
	}

	/// <summary><see cref="PlayMusic"/>'s steps after <see cref="StopMusic"/>: <see cref="MusicObject"/> made, loaded, repeated and played.</summary>
	private void CreateMusic(Es1SoundManager manager, short track) {
		MusicObject = manager.CreateObject(2, 0, track);
		if (MusicObject is not { } music) {
			return;
		}

		music.LoadSample();
		music.SetRepeats(-1, -1, -1);
		if (MusicOption() != 0) {
			music.Play();
		}
	}

	/// <summary>
	/// The "Skip pause between screens" tweak, <see cref="Es1TweakSettingDefinitions.SkipScreenPause"/>: the song's
	/// fades (<see cref="FadeOut"/>, and <see cref="PlayMusic"/>'s through <see cref="StopMusic"/>) run in the background
	/// at retail's rate, one volume step a <c>Shell_WaitTick</c>, rather than holding the shell until they end. Retail
	/// shows a screen whose song changed only once the last song has faded; with the tweak it shows at once. The new song
	/// still starts once the last has faded and been destroyed, as in retail. This engine's invention.
	/// </summary>
	public static bool SkipPause => TweakSettings.Current.GetSettingValue(Es1TweakSettingDefinitions.SkipScreenPause);

	// The background fade SkipPause runs: the song fading, whether it is stopped and destroyed at the end, and the volume
	// the next tick sets.
	private Es1SoundObject? _fading;
	private bool _fadingStops;
	private sbyte _nextVolume;

	// The track PlayMusic left to start once the song's background fade ends.
	private short? _pendingTrack;

	/// <summary>
	/// <see cref="PlayMusic"/> under <see cref="SkipPause"/>: <paramref name="track"/> left to start once no song fades;
	/// <see cref="MusicObject"/>, when set, faded in the background to be stopped and destroyed, and cleared. With no song
	/// to fade and none fading the track starts now. A track asked for while the last still fades replaces any left
	/// waiting.
	/// </summary>
	private void PlayMusicAfterFade(short track) {
		_pendingTrack = track;
		if (MusicObject is { } music) {
			BeginBackgroundFade(music, stopAfter: true);
			MusicObject = null;
		} else if (_fading == null) {
			StartPendingTrack();
		}
	}

	/// <summary>
	/// A background fade of <paramref name="sound"/> begun, its first step, the volume it has, set now as
	/// <see cref="FadeOut"/>'s first is. A fade of another song is ended first (<see cref="EndBackgroundFade"/>); a fade of
	/// this one goes on, now to stop it when <paramref name="stopAfter"/> is set. A song at volume 0 or less ends its fade
	/// at once, as retail's loop sets nothing for it.
	/// </summary>
	private void BeginBackgroundFade(Es1SoundObject sound, bool stopAfter) {
		if (_fading == sound) {
			_fadingStops |= stopAfter;
			return;
		}

		EndBackgroundFade();
		_fading = sound;
		_fadingStops = stopAfter;
		sbyte volume = unchecked((sbyte)sound.GetVolume());
		if (volume <= 0) {
			_nextVolume = 0;
			FinishBackgroundFade();
			return;
		}

		sound.SetVolume(volume);
		_nextVolume = (sbyte)(volume - 1);
	}

	/// <summary>
	/// One tick of the background fade, which <c>Shell_WaitTick</c> runs: the next volume set while it is above 0, so that
	/// the fade takes as many ticks as <see cref="FadeOut"/>'s waits; after the last, <see cref="FinishBackgroundFade"/>.
	/// Nothing with no fade.
	/// </summary>
	public void StepBackgroundFade() {
		if (_fading is not { } sound) {
			return;
		}

		if (_nextVolume > 0) {
			sound.SetVolume(_nextVolume);
			_nextVolume--;
			return;
		}

		FinishBackgroundFade();
	}

	/// <summary>The background fade ended (<see cref="EndBackgroundFade"/>) and the track left waiting started (<see cref="StartPendingTrack"/>).</summary>
	private void FinishBackgroundFade() {
		EndBackgroundFade();
		StartPendingTrack();
	}

	/// <summary>
	/// The background fade ended where it stands: its song stopped and destroyed when it was to be, otherwise, when cut
	/// short, set to volume 1, where <see cref="FadeOut"/> leaves it. No track starts.
	/// </summary>
	private void EndBackgroundFade() {
		if (_fading is not { } sound) {
			return;
		}

		_fading = null;
		if (_fadingStops) {
			sound.Stop();
			sound.Destroy();
		} else if (_nextVolume > 0) {
			sound.SetVolume(1);
		}
	}

	/// <summary>The track <see cref="PlayMusicAfterFade"/> left, made and played as <see cref="PlayMusic"/> makes it; nothing with none or no manager.</summary>
	private void StartPendingTrack() {
		if (_pendingTrack is not { } track || Manager is not { } manager) {
			return;
		}

		_pendingTrack = null;
		CreateMusic(manager, track);
	}

	/// <summary>
	/// <c>ShellMusic_Stop</c> (<c>00020a18</c>): with <see cref="MusicObject"/> set, <see cref="FadeOut"/> of it, its slot
	/// <c>+0x14</c> (stop), its slot <c>+8</c> with 3 (destroyed), and the object cleared. The fade runs whether or not the
	/// song plays.
	/// <para>With <see cref="SkipPause"/> the song, and any still fading, is stopped and destroyed at once with no fade,
	/// and no song waiting for a fade's end starts: the shell is closing.</para>
	/// </summary>
	public IEnumerable<Es1ShellWait> StopMusic() {
		if (SkipPause) {
			EndBackgroundFade();
			_pendingTrack = null;
			MusicObject?.Stop();
			MusicObject?.Destroy();
			MusicObject = null;
			yield break;
		}

		if (MusicObject is not { } music) {
			yield break;
		}

		foreach (var wait in FadeOut(music)) {
			yield return wait;
		}

		music.Stop();
		music.Destroy();
		MusicObject = null;
	}

	/// <summary>
	/// <c>ShellMusic_FadeOut</c> (<c>000209e0</c>): for each value from the low byte of
	/// <c>SoundObject_GetVolume</c>, signed, down to 1, the object's slot <c>+0x2c</c> with the value, then
	/// <c>Shell_WaitTicks</c>(1): a volume of 100 fades over 100 ticks.
	/// <para>With <see cref="SkipPause"/> the same steps run in the background (<see cref="StepBackgroundFade"/>) and
	/// nothing is waited for.</para>
	/// </summary>
	/// <param name="sound">The sound object faded.</param>
	public IEnumerable<Es1ShellWait> FadeOut(Es1SoundObject sound) {
		if (SkipPause) {
			BeginBackgroundFade(sound, stopAfter: false);
			yield break;
		}

		for (sbyte volume = unchecked((sbyte)sound.GetVolume()); volume > 0; volume--) {
			sound.SetVolume(volume);
			foreach (var wait in _waitTicks(1)) {
				yield return wait;
			}
		}
	}

	/// <summary>
	/// The MUSIC box's and CANCEL's step in <c>PrefsMenu_OnMessage</c> (docs/es1/shell/options.md#the-buttons--prefsmenu_onmessage-0002b82b):
	/// with <see cref="MusicObject"/> set, its slot <c>+0x10</c> (play) for a <paramref name="setting"/> of 1 and
	/// <c>+0x14</c> (stop) for 0.
	/// </summary>
	public void SetMusicPlaying(byte setting) {
		if (MusicObject is not { } music) {
			return;
		}

		if (setting == 0) {
			music.Stop();
		} else {
			music.Play();
		}
	}

	/// <summary>
	/// The digital driver's service of its voices (<see cref="Es1DigitalDriver.Update"/>), which retail's runs from the
	/// timer: called once a host frame, an engine choice, as the simulator's (<see cref="Es1Sound.UpdateDriver"/>).
	/// Nothing with no manager.
	/// </summary>
	public void UpdateDriver() => Manager?.Driver.Update();

	/// <summary>
	/// <c>Shell_ShutdownDevices</c>' first steps (docs/es1/shell/startup.md#the-shutdown): <see cref="ButtonSound"/>
	/// destroyed through its slot <c>+8</c> with 3 and cleared, then the manager destroyed
	/// (<see cref="Es1SoundManager.Destroy"/>) and cleared. <c>Shell_AssertCleanupHook</c> destroys the manager alone.
	/// Retail's <c>Shell_FreeData</c> has stopped the song by then (<see cref="StopMusic"/>, which the host runs as the
	/// frame loop ends); a song left by a window closed before that is stopped and destroyed first with no fade, an
	/// engine guard.
	/// </summary>
	public void Shutdown() {
		_pendingTrack = null;
		EndBackgroundFade();
		MusicObject?.Stop();
		MusicObject?.Destroy();
		MusicObject = null;
		ButtonSound?.Destroy();
		ButtonSound = null;
		DestroyManager();
	}

	/// <summary><c>SoundManager_Dtor</c>(manager, 3) with a manager, and the manager cleared.</summary>
	public void DestroyManager() {
		Manager?.Destroy();
		Manager = null;
	}
}
