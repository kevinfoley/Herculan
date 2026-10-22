using Herculan.Engine.Platform;
using Herculan.Engine.View;

namespace Herculan.Engine.Settings;

/// <summary>
/// The catalog of accessibility settings: one static, shared <see cref="A11ySettingDefinition{T}"/> per setting. This
/// is schema only — the values a player has chosen live in an <see cref="A11ySettings"/> instance, keyed by the
/// definition. Every one is HERCULAN's own, with no retail counterpart, and defaults off.
/// </summary>
public static class A11ySettingDefinitions {
	#region VISION
	/// <summary>
	/// Draw every ImGui window's text at <see cref="ScaledImGui.LargerFontSize"/> instead of
	/// <see cref="ScaledImGui.FontSize"/>. See <see cref="ScaledImGui"/>.
	/// </summary>
	public static readonly A11ySettingDefinition<bool> LargerFont = new("a11y.larger_font", A11yCategory.Vision, defaultValue: false, recommendedValue: false);

	/// <summary>
	/// Keep the theater's palette up through a hit on the cockpit, where the damage shake would flash the impact palette;
	/// the shake itself still runs. See <see cref="CockpitHitShake.FlashHidden"/>.
	/// </summary>
	public static readonly A11ySettingDefinition<bool> DisableDamageFlash = new("a11y.disable_damage_flash", A11yCategory.Vision, defaultValue: false, recommendedValue: false);

	/// <summary>
	/// Hold the cockpit still through a hit on it, where the damage shake would walk the display window; the palette
	/// flash still runs. See <see cref="CockpitHitShake.ShakeHidden"/>.
	/// </summary>
	public static readonly A11ySettingDefinition<bool> DisableDamageShake = new("a11y.disable_damage_shake", A11yCategory.Vision, defaultValue: false, recommendedValue: false);
	#endregion

	/// <summary>Every defined <c>bool</c> accessibility setting, keyed by ID for <see cref="A11ySettings"/> save/load.</summary>
	public static readonly IReadOnlyList<A11ySettingDefinition<bool>> All = new[] {
		LargerFont, DisableDamageFlash, DisableDamageShake,
	};
}
