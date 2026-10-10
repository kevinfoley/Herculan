namespace Herculan.Engine.Terrain;

/// <summary>
/// How <see cref="HeightGrid.RayWalk"/> treats a ground crossing the plane solve cannot place. The
/// two releases differ there, and the solve itself has a retail error that a tweak corrects; see
/// docs/retail/simulation/terrain-heightmap.md, "When the solve finds no point". The default is v1.0
/// with no tweak.
/// </summary>
/// <param name="HitNeedsSurfacePoint">
/// v1.10's rule (<c>Terrain_RayWalk</c> at <c>0046e980</c> in its <c>DBSIM.EXE</c>): a crossing counts
/// only when the solve finds a point on the ground. Without it, v1.0's: the crossing is a hit
/// whether or not it does.
/// </param>
/// <param name="ExactFarPlane">
/// The <see cref="Settings.TweakSettingDefinitions.FixTerrainHitPoint"/> tweak: solve a selector-2
/// cell's far triangle against its own plane, not the one with the near plane's constant that
/// both releases use.
/// </param>
public readonly record struct ThinRayRules(bool HitNeedsSurfacePoint, bool ExactFarPlane);
