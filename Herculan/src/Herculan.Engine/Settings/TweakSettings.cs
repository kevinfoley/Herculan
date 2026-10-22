namespace Herculan.Engine.Settings;

/// <summary>
/// The player's chosen tweak-setting values — see <see cref="TweakSettingDefinitions"/> for the settings themselves —
/// kept in <c>tweak-settings.json</c>. Nothing outside <see cref="TweaksMenu"/> writes to them at runtime, which is
/// what lets its Cancel discard edits by reloading the file.
/// </summary>
public sealed class TweakSettings : SettingsFile<TweakSettingDefinition<bool>> {

	private static TweakSettings? current;

	/// <summary>The player's settings, which the hosts load from <see cref="SettingsFile{TDefinition}.FilePath"/> and the menus edit.</summary>
	public static TweakSettings Current => current ??= new TweakSettings();

	/// <summary>Every setting at its default.</summary>
	public TweakSettings() : base(TweakSettingDefinitions.All, "tweak-settings.json") {
	}
}
