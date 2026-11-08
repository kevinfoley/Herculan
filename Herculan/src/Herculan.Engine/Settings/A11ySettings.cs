namespace Herculan.Engine.Settings;

/// <summary>
/// The player's chosen accessibility-setting values — see <see cref="A11ySettingDefinitions"/> for the core's settings
/// and their definitions — kept in <c>a11y-settings.json</c>. Nothing outside the host's Accessibility menu writes to
/// them at runtime, which is what lets its Cancel discard edits by reloading the file.
/// </summary>
public sealed class A11ySettings : SettingsFile<A11ySettingDefinition<bool>> {

	private static A11ySettings? current;

	/// <summary>
	/// The player's settings, which the hosts load from <see cref="SettingsFile{TDefinition}.FilePath"/> and the menus
	/// edit. A host sets it once at startup, with the games' catalogs; until then it holds the core's alone.
	/// </summary>
	public static A11ySettings Current {
		get => current ??= new A11ySettings();
		set => current = value;
	}

	/// <summary>The core's settings alone, each at its default.</summary>
	public A11ySettings() : this([]) {
	}

	/// <summary>The core's settings and then <paramref name="gameSettings"/>, each at its default.</summary>
	public A11ySettings(IReadOnlyList<A11ySettingDefinition<bool>> gameSettings)
		: base([.. A11ySettingDefinitions.All, .. gameSettings], "a11y-settings.json") {
	}
}
