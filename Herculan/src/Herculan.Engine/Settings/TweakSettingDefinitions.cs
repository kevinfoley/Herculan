namespace Herculan.Engine.Settings;

/// <summary>
/// The core's catalog of tweak settings, those every game's host honors: one static, shared
/// <see cref="TweakSettingDefinition{T}"/> per setting. This is schema only (ID, category, default, recommended,
/// hidden) — the values a player has chosen live in a <see cref="TweakSettings"/> instance instead, keyed by the
/// definition. A game's own tweaks are a catalog of its own, which its host hands <see cref="TweakSettings"/>.
/// </summary>
public static class TweakSettingDefinitions {
	#region COSMETIC
	/// <summary>
	/// Draw a mission at the window's own resolution and shape. Off, it is drawn at the game's own resolution and the
	/// frame is scaled to fit the window, centred, so a window wider than the game's shape gets bars at its sides;
	/// HERCULAN's own ImGui windows stay at the window's resolution either way.
	/// <para>Defaults on, an exception to the retail-by-default rule. See <see cref="Render.FixedResolutionTarget"/>.</para>
	/// </summary>
	public static readonly TweakSettingDefinition<bool> HighResolutionWidescreen = new("tweak.high_resolution_widescreen", TweakCategory.Cosmetic, defaultValue: true, recommendedValue: true);
	#endregion

	/// <summary>Every core tweak setting, listed before a game's own in the Tweaks menu.</summary>
	public static readonly IReadOnlyList<TweakSettingDefinition<bool>> All = new[] {
		HighResolutionWidescreen,
	};
}
