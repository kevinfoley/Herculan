using System.Numerics;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Render;

/// <summary>
/// Turns a loaded <see cref="HeightGrid"/> into drawable triangles.
///
/// <para>The triangulation deliberately mirrors <see cref="HeightGrid.HeightAtWorld"/> exactly:
/// each quad is split along the diagonal that cell's own selector bits choose, so the surface being
/// drawn is the same surface the simulation queries. If the renderer used a fixed diagonal instead,
/// objects would visibly sit slightly above or below the ground on sloped cells — a whole class of
/// "why is the mech floating" bug avoided by construction rather than by tuning an offset later.</para>
///
/// <para>Flat-shaded lighting: one normal per triangle, no smoothing — which is what the original's
/// per-polygon terrain fill does, and keeps the mesh honest about the geometry actually being tested
/// against. Surface <i>colour</i> comes from the theater's texture bank when one is supplied (see
/// <see cref="TerrainTextureBank"/>).</para>
///
/// <para><b>Every triangle also carries its untextured colour</b>, as the palette index
/// <c>Terrain_FillCellUntextured</c> (<c>0046bb40</c>) fills it with when terrain texturing is off —
/// TERRAIN TEXTURE in the preferences, and always in the MFD's missile camera. That fill ignores the
/// cell's material: it takes the palette's own material ramp <see cref="UntexturedRamp"/> at the
/// triangle's baked shade, and reads the result through the theater ramp's row for
/// <see cref="UntexturedRowShade"/> plus the cell's depth slice, which the shader does
/// (<see cref="SceneItem.GroundFill"/>). That is <c>Raster_ShadeRampRow(0x4b)[Palette_ShadeRampLookup(2,
/// shade)]</c>, the original's arm for shade mode 1, the one its draw runs in.</para>
///
/// <para><b>The light is baked here, not at draw time.</b> The original lights terrain exactly once,
/// at zone load: <c>Terrain_BuildSurface</c> walks every cell, normalises its two face normals to
/// length <c>0x800</c> and stores a shade byte per triangle in the cell itself (<c>+0xd</c> near,
/// <c>+0xe</c> far). <c>Terrain_DrawCellQuad</c> then hands that byte straight to the span setup as
/// the ramp row, and nothing recomputes it per frame. So the shade belongs in the mesh —
/// <see cref="MissionSun.ShadeFor"/> is the byte, and the shader spends it exactly as the original
/// does, by picking that row of the theater ramp and looking each texel's palette index up in it
/// (<see cref="PaletteRampTable"/>). Without a ramp or a palette for the theater there is no table
/// to look anything up in, and the mesh draws its texture unshaded.</para>
///
/// <para>Note this is the <b>terrain</b> shade curve, <c>Light_ComputeShadeForNormal</c> (<c>0048c060</c>) — not the one a shape's
/// polys use. See <see cref="MissionSun"/> for why they differ.</para>
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
