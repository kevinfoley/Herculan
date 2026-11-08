using Herculan.Engine.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// What the effect-light pass measures a drawn object from (<see cref="SceneItem.LightSubject"/>):
/// <c>LightManager_SelectLightsForObject</c> (<c>00407098</c>) takes the drawn entry's position and bounding radius, and
/// both come off the object rather than off the mesh, read as the item is drawn.
/// </summary>
public interface ILightSubject {
	/// <summary>The object's position in world units.</summary>
	Vec3i Position { get; }

	/// <summary>The bounding radius its render entry caches, in world units.</summary>
	int ShapeRadius { get; }
}
