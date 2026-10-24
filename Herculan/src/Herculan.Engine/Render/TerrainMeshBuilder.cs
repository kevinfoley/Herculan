using System.Numerics;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Render;

/// <summary>
/// Turns a loaded <see cref="HeightGrid"/> into drawable triangles.
///
/// <para>Each quad is split along the diagonal that cell's own selector bits choose, the same split
/// <see cref="HeightGrid.HeightAtWorld"/> uses, so the surface drawn is the surface the simulation
/// queries and objects sit on the ground on sloped cells.</para>
///
/// <para>One normal per triangle, no smoothing, as the original's per-polygon terrain fill has it.
/// Surface colour comes from the theater's texture bank when one is supplied (see
/// <see cref="TerrainTextureBank"/>). Every triangle also carries its untextured colour, the palette
/// index the flat fill uses when terrain texturing is off — <see cref="UntexturedRamp"/> at the
/// triangle's shade, which the shader reads through the ramp row for
/// <see cref="UntexturedRowShade"/> (<see cref="SceneItem.GroundFill"/>); see
/// docs/retail/rendering/terrain-texturing.md#the-terrain-texture-switch.</para>
///
/// <para>The light is baked into the mesh, as the original bakes a shade byte per triangle into the
/// cell at zone load (docs/retail/rendering/terrain-lighting.md): <see cref="MissionSun.ShadeFor"/>
/// is the byte, the terrain curve <c>Light_ComputeShadeForNormal</c> (<c>0048c060</c>) rather than
/// the shapes' one, and the shader spends it by picking that row of the theater ramp and looking each
/// texel's palette index up in it (<see cref="PaletteRampTable"/>). Without a ramp or a palette for
/// the theater the mesh draws its texture unshaded.</para>
/// </summary>
public static class TerrainMeshBuilder {
	/// <summary>
	/// Builds the whole zone as a single mesh. A retail 128x128 zone comes to about 32k triangles,
	/// which is small enough that chunking, culling and LOD are all premature — the original's own
	/// LOD parameter (<see cref="HeightGrid.DetailLod"/>) is carried on the grid for whenever they
	/// stop being premature.
	///
	/// <para>With a <paramref name="bank"/>, each cell gets the atlas rect its material selects; any
	/// cell whose material or frame does not resolve keeps the untextured ramp colour, which is why
	/// <see cref="MeshVertex.Textured"/> is per-vertex.</para>
	/// </summary>
	/// <param name="shading">
	/// The theater's ramp and palette, which bake the shade and resolve the untextured fill. Without
	/// them the mesh is drawn unshaded and an untextured cell takes
	/// <see cref="SurfaceColor"/>'s placeholder.
	/// </param>
	public static MeshVertex[] Build(HeightGrid grid, TerrainTextureBank? bank = null,
			SurfaceShading? shading = null) {
		bool bakeShade = shading != null;
		int quadsX = grid.Width - 1;
		int quadsY = grid.Height - 1;
		var vertices = new List<MeshVertex>(quadsX * quadsY * 6);

		// Normalise the colour gradient against this zone's own peak rather than a fixed height, so
		// a flat zone still reads as varied instead of uniformly low.
		float peak = MathF.Max(WorldScale.DistanceToRender(grid.MaxWorldHeight), 1f);

		for (int cellY = 0; cellY < quadsY; cellY++) {
			for (int cellX = 0; cellX < quadsX; cellX++) {
				Vector3 c00 = Corner(grid, cellX, cellY);
				Vector3 c10 = Corner(grid, cellX + 1, cellY);
				Vector3 c01 = Corner(grid, cellX, cellY + 1);
				Vector3 c11 = Corner(grid, cellX + 1, cellY + 1);

				// Corner UVs from the cell's own rect: u rises with cellX, v with cellY.
				var rect = bank?.CellRect(grid, cellX, cellY);
				bool textured = rect.HasValue;
				var r = rect ?? default;
				Vector2 t00 = new(r.U0, r.V0);
				Vector2 t10 = new(r.U1, r.V0);
				Vector2 t01 = new(r.U0, r.V1);
				Vector2 t11 = new(r.U1, r.V1);

				if (grid.DiagonalSelectorAt(cellX, cellY) == 2) {
					// Split along the c00-c11 diagonal, matching the height query's selector-2 case.
					AddTriangle(vertices, c00, c10, c11, t00, t10, t11, textured, peak, shading);
					AddTriangle(vertices, c00, c11, c01, t00, t11, t01, textured, peak, shading);
				} else {
					// Selector 0 splits along c01-c10. Selector 1 is a coplanar quad and selector 3 has
					// no observed producer; the height query treats both as a single plane through
					// c00/c10/c01, which this same split renders.
					AddTriangle(vertices, c00, c10, c01, t00, t10, t01, textured, peak, shading);
					AddTriangle(vertices, c10, c11, c01, t10, t11, t01, textured, peak, shading);
				}
			}
		}

		return vertices.ToArray();
	}

	private static Vector3 Corner(HeightGrid grid, int cellX, int cellY) =>
		WorldScale.ToRender(
			(float)cellX * grid.CellSize,
			(float)cellY * grid.CellSize,
			grid.WorldHeightAt(cellX, cellY));

	private static void AddTriangle(List<MeshVertex> vertices, Vector3 a, Vector3 b, Vector3 c,
			Vector2 uvA, Vector2 uvB, Vector2 uvC, bool textured, float peak,
			SurfaceShading? shading) {
		bool bakeShade = shading != null;
		Vector3 normal = Vector3.Cross(b - a, c - a);
		normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;

		// The world-space mapping flips handedness relative to a naive reading of the grid, so
		// keep normals pointing up rather than trusting the cross product's sign.
		if (normal.Y < 0f) {
			normal = -normal;
		}


		// One shade per triangle, from the triangle's own normal — the same granularity the original
		// bakes, which stores a byte per cell triangle rather than per corner. The normals differ in
		// derivation (Terrain_BuildCellSurface differences neighbouring heights; this is the cross
		// product of the triangle actually drawn) but describe the same surface.
		//
		// The byte itself, not a brightness derived from it: the shader picks a ramp row with it and
		// looks each texel's palette index up in that row, which is what Terrain_DrawCellQuad does
		// with the cell's stored byte. A per-poly multiplier over an expanded RGB texel was the
		// stand-in for that per-texel lookup, and is gone.
		float shade = bakeShade ? MissionSun.ShadeFor(normal) : 255f;

		// The untextured fill's palette index for this triangle, and the colour it resolves to unfogged
		// for a renderer with no ramp table bound.
		int fillIndex = shading?.RampedPaletteIndex(UntexturedRamp, (int)shade) ?? -1;
		Vector3 color = fillIndex >= 0
				&& shading!.Ramp.Resolve(fillIndex, UntexturedRowShade, shading.Palette) is { } fill
			? fill
			: SurfaceColor((a.Y + b.Y + c.Y) / 3f, normal, peak);

		vertices.Add(new MeshVertex(a, normal, color, uvA, textured, bakeShade, shade, solidPaletteIndex: fillIndex));
		vertices.Add(new MeshVertex(b, normal, color, uvB, textured, bakeShade, shade, solidPaletteIndex: fillIndex));
		vertices.Add(new MeshVertex(c, normal, color, uvC, textured, bakeShade, shade, solidPaletteIndex: fillIndex));
	}

	/// <summary>
	/// The palette material ramp <c>Terrain_FillCellUntextured</c> fills every untextured cell from,
	/// whatever the cell's material: its literal <c>2</c> to <c>Palette_ShadeRampLookup</c>.
	/// </summary>
	public const int UntexturedRamp = 2;

	/// <summary>
	/// The fixed shade <c>Terrain_FillCellUntextured</c> passes <c>Raster_ShadeRampRow</c>: its
	/// literal <c>0x4b</c>, where a flat solid face passes <see cref="ShadeRamp.UnlitShade"/>.
	/// </summary>
	public const int UntexturedRowShade = 0x4b;

	/// <summary>
	/// PLACEHOLDER: the untextured colour of a theater whose palette carries no shade ramps, which no
	/// retail theater lacks — low ground sandy, high ground grey-rocky, steep faces darker. Not
	/// retail's.
	/// </summary>
	private static Vector3 SurfaceColor(float renderHeight, Vector3 normal, float peak) {
		float elevation = System.Math.Clamp(renderHeight / peak, 0f, 1f);

		var low = new Vector3(0.44f, 0.40f, 0.28f);
		var high = new Vector3(0.58f, 0.58f, 0.56f);
		Vector3 color = Vector3.Lerp(low, high, elevation);

		float steepness = 1f - System.Math.Clamp(normal.Y, 0f, 1f);
		return Vector3.Lerp(color, color * 0.65f, steepness);
	}
}
