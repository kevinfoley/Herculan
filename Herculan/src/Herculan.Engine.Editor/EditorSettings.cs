using System.Text.Json;
using System.Text.Json.Serialization;
using Herculan.Engine.Platform;
using Silk.NET.Maths;

namespace Herculan.Engine.Editor;

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
	/// Where the editor's window was when the editor last closed on its own (not from the game, which hands it the
	/// game window's place), for it to open at again. Null until then, when it opens maximized.
	/// </summary>
	public SavedWindow? Window { get; set; }

	/// <summary>Where the settings live: <c>editor-settings.json</c> in <see cref="UserConfig.Folder"/>.</summary>
	public static string FilePath => UserConfig.PathOf("editor-settings.json");

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

	/// <summary>Writes the settings (<see cref="UserConfig.Write"/>).</summary>
	public void Save() => UserConfig.Write(FilePath, JsonSerializer.Serialize(this, SerializerOptions));

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

/// <summary><see cref="WindowPlacement"/> in a shape the settings file holds.</summary>
public sealed class SavedWindow {
	/// <inheritdoc cref="WindowPlacement.Position"/>
	public int? X { get; set; }

	/// <inheritdoc cref="WindowPlacement.Position"/>
	public int? Y { get; set; }

	/// <inheritdoc cref="WindowPlacement.Size"/>
	public int Width { get; set; }

	/// <inheritdoc cref="WindowPlacement.Size"/>
	public int Height { get; set; }

	/// <inheritdoc cref="WindowPlacement.Maximized"/>
	public bool Maximized { get; set; }

	public static SavedWindow From(WindowPlacement placement) => new() {
		X = placement.Position?.X,
		Y = placement.Position?.Y,
		Width = placement.Size.X,
		Height = placement.Size.Y,
		Maximized = placement.Maximized,
	};

	/// <summary>The placement, or null when the file's size is not a usable one.</summary>
	public WindowPlacement? ToPlacement() => Width > 0 && Height > 0
		? new WindowPlacement(X is { } x && Y is { } y ? new Vector2D<int>(x, y) : null, new Vector2D<int>(Width, Height),
			Maximized)
		: null;
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
