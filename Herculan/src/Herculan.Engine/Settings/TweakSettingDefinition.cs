namespace Herculan.Engine.Settings;

public enum TweakCategory {
	Cosmetic, AI, Functional, Input
}

public interface ITweakSettingDefinition<T> : ISettingDefinition<T> {
	public TweakCategory Category { get; }
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
