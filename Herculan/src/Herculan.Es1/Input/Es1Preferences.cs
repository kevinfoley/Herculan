namespace Herculan.Es1.Input;

/// <summary>
/// The simulator's 17 preference option bytes as <c>Prefs_Load</c> (<c>ES1DBSIM 0004a744</c>) reads them from
/// <c>data\prefs.cfg</c> into <c>000bd562</c>, all 0 when the file does not open
/// (docs/es1/simulation/mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc). The options the input
/// build reads are named here; <see cref="Es1ControlsState"/> runs the handlers of options 8 and 10.
/// </summary>
public sealed class Es1Preferences {
	/// <summary>The number of option bytes <c>Prefs_Load</c> reads.</summary>
	public const int OptionCount = 17;

	/// <summary>Option 7 (<c>000bd569</c>): which of the six joystick configurations the input build uses.</summary>
	public const int StickConfigurationOption = 7;

	/// <summary>Option 8 (<c>000bd56a</c>): the stick mode, and whether <c>Input_ToggleStickMode</c> may change it.</summary>
	public const int StickModeOption = 8;

	/// <summary>Option 9 (<c>000bd56b</c>): the stick's third axis drives an axis in configurations 0, 1, 3, 4 and 5.</summary>
	public const int ThirdAxisOption = 9;

	/// <summary>Option 10 (<c>000bd56c</c>): <c>Ctrl+I</c> and <c>Ctrl+M</c> step the throttle and the stick's Y no longer drives it.</summary>
	public const int ThrottleKeysOption = 10;

	private readonly byte[] _options;

	private Es1Preferences(byte[] options) => _options = options;

	/// <summary>The options of a <c>data\prefs.cfg</c> that does not open: all 0.</summary>
	public static Es1Preferences Default => new(new byte[OptionCount]);

	/// <summary>
	/// The options from the bytes of <c>data\prefs.cfg</c>, or <see cref="Default"/> for null, a file that does not open.
	/// A file shorter than 17 bytes gives 0 for the bytes it lacks.
	/// </summary>
	public static Es1Preferences FromFile(byte[]? file) {
		var options = new byte[OptionCount];

		// PLACEHOLDER: what FileRStream_Read leaves in the bytes past a short file's end is unread, so they read 0 here;
		// no plan step retires it.
		if (file != null) {
			Array.Copy(file, options, Math.Min(file.Length, OptionCount));
		}

		return new Es1Preferences(options);
	}

	/// <summary>Option <paramref name="index"/>, 0–16.</summary>
	public byte this[int index] => _options[index];

	/// <summary>Option 7, <see cref="StickConfigurationOption"/>.</summary>
	public byte StickConfiguration => _options[StickConfigurationOption];

	/// <summary>Option 8, <see cref="StickModeOption"/>.</summary>
	public byte StickMode => _options[StickModeOption];

	/// <summary>Option 9 on, <see cref="ThirdAxisOption"/>.</summary>
	public bool ThirdAxis => _options[ThirdAxisOption] != 0;

	/// <summary>
	/// <c>Prefs_SetOption</c> (<c>0004a914</c>)(1, 0, 9), <c>Input_CreateJoystickDevices</c>'s clear of option 9 under
	/// option 7 = 2, in memory only (docs/es1/simulation/joystick.md#building-the-devices). <c>Prefs_Init</c> registers no
	/// handler for option 9, so none runs. The old byte <c>Prefs_SetOption</c> saves at <c>000bd586</c> is not kept, as
	/// this type keeps none of <c>Prefs_Load</c>'s copies.
	/// </summary>
	public void ClearThirdAxis() => _options[ThirdAxisOption] = 0;

	/// <summary>The largest option 7 that names a stick model (docs/es1/simulation/joystick.md#building-the-devices).</summary>
	public const int LastStickModel = 5;

	/// <summary>
	/// <b>This engine's own:</b> option 7 set to <paramref name="model"/> for the run, in memory only, after the file's
	/// read and before the joystick devices are built; the <c>--stick-model</c> engine aid, which has no retail
	/// counterpart, until the in-mission preferences panel sets option 7.
	/// </summary>
	public void OverrideStickConfiguration(byte model) => _options[StickConfigurationOption] = model;

	/// <summary>Option 10 on, <see cref="ThrottleKeysOption"/>.</summary>
	public bool ThrottleKeys => _options[ThrottleKeysOption] != 0;
}
