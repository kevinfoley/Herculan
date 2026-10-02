namespace Herculan.Engine.Settings;

/// <summary>
/// The catalog of tweak settings: one static, shared <see cref="TweakSettingDefinition{T}"/>
/// per setting. This is schema only (ID, category, default, hidden) — the values a player has chosen
/// live in a <see cref="TweakSettings"/> instance instead, keyed by the definition.
/// </summary>
public static class TweakSettingDefinitions {
	#region COSMETIC
	/// <summary>
	/// Fix issues where HERC or weapon stats were displayed in the UI with incorrect
	/// values in vanilla.
	/// <para>CATEGORY: Cosmetic</para>
	/// <para>Changes:</para>
	/// <list type="bullet">
	/// <item><description>Display correct speed for Outlaw in VSHELL (100 kph, not 80 kph)</description></item>
	/// <item><description>Display correct hardpoints for Raptor II in VSHELL (5, not 4)</description></item>
	/// </list>
	///
	/// </summary>
	public static readonly TweakSettingDefinition<bool> ShowCorrectStats = new("tweak.show_correct_stats", TweakCategory.Cosmetic, false);

	/// <summary>
	/// Show accurate movement speed on HUD. Retail shows an inaccurate reading when the
	/// player's HERC is in walking stride.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> ShowAccurateSpeed = new("tweak.show_accurate_speed", TweakCategory.Cosmetic, false);

	/// <summary>
	/// On the MFD, show target distance in meters on the MFD F5 TARGET screen. Retail
	/// shows distance in engine units on this screen only.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> ShowTargetDistanceInMeters = new("tweak.target_distance_meters", TweakCategory.Cosmetic, false);

	/// <summary>
	/// A hit on the right nacelle of the Razor will draw the impact effect at the
	/// correct side instead of at the left nacelle.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> FixNacelleImpactEffectPosition = new("tweak.fix_nacelle_impact_position", TweakCategory.Cosmetic, false, true);

	/// <summary>
	/// Fix new sound effects overwriting the volume and position of previous sound
	/// effects of the same type.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> PreserveSoundPosition = new("tweak.preserve_sound_position", TweakCategory.Cosmetic, false, true);

	/// <summary>
	/// When the player takes critical damage, play "Damage Level Critical". Does not
	/// work in retail due to a typo in the code.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> CriticalDamageMessage = new("tweak.critical_damage_message", TweakCategory.Cosmetic, false);

	/// <summary>
	/// On the Heads-Down Display, number each occupied squad comm box with the key that selects that
	/// squadmate. Retail builds a label for it in every box but never gives it any text, so it never
	/// appears.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> ShowSquadmateNumber = new("tweak.show_squadmate_number", TweakCategory.Cosmetic, false);

	/// <summary>
	/// Cockpit computer voiceover is controlled by the COMPUTER MESSAGE preference
	/// instead of the PILOT MESSAGE preference.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> FixComputerMessagePreference = new("tweak.fix_computer_message_pref", TweakCategory.Cosmetic, false, true);

	/// <summary>
	/// A second hit on the cockpit inside the damage shake keeps the palette flash going for the rest
	/// of the shake. Retail stops the flash when the second hit lands while the impact palette is
	/// showing. See <see cref="Render.CockpitHitShake.FlashSurvivesRestart"/>.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> FlashThroughSecondHit = new("tweak.flash_through_second_hit", TweakCategory.Cosmetic, false);
	#endregion

	#region FUNCTIONAL
	/// <summary>
	/// Enable smoother turret movement when aiming with a joystick or other analog
	/// input. Retail rounds the turret rotation values, causing noticeable stutter
	/// when aiming at slow speeds. Irrelevant if controlling the turret with a
	/// keyboard.
	/// <para>Defaults on, an exception to the retail-by-default rule. See <see cref="Sim.Anim.AnimationThread.SeekToPosition"/>.</para>
	/// </summary>
	public static readonly TweakSettingDefinition<bool> SmootherTurretMovement = new("tweak.smoother_turret_movement", TweakCategory.Functional, true);

	/// <summary>
	/// Drive the outside view with the mouse instead of the controls: drag with the left button to
	/// swing the camera round the HERC, which stays under the player's control throughout, and the
	/// cockpit's keys keep working. Retail hands the stick and arrow keys to the camera and makes
	/// [Enter] swap them back. See <see cref="Render.ExternalCamera"/>.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> MouseExternalView = new("tweak.mouse_external_view", TweakCategory.Functional, false);

	/// <summary>
	/// Build each order the Heads-Down Display transmits from scratch. Retail reuses one order record
	/// and overwrites only the half the new order picks, so DEFEND POSITION on bare ground can guard a
	/// unit an earlier order named instead. See <see cref="Content.HddCommandScreen.Transmit"/>.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> FixDefendPositionOrder = new("tweak.fix_defend_position_order", TweakCategory.Functional, false);

	/// <summary>
	/// Each weapon's shots apply its own <c>PROJ.DAT</c> record. Retail applies the first record that
	/// shares its type and subtype id, so <c>ATC75</c> and <c>ATC100</c> hit like <c>ATC35</c> and
	/// <c>ATC50</c>, <c>LAS500</c> like <c>LAS300</c>, and <c>LAS400</c> like <c>LAS200</c> at a
	/// higher power. The four also take their own <c>BULLETS.DAT</c> and <c>BEAM.DAT</c> records, so
	/// <c>ATC75</c> and <c>ATC100</c> rounds expire sooner and the two lasers draw wider.
	/// <para>Defaults on, an exception to the retail-by-default rule. See <see cref="Sim.WeaponMount.ShotProjectile"/>.</para>
	/// </summary>
	public static readonly TweakSettingDefinition<bool> FixWeaponDamageRecords = new("tweak.fix_weapon_damage_records", TweakCategory.Functional, true);

	/// <summary>
	/// Click or drag an energy weapon row's charge bar to set that weapon's power level, as the
	/// <c>[-]</c>/<c>[=]</c> keys do. Retail builds the bar as a slider whose position the mount reads back as its charge
	/// target, but the row's select gadget is registered first over the whole row and takes every
	/// press, so the bar can never be reached. See <see cref="Content.ChargeBarSlider"/>.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> ChargeBarPowerLevel = new("tweak.charge_bar_power_level", TweakCategory.Functional, false);

	#endregion

	/// <summary>Every defined <c>bool</c> tweak setting, keyed by ID for <see cref="TweakSettings"/> save/load.</summary>
	public static readonly IReadOnlyList<TweakSettingDefinition<bool>> All = new[] {
		ShowCorrectStats, ShowAccurateSpeed, ShowTargetDistanceInMeters, FixNacelleImpactEffectPosition,
		PreserveSoundPosition, CriticalDamageMessage, ShowSquadmateNumber, FixComputerMessagePreference,
		SmootherTurretMovement, MouseExternalView, FixDefendPositionOrder, FixWeaponDamageRecords,
		ChargeBarPowerLevel, FlashThroughSecondHit,
	};
}
