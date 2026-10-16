using System.Text;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Read;

namespace Herculan.Engine.Input;

/// <summary>
/// <c>data\herculan-joystick.cfg</c>: a <see cref="JoystickDeviceMap"/> for every kind of device the player has set up,
/// so that switching controllers does not mean mapping again (docs/herculan/joystick-config.md, "One file, many
/// devices"). This engine's own file; retail never reads it.
///
/// <para>Each map is an INI section. <c>[Gamepad]</c> serves every gamepad, since GLFW reads all of them in one
/// layout, and also carries what each control does with one (<see cref="Gamepad"/>) and how one works as the mouse
/// (<see cref="Mouse"/>). <c>[Joystick &lt;GUID&gt;]</c> serves one model of joystick, by the GUID GLFW gives it, and <c>[Joystick]</c>
/// any joystick without a section of its own, which is also the one section a file written before per-device sections
/// has.</para>
/// </summary>
public sealed class JoystickMapFile {
	/// <summary>Where the file is, relative to the game's <c>data</c> folder.</summary>
	public const string FileName = "herculan-joystick.cfg";

	private const string GamepadSection = "Gamepad";
	private const string JoystickSection = "Joystick";

	private readonly Dictionary<string, Entry> _sections = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>A section's map, and the name of the device it was saved from, kept so the file stays readable.</summary>
	private sealed record Entry(string Section, JoystickDeviceMap Map, string? DeviceName);

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
			file._sections[Normalise(section)] = new Entry(Normalise(section), JoystickDeviceMap.FromValues(values), name);
			if (Normalise(section) == GamepadSection) {
				file.Gamepad = GamepadControls.FromValues(values);
				file.Mouse = GamepadMouseSettings.FromValues(values);
			}
		}

		return file;
	}

	/// <summary>Reads the file in a game's <c>data</c> folder, as <see cref="Load"/> does.</summary>
	public static JoystickMapFile LoadIn(string dataDirectory) => Load(CaseInsensitivePath.Combine(dataDirectory, FileName));

	/// <summary>Whether no device has a map.</summary>
	public bool IsEmpty => _sections.Count == 0;

	/// <summary>
	/// The map for a device: <c>[Gamepad]</c> for a gamepad; for a joystick its model's section, else <c>[Joystick]</c>.
	/// Null when the file has none, and the device's own defaults (<see cref="JoystickDeviceMap.ForDevice"/>) apply.
	/// </summary>
	public JoystickDeviceMap? For(bool isGamepad, string? guid) {
		if (isGamepad) {
			return _sections.GetValueOrDefault(GamepadSection)?.Map;
		}

		return (guid is null ? null : _sections.GetValueOrDefault(Normalise(ModelSection(guid)))?.Map)
			?? _sections.GetValueOrDefault(JoystickSection)?.Map;
	}

	/// <summary>
	/// Stores <paramref name="map"/> as the device's own: <c>[Gamepad]</c> for a gamepad, its model's section for a
	/// joystick GLFW gives a GUID, and <c>[Joystick]</c> for one it does not.
	/// </summary>
	public void Set(bool isGamepad, string? guid, string? deviceName, JoystickDeviceMap map) {
		string section = isGamepad ? GamepadSection : guid is null ? JoystickSection : ModelSection(guid);
		_sections[Normalise(section)] = new Entry(Normalise(section), map, deviceName);
	}

	/// <summary>Writes every section, gamepads first, then the shared joystick section, then each model's.</summary>
	public void Save(string path) {
		// Gamepad actions or mouse settings changed with no gamepad map saved yet: the section they go in, with the map it
		// implies.
		if ((!Gamepad.IsDefault || !Mouse.IsDefault) && !_sections.ContainsKey(GamepadSection)) {
			_sections[GamepadSection] = new Entry(GamepadSection, JoystickDeviceMap.ForGamepad(), null);
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

			entry.Map.WriteValues(text);
			if (entry.Section == GamepadSection) {
				text.AppendLine();
				Gamepad.WriteValues(text);
				text.AppendLine();
				Mouse.WriteValues(text);
			}
		}

		File.WriteAllText(path, text.ToString());
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
