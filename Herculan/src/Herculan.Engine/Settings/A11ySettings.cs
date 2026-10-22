namespace Herculan.Engine.Settings;

/// <summary>
/// The player's chosen accessibility-setting values — see <see cref="A11ySettingDefinitions"/> for the settings
/// themselves — kept in <c>a11y-settings.json</c>. Nothing outside <see cref="AccessibilityMenu"/> writes to them at
/// runtime, which is what lets its Cancel discard edits by reloading the file.
/// </summary>
public sealed class A11ySettings : SettingsFile<A11ySettingDefinition<bool>> {

	private static A11ySettings? current;

	/// <summary>The player's settings, which the hosts load from <see cref="SettingsFile{TDefinition}.FilePath"/> and the menus edit.</summary>
	public static A11ySettings Current => current ??= new A11ySettings();

	/// <summary>Every setting at its default.</summary>
	public A11ySettings() : base(A11ySettingDefinitions.All, "a11y-settings.json") {
	}
}
