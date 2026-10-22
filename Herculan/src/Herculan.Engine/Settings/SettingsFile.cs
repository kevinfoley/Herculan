using System.Text.Json;
using System.Text.Json.Serialization;
using Herculan.Engine.Platform;

namespace Herculan.Engine.Settings;

/// <summary>
/// A player's chosen values for one catalog of <c>bool</c> settings, kept in a JSON file of
/// <see cref="ISettingDefinition{T}.ID"/> to value in <see cref="UserConfig.Folder"/>. A setting with no value stored
/// here reads as its <see cref="ISettingDefinition{T}.DefaultValue"/>, so a setting added later reads as its default on
/// every existing save file without needing a migration.
/// </summary>
public abstract class SettingsFile<TDefinition> where TDefinition : SettingDefinition<bool> {
	private static readonly JsonSerializerOptions SerializerOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never
	};

	private readonly Dictionary<TDefinition, bool> _bools = new();
	private readonly IReadOnlyList<TDefinition> _all;
	private readonly string _fileName;

	/// <param name="all">Every setting in the catalog, which is what is read and written.</param>
	/// <param name="fileName">The file's name in <see cref="UserConfig.Folder"/>.</param>
	protected SettingsFile(IReadOnlyList<TDefinition> all, string fileName) {
		_all = all;
		_fileName = fileName;
	}

	public bool GetSettingValue(TDefinition setting)
		=> _bools.TryGetValue(setting, out bool value) ? value : setting.DefaultValue;

	public void SetSettingValue(TDefinition setting, bool value)
		=> _bools[setting] = value;

	/// <summary>Where the settings live.</summary>
	public string FilePath => UserConfig.PathOf(_fileName);

	/// <summary>
	/// Reads the settings file into this instance, replacing whatever values it already held. Used both at startup and
	/// by a menu's Cancel to discard in-progress edits — an unset value already reads as its default, so clearing and
	/// re-populating is enough to get back to the last saved state. Falls back to every setting at its default when the
	/// file is absent or unreadable; a corrupt or hand-edited file is not worth failing a launch over.
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

			foreach (TDefinition setting in _all) {
				if (saved.TryGetValue(setting.ID, out bool value)) {
					_bools[setting] = value;
				}
			}

			return true;
		} catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); using the default settings.");
			return false;
		}
	}

	/// <summary>Writes every setting's current value (<see cref="UserConfig.Write"/>).</summary>
	public bool SaveToDisk() {
		var toSave = new Dictionary<string, bool>();
		foreach (TDefinition setting in _all) {
			toSave[setting.ID] = GetSettingValue(setting);
		}

		return UserConfig.Write(FilePath, JsonSerializer.Serialize(toSave, SerializerOptions));
	}
}
