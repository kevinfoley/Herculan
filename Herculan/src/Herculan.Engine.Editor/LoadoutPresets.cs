using System.Text.Json;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;

namespace Herculan.Engine.Editor;

/// <summary>
/// The editor's loadout presets, each a named weapon fit for one chassis (docs/herculan/handoff-editor-roster.md#loadout-presets):
/// the editor's own data, not a mission's, and this engine's invention, which no retail screen has. Kept in
/// <c>loadout-presets.json</c> in <see cref="UserConfig.Folder"/>, read once per editor run (<see cref="Load"/>) and written
/// whole on every save, overwrite or delete. A preset belongs to a <c>MECHS.NAM</c> type, which the squad's chassis 0 to 8 share,
/// and its name is trimmed and unique within that type ignoring case.
/// </summary>
internal sealed class LoadoutPresets {
	private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

	// The file's shape: the presets in the order they were saved.
	private sealed class PresetFile {
		public List<LoadoutPreset?>? Presets { get; set; }
	}

	private readonly string? _path;
	private readonly List<LoadoutPreset> _presets;

	/// <summary>No presets, kept in memory only: what a test that opens a mission without an editor run has.</summary>
	public LoadoutPresets() : this(null, []) {
	}

	private LoadoutPresets(string? path, List<LoadoutPreset> presets) {
		_path = path;
		_presets = presets;
	}

	/// <summary>Where the presets live: <c>loadout-presets.json</c> in <see cref="UserConfig.Folder"/>.</summary>
	public static string FilePath => UserConfig.PathOf("loadout-presets.json");

	/// <summary>
	/// Reads the presets file: none when it is absent; none, reported, when it does not parse, the next save replacing it; and
	/// a preset with no name, no slot, a slot past <see cref="RosterFit.Slots"/> or twice, or a name its chassis already has,
	/// left out and counted in a report.
	/// </summary>
	public static LoadoutPresets Load() {
		string path = FilePath;
		if (!File.Exists(path)) {
			return new LoadoutPresets(path, []);
		}

		try {
			var file = JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(path), SerializerOptions);
			return new LoadoutPresets(path, Valid(file?.Presets ?? [], path));
		} catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not read {path} ({ex.Message}); no loadout presets loaded.");
			return new LoadoutPresets(path, []);
		}
	}

	/// <summary>The presets of <c>MECHS.NAM</c> type <paramref name="chassis"/>, by name.</summary>
	public IReadOnlyList<LoadoutPreset> For(int chassis) =>
		_presets.Where(preset => preset.Chassis == chassis).OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToList();

	/// <summary>The preset of <paramref name="chassis"/> named <paramref name="name"/>, trimmed and ignoring case, or null.</summary>
	public LoadoutPreset? Find(int chassis, string name) {
		string trimmed = name.Trim();
		return _presets.FirstOrDefault(preset => preset.Chassis == chassis && SameName(preset.Name, trimmed));
	}

	/// <summary>
	/// Saves <paramref name="slots"/> as <paramref name="chassis"/>'s preset <paramref name="name"/>, trimmed, in place of one
	/// of that name, and writes the file.
	/// </summary>
	public void Save(int chassis, string name, IReadOnlyList<LoadoutPresetSlot> slots) {
		string trimmed = name.Trim();
		if (trimmed.Length == 0) {
			throw new ArgumentException("A preset needs a name.", nameof(name));
		}

		_presets.RemoveAll(preset => preset.Chassis == chassis && SameName(preset.Name, trimmed));
		_presets.Add(new LoadoutPreset(chassis, trimmed, slots.ToList()));
		Write();
	}

	/// <summary>Deletes <paramref name="chassis"/>'s preset <paramref name="name"/>, ignoring case, and writes the file.</summary>
	public void Delete(int chassis, string name) {
		string trimmed = name.Trim();
		if (_presets.RemoveAll(preset => preset.Chassis == chassis && SameName(preset.Name, trimmed)) > 0) {
			Write();
		}
	}

	private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	private void Write() {
		if (_path != null) {
			UserConfig.Write(_path, JsonSerializer.Serialize(new PresetFile { Presets = [.. _presets] }, SerializerOptions));
		}
	}

	private static List<LoadoutPreset> Valid(IEnumerable<LoadoutPreset?> read, string path) {
		var kept = new List<LoadoutPreset>();
		int dropped = 0;
		foreach (var preset in read) {
			string name = preset?.Name?.Trim() ?? "";
			var slots = preset?.Slots;
			if (preset == null || name.Length == 0 || slots == null || slots.Count == 0
					|| slots.Any(slot => slot.Slot is < 0 or >= RosterFit.Slots)
					|| slots.Select(slot => slot.Slot).Distinct().Count() != slots.Count
					|| kept.Any(other => other.Chassis == preset.Chassis && SameName(other.Name, name))) {
				dropped++;
				continue;
			}

			kept.Add(preset with { Name = name });
		}

		if (dropped > 0) {
			Console.Error.WriteLine($"Left out {dropped} loadout preset{(dropped == 1 ? "" : "s")} of {path} with no name, no "
				+ "usable slots or a name its chassis already has.");
		}

		return kept;
	}
}

/// <summary>
/// A loadout preset: <c>MECHS.NAM</c> type <see cref="Chassis"/>'s fit named <see cref="Name"/>, its weapon and ammunition
/// type in each fit slot the HERC's view edited when it was saved (<see cref="LoadoutPresets"/>).
/// </summary>
internal sealed record LoadoutPreset(int Chassis, string Name, IReadOnlyList<LoadoutPresetSlot> Slots);

/// <summary>
/// Fit slot <see cref="Slot"/>, from 0 (shown as "Hardpoint" <see cref="Slot"/> + 1), holding weapon id <see cref="Weapon"/>
/// with ammunition type <see cref="Ammo"/>, as row 12 carries them.
/// </summary>
internal readonly record struct LoadoutPresetSlot(int Slot, short Weapon, short Ammo);
