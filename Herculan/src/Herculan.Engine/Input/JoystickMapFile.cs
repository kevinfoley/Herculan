using System.Text;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Read;
using Herculan.Engine.Platform;

namespace Herculan.Engine.Input;

/// <summary>
/// <c>herculan-joystick.cfg</c>: a <see cref="JoystickDeviceMap"/> for every kind of device the player has set up,
/// so that switching controllers does not mean mapping again (docs/herculan/joystick-config.md, "One file, many
/// devices"). This engine's own file; retail never reads it.
///
/// <para>The player keeps one, in <see cref="UserConfig.Folder"/>, which every game reads and writes
/// (<see cref="PlayerPath"/>). An install's <c>data</c> folder may hold one an earlier build kept there, which seeds the
/// player's while the player's is missing (<see cref="LoadForPlayer(string?)"/>) and is read for nothing else.</para>
///
/// <para>Each map is an INI section. <c>[Gamepad]</c> serves every gamepad, since GLFW reads all of them in one
/// layout, and also carries what each control does with one (<see cref="Gamepad"/>) and how one works as the mouse
/// (<see cref="Mouse"/>). <c>[Joystick &lt;GUID&gt;]</c> serves one model of joystick, by the GUID GLFW gives it, and <c>[Joystick]</c>
/// any joystick without a section of its own, which is also the one section a file written before per-device sections
/// has.</para>
///
/// <para>A section also says which <see cref="ControllerMode"/> the device runs in, and may hold its Custom
/// <see cref="ControllerProfile"/> beside Original's map, so switching modes loses neither
/// (docs/herculan/plan-controller-profiles.md).</para>
/// </summary>
public sealed class JoystickMapFile {
	/// <summary>The file's name, in the player's settings folder and in an install's <c>data</c> folder alike.</summary>
	public const string FileName = "herculan-joystick.cfg";

	/// <summary>The player's file, which every game reads and saves.</summary>
	public static string PlayerPath => UserConfig.PathOf(FileName);

	private const string GamepadSection = "Gamepad";
	private const string JoystickSection = "Joystick";

	private readonly Dictionary<string, Entry> _sections = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// A section: Original's map, the device's mode, its Custom profile when the section has one, and the name of the device
	/// it was saved from, kept so the file stays readable.
	/// </summary>
	private sealed record Entry(string Section, JoystickDeviceMap Map, string? DeviceName, ControllerMode Mode, ControllerProfile? Profile);

	/// <summary>What each control does while a gamepad is in use, from <c>[Gamepad]</c>, or the defaults.</summary>
	public GamepadControls Gamepad { get; private set; } = new();

	/// <summary>How a gamepad works as the mouse, from <c>[Gamepad]</c>, or the defaults.</summary>
	public GamepadMouseSettings Mouse { get; set; } = new();

	/// <summary>Reads the file; one that is missing or unreadable is empty.</summary>
	public static JoystickMapFile Load(string path) {
		var file = new JoystickMapFile();
		string[] lines;
		try {
			lines = File.ReadAllLines(path);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return file;
		}

		foreach (string section in IniSection.SectionNames(lines)) {
			if (!IsDeviceSection(section)) {
				continue;
			}

			var values = IniSection.Read(lines, section);
			values.TryGetValue("name", out string? name);
			// A section without Mode was written before modes existed, for Original's map.
			var mode = values.TryGetValue("mode", out string? modeText) && Enum.TryParse(modeText, ignoreCase: true, out ControllerMode parsed)
				&& Enum.IsDefined(parsed) && !char.IsDigit(modeText.Trim()[0])
				? parsed
				: ControllerMode.Original;
			file._sections[Normalise(section)] = new Entry(
				Normalise(section), JoystickDeviceMap.FromValues(values), name, mode, ControllerProfile.FromValues(values));
			if (Normalise(section) == GamepadSection) {
				file.Gamepad = GamepadControls.FromValues(values);
				file.Mouse = GamepadMouseSettings.FromValues(values);
			}
		}

		return file;
	}

	/// <summary>
	/// Reads the player's file (<see cref="PlayerPath"/>), seeding it first from the copy in the running game's
	/// <paramref name="installDataDirectory"/> while it is missing, as <see cref="LoadForPlayer(string, string?)"/> says.
	/// </summary>
	public static JoystickMapFile LoadForPlayer(string? installDataDirectory) => LoadForPlayer(PlayerPath,
		installDataDirectory is null ? null : CaseInsensitivePath.Combine(installDataDirectory, FileName));

	/// <summary>
	/// Reads <paramref name="playerPath"/>. While it is missing and <paramref name="installPath"/> exists, the install's
	/// copy is written there first, so the maps a player set up for one game serve every game from then on; with no
	/// install copy nothing is written, which leaves the seeding to the next game that has one. A seed that cannot be
	/// written reads the install's copy for this run.
	/// </summary>
	public static JoystickMapFile LoadForPlayer(string playerPath, string? installPath) {
		if (File.Exists(playerPath)) {
			return Load(playerPath);
		}

		if (installPath is null || !File.Exists(installPath)) {
			return new JoystickMapFile();
		}

		try {
			if (UserConfig.Write(playerPath, File.ReadAllText(installPath))) {
				Console.WriteLine($"Copied {installPath} to {playerPath}, which every game reads from now on.");
				return Load(playerPath);
			}
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {installPath}: {ex.Message}");
		}

		return Load(installPath);
	}

	/// <summary>Whether no device has a map.</summary>
	public bool IsEmpty => _sections.Count == 0;

	/// <summary>
	/// The map for a device: <c>[Gamepad]</c> for a gamepad; for a joystick its model's section, else <c>[Joystick]</c>.
	/// Null when the file has none, and the device's own defaults (<see cref="JoystickDeviceMap.ForDevice"/>) apply.
	/// </summary>
	public JoystickDeviceMap? For(bool isGamepad, string? guid) => Find(isGamepad, guid)?.Map;

	/// <summary>
	/// Stores <paramref name="map"/> as the device's own: <c>[Gamepad]</c> for a gamepad, its model's section for a
	/// joystick GLFW gives a GUID, and <c>[Joystick]</c> for one it does not.
	/// </summary>
	public void Set(bool isGamepad, string? guid, string? deviceName, JoystickDeviceMap map) =>
		Update(isGamepad, guid, deviceName, entry => entry with { Map = map });

	/// <summary>
	/// The device's mode: what its section says, <see cref="ControllerMode.Original"/> for a section written before modes
	/// existed, and <see cref="ControllerMode.Custom"/> for a device the file has no section for.
	/// </summary>
	public ControllerMode ModeFor(bool isGamepad, string? guid) => Find(isGamepad, guid)?.Mode ?? ControllerMode.Custom;

	/// <summary>
	/// The device's Custom profile from its section, or null when the section has none or there is no section, and the
	/// device's default (<see cref="ControllerProfile.ForDevice"/>) applies.
	/// </summary>
	public ControllerProfile? ProfileFor(bool isGamepad, string? guid) => Find(isGamepad, guid)?.Profile;

	/// <summary>
	/// Stores the device's mode and Custom profile in the section <see cref="Set"/> names, keeping its map. A joystick
	/// getting a section of its own keeps the map <c>[Joystick]</c> gave it, and a device the file has no section for gets
	/// <see cref="JoystickDeviceMap.ForGamepad"/> or <see cref="JoystickDeviceMap"/>'s own defaults.
	/// </summary>
	public void SetProfile(bool isGamepad, string? guid, string? deviceName, ControllerMode mode, ControllerProfile profile) =>
		Update(isGamepad, guid, deviceName, entry => entry with { Mode = mode, Profile = profile });

	private Entry? Find(bool isGamepad, string? guid) =>
		isGamepad ? _sections.GetValueOrDefault(GamepadSection)
		: (guid is null ? null : _sections.GetValueOrDefault(Normalise(ModelSection(guid))))
			?? _sections.GetValueOrDefault(JoystickSection);

	// A section made for a joystick that [Joystick] served starts from that one's settings; one made for a device without
	// a section starts from the defaults, in Custom mode.
	private void Update(bool isGamepad, string? guid, string? deviceName, Func<Entry, Entry> change) {
		string section = Normalise(isGamepad ? GamepadSection : guid is null ? JoystickSection : ModelSection(guid));
		var entry = _sections.GetValueOrDefault(section)
			?? (Find(isGamepad, guid) is { } serving
				? serving with { Section = section }
				: new Entry(section, isGamepad ? JoystickDeviceMap.ForGamepad() : new JoystickDeviceMap(), null, ControllerMode.Custom, null));
		_sections[section] = change(entry) with { DeviceName = deviceName };
	}

	/// <summary>
	/// Writes every section to <paramref name="path"/>, gamepads first, then the shared joystick section, then each
	/// model's, through <see cref="UserConfig.TryWrite"/>; false, with the reason in <paramref name="error"/>, when the
	/// write fails.
	/// </summary>
	public bool Save(string path, out string? error) {
		// Gamepad actions or mouse settings changed with no gamepad map saved yet: the section they go in, with the map it
		// implies.
		if ((!Gamepad.IsDefault || !Mouse.IsDefault) && !_sections.ContainsKey(GamepadSection)) {
			_sections[GamepadSection] = new Entry(GamepadSection, JoystickDeviceMap.ForGamepad(), null, ControllerMode.Custom, null);
		}

		var text = new StringBuilder();
		text.AppendLine(";; Herculan's own file. The simulator's own prefs.cfg still says what each");
		text.AppendLine(";; control DOES; this says which piece of hardware each control IS, which is");
		text.AppendLine(";; the one thing a 1996 gameport stick did not need to be told.");
		text.AppendLine(";;");
		text.AppendLine(";; One section per kind of device: [Gamepad] for every gamepad, [Joystick <GUID>]");
		text.AppendLine(";; for one model of joystick, and [Joystick] for any other joystick.");
		text.AppendLine(";; Axes and buttons are zero-based indices into what the device reports.");
		text.AppendLine(";; -1 leaves a control unmapped, which greys its row on the CONTROLS panel.");

		foreach (var entry in _sections.Values.OrderBy(Order).ThenBy(entry => entry.Section, StringComparer.OrdinalIgnoreCase)) {
			text.AppendLine();
			text.AppendLine($"[{entry.Section}]");
			if (!string.IsNullOrWhiteSpace(entry.DeviceName)) {
				// A ';' would start a comment and cut the name short when it is read back.
				text.AppendLine($"Name = {entry.DeviceName.Replace(';', ',')}");
			}

			text.AppendLine($"{$"Mode = {entry.Mode}",-28} ;; Original: the game's CONTROLS panel; Custom: below");
			text.AppendLine();
			text.AppendLine(";; Original: which piece of hardware each control is.");
			entry.Map.WriteValues(text);
			if (entry.Section == GamepadSection) {
				text.AppendLine();
				Gamepad.WriteValues(text);
				text.AppendLine();
				Mouse.WriteValues(text);
			}

			if (entry.Profile is { } profile) {
				text.AppendLine();
				profile.WriteValues(text);
			}
		}

		return UserConfig.TryWrite(path, text.ToString(), out error);
	}

	private static int Order(Entry entry) =>
		entry.Section.Equals(GamepadSection, StringComparison.OrdinalIgnoreCase) ? 0
		: entry.Section.Equals(JoystickSection, StringComparison.OrdinalIgnoreCase) ? 1
		: 2;

	private static string ModelSection(string guid) => $"{JoystickSection} {guid.Trim().ToLowerInvariant()}";

	private static bool IsDeviceSection(string section) =>
		section.Equals(GamepadSection, StringComparison.OrdinalIgnoreCase)
		|| section.Equals(JoystickSection, StringComparison.OrdinalIgnoreCase)
		|| section.StartsWith(JoystickSection + " ", StringComparison.OrdinalIgnoreCase);

	// One spelling per section, so a hand-edited "[gamepad]" or an upper-case GUID still matches.
	private static string Normalise(string section) {
		section = section.Trim();
		if (section.Equals(GamepadSection, StringComparison.OrdinalIgnoreCase)) {
			return GamepadSection;
		}

		return section.Length > JoystickSection.Length
			? ModelSection(section[(JoystickSection.Length + 1)..])
			: JoystickSection;
	}
}
