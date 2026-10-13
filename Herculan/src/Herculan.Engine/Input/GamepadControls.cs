using System.Globalization;
using System.Text;
using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Content;

namespace Herculan.Engine.Input;

/// <summary>
/// What each control does while a gamepad is the device being read: the walker's and the RAZOR's CONTROLS settings,
/// in <c>prefs.cfg</c>'s own layout (four axis rows, then eight action codes), standing in for <c>prefs.cfg</c>'s through
/// <see cref="SimulatorPreferences.ControlsOverride"/>. <b>This engine's own</b>, kept in <c>herculan-joystick.cfg</c>'s
/// <c>[Gamepad]</c> section, so a joystick and the original game go on using <c>prefs.cfg</c>'s
/// (docs/herculan/joystick-config.md, "Gamepads").
///
/// <para>The axis rows are fixed: with <see cref="JoystickDeviceMap.ForGamepad"/>'s map they give a walker the left
/// stick for steering and a rate throttle and the right stick for the turret, and the RAZOR the right stick for pitch
/// and roll and the left for rudder and throttle. Only the eight actions are the player's to change.</para>
/// </summary>
public sealed class GamepadControls : IControlsOverride {
	private const int BlockLength = Prefs.ControlsOptionCount;

	// JOYSTICK on movement and THROTTLE on turret, so the left stick's Y moves the throttle at a rate rather than
	// setting it as a lever (docs/retail/simulation/mech-locomotion.md); RUDDER on turret; the hat on VIEWS.
	private static readonly byte[] HercAxes = [1, 2, 2, 1];

	// The flight half: the stick (the left) on rudder and throttle, and the Throttle and Rudder slots (the right stick)
	// on elevator and aileron. THROTTLE on 1 rather than 2, the RAZOR's THROTTLE word, keeps its throttle a rate
	// (docs/retail/simulation/razor-flight.md, "Throttle").
	private static readonly byte[] RazorAxes = [2, 1, 1, 1];

	// BUTTON 1-8: FIRE, TARGET, NEXT WEAPON, LINK WEAPON, NEXT CHAIN, SHIELDS REAR, SHIELDS FRONT, MFD DISPLAYS.
	private static readonly byte[] DefaultActions = [0x01, 0x02, 0x13, 0x0d, 0x12, 0x0a, 0x09, 0x0e];

	private static readonly byte[] Defaults = [.. HercAxes, .. DefaultActions, .. RazorAxes, .. DefaultActions];

	private readonly byte[] _options = (byte[])Defaults.Clone();

	/// <summary>Writes the settings, set by whoever holds the file they live in; null leaves <see cref="Save"/> doing nothing.</summary>
	public Action? Persist { get; set; }

	/// <summary>Whether the settings are the defaults, which a file need not carry.</summary>
	public bool IsDefault => _options.AsSpan().SequenceEqual(Defaults);

	/// <inheritdoc/>
	public bool AxisRowsFixed => true;

	/// <inheritdoc/>
	public bool Covers(int index) => index >= Prefs.HercControlsBase && index < Prefs.HercControlsBase + Defaults.Length;

	/// <inheritdoc/>
	public byte this[int index] {
		get => Covers(index) ? _options[index - Prefs.HercControlsBase] : (byte)0;
		set {
			if (Covers(index)) {
				_options[index - Prefs.HercControlsBase] = value;
			}
		}
	}

	/// <inheritdoc/>
	public byte Recommended(int index) => Covers(index) ? Defaults[index - Prefs.HercControlsBase] : (byte)0;

	/// <inheritdoc/>
	public void Save() => Persist?.Invoke();

	/// <summary>
	/// Reads the <c>HercControls</c> and <c>RazorControls</c> settings of a <c>[Gamepad]</c> section. A block that is
	/// missing or not twelve numbers keeps its defaults, and its axis rows are always the fixed ones.
	/// </summary>
	public static GamepadControls FromValues(Dictionary<string, string> values) {
		var controls = new GamepadControls();
		controls.ReadBlock(values, "hercControls", Prefs.HercControlsBase);
		controls.ReadBlock(values, "razorControls", Prefs.RazorControlsBase);
		return controls;
	}

	/// <summary>Writes the two settings <see cref="FromValues"/> reads, with a comment on what they are.</summary>
	public void WriteValues(StringBuilder text) {
		text.AppendLine(";; What each control does while a gamepad is in use, in place of the CONTROLS");
		text.AppendLine(";; settings in prefs.cfg: JOYSTICK, THROTTLE, RUDDER and HAT (fixed), then the");
		text.AppendLine(";; action code of BUTTON 1-8. The CONTROLS panel edits the actions.");
		text.AppendLine($"HercControls = {Block(Prefs.HercControlsBase)}");
		text.AppendLine($"RazorControls = {Block(Prefs.RazorControlsBase)}");
	}

	private string Block(int start) =>
		string.Join(' ', _options.Skip(start - Prefs.HercControlsBase).Take(BlockLength)
			.Select(option => option.ToString(CultureInfo.InvariantCulture)));

	private void ReadBlock(Dictionary<string, string> values, string key, int start) {
		if (!values.TryGetValue(key, out string? text)) {
			return;
		}

		string[] parts = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != BlockLength) {
			return;
		}

		var parsed = new byte[BlockLength];
		for (int i = 0; i < BlockLength; i++) {
			if (!byte.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i])) {
				return;
			}
		}

		// The axis rows stay the fixed ones whatever the file says; only the actions are read.
		for (int i = Prefs.ControlsAxisCount; i < BlockLength; i++) {
			_options[start - Prefs.HercControlsBase + i] = parsed[i];
		}
	}
}
