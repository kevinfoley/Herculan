namespace Herculan.Engine.Settings;

/// <summary>
/// The player's chosen tweak-setting values — see <see cref="TweakSettingDefinitions"/> for the core's settings and
/// their definitions — kept in <c>tweak-settings.json</c>. Nothing outside the host's Tweaks menu writes to them at
/// runtime, which is what lets its Cancel discard edits by reloading the file.
/// </summary>
public sealed class TweakSettings : SettingsFile<TweakSettingDefinition<bool>> {

	private static TweakSettings? current;

	/// <summary>
	/// The player's settings, which the hosts load from <see cref="SettingsFile{TDefinition}.FilePath"/> and the menus
	/// edit. A host sets it once at startup, with the games' catalogs; until then it holds the core's alone.
	/// </summary>
	public static TweakSettings Current {
		get => current ??= new TweakSettings();
		set => current = value;
	}

	/// <summary>The core's settings alone, each at its default.</summary>
	public TweakSettings() : this([]) {
	}

	/// <summary>The core's settings and then <paramref name="gameSettings"/>, each at its default.</summary>
	public TweakSettings(IReadOnlyList<TweakSettingDefinition<bool>> gameSettings)
		: base([.. TweakSettingDefinitions.All, .. gameSettings], "tweak-settings.json") {
	}
}
