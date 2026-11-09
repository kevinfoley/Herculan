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
	#endregion

	#region FUNCTIONAL
	/// <summary>
	/// SYSTEM REPAIR's PROCEED keeps only the levels + and − paid for, where retail's raises the item one level further for
	/// nothing after a + (plan decision 23, docs/herculan/plan-es1-first-mission.md#decisions). See
	/// <see cref="Es1SystemRepairMenu.ProceedKeepsLevels"/>.
	/// </summary>
	public static readonly TweakSettingDefinition<bool> ProceedKeepsLevels = new("tweak.es1_no_free_repair_level", TweakCategory.Functional, defaultValue: false, recommendedValue: true);
	#endregion

	/// <summary>Every Metaltech: Earthsiege tweak setting, listed after the core's in its file.</summary>
	public static readonly IReadOnlyList<TweakSettingDefinition<bool>> All = new[] {
		PerspectiveTextureMapping, NearestOfferWins, Stereo, ProceedKeepsLevels,
	};

	/// <summary>
	/// The tweaks Metaltech: Earthsiege's Tweaks menu offers, those its code reads: of the core's,
	/// <see cref="TweakSettingDefinitions.HighResolutionWidescreen"/> (<see cref="View.Es1Monitor.Fit"/>), then all of
	/// its own (docs/herculan/planning.md#how-the-two-games-share-code).
	/// </summary>
	public static readonly IReadOnlyList<TweakSettingDefinition<bool>> Offered = new[] {
		TweakSettingDefinitions.HighResolutionWidescreen,
		PerspectiveTextureMapping, NearestOfferWins, Stereo, ProceedKeepsLevels,
	};
}
