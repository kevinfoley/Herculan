using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// Where a view ray first meets the ground: a march along the ray against
/// <see cref="HeightGrid.HeightAtWorld"/>, refined by bisection once a step lands below the surface.
/// The terrain mesh is built from the same heights, so the point is on the ground as drawn.
/// </summary>
internal static class GroundPick {
	/// <summary>Steps per terrain cell. Two is fine enough that a ray cannot step over a ridge a cell wide.</summary>
	private const int StepsPerCell = 2;

	private const int BisectionSteps = 16;

	/// <summary>
	/// The ground point under a render-space ray, in world units, or null when the ray meets no ground
	/// within <paramref name="maxDistanceRender"/>.
	/// </summary>
	public static Vec3i? Cast(Vector3 originRender, Vector3 directionRender, HeightGrid terrain,
			float maxDistanceRender) {
		float step = terrain.CellSize / WorldScale.WorldUnitsPerMeter / StepsPerCell;
		float previous = 0f;

		if (Below(originRender, terrain)) {
			return null;
		}

		for (float t = step; t <= maxDistanceRender; t += step) {
			if (!Below(originRender + directionRender * t, terrain)) {
				previous = t;
				continue;
			}

			float low = previous;
			float high = t;
			for (int i = 0; i < BisectionSteps; i++) {
				float mid = (low + high) * 0.5f;
				if (Below(originRender + directionRender * mid, terrain)) {
					high = mid;
				} else {
					low = mid;
				}
			}

			var hit = ToWorld(originRender + directionRender * high);
			return new Vec3i(hit.X, hit.Y, terrain.HeightAtWorld(hit.X, hit.Y));
		}

		return null;
	}

	private static bool Below(Vector3 pointRender, HeightGrid terrain) {
		var world = ToWorld(pointRender);
		return world.Z <= terrain.HeightAtWorld(world.X, world.Y);
	}

	/// <summary>The inverse of <see cref="WorldScale.ToRender(Vec3i)"/>.</summary>
	private static Vec3i ToWorld(Vector3 render) => new(
		(int)(render.X * WorldScale.WorldUnitsPerMeter),
		(int)(-render.Z * WorldScale.WorldUnitsPerMeter),
		(int)(render.Y * WorldScale.WorldUnitsPerMeter));
}
