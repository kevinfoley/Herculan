using Herculan.Engine.Settings;
using Herculan.Engine;
using Herculan.Es2.View;

namespace Herculan.Es2.Settings;

/// <summary>
/// Earthsiege 2's accessibility settings, which a host gives <see cref="A11ySettings"/> beside the core's
/// <see cref="A11ySettingDefinitions"/>. Schema only, as the core's catalog is, and like it HERCULAN's own and off by
/// default.
/// </summary>
public static class Es2A11ySettingDefinitions {
	#region VISION
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

	/// <summary>Every Earthsiege 2 accessibility setting, listed after the core's in the Accessibility menu.</summary>
	public static readonly IReadOnlyList<A11ySettingDefinition<bool>> All = new[] {
		DisableDamageFlash, DisableDamageShake,
	};
}
