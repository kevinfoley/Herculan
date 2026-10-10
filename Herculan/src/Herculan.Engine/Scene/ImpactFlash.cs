using Herculan.Engine.Render;

namespace Herculan.Engine.Scene;

/// <summary>
/// The scene as it is drawn while the cockpit damage flash is up — the theater's own resources
/// rebuilt against its <c>IMPACT&lt;n&gt;.DPL</c> instead of its ordinary palette.
///
/// <para><b>Only the palette differs.</b> The original's flash swaps the palette <i>object</i>
/// (<c>Palette_ActivateImpact</c>, <c>0042ea44</c>) and nothing else, and retail ships no
/// <c>IMPACT&lt;n&gt;.RMP</c> — so the shade ramp, the depth slices and every distance in
/// <see cref="Atmosphere"/> are the theater's, and these tables are the same size as the ones they
/// stand in for. That is what lets the renderer swap between them without re-deriving a single
/// uniform. Retail's impact palettes are their theater's run through a heavy red shift, so what the
/// player sees is the world going red for a fraction of a second.</para>
///
/// <para>Built once with the scene, because a flash lasts under a second and lands whenever the
/// player is hit — rebuilding two lookup tables and a sky at that moment would stutter.</para>
/// </summary>
/// <param name="ShadeRamps">
/// <see cref="MissionModels.ShadeRamps"/>' counterpart: every untextured lit surface's colour.
/// </param>
/// <param name="PaletteRamp">
/// <see cref="MissionModels.PaletteRamp"/>'s counterpart: every textured surface's, terrain included.
/// </param>
/// <param name="Atmosphere">
/// <see cref="MissionModels.Atmosphere"/>'s counterpart — the same distances, the impact palette's
/// sky bands and fog colour. Distant terrain and the sky are most of the screen in an open zone, so
/// leaving these out would flash everything except the backdrop.
/// </param>
public sealed record ImpactFlash(
	SurfaceRampTable? ShadeRamps,
	PaletteRampTable? PaletteRamp,
	Atmosphere Atmosphere);
