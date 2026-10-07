namespace Herculan.Engine.Settings;

public interface ISettingDefinition<T> {
	public string ID { get; }
	public string DisplayNameKey { get; }
	public string DescriptionKey { get; }
	public T DefaultValue { get; }
	public T RecommendedValue { get; }
	public bool Hidden { get; }
}

public enum TweakCategory {
	Cosmetic, AI, Functional, Input
}

public interface ITweakSettingDefinition<T> : ISettingDefinition<T> {
	public TweakCategory Category { get; }
}

public class SettingDefinition<T> : ISettingDefinition<T> {
	public SettingDefinition(string id, T defaultValue, T recommendedValue, bool hidden = false) {
		ID = id;
		DisplayNameKey = $"{id}.display_name";
		DescriptionKey = $"{id}.description";
		DefaultValue = defaultValue;
		RecommendedValue = recommendedValue;
		Hidden = hidden;
	}

	/// <summary>
	/// Unique identifier, used for saving and loading the setting, and
	/// also as a prefix for localization keys.
	/// </summary>
	public string ID { get; }

	/// <summary>
	/// Key for the display name in localized strings.
	/// </summary>
	public string DisplayNameKey { get; }

	/// <summary>
	/// Key for the description in localized strings.
	/// </summary>
	public string DescriptionKey { get; }

	public T DefaultValue { get; }

	/// <summary>
	/// The value the settings UI's "Recommended" preset applies. Independent of
	/// <see cref="DefaultValue"/>, which follows the retail-by-default rule.
	/// </summary>
	public T RecommendedValue { get; }

	/// <summary>
	/// If true, this setting is not rendered in the settings UI. Use for
	/// future settings that have not been implemented in code yet.
	/// </summary>
	public bool Hidden { get; }
}

/// <summary>
/// The schema for one tweak setting — see <see cref="TweakSettingDefinitions"/> for the
/// full catalog and <see cref="TweakSettings"/> for where the chosen values live. A definition
/// carries no state of its own beyond its identity and its default and recommended values, so a single static instance is shared
/// by every <see cref="TweakSettings"/> instance and doubles as the dictionary key those
/// instances store their value under.
/// </summary>
public class TweakSettingDefinition<T> : SettingDefinition<T>, ITweakSettingDefinition<T> {
	public TweakSettingDefinition(string iD, TweakCategory category, T defaultValue, T recommendedValue, bool hidden = false) : base(iD, defaultValue, recommendedValue, hidden) {
		Category = category;
	}

	public TweakCategory Category { get; }
}
