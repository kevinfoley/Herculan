namespace Herculan.Engine.Settings;

public enum A11yCategory {
	Motor, Cognitive, Vision, Hearing
}

public interface IA11ySettingDefinition<T> : ISettingDefinition<T> {
	public A11yCategory Category { get; }
}

/// <summary>
/// The schema for one accessibility setting — see <see cref="A11ySettingDefinitions"/> for the full catalog and
/// <see cref="A11ySettings"/> for where the chosen values live. As with <see cref="TweakSettingDefinition{T}"/>, one
/// static instance is shared by every <see cref="A11ySettings"/> instance and doubles as the key its value is stored under.
/// </summary>
public class A11ySettingDefinition<T> : SettingDefinition<T>, IA11ySettingDefinition<T> {
	public A11ySettingDefinition(string iD, A11yCategory category, T defaultValue, T recommendedValue, bool hidden = false) : base(iD, defaultValue, recommendedValue, hidden) {
		Category = category;
	}

	public A11yCategory Category { get; }
}
