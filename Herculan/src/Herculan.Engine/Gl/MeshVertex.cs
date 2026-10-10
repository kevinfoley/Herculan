using System.Numerics;
using System.Runtime.InteropServices;

namespace Herculan.Engine.Gl;

/// <summary>
/// The engine's single vertex format: position, normal, colour, UV, and whether the UV means
/// anything.
///
/// <para><see cref="Textured"/> is per-vertex rather than per-draw because both meshes that carry
/// textures are mixed: a mech mesh has a handful of texture polys whose frame index does not resolve
/// (see docs/retail/rendering/dts-texture-binding.md's fleet audit), and a terrain mesh can have cells whose
/// material selects a frame the theater's bank does not have. Those fall back to
/// <see cref="Color"/> while their neighbours sample the atlas, which a single per-draw flag cannot
/// express — it would either sample garbage UVs for the strays or drop the whole mesh's texturing.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshVertex {
	public Vector3 Position;
	public Vector3 Normal;
	public Vector3 Color;
	public Vector2 UV;

	/// <summary>1 when <see cref="UV"/> addresses a real atlas rect, 0 to use <see cref="Color"/>.</summary>
	public float Textured;

	/// <summary>
	/// 1 to take <see cref="Color"/> exactly as given, with no light term over it; 0 to shade it.
	///
	/// <para>Sensed rather than styled: the original's flat solid poly (<c>TSSolidPoly_Render</c>,
	/// <c>00474db4</c>) never computes a light term — it looks its colour up in the theater's ramp at
	/// a fixed shade byte and fills. Its shaded sibling <c>TSShadedPoly</c> does light the face, and
	/// that is what almost every surface of a HERC or a building is. So the two need different
	/// treatment on the same mesh, per vertex, for the same reason <see cref="Textured"/> is per
	/// vertex — see <see cref="Content.ShadeRamp"/>.</para>
	///
	/// <para>Zero by default, so a vertex built any other way stays lit.</para>
	/// </summary>
	public float Unlit;

	/// <summary>
	/// A <b>shade byte</b>, 0-255, for a surface that carries its own instead of having one computed
	/// per frame — read only when <see cref="Unlit"/> is set.
	///
	/// <para>This is how a surface the original shades <i>ahead of time</i> gets drawn. Terrain is the
	/// case: <c>Terrain_BuildSurface</c> lights every cell at zone load and stores the two shade
	/// bytes in the cell itself (offsets <c>+0xd</c> and <c>+0xe</c>, one per triangle), and
	/// <c>Terrain_DrawCellQuad</c> hands the byte straight to the span setup. Nothing about it is
	/// recomputed per frame, so running the renderer's own light term over terrain is not a stand-in
	/// for the original, it is a second, different light.
	/// Terrain carries <see cref="Unlit"/> set and its baked byte here instead.</para>
	///
	/// <para>See <see cref="Render.MissionSun"/> for where the byte comes from and
	/// <see cref="Render.PaletteRampTable"/> for what the shader does with it — it selects a row of
	/// the theater ramp, and each texel's palette index selects the column, which is the original's
	/// own per-pixel operation rather than a brightness applied over an expanded colour.</para>
	/// </summary>
	public float Shade;

	/// <summary>
	/// Which of the theater palette's <b>material shade ramps</b> this surface names, or -1 for a
	/// surface that is not shaded that way. The default.
	///
	/// <para>This is the vertex half of <see cref="Render.SurfaceShading.ShadedColor"/>. A
	/// <c>TSShadedPoly</c>'s colour is not a colour at all until a light level is known — the surface
	/// value picks a ramp and the face's shade picks a step along it — and the shade depends on the
	/// face's <i>world</i> normal, which differs per instance because one built mesh is shared by
	/// every structure of a type at its own heading. So the ramp number travels to the GPU and the
	/// lookup happens there, against <see cref="Render.SurfaceRampTable"/>. Baking it here
	/// would pin every instance to the rest pose's lighting.</para>
	///
	/// <para>It is more than a bare ramp number: a <c>TSGouraudPoly</c>'s value carries
	/// <see cref="Render.SurfaceRampTable.GouraudRowOffset"/> on top, because the two lit types spend
	/// the same ramp through different chains — see <see cref="Render.SurfaceShading.GouraudColor"/>.
	/// The pair names the chain and the ramp; the shader turns it into a row, since the shaded chain
	/// is stored once per depth slice and the Gouraud chain once in total.</para>
	/// </summary>
	public float ShadeRamp;

	/// <summary>
	/// The <b>face's</b> own normal, identical across the triangle's three corners, where
	/// <see cref="Normal"/> may be a smoothed per-corner one.
	///
	/// <para>It exists for the front/back decision, which the original makes once per poly rather
	/// than per pixel: <c>TSPoly_FrontBackVisibilityTest</c> takes the poly's stored normal and
	/// centre (<see cref="FaceCenter"/>), and every renderer negates <i>all</i> of the poly's normals
	/// together when the answer is "back". Making that call from the smoothed normal instead would let
	/// one corner of a Gouraud poly flip while another did not, which shows up as a seam along a
	/// silhouette.</para>
	///
	/// <para>Defaults to <see cref="Normal"/>, which is right for every flat poly — there the two
	/// are the same vector.</para>
	/// </summary>
	public Vector3 FaceNormal;

	/// <summary>
	/// The homogeneous weight <see cref="UV"/> is premultiplied by, or 0 for a vertex whose
	/// <see cref="UV"/> is a plain coordinate — the default, and what terrain and every non-quad poly
	/// carry.
	///
	/// <para>Interpolating <c>(u·w, v·w)</c> and <c>w</c> and dividing per fragment is what makes a
	/// textured quad's two triangles share one projective map instead of each getting its own affine
	/// one. See <see cref="Render.DtsMeshBuilder"/>'s <c>QuadUvWeights</c> for the weights and why.</para>
	/// </summary>
	public float UvWeight;

	/// <summary>
	/// The <b>palette index</b> a flat solid face names, or -1 for every other surface — the default,
	/// and what a fallback colour carries.
	///
	/// <para><c>TSSolidPoly_Render</c> (<c>00474db4</c>) resolves its surface value as
	/// <c>rampRow(UnlitShade)[index]</c>, which is one row of
	/// <see cref="Render.PaletteRampTable"/> — the same table a lit textured texel is resolved
	/// through, read at a fixed row instead of the light's, in the depth slice the object's distance
	/// installs (<c>Raster_ShadeRampRow</c> (<c>00468054</c>) adds the slice to the row). So this
	/// travels to the GPU for the same reason <see cref="ShadeRamp"/> does: the table is swapped
	/// wholesale for the cockpit's damage flash (<see cref="Scene.ImpactFlash"/>), and a colour
	/// resolved on the CPU can follow neither that nor the slice. The outline pass carries its line
	/// entry's index the same way.</para>
	///
	/// <para>Moving the lookup to the GPU changes no colour: over every palette index of all ten
	/// theaters, through both the ordinary and the impact palette, the table row and the colour
	/// resolved on the CPU agree on all 5120 pairs. The class is 2.8% of the triangle vertices
	/// across the 55 retail <c>.DTS</c> files, but it is concentrated in combat geometry: all of
	/// <c>ROCKETS</c> and <c>METEOR</c>, 90% of <c>FLAT2</c>, 66% of <c>BULLETS</c>, 11-13% of the
	/// fitted weapon models, 1.5% of machines and structures, 0.2% of debris.</para>
	///
	/// <para><see cref="Color"/> still carries the resolved colour and is what draws when no palette
	/// ramp is installed, so a theater whose palette did not load is unaffected by this path.</para>
	/// </summary>
	public float SolidPaletteIndex;

	/// <summary>
	/// The <b>face's</b> own centre point, identical across every corner of one poly — the other half
	/// of the front/back decision <see cref="FaceNormal"/> is the first half of.
	///
	/// <para><c>TSPoly_FrontBackVisibilityTest</c> (<c>0048c620</c>) measures the eye against the
	/// poly's stored centre (<c>poly+6</c>, a point index like the normal), not against a corner, and
	/// the two differ because a stored centre need not lie on its poly's plane
	/// (docs/retail/rendering/dts-texture-binding.md, "<c>TSPoly_FrontBackVisibilityTest</c>"). It also makes
	/// the answer the same at every corner of the poly, which <see cref="Side"/> relies on.</para>
	///
	/// <para>Defaults to <see cref="Position"/>, which leaves every surface that is not a shape poly —
	/// terrain — measured at its own corner.</para>
	/// </summary>
	public Vector3 FaceCenter;

	/// <summary>
	/// Which side of its poly this vertex's copy draws: <c>+1</c> only while the poly faces the eye,
	/// <c>-1</c> only while it faces away, <c>0</c> either way — the default.
	///
	/// <para>A shape poly resolves a surface pair per side, and either side's pair can say "do not
	/// draw" — the format's back-face culling; see <see cref="Render.DtsMeshBuilder"/>. A poly whose two
	/// sides resolve alike goes to the GPU once with <c>0</c>; otherwise once per side it draws, and the
	/// shader drops the copy whose side the eye is not on.</para>
	/// </summary>
	public float Side;

	/// <summary>
	/// For one end of a <c>TSShadedPoly</c>'s outline, the material ramp of the fill it outlines; for
	/// one end of a plain <c>TSSolidPoly</c>'s outline, the fill's palette index; -1 for every other
	/// vertex — the default.
	///
	/// <para>A solid face's outline is tested the same way: <c>TSSolidPoly_Render</c>
	/// (<c>00474db4</c>) compares the two bytes read through <c>Raster_ShadeRampRow</c>, whose row
	/// carries the depth slice, so whether the outline shows can change with distance. Its edges go up
	/// whenever the line names a different palette index, and the shader drops the fragment where the
	/// two bytes agree at the slice in force (<see cref="Render.PaletteRampTable"/>'s alpha).</para>
	///
	/// <para><c>TSShadedPoly_Render</c> (<c>0047542c</c>) resolves its line entry through the same two
	/// lookups as its fill, at the same shade, and <c>PolyFill_FillThenOutline</c> (<c>0048d518</c>)
	/// draws the edge loop only when the two resolved palette bytes differ. The shade is the face's
	/// light, which only the shader knows, so the outline carries both ramps — its own in
	/// <see cref="ShadeRamp"/>, the fill's here — and the shader drops the fragment where the two
	/// bytes agree. See <see cref="Render.SurfaceRampTable"/>.</para>
	/// </summary>
	public float OutlineFillRamp;

	/// <summary>
	/// The plane of another poly whose facing this copy is drawn under — its normal in <c>xyz</c> and
	/// its centre's offset along that normal in <c>w</c> — read only when <see cref="DependSide"/> is
	/// non-zero. Zero by default.
	///
	/// <para>It exists for a back-facing three-vertex <c>TSTexture4Poly</c>, whose second corner is
	/// whatever an earlier texture poly left in <c>TSTexture4Poly_Render</c>'s position array, and so
	/// depends on which way that poly faces this frame. See <see cref="Render.TextureCornerSlot"/>.</para>
	/// </summary>
	public Vector4 DependFace;

	/// <summary>
	/// Which way the poly <see cref="DependFace"/> describes must face for this copy to draw: <c>+1</c>
	/// the eye, <c>-1</c> away, decided as <see cref="Side"/> is; <c>0</c>, the default, is no
	/// condition.
	/// </summary>
	public float DependSide;

	public MeshVertex(Vector3 position, Vector3 normal, Vector3 color, Vector2 uv = default,
			bool textured = false, bool unlit = false, float shade = 1f, int shadeRamp = -1,
			Vector3? faceNormal = null, float uvWeight = 0f, int solidPaletteIndex = -1,
			Vector3? faceCenter = null, int side = 0, int outlineFillRamp = -1,
			Vector4 dependFace = default, int dependSide = 0) {
		Position = position;
		Normal = normal;
		FaceNormal = faceNormal ?? normal;
		Color = color;
		UV = uv;
		Textured = textured ? 1f : 0f;
		Unlit = unlit ? 1f : 0f;
		Shade = shade;
		ShadeRamp = shadeRamp;
		UvWeight = uvWeight;
		SolidPaletteIndex = solidPaletteIndex;
		FaceCenter = faceCenter ?? position;
		Side = side;
		OutlineFillRamp = outlineFillRamp;
		DependFace = dependFace;
		DependSide = dependSide;
	}

	/// <summary>Bytes per vertex, used as the vertex-attribute stride.</summary>
	public const uint SizeInBytes = 30 * sizeof(float);
}
