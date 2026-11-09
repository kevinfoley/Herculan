namespace Herculan.Engine.Settings;

/// <summary>
/// The player's chosen tweak-setting values — see <see cref="TweakSettingDefinitions"/> for the core's settings and
/// their definitions — kept in a file of the running game's own (docs/herculan/planning.md#how-the-two-games-share-code),
/// so a core tweak is set separately in each game. Nothing outside the host's Tweaks menu writes to them at runtime,
/// which is what lets its Cancel discard edits by reloading the file.
/// </summary>
public sealed class TweakSettings : SettingsFile<TweakSettingDefinition<bool>> {

	private static TweakSettings? current;

	/// <summary>
	/// The player's settings, which the hosts load from <see cref="SettingsFile{TDefinition}.FilePath"/> and the menus
	/// edit. A host sets it once at startup, with the running game's catalog; until then it holds the core's alone.
	/// </summary>
	public static TweakSettings Current {
		get => current ??= new TweakSettings();
		set => current = value;
	}

	/// <summary>The core's settings alone, each at its default, kept in <c>tweak-settings.json</c>.</summary>
	public TweakSettings() : this([], "tweak-settings.json") {
	}

	/// <summary>
	/// The core's settings and then <paramref name="gameSettings"/>, each at its default, kept in
	/// <paramref name="fileName"/> in <see cref="Platform.UserConfig.Folder"/>.
	/// </summary>
	public TweakSettings(IReadOnlyList<TweakSettingDefinition<bool>> gameSettings, string fileName)
		: base([.. TweakSettingDefinitions.All, .. gameSettings], fileName) {
	}
}
