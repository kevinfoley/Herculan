using Herculan.Engine.Platform;

namespace Herculan.Engine.Settings;

/// <summary>
/// The core's catalog of accessibility settings, those every game's host can offer: one static, shared
/// <see cref="A11ySettingDefinition{T}"/> per setting. This is schema only — the values a player has chosen live in an
/// <see cref="A11ySettings"/> instance, keyed by the definition. Every one is HERCULAN's own, with no retail
/// counterpart, and defaults off. A game's own settings are a catalog of its own, which its host hands
/// <see cref="A11ySettings"/>.
/// </summary>
public static class A11ySettingDefinitions {
	#region VISION
	/// <summary>
	/// Draw every ImGui window's text at <see cref="ScaledImGui.LargerFontSize"/> instead of
	/// <see cref="ScaledImGui.FontSize"/>. See <see cref="ScaledImGui"/>.
	/// </summary>
	public static readonly A11ySettingDefinition<bool> LargerFont = new("a11y.larger_font", A11yCategory.Vision, defaultValue: false, recommendedValue: false);
	#endregion

	/// <summary>Every core accessibility setting, listed before a game's own in the Accessibility menu.</summary>
	public static readonly IReadOnlyList<A11ySettingDefinition<bool>> All = new[] {
		LargerFont,
	};
}
