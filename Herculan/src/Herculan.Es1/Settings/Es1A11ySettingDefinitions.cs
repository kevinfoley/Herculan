using Herculan.Engine.Settings;
using Herculan.Es1.Host;

namespace Herculan.Es1.Settings;

/// <summary>
/// Metaltech: Earthsiege's accessibility settings, which a host gives <see cref="A11ySettings"/> beside the core's
/// <see cref="A11ySettingDefinitions"/>. Schema only, as the core's catalog is, and like it HERCULAN's own and off by
/// default.
/// </summary>
public static class Es1A11ySettingDefinitions {
	#region VISION
	/// <summary>
	/// Hold the screen still through the jolt at the top of the lift start, where the view shake would move it; the jolt's
	/// sound still plays. <see cref="A11ySettingDefinitions.DisableDamageShake"/> covers hits alone. See
	/// <see cref="Es1LiftStart.RunRise"/>.
	/// </summary>
	public static readonly A11ySettingDefinition<bool> DisableLiftJolt = new("a11y.disable_lift_jolt", A11yCategory.Vision, defaultValue: false, recommendedValue: false);
	#endregion

	/// <summary>Every Metaltech: Earthsiege accessibility setting, listed after the core's in the Accessibility menu.</summary>
	public static readonly IReadOnlyList<A11ySettingDefinition<bool>> All = new[] {
		DisableLiftJolt,
	};
}
