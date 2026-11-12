using System.Text;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Read;
using Herculan.Engine.Platform;

namespace Herculan.Engine.Input;

/// <summary>
/// <c>herculan-joystick.cfg</c>: the controller settings for every kind of device the player has set up,
/// so that switching controllers does not mean mapping again (docs/herculan/joystick-config.md, "One file, many
/// devices"). This engine's own file; retail never reads it.
///
/// <para>The player keeps one, in <see cref="UserConfig.Folder"/>, which every game reads and writes
/// (<see cref="PlayerPath"/>). An install's <c>data</c> folder may hold one an earlier build kept there. That copy
/// supplies the initial copy of the player's file when that file is missing (<see cref="LoadForPlayer(string?)"/>), and
/// is read for nothing else.</para>
///
/// <para>Each map is an INI section. <c>[Gamepad]</c> serves every gamepad, since GLFW reads all of them in one
/// layout. It also carries how a gamepad works as the mouse (<see cref="Mouse"/>). <c>[Joystick &lt;GUID&gt;]</c> serves
/// one model of joystick, by the GUID GLFW gives it. <c>[Joystick]</c> serves any joystick without a section of its own,
/// and it is the only section that a file written before per-device sections contains.</para>
///
/// <para>A joystick's section also says which <see cref="ControllerMode"/> it runs in, and may hold its Custom
/// <see cref="ControllerProfile"/> beside Original's <see cref="JoystickDeviceMap"/>, so switching modes loses neither
/// (docs/herculan/plan-controller-profiles.md). A gamepad always runs in Custom, so its section holds its Custom profile
/// and no Original map.</para>
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
	/// it was saved from. The name is written so a person reading the file can tell which device a section belongs to.
	/// </summary>
	private sealed record Entry(string Section, JoystickDeviceMap Map, string? DeviceName, ControllerMode Mode, ControllerProfile? Profile);

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
			// A gamepad is always Custom. A joystick section without Mode was written before modes existed, for Original's map.
			var mode = Normalise(section) == GamepadSection ? ControllerMode.Custom
				: values.TryGetValue("mode", out string? modeText) && Enum.TryParse(modeText, ignoreCase: true, out ControllerMode parsed)
					&& Enum.IsDefined(parsed) && !char.IsDigit(modeText.Trim()[0])
				? parsed
				: ControllerMode.Original;
			// A gamepad is always Custom, so its section keeps no Original map; the gamepad default stands in for it.
			var map = Normalise(section) == GamepadSection ? JoystickDeviceMap.ForGamepad() : JoystickDeviceMap.FromValues(values);
			file._sections[Normalise(section)] = new Entry(Normalise(section), map, name, mode, ControllerProfile.FromValues(values));
			if (Normalise(section) == GamepadSection) {
				file.Mouse = GamepadMouseSettings.FromValues(values);
			}
		}

		return file;
	}

	/// <summary>
	/// Reads the player's file (<see cref="PlayerPath"/>). When it is missing, it is first copied from the running game's
	/// <paramref name="installDataDirectory"/>, as <see cref="LoadForPlayer(string, string?)"/> says.
	/// </summary>
	public static JoystickMapFile LoadForPlayer(string? installDataDirectory) => LoadForPlayer(PlayerPath,
		installDataDirectory is null ? null : CaseInsensitivePath.Combine(installDataDirectory, FileName));

	/// <summary>
	/// Reads <paramref name="playerPath"/>. While it is missing and <paramref name="installPath"/> exists, the install's
	/// copy is first written to <paramref name="playerPath"/>, so the maps a player set up for one game serve every game
	/// from then on. With no install copy nothing is written, which leaves that to the next game that has one. If the copy
	/// cannot be written, this run reads the install's copy instead.
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
	/// The device's Original-mode map, from the section that serves it. Returns null when no section does; the caller then
	/// uses the device's defaults (<see cref="JoystickDeviceMap.ForDevice"/>).
	/// </summary>
	public JoystickDeviceMap? For(bool isGamepad, string? guid) => Find(isGamepad, guid)?.Map;

	/// <summary>
	/// Stores the device's Original-mode map in the device's own section, creating that section if the file has none. A new
	/// section starts as a copy of the section that serves the device. The change stays in memory until
	/// <see cref="Save"/>.
	/// </summary>
	public void Set(bool isGamepad, string? guid, string? deviceName, JoystickDeviceMap map) =>
		Update(isGamepad, guid, deviceName, entry => entry with { Map = map });

	/// <summary>
	/// The device's mode: <see cref="ControllerMode.Custom"/> for a gamepad, always; for a joystick what its section says,
	/// <see cref="ControllerMode.Original"/> for a section with no valid Mode (one written before modes existed, or one
	/// whose Mode is misspelled or a number), and Custom when the file has no section for it.
	/// </summary>
	public ControllerMode ModeFor(bool isGamepad, string? guid) =>
		isGamepad ? ControllerMode.Custom : Find(isGamepad, guid)?.Mode ?? ControllerMode.Custom;

	/// <summary>
	/// The Custom profile saved in the device's section. Returns null when the section has no Custom settings, or when the
	/// file has no section for the device; the caller then uses the device's default (<see cref="ControllerProfile.ForDevice"/>).
	/// For a gamepad, <see cref="ControllerProfile.GamepadMenuBarInput"/> is always pinned to the menu bar, whatever the
	/// file binds (<see cref="ControllerProfile.WithMenuBarPinnedTo"/>).
	/// </summary>
	public ControllerProfile? ProfileFor(bool isGamepad, string? guid) {
		var profile = Find(isGamepad, guid)?.Profile;
		return isGamepad ? profile?.WithMenuBarPinnedTo(ControllerProfile.GamepadMenuBarInput) : profile;
	}

	/// <summary>
	/// Stores the device's mode and Custom profile in the device's own section, as <see cref="Set"/> does, keeping its map.
	/// A gamepad's mode is always Custom, whatever <paramref name="mode"/> says. A null <paramref name="profile"/> removes
	/// the Custom settings, so the device falls back to its default profile.
	/// </summary>
	public void SetProfile(bool isGamepad, string? guid, string? deviceName, ControllerMode mode, ControllerProfile? profile) =>
		Update(isGamepad, guid, deviceName, entry => entry with { Mode = isGamepad ? ControllerMode.Custom : mode, Profile = profile });

	private Entry? Find(bool isGamepad, string? guid) =>
		isGamepad ? _sections.GetValueOrDefault(GamepadSection)
		: (guid is null ? null : _sections.GetValueOrDefault(Normalise(ModelSection(guid))))
			?? _sections.GetValueOrDefault(JoystickSection);

	// Applies a change to the device's own section and records the device's name there. If the device has no section of
	// its own yet, the new section starts as a copy of the section that serves the device. If no section serves it either,
	// the new section starts with the default map, Custom mode and no Custom profile.
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
	/// model's, through <see cref="UserConfig.TryWrite"/>; returns false, with the reason in <paramref name="error"/>,
	/// when the write fails. When <see cref="Mouse"/> differs from the defaults and the file has no <c>[Gamepad]</c>
	/// section, this creates one to hold the mouse settings.
	/// </summary>
	public bool Save(string path, out string? error) {
		// Mouse settings changed with no gamepad section yet: the section they go in.
		if (!Mouse.IsDefault && !_sections.ContainsKey(GamepadSection)) {
			_sections[GamepadSection] = new Entry(GamepadSection, JoystickDeviceMap.ForGamepad(), null, ControllerMode.Custom, null);
		}

		var text = new StringBuilder();
		text.AppendLine(";; HERCULAN's own controller settings, read by every game.");
		text.AppendLine(";;");
		text.AppendLine(";; One section per kind of device: [Gamepad] for every gamepad, [Joystick <GUID>]");
		text.AppendLine(";; for one model of joystick, and [Joystick] for any other joystick.");
		text.AppendLine(";; A joystick section's Mode says how its controls reach the game: Original reads");
		text.AppendLine(";; the game's own controls panel over the map below it, and Custom reads the");
		text.AppendLine(";; Custom settings instead. A gamepad always uses Custom.");
		text.AppendLine(";; Axes and buttons are zero-based indices into what the device reports.");

		foreach (var entry in _sections.Values.OrderBy(Order).ThenBy(entry => entry.Section, StringComparer.OrdinalIgnoreCase)) {
			text.AppendLine();
			text.AppendLine($"[{entry.Section}]");
			if (!string.IsNullOrWhiteSpace(entry.DeviceName)) {
				// A ';' would start a comment and cut the name short when it is read back.
				text.AppendLine($"Name = {entry.DeviceName.Replace(';', ',')}");
			}

			// A gamepad is always Custom, so its section has no Mode and no Original map.
			if (entry.Section != GamepadSection) {
				text.AppendLine($"{$"Mode = {entry.Mode}",-28} ;; Original: the game's CONTROLS panel; Custom: below");
				text.AppendLine();
				text.AppendLine(";; Original: which piece of hardware each control is. -1 leaves a control");
				text.AppendLine(";; unmapped, which greys its row on the game's controls panel.");
				entry.Map.WriteValues(text);
			} else {
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
