using System.Text.Json;
using System.Text.Json.Serialization;

namespace Herculan.Engine.Settings;

/// <summary>
/// The player's chosen tweak-setting values — see <see cref="TweakSettingDefinitions"/>
/// for the settings themselves. A setting with no value stored here reads as its
/// <see cref="ISettingDefinition{T}.DefaultValue"/>, so a setting added later reads as its default on
/// every existing save file without needing a migration.
/// </summary>
public sealed class TweakSettings {

	private static TweakSettings? current;

	/// <summary>The player's settings, which the hosts load from <see cref="FilePath"/> and the menus edit.</summary>
	public static TweakSettings Current => current ??= new TweakSettings();

	private static readonly JsonSerializerOptions SerializerOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never
	};

	private readonly Dictionary<TweakSettingDefinition<bool>, bool> _bools = new();

	/// <summary>Every setting at its default.</summary>
	public TweakSettings() {
	}

	public bool GetSettingValue(TweakSettingDefinition<bool> setting)
		=> _bools.TryGetValue(setting, out bool value) ? value : setting.DefaultValue;

	public void SetSettingValue(TweakSettingDefinition<bool> setting, bool value)
		=> _bools[setting] = value;

	/// <summary>
	/// Where the settings live: <c>%APPDATA%\Herculan\tweak-settings.json</c> on Windows, and the
	/// platform's equivalent user-config directory elsewhere.
	/// </summary>
	public static string FilePath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
			Environment.SpecialFolderOption.DoNotVerify),
		"Herculan", "tweak-settings.json");

	/// <summary>
	/// Reads the settings file into this instance, replacing whatever values it already held. Used
	/// both at startup and to discard in-progress edits in <see cref="TweaksMenu"/> — an unset
	/// value already reads as its default, so clearing and re-populating is enough to get back to the
	/// last saved state. Falls back to every setting at its default when the file is absent or
	/// unreadable; a corrupt or hand-edited file is not worth failing a launch over.
	/// </summary>
	public bool LoadFromDisk() {
		_bools.Clear();

		string path = FilePath;
		if (!File.Exists(path)) {
			return true;
		}

		try {
			Dictionary<string, bool>? saved = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path), SerializerOptions);
			if (saved == null) {
				return true;
			}

			foreach (TweakSettingDefinition<bool> setting in TweakSettingDefinitions.All) {
				if (saved.TryGetValue(setting.ID, out bool value)) {
					_bools[setting] = value;
				}
			}

			return true;
		} catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); using default tweak settings.");
			return false;
		}
	}

	/// <summary>Writes every setting's current value, creating the directory if this is the first save.</summary>
	public bool SaveToDisk() {
		string path = FilePath;
		try {
			var toSave = new Dictionary<string, bool>();
			foreach (TweakSettingDefinition<bool> setting in TweakSettingDefinitions.All) {
				toSave[setting.ID] = GetSettingValue(setting);
			}

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(toSave, SerializerOptions));
			return true;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not write {path}: {ex.Message}");
			return false;
		}
	}
}
