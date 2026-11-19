using Herculan.Engine.Settings;
using Herculan.Es1.Render;
using Herculan.Es1.Shell.Menus;
using Herculan.Es1.Sound;

namespace Herculan.Es1.Settings;

/// <summary>
/// Metaltech: Earthsiege's tweak settings, which a host gives <see cref="TweakSettings"/> beside the core's
/// <see cref="TweakSettingDefinitions"/>, kept in <see cref="FileName"/>. Schema only, as the core's catalog is. Each
/// is read through the switch its summary names.
/// </summary>
public static class Es1TweakSettingDefinitions {
	/// <summary>
	/// The file Metaltech: Earthsiege's tweak settings are kept in, apart from Earthsiege 2's
	/// (docs/herculan/planning.md#how-the-two-games-share-code).
	/// </summary>
	public const string FileName = "es1-tweak-settings.json";

	#region COSMETIC
	/// <summary>
	/// Map the textures of the HERCs, flyers and structures with perspective, in place of retail's row walk in screen
	/// space (plan decision 8, docs/herculan/plan-es1-first-mission.md#decisions). See
	/// <see cref="Es1MechModels.PerspectiveTextureMapping"/>.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> PerspectiveTextureMapping = new("tweak.es1_perspective_textures", TweakCategory.Cosmetic, defaultValue: false, recommendedValue: true);

	/// <summary>
	/// The sound every flyer offers its position for plays from the nearest flyer; retail's plays from the first to move
	/// in the frame (plan decision 28, docs/herculan/plan-es1-first-mission.md#decisions). See
	/// <see cref="Es1Sound.NearestOfferWins"/>.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> NearestOfferWins = new("tweak.es1_nearest_flyer_sound", TweakCategory.Cosmetic, defaultValue: false, recommendedValue: true);

	/// <summary>
	/// Placed sounds play in stereo, panned by where each lies across the camera's view. This engine's invention.
	/// <para>Defaults on, an exception to the retail-by-default rule, the user's choice (plan decision 29,
	/// docs/herculan/plan-es1-first-mission.md#decisions). See <see cref="Es1Sound.Stereo"/>.</para>
	/// </summary>
	public static readonly TweakSettingDefinition<bool> Stereo = new("tweak.es1_stereo_sound", TweakCategory.Cosmetic, defaultValue: true, recommendedValue: true);

	/// <summary>
	/// Every LOD switch, of a whole model's root and of each <c>TSDetailPart</c>'s level, lies three times further out than
	/// retail's. This engine's invention. See <see cref="Es1ShapeDetail.RangeFactor"/>.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> ExtendedDetailRange = new("tweak.es1_extended_detail_range", TweakCategory.Cosmetic, defaultValue: false, recommendedValue: true);

	/// <summary>
	/// The return from the external view copies the cockpit's page without retail's column past its edge, which moves the
	/// other page's left-most column by a row, so that column no longer flickers every frame after.
	/// <para>Defaults on, an exception to the retail-by-default rule, the user's choice (plan decision 35,
	/// docs/herculan/plan-es1-first-mission.md#decisions). See <see cref="View.Es1ViewManager.KeepCockpitEdge"/>.</para>
	/// </summary>
	public static readonly TweakSettingDefinition<bool> FixCockpitEdgeFlicker = new("tweak.es1_fix_cockpit_edge_flicker", TweakCategory.Cosmetic, defaultValue: true, recommendedValue: true);

	/// <summary>
	/// CREW's CANCEL puts the pilots' HERCs back before it makes the opening's pilot current again, so the bay selected is
	/// the one the service bay shows and its HERC is not drawn over itself.
	/// <para>Defaults on, an exception to the retail-by-default rule, the user's choice (plan decision 36,
	/// docs/herculan/plan-es1-first-mission.md#decisions). See <see cref="Es1CrewAssignMenu.CancelRestoresFirst"/>.</para>
	/// </summary>
	public static readonly TweakSettingDefinition<bool> FixCrewCancelBay = new("tweak.es1_fix_crew_cancel_bay", TweakCategory.Cosmetic, defaultValue: true, recommendedValue: true);

	/// <summary>
	/// The shell's song fades out behind the next screen rather than holding the shell until it ends (plan decision 37,
	/// docs/herculan/plan-es1-first-mission.md#decisions). See <see cref="Shell.Es1ShellSound.SkipPause"/>.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> SkipScreenPause = new("tweak.es1_skip_screen_pause", TweakCategory.Cosmetic, defaultValue: false, recommendedValue: true);
	#endregion

	#region FUNCTIONAL
	/// <summary>
	/// SYSTEM REPAIR's PROCEED keeps only the levels + and − paid for, where retail's raises the item one level further for
	/// nothing after a + (plan decision 23, docs/herculan/plan-es1-first-mission.md#decisions). See
	/// <see cref="Es1SystemRepairMenu.ProceedKeepsLevels"/>.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> ProceedKeepsLevels = new("tweak.es1_no_free_repair_level", TweakCategory.Functional, defaultValue: false, recommendedValue: true);

	/// <summary>
	/// A gun round's lifetime counts time rather than ticks, so its reach no longer falls with the frame rate. This engine's
	/// invention.
	/// <para>Defaults on, an exception to the retail-by-default rule, the user's choice (plan decision 33,
	/// docs/herculan/plan-es1-first-mission.md#decisions). See <see cref="Projectiles.Es1Bullet.RealTimeLife"/>.</para>
	/// </summary>
	public static readonly TweakSettingDefinition<bool> RealTimeRoundLife = new("tweak.es1_real_time_round_life", TweakCategory.Functional, defaultValue: true, recommendedValue: true);

	/// <summary>
	/// A flyer steps its type's speed along its nose per 40 ms rather than per tick, about Earthsiege 2's flyer speed at any
	/// frame rate. This engine's invention.
	/// <para>Defaults on, an exception to the retail-by-default rule, the user's choice (plan decision 34,
	/// docs/herculan/plan-es1-first-mission.md#decisions). See <see cref="Sim.Es1FlyerFlight.Es2Speed"/>.</para>
	/// </summary>
	public static readonly TweakSettingDefinition<bool> Es2FlyerSpeed = new("tweak.es1_es2_flyer_speed", TweakCategory.Functional, defaultValue: true, recommendedValue: true);
	#endregion

	/// <summary>Every Metaltech: Earthsiege tweak setting, listed after the core's in its file.</summary>
	public static readonly IReadOnlyList<TweakSettingDefinition<bool>> All = new[] {
		PerspectiveTextureMapping, NearestOfferWins, Stereo, ExtendedDetailRange, FixCockpitEdgeFlicker, FixCrewCancelBay, SkipScreenPause, ProceedKeepsLevels,
		RealTimeRoundLife, Es2FlyerSpeed,
	};

	/// <summary>
	/// The tweaks Metaltech: Earthsiege's Tweaks menu offers, those its code reads: of the core's,
	/// <see cref="TweakSettingDefinitions.HighResolutionWidescreen"/> (<see cref="View.Es1ScreenPresenter.Fit"/>),
	/// <see cref="TweakSettingDefinitions.SmootherTurretMovement"/> (<see cref="Sim.Es1Turret.Rounding"/>, plan decision 31),
	/// <see cref="TweakSettingDefinitions.FasterKeyboardAiming"/> (<see cref="Input.Es1KeyboardAxes.Build"/>, plan
	/// decision 32, docs/herculan/plan-es1-first-mission.md#decisions) and
	/// <see cref="TweakSettingDefinitions.MouseOutsideView"/> (<see cref="View.Es1MouseOrbit"/>), then all of its own
	/// (docs/herculan/planning.md#how-the-two-games-share-code).
	/// </summary>
	public static readonly IReadOnlyList<TweakSettingDefinition<bool>> Offered = new[] {
		TweakSettingDefinitions.HighResolutionWidescreen, TweakSettingDefinitions.SmootherTurretMovement,
		TweakSettingDefinitions.FasterKeyboardAiming, TweakSettingDefinitions.MouseOutsideView,
		PerspectiveTextureMapping, NearestOfferWins, Stereo, ExtendedDetailRange, FixCockpitEdgeFlicker, FixCrewCancelBay, SkipScreenPause, ProceedKeepsLevels,
		RealTimeRoundLife, Es2FlyerSpeed,
	};
}
