using System.Numerics;
using Herculan.Engine.Gl;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>One mesh plus the transform that places it in the world.</summary>
public sealed class SceneItem {
	public SceneItem(GpuMesh mesh, Matrix4x4 transform, uint? textureHandle = null, bool fullbright = false) {
		Mesh = mesh;
		Transform = transform;
		TextureHandle = textureHandle;
		Fullbright = fullbright;
	}

	/// <summary>
	/// The geometry. Settable because a structure that falls is redrawn as its wreck rather than
	/// rebuilt — see <see cref="Sim.BaseObject.ShowingHulk"/>.
	/// </summary>
	public GpuMesh Mesh { get; set; }

	/// <summary>Model-to-world transform, in render space.</summary>
	public Matrix4x4 Transform { get; set; }

	/// <summary>
	/// Whether this item is drawn at all this frame. It is how a shape built one cell at a time shows
	/// the cell its object's <see cref="Sim.ShapeCellFrames"/> names and no other: every item under a
	/// sequence goes false except the one standing on the cell it has reached. A machine's destroyed
	/// limb lands on a blank cell and so comes off; a structure's collapsed part lands on its rubble.
	/// </summary>
	public bool Visible { get; set; } = true;

	/// <summary>
	/// Whether this item belongs to the LOD root its object is currently drawn as — see
	/// <see cref="ShapeDetail"/>. A machine's shape is uploaded once per root and only one root's
	/// items are selected at a time, so this is a second, independent reason for a piece not to be
	/// drawn: <see cref="Visible"/> answers which <i>cell</i> of the shape is showing and this
	/// answers which <i>shape</i>. Everything that has no LOD chain leaves it true.
	/// </summary>
	public bool DetailSelected { get; set; } = true;

	/// <summary>
	/// How the object this item draws is filed by terrain cell, which is a third reason for it not to
	/// be drawn: <see cref="DrawEntry.Drawn"/>, settled per pass by the <see cref="ObjectDrawTable"/> of
	/// the <see cref="GroundShapeLayer"/> it is drawn with. Every item of one object shares one entry.
	/// Null for the terrain, which is not filed.
	/// </summary>
	public DrawEntry? Filing { get; set; }

	/// <summary>Optional texture for this item. If null, flat-shaded rendering is used.</summary>
	public uint? TextureHandle { get; set; }

	/// <summary>
	/// Whether the editor's measuring grid (<see cref="SceneRenderer.Grid"/>) is painted onto this
	/// item's surface. Set on the terrain only — the grid is a reading of the ground, and running it
	/// over the machines standing on it would just be stripes. Has no effect unless the renderer was
	/// built with the grid compiled in.
	/// </summary>
	public bool ShowGrid { get; set; }

	/// <summary>
	/// Whether this item's <b>textured</b> surfaces skip the theater ramp entirely and draw the
	/// palette straight through — no light term and no shade row. It is a property of the draw, not
	/// of the shape: <c>Bullet_Draw</c> (<c>0040a120</c>) zeroes the ramp's row count for the
	/// duration of a projectile's shape render, which switches <c>TSTexture4Poly_Render</c>
	/// (<c>00474e9c</c>) to a plain texture copy, and restores it afterwards. The same vtable slot
	/// draws launcher rounds, so both classes set it — see <see cref="PaletteRampTable.FullbrightRow"/>.
	///
	/// <para>Untextured surfaces are unaffected, as they are in the original: a projectile's
	/// <c>TSSolidPoly</c> geometry was never lit to begin with.</para>
	/// </summary>
	public bool Fullbright { get; set; }

	/// <summary>
	/// The object this item's geometry belongs to, which is what the effect-light pass measures
	/// from — <c>LightManager_SelectLightsForObject</c> (<c>00407098</c>) takes the drawn entry's own position and bounding radius, and both
	/// come off the object rather than off the mesh. A machine drawn as one item per posed node names
	/// the machine on every one of them, because the original selects once for the whole shape.
	///
	/// <para>Null leaves the item lit by the mission sun alone, which is right for the two things
	/// that carry no object: the terrain, whose shade is baked at zone load and cannot respond to a
	/// light at all, and a projectile, drawn fullbright.</para>
	/// </summary>
	public SimObject? LightSubject { get; set; }

	/// <summary>
	/// Whether this item takes its fog distance a cell at a time the way <c>Terrain_DrawCellQuad</c>
	/// does rather than a pixel at a time — see <see cref="SceneRenderer.FogCellSize"/>. Set on the
	/// terrain, and on the ground shapes, which the original draws under the fade their cell's quad
	/// installed (docs/retail/simulation/ground-shapes.md, "The draw pass"). Every other drawn thing is
	/// fogged from one distance of its own (<c>ObjList_DrawEntryRender</c> passes the render entry's
	/// <c>+0x12</c>), which is already what a small object per-pixel amounts to.
	/// </summary>
	public bool CellQuantisedFog { get; set; }

	/// <summary>
	/// Whether this item is the terrain, whose untextured cells fill the way
	/// <c>Terrain_FillCellUntextured</c> (<c>0046bb40</c>) fills them rather than as a flat solid face
	/// — see <see cref="TerrainMeshBuilder"/>. Both carry a palette index per vertex; the two read
	/// different rows of the theater ramp, and only the terrain's is fogged through it.
	/// </summary>
	public bool GroundFill { get; set; }

	/// <summary>
	/// The <c>TSBSPPart</c> this item draws a child of, or null for geometry under none — see
	/// <see cref="BspDrawGroup"/>. The renderer paints a group's items together, in the tree's order
	/// for the eye, rather than where the item list puts them.
	/// </summary>
	public BspDrawGroup? BspGroup { get; set; }

	/// <summary>Which child of <see cref="BspGroup"/> — the <see cref="BspLeaf.Index"/> its geometry was built under.</summary>
	public int BspLeaf { get; set; }

	/// <summary>
	/// Which paint layer of its child of <see cref="BspGroup"/> this item is — <see cref="MeshCell.Layer"/>.
	/// A child's layers are painted in ascending order, each over the ones before it.
	/// </summary>
	public int PaintLayer { get; set; }
}

/// <summary>
/// Tunables for the grid <see cref="SceneItem.ShowGrid"/> paints onto a surface. Spacing is in render
/// units (metres) and widths are in screen pixels, held constant at any distance or view angle.
/// </summary>
public sealed class TerrainGridOverlay {
	public Vector3 Color { get; set; } = new(0.72f, 0.76f, 0.82f);

	public float SpacingMeters { get; set; } = 10f;

	public float Opacity { get; set; } = 0.35f;

	public float MajorLineOpacity { get; set; } = 0.55f;

	public float LineWidthPixels { get; set; } = 1.5f;

	public int MajorLineEvery { get; set; } = 10;

	public float MajorLineWidthScale { get; set; } = 2f;

	/// <summary>On-screen cell size, in pixels, below which a set of lines fades out.</summary>
	public float MinCellPixels { get; set; } = 6f;

	/// <summary>Distance from the camera, in render units (metres), at which the grid starts fading.</summary>
	public float FadeStartMeters { get; set; } = 450f;

	/// <summary>Distance at which it has faded out completely.</summary>
	public float FadeEndMeters { get; set; } = 1000f;
}

/// <summary>
/// Draws a list of <see cref="SceneItem"/>s from a <see cref="Camera"/> with one directional light
/// plus ambient. Deliberately minimal — the first milestone's rendering goal is a correct,
/// legible view of real game geometry, not a material system.
/// </summary>
public sealed class SceneRenderer : IDisposable {
	private readonly GL _gl;
	private readonly ShaderProgram _shader;
	private readonly ShaderProgram _skyShader;
	private readonly uint _skyVertexArray;
	private readonly bool _hasGrid;
	private GpuTexture? _shadeRampTexture;
	private GpuTexture? _paletteRampTexture;
	private GpuTexture? _impactShadeRampTexture;
	private GpuTexture? _impactPaletteRampTexture;
	private int _paletteRampRows;
	private int _paletteRampShadeRows;
	private int _paletteRampUnlitRow;
	private int _paletteRampGroundRow;
	private int _depthSlices;
	private int _shadeRampRows;
	private int _shadeRampGouraudRow;
	private int _shadeRampPaletteRow;
	private TerrainPaintRankBuffer? _paintRanks;
	private readonly SelectedEffectLight[] _effectLights =
		new SelectedEffectLight[EffectLightSelection.MaxPerObject];

	// Built once because these are set per drawn item: composing the subscript into a string each
	// time would put a few thousand allocations a frame in front of the draw loop.
	private static readonly string[] EffectLightNames = Enumerable
		.Range(0, EffectLightSelection.MaxPerObject)
		.Select(i => $"uEffectLights[{i}]").ToArray();
	private static readonly string[] EffectLightIntensityNames = Enumerable
		.Range(0, EffectLightSelection.MaxPerObject)
		.Select(i => $"uEffectLightIntensity[{i}]").ToArray();

	/// <param name="editorGrid">
	/// Compiles the measuring grid into the scene program, so <see cref="SceneItem.ShowGrid"/> and
	/// <see cref="Grid"/> do something. Off by default, and the simulator leaves it off: the grid is
	/// a tool, and this way none of it — not a uniform, not a varying, not a branch — reaches the
	/// program the game is drawn with.
	/// </param>
	public SceneRenderer(GL gl, bool editorGrid = false) {
		_gl = gl;
		_hasGrid = editorGrid;
		_shader = editorGrid
			? ShaderProgram.Load(gl, "Scene.glsl", "EDITOR_GRID")
			: ShaderProgram.Load(gl, "Scene.glsl");
		_skyShader = ShaderProgram.Load(gl, "Sky.glsl");
		_skyVertexArray = gl.GenVertexArray();

		_gl.Enable(EnableCap.DepthTest);
		_gl.DepthFunc(DepthFunction.Less);

		// The default framebuffer's stencil, which BSP children are painted through — see
		// DrawBspGroup. EngineWindow asks for eight bits.
		_gl.GetFramebufferAttachmentParameter(GLEnum.Framebuffer, GLEnum.Stencil,
			GLEnum.FramebufferAttachmentStencilSize, out int stencilBits);
		_stencilBits = stencilBits;
		if (stencilBits == 0) {
			Console.Error.WriteLine(
				"WARNING: the framebuffer has no stencil buffer; a BSP part's children are drawn " +
				"on the depth test alone, so a later child does not reliably cover an earlier one.");
		}
	}

	/// <summary>How many stencil bits the framebuffer has; zero leaves BSP children painted without one.</summary>
	private readonly int _stencilBits;

	/// <summary>Numbers <see cref="Render"/>'s passes, so a <see cref="BspDrawGroup"/> knows when to forget the last one.</summary>
	private int _pass;

	/// <summary>The top-level BSP groups a pass has touched, in the order it first touched them.</summary>
	private readonly List<BspDrawGroup> _bspRoots = new();

	/// <summary>The nodes the original has composed so far in the group being prepared — see PrepareBspGroup.</summary>
	private readonly HashSet<int> _composedNodes = new();

	/// <summary>
	/// The matrix that takes a light's render-world position (or direction) into the space of frame
	/// <paramref name="lightFrame"/> and reads it back out as though it were in
	/// <paramref name="drawnFrame"/>: where the original's shading of a face in
	/// <paramref name="drawnFrame"/> finds a light whose model-space copy was made for
	/// <paramref name="lightFrame"/>. Null if the frame cannot be inverted.
	/// </summary>
	private static Matrix4x4? LightRetarget(BspDrawGroup frames, int lightFrame, int drawnFrame) =>
		Matrix4x4.Invert(frames.FrameToWorld(lightFrame), out var worldToLight)
			? worldToLight * frames.FrameToWorld(drawnFrame)
			: null;

	/// <summary>The stencil value the pass has handed out last — see DrawBspGroup.</summary>
	private int _stencilUsed;

	/// <summary>Whether this pass has cleared the stencil yet. It does so before its first group.</summary>
	private bool _stencilCleared;

	/// <summary>
	/// Direction the sun's light travels, in render space — <see cref="MissionSun.Direction"/>.
	/// Settable so a tool can override it; every mission uses the one hardcoded sun.
	/// </summary>
	public Vector3 LightDirection { get; set; } = MissionSun.Direction;

	/// <summary>
	/// The impact effects' dynamic lights — <see cref="EffectPools.Lights"/>. Each drawn item
	/// whose <see cref="SceneItem.LightSubject"/> is set gets its own selection out of these, which
	/// is what <c>LightManager_SelectLightsForObject</c> (<c>00407098</c>) does per render entry. Null lights the scene by the sun alone,
	/// which is what a tool with no simulation running gets.
	/// </summary>
	public EffectLightField? EffectLights { get; set; }

	/// <summary>
	/// Installs the theater's shaded-surface colours, which every <c>TSShadedPoly</c> and
	/// <c>TSGouraudPoly</c> in the scene is drawn through — see <see cref="SurfaceRampTable"/>. Passing null (a theater whose palette
	/// carries no ramp table) leaves those surfaces on the mesh builder's fallback colour instead.
	///
	/// <para>Call once per loaded mission, before the first <see cref="Render"/>. Uploading again
	/// replaces the previous table.</para>
	/// </summary>
	public void SetShadeRamps(SurfaceRampTable? table) {
		_shadeRampTexture?.Dispose();
		_shadeRampTexture = table == null
			? null
			: new GpuTexture(_gl, table.Pixels, SurfaceRampTable.Width, table.Height);
		_shadeRampRows = table?.Height ?? 0;
		_shadeRampGouraudRow = table?.GouraudBlockRow ?? 0;
		_shadeRampPaletteRow = table?.PaletteRow ?? 0;
		_depthSlices = table?.DepthSlices ?? _depthSlices;
	}

	/// <summary>
	/// Installs the theater's palette-by-shade-row table, which every <b>lit textured</b> surface in
	/// the scene is drawn through — see <see cref="PaletteRampTable"/>. Passing null leaves those
	/// surfaces sampling their expanded colour unlit.
	///
	/// <para>Call once per loaded mission alongside <see cref="SetShadeRamps"/>. A caller that
	/// installs this <b>must</b> bind atlases built from
	/// <see cref="TextureAtlas.IndexPixels"/> rather than <see cref="TextureAtlas.Pixels"/>: the
	/// shader reads the red channel as a palette index once this is set.</para>
	/// </summary>
	public void SetPaletteRamp(PaletteRampTable? table) {
		_paletteRampTexture?.Dispose();
		_paletteRampTexture = table == null
			? null
			: new GpuTexture(_gl, table.Pixels, PaletteRampTable.Width, table.Height);
		_paletteRampRows = table?.Height ?? 0;
		_paletteRampShadeRows = table?.ShadeRows ?? 0;
		_paletteRampUnlitRow = table?.UnlitRow ?? 0;
		_paletteRampGroundRow = table?.GroundRow ?? 0;
		_depthSlices = table?.DepthSlices ?? _depthSlices;
	}

	/// <summary>
	/// Installs the counterparts of the two tables above built against the theater's damage-flash
	/// palette, which <see cref="ImpactPaletteActive"/> then swaps to and from. Both stay resident:
	/// the flash toggles several times a second and rebuilding a table at that rate would stutter.
	///
	/// <para>Call this <b>after</b> <see cref="SetShadeRamps"/> and <see cref="SetPaletteRamp"/>: it
	/// measures each table against the one already installed.</para>
	///
	/// <para>A table whose dimensions do not match the one it stands in for is <b>dropped</b> rather
	/// than installed, because the row counts are uploaded as uniforms once and the swap does not
	/// re-derive them. Nothing in retail data can trip this — both sides are built from the same
	/// theater <c>.RMP</c> — so it would mean a hand-made palette, and a silently mis-rowed lookup
	/// reads as corrupted geometry colour rather than as a bad file.</para>
	/// </summary>
	public void SetImpactRamps(SurfaceRampTable? shadeRamps, PaletteRampTable? paletteRamp) {
		_impactShadeRampTexture?.Dispose();
		_impactShadeRampTexture = shadeRamps != null && shadeRamps.Height == _shadeRampRows
			&& shadeRamps.GouraudBlockRow == _shadeRampGouraudRow
			? new GpuTexture(_gl, shadeRamps.Pixels, SurfaceRampTable.Width, shadeRamps.Height)
			: null;

		_impactPaletteRampTexture?.Dispose();
		_impactPaletteRampTexture = paletteRamp != null && paletteRamp.Height == _paletteRampRows
			&& paletteRamp.ShadeRows == _paletteRampShadeRows
			&& paletteRamp.UnlitRow == _paletteRampUnlitRow
			&& paletteRamp.GroundRow == _paletteRampGroundRow
			? new GpuTexture(_gl, paletteRamp.Pixels, PaletteRampTable.Width, paletteRamp.Height)
			: null;
	}

	/// <summary>
	/// Whether the scene draws through the damage-flash palette this frame. The cockpit's shake owns
	/// it — see <c>CockpitHitShake.FlashActive</c> — and it does nothing until
	/// <see cref="SetImpactRamps"/> has supplied a table to swap to.
	/// </summary>
	public bool ImpactPaletteActive { get; set; }

	/// <summary>
	/// What distant geometry fades into. The theater's own ramp knows this colour — see
	/// <see cref="Content.ShadeRamp.FogColor"/> — and <see cref="Scene.Atmosphere"/> supplies it.
	/// The value here is only the fallback for a mission whose ramp did not load.
	/// </summary>
	public Vector3 FogColor { get; set; } = new(0.55f, 0.60f, 0.68f);

	/// <summary>
	/// The theater's sky backdrop, banded out of its own palette — see
	/// <see cref="Content.SkyGradient"/>. Null draws a flat <see cref="SkyColor"/> instead.
	/// </summary>
	public Content.SkyGradient? Sky { get; set; }

	/// <summary>
	/// Flat fallback sky, used to clear the framebuffer and drawn instead of the gradient when
	/// <see cref="Sky"/> is null.
	///
	/// <para>Deliberately <b>not</b> <see cref="FogColor"/> — the sky and the
	/// colour distant terrain fades into are separate things in the original, which match in seven
	/// theaters of ten (docs/retail/rendering/distance-fog-and-sky.md, "Where the two meet").</para>
	/// </summary>
	public Vector3 SkyColor { get; set; } = new(0.55f, 0.60f, 0.68f);

	/// <summary>
	/// Distance in render units (metres) at which fog starts — half the zone's visibility range,
	/// which is where the original's ramp fade begins. See <see cref="Scene.Atmosphere"/>.
	/// </summary>
	public float FogStart { get; set; } = 900f;

	/// <summary>Distance in render units at which fog is total — the zone's visibility range.</summary>
	public float FogEnd { get; set; } = 9000f;

	/// <summary>
	/// The zone's cell size in render units, which is the grain the terrain's fog is measured at.
	/// Retail fogs a whole cell from its nearest corner (docs/retail/rendering/distance-fog-and-sky.md); the
	/// renderer spends that rule as its mean instead. Over a cell's four corners the minimum of
	/// <c>i*a + j*b</c> is <c>min(0,a) + min(0,b)</c> and the centre is <c>(a+b)/2</c>, so centre to
	/// nearest corner is exactly <c>(|a| + |b|)/2</c>, with <c>a</c> and <c>b</c> the depth one cell
	/// step along each grid axis covers. That is worked out per frame from the camera's forward
	/// direction and subtracted from the terrain's own depth: the same fog on the same ground without
	/// a per-vertex attribute carrying the four corners, missing only the flat step across each cell.
	///
	/// <para>Zero leaves the terrain fogged per pixel. Set from <see cref="Scene.Atmosphere"/>.</para>
	/// </summary>
	public float FogCellSize { get; set; }

	/// <summary>
	/// Whether distance fog is applied at all. On by default, and the simulator never turns it off:
	/// the fade is the original's own behaviour rather than an effect. It exists for tools — the
	/// mission editor lets it be switched off so distant geometry stays legible while placing things
	/// out past the zone's visibility range.
	/// </summary>
	public bool FogEnabled { get; set; } = true;

	/// <summary>
	/// Fog bounds far enough out that the shader's fade fraction clamps to zero everywhere, which is
	/// how <see cref="FogEnabled"/> is spent — no shader branch and no second program.
	/// </summary>
	private const float FogDisabledDistance = 1e9f;

	/// <summary>
	/// Settings for the grid painted onto any item whose <see cref="SceneItem.ShowGrid"/> is set.
	/// Ignored unless this renderer was built with the grid compiled in.
	/// </summary>
	public TerrainGridOverlay Grid { get; } = new();

	/// <summary>
	/// Clears the whole framebuffer once per frame. Split out from <see cref="Render"/> so a host can
	/// draw several passes into one frame (the cockpit's three panels, each under its own scissor)
	/// without each call wiping the ones already drawn — call this once, then <see cref="Render"/>
	/// once per pass.
	/// </summary>
	public void Clear() {
		_gl.ClearColor(SkyColor.X, SkyColor.Y, SkyColor.Z, 1f);
		_gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
	}

	/// <summary>
	/// Draws one pass into the viewport sub-rect (<paramref name="viewportX"/>, <paramref
	/// name="viewportY"/>, <paramref name="viewportWidth"/>, <paramref name="viewportHeight"/>) —
	/// origin bottom-left in GL viewport convention, matching <c>GL.Viewport</c>'s own. Does not clear
	/// — call <see cref="Clear"/> once per frame before the first pass. Honours whatever scissor the
	/// caller has set, which is how passes sharing one viewport stay out of each other's pixels.
	/// </summary>
	public void Render(Camera camera, IEnumerable<SceneItem> items,
			int viewportX, int viewportY, int viewportWidth, int viewportHeight) =>
		Render(camera, items, null, viewportX, viewportY, viewportWidth, viewportHeight);

	/// <summary>
	/// The same pass with the ground drawn as the original paints it: <paramref name="ground"/>'s
	/// terrain first, then its ground shapes in the terrain's paint order, then everything else in
	/// <paramref name="items"/> (which may hold the terrain item too; it is not drawn twice).
	///
	/// <para>The original paints each ground shape straight after its own cell's ground, so the
	/// ground of every cell its walk paints later covers it and it covers everything painted
	/// before (docs/retail/simulation/ground-shapes.md, "The draw pass"). Here each shape is filed under the
	/// cell <see cref="HeightGrid.PickDrawCell"/> picks, as the original's submit files it, takes that
	/// cell's <see cref="TerrainPaintOrder.Rank"/>, and is drawn with depth testing off, keeping only
	/// the pixels where the ground showing ranks no later — <see cref="TerrainPaintRankBuffer"/>. Where
	/// no ground shows, the sky, it draws. The shapes are drawn in rank order, so where two overlap
	/// the later cell's is on top, and within one cell in the order given, the original's submit
	/// order. A shape off the grid ranks after all of it, as the original's no-cell bucket
	/// draws.</para>
	///
	/// <para>A structure's ground plane — its shadow and floor plates, <see cref="MeshCell.Ground"/> —
	/// is drawn in the same pass, ranked by the cell its object was filed under, after that cell's
	/// shapes. The original paints the whole structure over its own cell's ground
	/// (docs/retail/rendering/terrain-drawing.md, "Objects in the walk"), and these polys lie in the
	/// terrain's own plane on a level pad, where a depth test could only tie with the ground.</para>
	///
	/// <para>Everything else is drawn afterwards with the depth test, so a machine stands over its
	/// own shadow and a structure over its floor. The original also paints a shape over an object filed
	/// under a cell painted earlier where the two overlap on screen; that is not reproduced here — every
	/// object is drawn over every shape — and KNOWN_ISSUES.md lists it.</para>
	///
	/// <para>Before any of it, the pass rebuilds the zone's visible region for its own view, as
	/// <c>Terrain_SetupVisibleRegion</c> does before the submit, and files <paramref name="ground"/>'s
	/// <see cref="GroundShapeLayer.Objects"/> by it; an item whose <see cref="SceneItem.Filing"/> that
	/// leaves undrawn, and a ground shape filed under a cell the draw does not reach, are skipped. The
	/// billboards and beams drawn after this call for the same pass read the same answers.</para>
	/// </summary>
	public void Render(Camera camera, IEnumerable<SceneItem> items, GroundShapeLayer? ground,
			int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		float aspect = (float)viewportWidth / System.Math.Max(viewportHeight, 1);
		var projection = camera.ProjectionMatrix(aspect);

		var order = ground != null ? FileObjects(camera, aspect, ground) : default;

		// Before anything is drawn into the frame, because it draws into a target of its own.
		var groundShapes = ground is { Shapes.Count: > 0 } or { ObjectGround.Count: > 0 }
			? RankGroundShapes(camera, projection, ground, order,
				viewportX, viewportY, viewportWidth, viewportHeight)
			: null;

		_gl.Viewport(viewportX, viewportY, (uint)System.Math.Max(viewportWidth, 1), (uint)System.Math.Max(viewportHeight, 1));

		DrawSky(camera, viewportX, viewportY, viewportWidth, viewportHeight);

		_shader.Use();
		_shader.SetMatrix("uView", camera.ViewMatrix);
		_shader.SetMatrix("uProjection", projection);
		_shader.SetVector3("uLightDirection", LightDirection);
		_shader.SetVector3("uFogColor", FogColor);
		_shader.SetFloat("uFogStart", FogEnabled ? FogStart : FogDisabledDistance);
		_shader.SetFloat("uFogEnd", FogEnabled ? FogEnd : FogDisabledDistance);

		// Always on its own unit, even with no layer: a usampler2D left on unit 0 beside uTexture's
		// sampler2D is two sampler types on one unit, which fails every draw.
		_shader.SetInt("uPaintRank", PaintRankUnit);
		_shader.SetInt("uPaintRankTest", 0);

		if (_hasGrid) {
			_shader.SetVector3("uGridColor", Grid.Color);
			_shader.SetFloat("uGridSpacing", MathF.Max(Grid.SpacingMeters, 0.001f));
			_shader.SetFloat("uGridMinorOpacity", Grid.Opacity);
			_shader.SetFloat("uGridMajorOpacity", Grid.MajorLineOpacity);
			_shader.SetFloat("uGridLineWidthPixels", MathF.Max(Grid.LineWidthPixels, 0.1f));
			_shader.SetFloat("uGridMajorEvery", System.Math.Max(Grid.MajorLineEvery, 1));
			_shader.SetFloat("uGridMajorWidthScale", MathF.Max(Grid.MajorLineWidthScale, 1f));
			_shader.SetFloat("uGridMinCellPixels", MathF.Max(Grid.MinCellPixels, 0.1f));
			_shader.SetFloat("uGridFadeStart", Grid.FadeStartMeters);
			_shader.SetFloat("uGridFadeEnd", MathF.Max(Grid.FadeEndMeters, Grid.FadeStartMeters + 0.001f));
		}

		// Which of each pair the flash is showing. Both are the same shape, so only the handle moves.
		var shadeRampTexture = ImpactPaletteActive && _impactShadeRampTexture != null
			? _impactShadeRampTexture
			: _shadeRampTexture;
		var paletteRampTexture = ImpactPaletteActive && _impactPaletteRampTexture != null
			? _impactPaletteRampTexture
			: _paletteRampTexture;

		// Unit 1, so a per-item atlas can keep unit 0 without rebinding this every draw.
		if (shadeRampTexture != null) {
			_shader.SetSamplerTexture("uShadeRampTable", shadeRampTexture.Handle, 1);
			_shader.SetInt("uShadeRampEnabled", 1);
			_shader.SetFloat("uShadeRampRows", _shadeRampRows);
			_shader.SetFloat("uShadeRampGouraudRow", _shadeRampGouraudRow);
			_shader.SetFloat("uShadeRampPaletteRow", _shadeRampPaletteRow);
		} else {
			_shader.SetInt("uShadeRampEnabled", 0);
		}

		if (paletteRampTexture != null) {
			_shader.SetSamplerTexture("uPaletteRamp", paletteRampTexture.Handle, 2);
			_shader.SetInt("uPaletteRampEnabled", 1);
			_shader.SetFloat("uShadeLevels", _paletteRampShadeRows);
			_shader.SetFloat("uPaletteRampRows", _paletteRampRows);
			_shader.SetFloat("uPaletteRampUnlitRow", _paletteRampUnlitRow);
			_shader.SetFloat("uPaletteRampGroundRow", _paletteRampGroundRow);
		} else {
			_shader.SetInt("uPaletteRampEnabled", 0);
		}

		// The depth slice both ramps are read at, as the original derives it — see
		// Content.ShadeRamp.DepthSliceFor. Zero slices disables the whole mechanism and leaves the
		// blend below as the only fade, which is what a theater with no ramp gets.
		_shader.SetFloat("uDepthSlices", FogEnabled ? _depthSlices : 0f);

		// How much nearer than its own depth a cell's leading corner is, for this frame's view
		// direction — see FogCellSize. The grid runs along render X and Z, so a step along either
		// axis covers cellSize * that component of the forward direction.
		var forward = camera.Forward;
		float cellFogBias = 0.5f * FogCellSize
			* (MathF.Abs(forward.X) + MathF.Abs(forward.Z));

		// Whether any impact effect is carrying a light this frame at all, asked once rather than
		// per item: the usual answer is no, and it is what lets the selection below be skipped
		// outright instead of walking twenty empty slots for every drawn thing.
		bool anyEffectLights = HasLiveEffectLight();
		_shader.SetFloat("uEffectLightFalloff", EffectLightSelection.PointFalloff);

		// The count last uploaded, so that a scene with no effect lights sets it once and every
		// item after the first costs nothing.
		int uploadedLightCount = -1;

		if (ground != null) {
			Draw(ground.Terrain);
		}

		if (groundShapes != null) {
			_gl.Disable(EnableCap.DepthTest);
			_gl.DepthMask(false);
			_gl.ActiveTexture(TextureUnit.Texture0 + PaintRankUnit);
			_gl.BindTexture(TextureTarget.Texture2D, _paintRanks!.RankTexture);
			_shader.SetInt("uPaintRankTest", 1);

			foreach (var (item, rank) in groundShapes) {
				_shader.SetUInt("uGroundShapeRank", rank);
				Draw(item);
			}

			_shader.SetInt("uPaintRankTest", 0);
			_gl.DepthMask(true);
			_gl.Enable(EnableCap.DepthTest);
		}

		// A child of a BSP part is held back and painted with the rest of its part, in the walk's order
		// — see DrawBspGroup.
		int pass = ++_pass;
		var eyeWorld = WorldScale.ToRender(camera.Position);
		_bspRoots.Clear();
		_stencilCleared = false;

		foreach (var item in items) {
			if (ground != null && ReferenceEquals(item, ground.Terrain)) {
				continue;
			}

			if (item.BspGroup is { } group) {
				if (Drawn(item) && item.BspLeaf >= 0 && item.BspLeaf < group.Tree.LeafCount) {
					Touch(group).Items[item.BspLeaf].Add(item);
				}
			} else {
				Draw(item);
			}
		}

		foreach (var root in _bspRoots) {
			PrepareBspGroup(root);
			DrawBspGroup(root);
		}

		// Walks a top-level group's paint order ahead of the draw, recording it for DrawStenciled and
		// DrawPainted, and follows the node state the original carries through that order: the first
		// child on a node composes it and moves every registered light into its space, and a later child
		// on it, or on no node, takes back a saved state that restores the sun alone, so its effect lights
		// stay in the space of the node composed last (ShapeNode_InstallTransform (00476030),
		// Raster_SaveState (0048d60c), Raster_RestoreState (0048d6e0)). Each child whose own node is not
		// that one gets the matrix that puts a light where the original's shading finds it — the light
		// brought into the last node's space and read as though in the child's. The original starts the
		// state afresh per shape draw (ShapeInst_BindNodeTransformArray, 00475fd8), with the lights in the
		// object's own frame; starting it per group is the same thing because every retail shape whose
		// parts carry nodes draws through at most one TSBSPPart, outside which nothing installs a node
		// before it. A group drawn inside a child (a gun in its hardpoint slot) is lit as that
		// child: every part under the mount carries the slot's node or none (Shape_StampTransformId,
		// 00417530), so it composes nothing new. See docs/retail/rendering/effect-lights.md, "A part
		// drawn through a saved state shades the effect lights in another node's frame".
		void PrepareBspGroup(BspDrawGroup root) {
			_composedNodes.Clear();
			int lastComposed = -1;
			Prepare(root, root, ref lastComposed, null, nested: false);
		}

		void Prepare(BspDrawGroup group, BspDrawGroup frames, ref int lastComposed, Matrix4x4? inherited,
				bool nested) {
			group.OrderCount = group.Tree.PaintOrder(group.EyeInFrame, group.Order);
			for (int k = 0; k < group.OrderCount; k++) {
				int leaf = group.Order[k];
				var retarget = inherited;
				if (!nested) {
					// A hardpoint slot installs its node only when a mount is spliced into it; the blank
					// record that stands in otherwise draws nothing and installs nothing.
					int node = group.Tree.LeafNode(leaf);
					if (node >= 0 && (!group.Tree.IsAttachmentSlot(leaf) || group.Children[leaf].Count > 0)
							&& _composedNodes.Add(node)) {
						lastComposed = node;
					}

					retarget = anyEffectLights && lastComposed != node
						? LightRetarget(frames, lastComposed, node)
						: null;
				}

				group.LightRetarget[leaf] = retarget;
				foreach (var child in group.Children[leaf]) {
					Prepare(child, frames, ref lastComposed, retarget, nested: true);
				}
			}
		}

		// Readies a group for this pass the first time the pass reaches it, and files it under the
		// child of its parent it is drawn inside, or as a top-level group.
		BspDrawGroup Touch(BspDrawGroup group) {
			if (group.Pass != pass) {
				group.BeginPass(pass, eyeWorld);
				if (group.Parent is { } parent && group.ParentLeaf >= 0
						&& group.ParentLeaf < parent.Tree.LeafCount) {
					Touch(parent).Children[group.ParentLeaf].Add(group);
				} else {
					_bspRoots.Add(group);
				}
			}

			return group;
		}

		// The original paints a TSBSPPart's children one after another in its tree's order, back to
		// front from the eye, with no depth buffer: wherever two children overlap on screen, the one
		// the walk reaches later is what shows, whichever is nearer. That is docs/retail/formats/
		// dts-texture-binding.md's "TSBSPPart child selection". The rest of this scene is depth-
		// buffered, between objects and between the polys of one child, so the part's own order is
		// laid over the depth test with the stencil buffer:
		//
		// - The children are drawn in REVERSE paint order, each with its own stencil value, the
		//   later-painted child the higher value, and a fragment passes only where the stencil holds
		//   no more than its child's value (GEQUAL), writing that value where it lands. A child the
		//   original paints later has therefore already claimed every pixel it covers, and nothing
		//   painted before it can land there, nearer or not.
		// - A child painted in more than one layer (PaintLayers: a decal lying over an earlier poly of
		//   its own child) takes a value per layer, the later layer the higher, in the same way.
		// - Within one layer of one child the depth test alone decides, and against the rest of the
		//   scene too.
		//
		// A coplanar marking or insignia in a later child or layer wins its pixels outright, with no depth
		// precision involved. Polygon offset by walk rank or a depth-equal test would only settle
		// exact ties; retail's later child also covers an earlier one that is genuinely nearer
		// wherever a part's planes do not separate its children, and only the painted order
		// reproduces that.
		//
		// A group drawn inside a child (a weapon in its hardpoint slot) takes stencil values under
		// that child's own and is painted in its turn, so it stands to the machine's other children
		// exactly as that child does. Values are handed out in increasing blocks over the pass, so
		// a later group always passes over an earlier one's marks; the stencil is cleared before the
		// first group of a pass and whenever the eight bits run out. With no stencil buffer the
		// children are drawn in paint order on the depth test alone.
		void DrawBspGroup(BspDrawGroup root) {
			int span = root.StencilSpan();
			int maxValue = _stencilBits >= 8 ? 255 : (1 << _stencilBits) - 1;
			if (span > maxValue) {
				DrawPainted(root);
				return;
			}

			if (!_stencilCleared || _stencilUsed + span > maxValue) {
				_gl.ClearStencil(0);
				_gl.Clear(ClearBufferMask.StencilBufferBit);
				_stencilCleared = true;
				_stencilUsed = 0;
			}

			_gl.Enable(EnableCap.StencilTest);
			_gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
			DrawStenciled(root, _stencilUsed + span);
			_gl.Disable(EnableCap.StencilTest);
			_stencilUsed += span;
		}

		// Paints a group's children from the last-painted to the first, the child at `top` and the
		// ones before it below: each child's layers from its last down, then its nested groups under
		// its first layer's value. The order is the one PrepareBspGroup recorded.
		void DrawStenciled(BspDrawGroup group, int top) {
			for (int k = group.OrderCount - 1; k >= 0; k--) {
				int leaf = group.Order[k];
				int layers = group.LayerCount(leaf);
				for (int layer = layers - 1; layer >= 0; layer--) {
					_gl.StencilFunc(StencilFunction.Gequal, top - (layers - 1 - layer), 0xff);
					foreach (var item in group.Items[leaf]) {
						if (item.PaintLayer == layer) {
							Draw(item, group.LightRetarget[leaf]);
						}
					}
				}

				int below = top - layers;
				foreach (var child in group.Children[leaf]) {
					DrawStenciled(child, below);
					below -= child.StencilSpan();
				}

				top -= group.ChildSpan(leaf);
			}
		}

		void DrawPainted(BspDrawGroup group) {
			for (int k = 0; k < group.OrderCount; k++) {
				int leaf = group.Order[k];
				foreach (var child in group.Children[leaf]) {
					DrawPainted(child);
				}

				int layers = group.LayerCount(leaf);
				for (int layer = 0; layer < layers; layer++) {
					foreach (var item in group.Items[leaf]) {
						if (item.PaintLayer == layer) {
							Draw(item, group.LightRetarget[leaf]);
						}
					}
				}
			}
		}

		bool Drawn(SceneItem item) =>
			item.Visible && item.DetailSelected && item.Filing is not { Drawn: false };

		// `lightRetarget` moves the effect lights into the frame the original shades this item's faces
		// against them in — see PrepareBspGroup.
		void Draw(SceneItem item, Matrix4x4? lightRetarget = null) {
			if (!Drawn(item)) {
				return;
			}

			_shader.SetMatrix("uModel", item.Transform);

			// LightManager_SelectLightsForObject (00407098), run per drawn object just as ObjList_DrawEntryRender runs it — see
			// EffectLightSelection.
			int lightCount = 0;
			if (anyEffectLights && item.LightSubject is { } subject) {
				lightCount = EffectLightSelection.Select(EffectLights!, subject.Position,
					subject.ShapeRadius, _effectLights);

				for (int i = 0; i < lightCount; i++) {
					var light = _effectLights[i];
					var vector = light.Vector;
					if (lightRetarget is { } retarget) {
						vector = light.Directional
							? Vector3.Normalize(Vector3.TransformNormal(vector, retarget))
							: Vector3.Transform(vector, retarget);
					}

					// w is the original's own light type tag: 1 directional, 2 point.
					_shader.SetVector4(EffectLightNames[i],
						new Vector4(vector, light.Directional ? 1f : 2f));
					_shader.SetFloat(EffectLightIntensityNames[i], light.Intensity);
				}
			}

			if (lightCount != uploadedLightCount) {
				_shader.SetInt("uEffectLightCount", lightCount);
				uploadedLightCount = lightCount;
			}
			if (_hasGrid) {
				_shader.SetInt("uGridEnabled", item.ShowGrid ? 1 : 0);
			}

			_shader.SetInt("uFullbright", item.Fullbright ? 1 : 0);
			_shader.SetInt("uGroundFill", item.GroundFill ? 1 : 0);
			_shader.SetFloat("uFogDepthBias", item.CellQuantisedFog ? cellFogBias : 0f);

			// Bind texture if available, otherwise use flat shading.
			if (item.TextureHandle.HasValue) {
				_shader.SetSamplerTexture("uTexture", item.TextureHandle.Value, 0);
				_shader.SetInt("uTextureEnabled", 1);
			} else {
				_shader.SetInt("uTextureEnabled", 0);
			}

			item.Mesh.Draw();
		}
	}

	/// <summary>The texture unit the terrain's paint ranks are sampled from.</summary>
	private const int PaintRankUnit = 3;

	/// <summary>
	/// This pass's side of the original's submit: rebuilds the zone's visible region for the pass's
	/// view, as <c>Terrain_SetupVisibleRegion</c> does before the submit, and files the layer's objects
	/// by it. Returns the walk's order, which the ground shapes are ranked by.
	/// </summary>
	private static TerrainPaintOrder FileObjects(Camera camera, float aspect, GroundShapeLayer ground) {
		var grid = ground.Grid;
		var viewer = camera.Position;
		ground.Region.Update(grid, viewer, camera.ViewRotation, camera.EdgeSlopes(aspect));
		var order = TerrainPaintOrder.For(grid, viewer, camera.SimHeading);
		ground.Objects.File(grid, ground.Region, viewer, order);
		return order;
	}

	/// <summary>
	/// The ground shapes' side of it: files each shape under the cell
	/// <see cref="HeightGrid.PickDrawCell"/> picks, drops it when this pass's draw does not reach that
	/// cell (<see cref="ObjectDrawTable.CellDrawn"/>), takes the cell's rank in this view's walk, and
	/// draws the terrain's ranks for the shapes' fragments to test against. The structures' ground
	/// planes (<see cref="GroundShapeLayer.ObjectGround"/>) take the rank of the cell their object was
	/// filed under. Returns everything in the order to draw it.
	/// </summary>
	private List<(SceneItem Item, uint Rank)> RankGroundShapes(Camera camera, Matrix4x4 projection,
			GroundShapeLayer ground, TerrainPaintOrder order,
			int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		var grid = ground.Grid;
		var viewer = camera.Position;

		var ranked = new List<(SceneItem Item, uint Rank, int Distance)>(
			ground.Shapes.Count + ground.ObjectGround.Count);
		foreach (var shape in ground.Shapes) {
			var cell = grid.PickDrawCell(shape.Position, shape.Radius, viewer, ground.Region);
			if (ground.ShapesFollowWalk && cell is { } filed && !ground.Objects.CellDrawn(filed)) {
				continue;
			}

			ranked.Add((shape.Item, RankOf(cell), -1));
		}

		// A structure's ground plane, under the cell its object was filed under this pass. Within that
		// cell it comes after the cell's ground shapes, which ObjList_DrawCellObjects (00428c60) draws on
		// the spot before the rest, and with the cell's other structures farthest first, the order
		// ObjList_DrawSorted (00429620) paints them in. An entry this pass does not draw is skipped by the
		// draw itself.
		foreach (var item in ground.ObjectGround) {
			if (item.Filing is not { } entry) {
				continue;
			}

			ranked.Add((item, RankOf(entry.Cell), entry.Position.ApproxDistanceTo(viewer)));
		}

		// Stable, so shapes sharing a cell keep the order they were given in.
		var result = ranked.OrderBy(entry => entry.Rank).ThenByDescending(entry => entry.Distance < 0)
			.ThenByDescending(entry => entry.Distance)
			.Select(entry => (entry.Item, entry.Rank)).ToList();

		_paintRanks ??= new TerrainPaintRankBuffer(_gl);
		_paintRanks.Draw(ground.Terrain, grid, order, camera.ViewMatrix, projection,
			viewportX, viewportY, viewportWidth, viewportHeight);

		return result;

		// A cell's place in this view's walk; the no-cell bucket is drawn after all of it.
		uint RankOf((int X, int Y)? cell) =>
			cell is { } filed ? order.Rank(filed.X, filed.Y) : TerrainPaintOrder.AfterTerrain;
	}

	/// <summary>
	/// Whether <see cref="EffectLights"/> holds a slot bright enough to light anything — the test
	/// <c>LightManager_SelectLightsForObject</c> (<c>00407098</c>) makes per slot, hoisted out of the draw loop.
	/// </summary>
	private bool HasLiveEffectLight() {
		if (EffectLights is not { } field) {
			return false;
		}

		for (int i = 0; i < field.Slots.Count; i++) {
			if (field.Slots[i].IsLive) {
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Paints the panel's sky before any geometry goes into it, as <c>Scene_DrawTerrainPass</c> paints
	/// the <c>hzline</c> before the terrain. Depth-testing and depth-writing are both off, so this is
	/// a background fill rather than something at the far plane — the scene draws straight over it and
	/// nothing needs the far plane to sit beyond the sky. The line and the band rule are
	/// <see cref="Content.SkyGradient"/>'s; this hands them to Sky.glsl.
	/// </summary>
	private void DrawSky(Camera camera, int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		if (Sky is not { } sky) {
			return;
		}

		int height = System.Math.Max(viewportHeight, 1);
		float focalPixels = height / (2f * MathF.Tan(camera.FieldOfView / 2f));

		// PrincipalPoint is measured from the viewport's top-left, y down; the shader works in window
		// coordinates, y up from the bottom of the whole framebuffer.
		var centre = new Vector2(
			viewportX + camera.PrincipalPoint.X * viewportWidth,
			viewportY + (1f - camera.PrincipalPoint.Y) * height);
		var line = sky.Place(unchecked((short)camera.Pitch), unchecked((short)camera.Roll), centre, focalPixels);

		_skyShader.Use();
		for (int band = 0; band < sky.Bands.Length; band++) {
			_skyShader.SetVector3($"uBands[{band}]", sky.Bands[band]);
		}

		_skyShader.SetInt("uBandCount", sky.Bands.Length);
		_skyShader.SetFloat("uBandHeight", sky.BandHeight);
		_skyShader.SetFloat("uGap", sky.HorizonGap);
		_skyShader.SetVector2("uLineMid", line.Mid);
		_skyShader.SetVector2("uLineUp", line.Up);
		_skyShader.SetFloat("uScale", line.Scale);
		_skyShader.SetInt("uRolled", line.Rolled ? 1 : 0);
		_skyShader.SetFloat("uCosRoll", line.CosRoll);

		_gl.Disable(EnableCap.DepthTest);
		_gl.DepthMask(false);
		_gl.BindVertexArray(_skyVertexArray);
		_gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
		_gl.BindVertexArray(0);
		_gl.DepthMask(true);
		_gl.Enable(EnableCap.DepthTest);
	}

	public void Dispose() {
		_shader.Dispose();
		_skyShader.Dispose();
		_shadeRampTexture?.Dispose();
		_paletteRampTexture?.Dispose();
		_impactShadeRampTexture?.Dispose();
		_impactPaletteRampTexture?.Dispose();
		_paintRanks?.Dispose();
		_gl.DeleteVertexArray(_skyVertexArray);
	}
}
