using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io;
using Herculan.Engine.Audio;
using Herculan.Es1.Sim;
using Herculan.Es1.Sound;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's sound manager (docs/es1/formats/sound-samples.md#the-shells-sound-manager): <c>ShellSound_Manager</c>
/// (<c>ES1GO 00082768</c>), which <see cref="Init"/> makes from <c>resource.cfg</c> at start-up with the 20 names of
/// <see cref="Names"/> and no catalog, and the one sound it makes, the buttons' click <c>ShellSound_ButtonSound</c>
/// (<c>0007fd00</c>), which the menus' buttons (<see cref="Menus.Es1MTButton"/>) and the shell's panels
/// (<see cref="PlayClick"/>) play. The manager is <see cref="Es1SoundManager"/>, the simulator's, as the shell's copies
/// hold the simulator's statements.
/// </summary>
public sealed class Es1ShellSound {
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
	/// <exception cref="Es1FatalAssertException"><c>resource.cfg</c> does not open.</exception>
	public void Init(string installRoot, bool debugMode, Func<string, string, byte[]?> read, IAudioBackend backend) {
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

		var manager = new Es1SoundManager(read, new Es1DigitalDriver(backend)) { Devices = devices };
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
	/// <c>ShellSound_PlayClick</c> (<c>0004dde4</c>)(<paramref name="row"/>): for <see cref="ClickRow"/>, with
	/// <see cref="ButtonSound"/> set, the click played through its slot <c>+0x10</c>, with no stop first; nothing for any
	/// other row.
	/// </summary>
	public void PlayClick(short row) {
		if (row == ClickRow) {
			ButtonSound?.Play();
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
	/// </summary>
	public void Shutdown() {
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
