using System.Text.Json;
using System.Text.Json.Serialization;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// The editor's own preferences — tool state, not mission data, so it is kept next to the user's
/// other application settings rather than in the game install (which is read-only as far as this
/// engine is concerned) or beside the executable (which is a build output).
/// </summary>
public sealed class EditorSettings {
	private static readonly JsonSerializerOptions SerializerOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never
	};

	/// <summary>
	/// Whether the scene is drawn with the zone's distance fog — see
	/// <see cref="Render.SceneRenderer.FogEnabled"/>. On by default, matching what the simulator
	/// draws.
	/// </summary>
	public bool RenderFog { get; set; } = true;

	/// <summary>
	/// Whether the horizontal measuring grid is drawn under the camera — see
	/// <see cref="Render.GroundGridRenderer"/>.
	/// </summary>
	public bool ShowGrid { get; set; } = true;

	/// <summary>Whether the mission's trigger areas are drawn — see <see cref="MissionOverlay"/>.</summary>
	public bool ShowTriggerAreas { get; set; } = true;

	/// <summary>Whether the routes the mission's orders name are drawn.</summary>
	public bool ShowRoutes { get; set; } = true;

	/// <summary>Whether the mission box and its two margins are drawn.</summary>
	public bool ShowMissionBox { get; set; } = true;

	/// <summary>Whether the terrain tile each paints-ground structure group repaints is outlined.</summary>
	public bool ShowBasePads { get; set; } = true;

	/// <summary>
	/// How the members of a group waiting on an action are shown. The simulation draws none of them
	/// until the action fires, and most stand at a placeholder point, so by default they are outlined
	/// rather than drawn.
	/// </summary>
	[JsonConverter(typeof(JsonStringEnumConverter<WaitingGroupDisplay>))]
	public WaitingGroupDisplay WaitingGroups { get; set; } = WaitingGroupDisplay.Outlined;

	/// <summary>
	/// Where the settings live: <c>%APPDATA%\Herculan\editor-settings.json</c> on Windows, and the
	/// platform's equivalent user-config directory elsewhere.
	/// </summary>
	public static string FilePath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
			Environment.SpecialFolderOption.DoNotVerify),
		"Herculan", "editor-settings.json");

	/// <summary>
	/// Reads the settings file, falling back to the defaults when it is absent or unreadable. A
	/// corrupt or hand-edited file is not worth failing a launch over, so it is reported and
	/// replaced by the defaults; the next <see cref="Save"/> overwrites it.
	/// </summary>
	public static EditorSettings Load() {
		string path = FilePath;
		if (!File.Exists(path)) {
			return new EditorSettings();
		}

		try {
			return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(path), SerializerOptions)
				?? new EditorSettings();
		} catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); using default editor settings.");
			return new EditorSettings();
		}
	}

	/// <summary>Writes the settings, creating the directory if this is the first save.</summary>
	public void Save() {
		string path = FilePath;
		try {
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not write {path}: {ex.Message}");
		}
	}

	/// <summary>A detached copy, for holding the pre-edit values behind a Cancel button.</summary>
	public EditorSettings Clone() {
		var newSettings = new EditorSettings();
		newSettings.CopyFrom(this);
		return newSettings;
	}

	/// <summary>Copies every value out of <paramref name="other"/> — the other half of Cancel.</summary>
	public void CopyFrom(EditorSettings other) {
		RenderFog = other.RenderFog;
		ShowGrid = other.ShowGrid;
		ShowTriggerAreas = other.ShowTriggerAreas;
		ShowRoutes = other.ShowRoutes;
		ShowMissionBox = other.ShowMissionBox;
		ShowBasePads = other.ShowBasePads;
		WaitingGroups = other.WaitingGroups;
	}
}

/// <summary>The three ways <see cref="EditorSettings.WaitingGroups"/> shows a waiting group's members.</summary>
public enum WaitingGroupDisplay {
	/// <summary>Drawn like any other object.</summary>
	Drawn,

	/// <summary>Not drawn, but boxed, labelled and still selectable.</summary>
	Outlined,

	/// <summary>Neither drawn nor selectable in the viewport; the outliner still lists them.</summary>
	Hidden
}
