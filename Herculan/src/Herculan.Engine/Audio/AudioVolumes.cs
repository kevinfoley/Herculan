using System.Text.Json;

namespace Herculan.Engine.Audio;

/// <summary>
/// The player's music and sound-effects volumes, each a gain from 0 to 1 laid over the gain a sound already has. They are
/// this engine's: retail has no volume control, and the master volume of each of its sound managers stays on the 100 its
/// image holds (docs/retail/simulation/audio.md#backend-volume-and-panning, docs/retail/shell/movies-and-sound.md#sound), which both volumes at 1
/// reproduce. The MUSIC and SOUNDS preferences still switch each off as they do in retail.
///
/// <para>Music is the shell's music and a mission's CD tracks; effects are every other sound, speech included. A movie's
/// soundtrack is neither, as it is not the sound manager's in retail either. CD music that MCI plays straight off a
/// drive is the drive's own and is not scaled.</para>
/// </summary>
public sealed class AudioVolumes {
	private static AudioVolumes? current;

	private float _music = 1f;
	private float _effects = 1f;

	/// <summary>The player's volumes, read from <see cref="FilePath"/> on first use and edited by the Settings window.</summary>
	public static AudioVolumes Current => current ??= Load();

	/// <summary>
	/// Where the volumes live: <c>%APPDATA%\Herculan\volume.json</c> on Windows, and the platform's equivalent
	/// user-config directory elsewhere.
	/// </summary>
	public static string FilePath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
			Environment.SpecialFolderOption.DoNotVerify),
		"Herculan", "volume.json");

	/// <summary>The music's gain, 0 to 1.</summary>
	public float Music {
		get => _music;
		set => _music = Clamp(value);
	}

	/// <summary>Every other sound's gain, 0 to 1.</summary>
	public float Effects {
		get => _effects;
		set => _effects = Clamp(value);
	}

	/// <summary>Writes both volumes, creating the directory if this is the first save. A failed write is logged.</summary>
	public void Save() {
		string path = FilePath;
		try {
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(new Saved(Music, Effects)));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not write {path}: {ex.Message}");
		}
	}

	// Both at full when the file is absent or unreadable: a corrupt or hand-edited file is not worth failing a launch over.
	private static AudioVolumes Load() {
		var volumes = new AudioVolumes();
		string path = FilePath;
		if (!File.Exists(path)) {
			return volumes;
		}

		try {
			if (JsonSerializer.Deserialize<Saved>(File.ReadAllText(path)) is { } saved) {
				volumes.Music = saved.Music;
				volumes.Effects = saved.Effects;
			}
		} catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); using full volume.");
		}

		return volumes;
	}

	private static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 1f;

	// A volume the file leaves out reads as full.
	private sealed class Saved {
		public Saved() {
		}

		public Saved(float music, float effects) {
			Music = music;
			Effects = effects;
		}

		public float Music { get; set; } = 1f;

		public float Effects { get; set; } = 1f;
	}
}
