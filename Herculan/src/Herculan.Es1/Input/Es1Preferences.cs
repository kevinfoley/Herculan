using HercWorks.Core.Io;

namespace Herculan.Es1.Input;

/// <summary>
/// The simulator's 17 preference options (docs/es1/simulation/preferences.md#the-option-array): <c>Prefs_Options</c>
/// (<c>ES1DBSIM 000bd562</c>) as <c>Prefs_Load</c> (<c>0004a744</c>) reads them from <c>data\prefs.cfg</c>, all 0 when
/// the file does not open, with its two copies, <c>Prefs_LoadedOptions</c> (<c>000bd574</c>) and
/// <c>Prefs_PreviousOptions</c> (<c>000bd586</c>); the handler table <c>Prefs_HandlerTable</c> (<c>000bd598</c>); the
/// writes the panels make through <c>Prefs_SetOption</c>; and the save back to the file. The shell keeps the same 17
/// options with <c>ES1GO</c>'s own <c>Prefs_*</c> functions, the <c>Shell</c>-prefixed methods where they differ from the
/// simulator's, its <c>Prefs_Committed</c> held as the load-time copy. The options the input build
/// reads are named here; <see cref="Es1ControlsState"/> registers the handlers of options 8 and 10,
/// <see cref="Es1InputBuild"/> that of option 11, and this type those of options 0, 1, 2 and 6
/// (docs/es1/simulation/input.md#the-preferences-start-up).
/// </summary>
public sealed class Es1Preferences {
	/// <summary>The number of option bytes <c>Prefs_Load</c> reads.</summary>
	public const int OptionCount = 17;

	/// <summary>Options 0 to 2 (<c>000bd562</c>–<c>000bd564</c>): MUSIC and SOUNDS, 0 off and 1 on, and RADIO, 0 to 2.</summary>
	public const int MusicOption = 0, SoundsOption = 1, RadioOption = 2;

	/// <summary>Option 3 (<c>000bd565</c>): HORIZON, 0 off and 1 on.</summary>
	public const int HorizonOption = 3;

	/// <summary>Options 4 to 6 (<c>000bd566</c>–<c>000bd568</c>): SKY, GROUND and SHAPES, 0 to 255; option 6 is the detail setting.</summary>
	public const int SkyOption = 4, GroundOption = 5, DetailOption = 6;

	/// <summary>Option 7 (<c>000bd569</c>): which of the six joystick configurations the input build uses.</summary>
	public const int StickConfigurationOption = 7;

	/// <summary>Option 8 (<c>000bd56a</c>): the stick mode, and whether <c>Input_ToggleStickMode</c> may change it.</summary>
	public const int StickModeOption = 8;

	/// <summary>Option 9 (<c>000bd56b</c>): the stick's third axis drives an axis in configurations 0, 1, 3, 4 and 5.</summary>
	public const int ThirdAxisOption = 9;

	/// <summary>Option 10 (<c>000bd56c</c>): <c>Ctrl+I</c> and <c>Ctrl+M</c> step the throttle and the stick's Y no longer drives it.</summary>
	public const int ThrottleKeysOption = 10;

	/// <summary>Option 11 (<c>000bd56d</c>): the mouse driver on, 1, or off, 0.</summary>
	public const int MouseOption = 11;

	/// <summary>The most option 6 may hold in the low-memory mode, which <c>Prefs_LimitDetailInLowMemory</c> enforces.</summary>
	public const byte LowMemoryDetailLimit = 160;

	/// <summary>The voice archive <c>Voice_ArchiveExists</c> (<c>0004aed8</c>) opens, a fixed path from the current directory.</summary>
	public static readonly string[] VoiceArchivePath = ["vol", "simvoice.vol"];

	private readonly byte[] _options;
	private readonly byte[] _loaded;
	private readonly byte[] _previous;
	private readonly Action<byte>?[] _handlers = new Action<byte>?[OptionCount];

	/// <summary>
	/// <c>Prefs_Load</c>(1)'s copies: the options as read, and both copies made from them. The handlers this type owns are
	/// registered, as <c>Prefs_Init</c> (<c>0004afe8</c>) registers them before the load.
	/// </summary>
	private Es1Preferences(byte[] options, string? path) {
		_options = options;
		_loaded = (byte[])options.Clone();
		_previous = (byte[])options.Clone();
		FilePath = path;
		SetHandler(MusicOption, ApplyMusicOption);
		SetHandler(SoundsOption, ApplySoundsOption);
		SetHandler(RadioOption, ApplyRadioOption);
		SetHandler(DetailOption, LimitDetailInLowMemory);
	}

	/// <summary>The options of a <c>data\prefs.cfg</c> that does not open: all 0, with no file to save to.</summary>
	public static Es1Preferences Default => new(new byte[OptionCount], null);

	/// <summary>
	/// The options from the bytes of <c>data\prefs.cfg</c>, or all 0 for null, a file that does not open: the read of
	/// <c>Prefs_Load</c> and its two copies. A file shorter than 17 bytes gives 0 for the bytes it lacks. No handler runs.
	/// </summary>
	/// <param name="path">Where <see cref="SaveSelectedOptions"/> and <see cref="ShellSave"/> write, <c>data\prefs.cfg</c> in the install; none saves nothing.</param>
	public static Es1Preferences FromFile(byte[]? file, string? path = null) {
		var options = new byte[OptionCount];

		// PLACEHOLDER: what FileRStream_Read leaves in the bytes past a short file's end is unread, so they read 0 here;
		// no plan step retires it.
		if (file != null) {
			Array.Copy(file, options, Math.Min(file.Length, OptionCount));
		}

		return new Es1Preferences(options, path);
	}

	/// <summary>
	/// <c>Prefs_Init</c>'s steps on the options from <paramref name="file"/>, as far as this type keeps them
	/// (docs/es1/simulation/input.md#the-preferences-start-up): <c>PrefsInitInProgress</c> set; <c>Prefs_Load</c>(1),
	/// which runs each registered handler with its option; <c>VoiceArchivePresent</c> from
	/// <paramref name="voiceArchivePresent"/>, and option 2 set to 0 (<c>Prefs_SetOption</c>(1, 0, 2)) when it is clear;
	/// <c>PrefsInitInProgress</c> cleared. The handlers of options 8 and 10 run when <see cref="Es1ControlsState"/>
	/// registers them, and the joystick devices are built by the input build (<see cref="Es1InputBuild"/>). Under
	/// <paramref name="demoMode"/> the load runs twice, <c>Prefs_Load</c>(1) and then <c>Prefs_Load</c>(0), and
	/// <c>Prefs_RevertSelectedOptions</c> (<c>0004aaf0</c>) puts options 0–6, the list at <c>0009570c</c>, back to their
	/// loaded values. Both loads read <c>data\prefs.cfg</c>, whatever a tape plays, so the second reads the bytes the first
	/// did.
	/// </summary>
	/// <param name="path"><c>data\prefs.cfg</c> in the install, which <see cref="SaveSelectedOptions"/> writes.</param>
	/// <param name="voiceArchivePresent">What <see cref="VoiceArchiveExists"/> answers for the install.</param>
	/// <param name="demoMode"><c>DemoMode</c> (<c>000b5bd0</c>), which <c>-D</c> sets.</param>
	public static Es1Preferences Load(byte[]? file, string path, bool voiceArchivePresent, bool demoMode = false) {
		var preferences = FromFile(file, path);
		preferences.InitInProgress = true;
		preferences.RunHandlers();
		if (demoMode) {
			var reloaded = FromFile(file);
			Array.Copy(reloaded._options, preferences._options, OptionCount);
			preferences.RunHandlers();
			Array.Copy(preferences._loaded, preferences._previous, OptionCount);
			foreach (int index in RevertedInDemo) {
				preferences._options[index] = preferences._loaded[index];
			}
		}

		preferences.VoiceArchivePresent = voiceArchivePresent;
		if (!voiceArchivePresent) {
			preferences.SetOption(true, 0, RadioOption);
		}

		preferences.InitInProgress = false;
		return preferences;
	}

	/// <summary>The seven options <c>Prefs_Init</c> reverts under <c>DemoMode</c>, the <c>int16</c> list at <c>0009570c</c>.</summary>
	private static readonly int[] RevertedInDemo = [0, 1, 2, 3, 4, 5, 6];

	/// <summary><c>Prefs_Load</c>'s pass after the read: each option's registered handler called with it.</summary>
	private void RunHandlers() {
		for (int index = 0; index < OptionCount; index++) {
			_handlers[index]?.Invoke(_options[index]);
		}
	}

	/// <summary>
	/// <c>Voice_ArchiveExists</c> (<c>0004aed8</c>): whether <c>vol\simvoice.vol</c> opens, from the directory the
	/// simulator runs in, <paramref name="installRoot"/>.
	/// </summary>
	public static bool VoiceArchiveExists(string installRoot) =>
		File.Exists(CaseInsensitivePath.Combine(installRoot, VoiceArchivePath));

	/// <summary><c>data\prefs.cfg</c> in the install, which <see cref="SaveSelectedOptions"/> reads and writes and <see cref="ShellSave"/> writes; none saves nothing.</summary>
	public string? FilePath { get; }

	/// <summary>
	/// <c>VoiceArchivePresent</c> (<c>000956bf</c>), which <c>Prefs_Init</c> sets from <c>Voice_ArchiveExists</c> and
	/// the preferences panel's RADIO reads. Clear for options not loaded through <see cref="Load"/>.
	/// </summary>
	public bool VoiceArchivePresent { get; internal set; }

	/// <summary><c>PrefsInitInProgress</c> (<c>000b1734</c>): set while <see cref="Load"/> runs.</summary>
	public bool InitInProgress { get; private set; }

	/// <summary>
	/// <c>g_LowMemory</c> (<c>000bda98</c>), which retail sets under <c>-l</c> or when its heap is under 3,950,000 bytes
	/// (docs/es1/formats/chassis-dat.md#low-memory). This engine takes no <c>-l</c> and its memory is never that short,
	/// so it is clear.
	/// </summary>
	public bool LowMemory => false;

	/// <summary>Option <paramref name="index"/>, 0–16.</summary>
	public byte this[int index] => _options[index];

	/// <summary><c>Prefs_LoadedOptions</c>[<paramref name="index"/>]: the option as loaded or last committed.</summary>
	public byte Loaded(int index) => _loaded[index];

	/// <summary><c>Prefs_PreviousOptions</c>[<paramref name="index"/>]: the option before its last write.</summary>
	public byte Previous(int index) => _previous[index];

	/// <summary>Option 7, <see cref="StickConfigurationOption"/>.</summary>
	public byte StickConfiguration => _options[StickConfigurationOption];

	/// <summary>Option 8, <see cref="StickModeOption"/>.</summary>
	public byte StickMode => _options[StickModeOption];

	/// <summary>Option 9 on, <see cref="ThirdAxisOption"/>.</summary>
	public bool ThirdAxis => _options[ThirdAxisOption] != 0;

	/// <summary>Option 10 on, <see cref="ThrottleKeysOption"/>.</summary>
	public bool ThrottleKeys => _options[ThrottleKeysOption] != 0;

	/// <summary>
	/// <c>Prefs_SetOption</c> (<c>0004a914</c>)(1, 0, 9), <c>Input_CreateJoystickDevices</c>'s clear of option 9 under
	/// option 7 = 2, in memory only (docs/es1/simulation/joystick.md#building-the-devices). No handler is registered for
	/// option 9, so none runs.
	/// </summary>
	public void ClearThirdAxis() => SetOption(true, 0, ThirdAxisOption);

	/// <summary><c>Prefs_SetHandler</c> (<c>0004ac4c</c>)(<paramref name="handler"/>, <paramref name="index"/>): the option's handler, none for null.</summary>
	public void SetHandler(int index, Action<byte>? handler) => _handlers[index] = handler;

	/// <summary>
	/// <c>Prefs_SetOption</c> (<c>0004a914</c>)(<paramref name="apply"/>, <paramref name="value"/>,
	/// <paramref name="index"/>): the old value kept in <c>Prefs_PreviousOptions</c>, the new one stored, and with
	/// <paramref name="apply"/> the option's handler, when it has one, called with it.
	/// </summary>
	public void SetOption(bool apply, byte value, int index) {
		_previous[index] = _options[index];
		_options[index] = value;
		if (apply) {
			_handlers[index]?.Invoke(value);
		}
	}

	/// <summary><c>Prefs_ToggleOption</c> (<c>0004a94c</c>)(<paramref name="apply"/>, <paramref name="index"/>): the value xor 1, through <see cref="SetOption"/>; returns it.</summary>
	public byte ToggleOption(bool apply, int index) {
		_previous[index] = _options[index];
		byte value = (byte)(_options[index] ^ 1);
		SetOption(apply, value, index);
		return value;
	}

	/// <summary>
	/// <c>Prefs_StepOption</c> (<c>0004a988</c>)(<paramref name="apply"/>, <paramref name="modulus"/>,
	/// <paramref name="index"/>): one more, in a byte, and 0 when that equals the modulus; through
	/// <see cref="SetOption"/>. Returns it.
	/// </summary>
	public byte StepOption(bool apply, int modulus, int index) {
		_previous[index] = _options[index];
		byte value = (byte)(_options[index] + 1);
		if (value == modulus) {
			value = 0;
		}

		SetOption(apply, value, index);
		return value;
	}

	/// <summary>
	/// <c>Prefs_StepOptionBack</c> (<c>0004a9d0</c>)(<paramref name="apply"/>, <paramref name="modulus"/>,
	/// <paramref name="index"/>): one less, in a byte, and the modulus less 1 when that gives <c>0xff</c>; through
	/// <see cref="SetOption"/>. Returns it.
	/// </summary>
	public byte StepOptionBack(bool apply, int modulus, int index) {
		_previous[index] = _options[index];
		byte value = (byte)(_options[index] - 1);
		if (value == 0xff) {
			value = (byte)(modulus - 1);
		}

		SetOption(apply, value, index);
		return value;
	}

	/// <summary>
	/// <c>Prefs_CommitOptions</c> (<c>0004a83c</c>)(<paramref name="apply"/>): for each option that differs from its
	/// load-time copy, its handler called with it when <paramref name="apply"/> is set and it has one, then the copy made
	/// current; every option copied to <c>Prefs_PreviousOptions</c> (docs/es1/simulation/preferences.md#the-option-array).
	/// </summary>
	public void CommitOptions(bool apply) {
		for (int index = 0; index < OptionCount; index++) {
			if (_loaded[index] != _options[index]) {
				if (apply) {
					_handlers[index]?.Invoke(_options[index]);
				}

				_loaded[index] = _options[index];
			}

			_previous[index] = _options[index];
		}
	}

	/// <summary>
	/// <c>Prefs_IncreaseOption</c> (<c>0004aa18</c>)(<paramref name="apply"/>, <paramref name="amount"/>,
	/// <paramref name="index"/>): 0 from 255, 255 when the amount would pass it, the value plus the amount otherwise;
	/// through <see cref="SetOption"/>. Returns it.
	/// </summary>
	public byte IncreaseOption(bool apply, byte amount, int index) {
		_previous[index] = _options[index];
		byte old = _options[index];
		byte value = old == 0xff ? (byte)0 : 0xff - old < amount ? (byte)0xff : (byte)(old + amount);
		SetOption(apply, value, index);
		return value;
	}

	/// <summary>
	/// <c>Prefs_DecreaseOption</c> (<c>0004aa78</c>)(<paramref name="apply"/>, <paramref name="amount"/>,
	/// <paramref name="index"/>): 255 from 0, 0 when the value is below the amount, the value less the amount otherwise;
	/// through <see cref="SetOption"/>. Returns it.
	/// </summary>
	public byte DecreaseOption(bool apply, byte amount, int index) {
		_previous[index] = _options[index];
		byte old = _options[index];
		byte value = old == 0 ? (byte)0xff : old < amount ? (byte)0 : (byte)(old - amount);
		SetOption(apply, value, index);
		return value;
	}

	/// <summary><c>Prefs_OptionChanged</c> (<c>0004ac2c</c>)(<paramref name="index"/>): whether the option differs from its load-time copy.</summary>
	public bool OptionChanged(int index) => _options[index] != _loaded[index];

	/// <summary>
	/// <c>Prefs_SaveSelectedOptions</c> (<c>0004ab6c</c>)(<paramref name="indices"/>, count): the 17 bytes of
	/// <see cref="FilePath"/> read, the options <paramref name="indices"/> names copied over them, or all 17 options
	/// taken when the file does not open; then the 17 written back when the file opens for writing. Bytes past a short
	/// file's end read 0, as in <see cref="FromFile"/>. With no <see cref="FilePath"/> nothing is saved.
	/// </summary>
	public void SaveSelectedOptions(IReadOnlyList<int> indices) {
		if (FilePath == null) {
			return;
		}

		byte[] buffer;
		byte[]? file = null;
		try {
			file = File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			// The file does not open.
		}

		if (file != null) {
			buffer = new byte[OptionCount];
			Array.Copy(file, buffer, Math.Min(file.Length, OptionCount));
			foreach (int index in indices) {
				buffer[index] = _options[index];
			}
		} else {
			buffer = (byte[])_options.Clone();
		}

		try {
			File.WriteAllBytes(FilePath, buffer);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			// FileWStream_Ctor's open failed, and nothing is written.
		}
	}

	/// <summary>
	/// The shell's <c>Prefs_SetOption</c> (<c>ES1GO 00008378</c>)(<paramref name="apply"/>, <paramref name="value"/>,
	/// <paramref name="index"/>) (docs/es1/shell/single-mission.md#the-mission-params-menu): <see cref="SetOption"/>
	/// without the old value kept in <c>Prefs_PreviousOptions</c>, a copy the shell does not have; the value stored and,
	/// with <paramref name="apply"/>, the option's handler, when it has one, called with it.
	/// </summary>
	public void ShellSetOption(bool apply, byte value, int index) {
		_options[index] = value;
		if (apply) {
			_handlers[index]?.Invoke(value);
		}
	}

	/// <summary>
	/// The shell's <c>Prefs_CommitAll</c> (<c>ES1GO 000082b0</c>)(<paramref name="apply"/>)
	/// (docs/es1/shell/single-mission.md#the-mission-params-menu): <see cref="CommitOptions"/> without the copy of every
	/// option into <c>Prefs_PreviousOptions</c>. For each option that differs from its committed copy
	/// <c>Prefs_Committed</c> (<c>000aa260</c>), held as <see cref="Loaded"/>, its handler called with it when
	/// <paramref name="apply"/> is set and it has one, then the copy made current.
	/// </summary>
	public void ShellCommitAll(bool apply) {
		for (int index = 0; index < OptionCount; index++) {
			if (_loaded[index] != _options[index]) {
				if (apply) {
					_handlers[index]?.Invoke(_options[index]);
				}

				_loaded[index] = _options[index];
			}
		}
	}

	/// <summary>
	/// The shell's <c>Prefs_RevertAll</c> (<c>ES1GO 00008316</c>)(<paramref name="apply"/>)
	/// (docs/es1/shell/single-mission.md#the-mission-params-menu), the same statements as the simulator's <c>0004a8a8</c>,
	/// which this type has no other use for: each option that differs from its committed copy (<see cref="Loaded"/>) set
	/// back to it, and its handler called with it when <paramref name="apply"/> is set and it has one.
	/// </summary>
	public void ShellRevertAll(bool apply) {
		for (int index = 0; index < OptionCount; index++) {
			if (_options[index] != _loaded[index]) {
				_options[index] = _loaded[index];
				if (apply) {
					_handlers[index]?.Invoke(_options[index]);
				}
			}
		}
	}

	/// <summary>
	/// The shell's <c>Prefs_Save</c> (<c>ES1GO 00008267</c>) (docs/es1/shell/single-mission.md#the-mission-params-menu):
	/// the 17 options written to <see cref="FilePath"/> when it opens for writing. Unlike
	/// <see cref="SaveSelectedOptions"/> it does not read the file first. With no <see cref="FilePath"/> nothing is saved.
	/// </summary>
	public void ShellSave() {
		if (FilePath == null) {
			return;
		}

		try {
			File.WriteAllBytes(FilePath, _options);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			// FileWStream_Ctor's open failed, and nothing is written.
		}
	}

	/// <summary>
	/// <c>Prefs_ApplyMusicOption</c> (<c>0004ad0c</c>), option 0's handler. PLACEHOLDER: <c>0004e294</c>(value), then
	/// <c>Music_Tick</c> (<c>000585dc</c>) when the value is not 0 and <see cref="InitInProgress"/> is clear: ES1's
	/// sound, which no plan step ports yet (docs/herculan/plan-es1-first-mission.md#open).
	/// </summary>
	private void ApplyMusicOption(byte value) {
		_ = value;
	}

	/// <summary>
	/// <c>Prefs_ApplySoundsOption</c> (<c>0004acfc</c>), option 1's handler. PLACEHOLDER: <c>0004e2c8</c>(value): ES1's
	/// sound, which no plan step ports yet.
	/// </summary>
	private void ApplySoundsOption(byte value) {
		_ = value;
	}

	/// <summary>
	/// <c>MessagePort_ShowEnabled</c> (<c>000930d2</c>), the message boxes' file's byte, 1 in the image: while it is
	/// clear a port's tick counts a due and ready message as shown without showing it
	/// (docs/es1/simulation/cockpit-messages.md#the-lifecycle). Kept with the options because option 2's handler writes it
	/// from the options' load on, before any port is built.
	/// </summary>
	public bool MessagePortShowEnabled { get; set; } = true;

	/// <summary>
	/// The byte <c>000959e4</c>, 0 in the image, which option 2's handler sets beside
	/// <see cref="MessagePortShowEnabled"/>. PLACEHOLDER: its readers are ES1's sound, which no plan step ports yet
	/// (docs/es1/simulation/preferences.md#open).
	/// </summary>
	public bool VoiceLinesGate { get; private set; }

	/// <summary>
	/// <c>Prefs_ApplyRadioOption</c> (<c>0004ad30</c>), option 2's handler: <see cref="VoiceLinesGate"/> = (value ≠ 0)
	/// and <see cref="MessagePortShowEnabled"/> = (value ≠ 1) (docs/es1/simulation/preferences.md#the-option-array).
	/// </summary>
	private void ApplyRadioOption(byte value) {
		VoiceLinesGate = value != 0;
		MessagePortShowEnabled = value != 1;
	}

	/// <summary>
	/// <c>Prefs_LimitDetailInLowMemory</c> (<c>0004adb0</c>), option 6's handler, which reads the option's byte and not
	/// its argument (docs/es1/simulation/object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08):
	/// with <see cref="LowMemory"/> set and the option above 160, during <see cref="Load"/> it sets the option to 160
	/// (<c>Prefs_SetOption</c>(1, 160, 6)), and otherwise it raises the status panel's status <c>0x11</c>. With
	/// <see cref="LowMemory"/> always clear here, it does nothing.
	/// </summary>
	private void LimitDetailInLowMemory(byte value) {
		_ = value;
		if (!LowMemory || _options[DetailOption] <= LowMemoryDetailLimit) {
			return;
		}

		if (InitInProgress) {
			SetOption(true, LowMemoryDetailLimit, DetailOption);
			return;
		}

		// PLACEHOLDER: outside the load, Prefs_UndoSetOption (0004a900)(6), which puts option 6 back to its value before
		// its last write and runs no handler, then StatusAlertPanel_Ctor(0, 0, 1, 0x11) run through its slot +0xc and
		// destroyed with GeneralAlert_Dtor(panel, 3); never reached while LowMemory is clear, and no plan step ports the
		// low-memory mode.
	}
}
