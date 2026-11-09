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

	#region FUNCTIONAL
	/// <summary>
	/// A seek of a turret's animation thread keeps the sub-tick remainder retail's scale-down drops, so a turret aimed
	/// slowly, as with a joystick or other analog input, moves every tick rather than in whole animation ticks. Each game
	/// reads it where it seeks a turret's thread.
	/// <para>Defaults on, an exception to the retail-by-default rule. See <see cref="Anim.SeekRounding.KeepRemainder"/>.</para>
	/// </summary>
	public static readonly TweakSettingDefinition<bool> SmootherTurretMovement = new("tweak.smoother_turret_movement", TweakCategory.Functional, defaultValue: true, recommendedValue: true);

	/// <summary>
	/// A held key aims the turret as fast as a stick pushed all the way. Retail's key is worth less than a full stick:
	/// half of one in Earthsiege 2, and a quarter in Metaltech: Earthsiege unless <c>Alt</c> is held. Each game reads it
	/// where it builds its keyboard axes.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> FasterKeyboardAiming = new("tweak.faster_keyboard_aiming", TweakCategory.Functional, defaultValue: false, recommendedValue: false);
	#endregion

	/// <summary>Every core tweak setting, listed before a game's own in the Tweaks menu.</summary>
	public static readonly IReadOnlyList<TweakSettingDefinition<bool>> All = new[] {
		HighResolutionWidescreen, SmootherTurretMovement, FasterKeyboardAiming,
	};
}
