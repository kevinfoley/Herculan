using System.Numerics;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dgs;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Anim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Anim;
using Herculan.Engine.World;

namespace Herculan.Engine.Scene;

/// <summary>
/// One drawable model, built once and shared by every object of its type. A mission that fields
/// three ACHILLES and five of one structure holds two entries here, not eight.
/// </summary>
/// <param name="Key">Stable identity, e.g. <c>dts\ACHILLES.DTS#0</c>.</param>
/// <param name="Mesh">Triangles, outline edges then points in model space, ready to upload — see
/// <see cref="MeshBuild"/>. A structure's ground plane is not in it but in
/// <paramref name="GroundMesh"/>.</param>
/// <param name="TriangleVertexCount">Where <paramref name="Mesh"/>'s outline range starts.</param>
/// <param name="Atlas">
/// The model's packed texture bank, or null when no bank could be resolved — in which case the
/// mesh's UVs mean nothing and it must be drawn untextured.
/// </param>
/// <param name="HeightWorldUnits">Height of the model's bounding box, in world units.</param>
/// <param name="Segments">
/// The same geometry split by the node that places each part, for an object whose shape animates —
/// see <see cref="DtsMeshBuilder.BuildSegments"/>. A caller draws either <paramref name="Mesh"/> or
/// these, never both: they are the same triangles twice.
/// </param>
/// <param name="Sprites">
/// The shape's billboards, a frame at a time — see <see cref="DtsSpriteBuilder.Build"/>. Empty for
/// ordinary geometry; for a shape that is <i>only</i> billboards (every EMP round, every impact
/// effect) this is the whole model and <paramref name="Mesh"/> is empty instead. A caller draws both
/// where both exist: they are different parts of one shape, not two versions of it.
/// </param>
/// <param name="Cells">
/// The same geometry as <paramref name="Mesh"/> split by the cell each piece stands on, for an
/// object whose shape loses parts to damage but does not animate — see
/// <see cref="DtsMeshBuilder.BuildCells"/> — or by <c>TSDetailPart</c> level alone, for a weapon, a
/// launcher round or a piece of debris, whose levels are chosen per frame but whose flipbook cell is
/// already baked (<see cref="DtsMeshBuilder.BuildDetailLevels"/>). A caller draws either
/// <paramref name="Mesh"/> or these, never both. Empty for every roster that does not ask for it, and
/// for a leveled shape that has no detail part.
/// </param>
/// <param name="PointVertexCount">How many vertices at the end of <paramref name="Mesh"/> are points.</param>
/// <param name="ShapeRadius">
/// The root's own <c>TSBasePart.Radius</c>, the <c>shape+8</c> that <c>SimObject_GetShapeRadius</c>
/// (<c>0046b80c</c>) reports for an object drawing this shape. Zero for a root that is not a
/// <c>TSBasePart</c>.
/// </param>
/// <param name="GroundMesh">
/// A structure's ground plane at the rest pose <paramref name="Mesh"/> is built at — see
/// <see cref="MeshCell.Ground"/> — for a caller that draws the model whole and the ground plane with
/// the ground. Null for every other model, and empty for a structure that has none.
/// </param>
public sealed record SceneModel(
	string Key, MeshVertex[] Mesh, int TriangleVertexCount, TextureAtlas? Atlas,
	int HeightWorldUnits, MeshSegment[] Segments, SpriteQuad[][] Sprites,
	MeshCell[] Cells, int PointVertexCount = 0, int ShapeRadius = 0, MeshBuild? GroundMesh = null);

/// <summary>
/// Loads and caches the models a mission needs, keyed so identical unit types share one mesh and one
/// atlas.
///
/// <para>Three resolution paths, one per roster, each following the original's own selection rule:</para>
/// <list type="bullet">
/// <item><b>Mechs</b> — every root of <c>dts\&lt;name&gt;.DTS</c>, which are LOD variants of the one
/// chassis and are drawn one at a time (see <see cref="MechDetailRoots"/>), textured by the bank
/// <c>HercSimDat.TextureGroup</c> selects (see docs/retail/rendering/dts-texture-binding.md).</item>
/// <item><b>Flyers</b> — <c>dts\&lt;name&gt;.DTS</c> root 0, textured from <c>ENEMY.DBA</c>: the flyer
/// type loader binds one fixed slot rather than choosing by chassis, and that slot is the Cybrid
/// mechs' own (see <see cref="FlyerTextureGroup"/>).</item>
/// <item><b>Structures</b> — either a root of <c>dts\BASES_AN.DTS</c> (the 8 animated types) or a
/// record of <c>dgs\BASES.DGS</c> (the other 57, static types), textured by the bank
/// <c>dat\BASES.DAT</c> names — see <see cref="BasesDgsTransformer"/> for how the latter resolves
/// to the same drawable shape as the former.</item>
/// </list>
///
/// <para>Every path is best-effort: a missing file or an unresolvable bank yields null or an
/// untextured model rather than throwing, because one unknown unit type is not a reason to refuse to
/// show a mission.</para>
/// </summary>
public sealed class SceneModelLibrary {
	private readonly GameContent _content;
	private readonly DynamixPalette? _palette;
	private readonly SurfaceShading? _shading;
	private readonly SurfaceShading? _impactShading;

	private readonly Dictionary<string, SceneModel?> _models = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TextureAtlas?> _atlases = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, DynamixThreeSpaceModel?> _files = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, GridShapeLibrary?> _shapeLibraries = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<(string Library, int Index), ShapeVolume?> _volumes = new();
	private readonly Dictionary<string, HercSimDat?> _mechData = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, FlyerSimData?> _flyerData = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ShapeAnimation?> _animations = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, GunLayout?> _hardpoints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ColliderNode[]> _collision = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, HercSimDamage?> _damageData = new(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, FlightModel?> _flightModels = new(StringComparer.OrdinalIgnoreCase);

	// The mech shapes whose crude roots have been renumbered in place — see LoadMechShape.
	private readonly HashSet<string> _remappedMechShapes = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// The folder hardpoint lists live in — <c>ResourcePath_BuildFolderName(name, "gl")</c> at the
	/// head of the mech-type loader (<c>FUN_00420298</c>).
	/// </summary>
	private const string GunLayoutFolder = "gl";

	/// <summary>
	/// <paramref name="theater"/> supplies the palette every bank in this mission decodes against —
	/// the WORLD&lt;n&gt; palettes are per-theater, loaded once by <c>maybe_World_LoadTheater</c> and
	/// active for everything the theater draws, so a mech's colours depend on where it is standing.
	/// </summary>
	public SceneModelLibrary(GameContent content, TheaterDescriptor theater) {
		_content = content;

		byte[]? paletteBytes = content.Read("dpl", theater.PaletteName + ".DPL");
		_palette = paletteBytes != null
			? new DynamixPaletteTransformer().Parse(paletteBytes) as DynamixPalette
			: null;

		// The theater ships its colour ramp beside its palette, under the same base name, and a flat
		// solid face is nothing without it — see DtsMeshBuilder's ResolveSolidColors.
		_shading = ShadeRamp.Load(content, theater.PaletteName) is { } ramp
			? new SurfaceShading(ramp, _palette)
			: null;

		// Every lit flat surface — which is nearly every surface of a HERC or a building — takes its
		// colour from the palette's shade-ramp table, so without one they all come out
		// DtsMeshBuilder.FallbackColor grey. Every retail theater palette carries the table, so this
		// means the palette failed to load or is not one of the game's.
		if (_shading is not { HasShadeRamps: true }) {
			Console.Error.WriteLine(
				$"WARNING: theater palette {theater.PaletteName}.DPL has no shade-ramp table; " +
				"lit flat surfaces will draw untextured and unlit.");
		}

		// The same theater, through its damage-flash palette instead. Only the palette changes: the
		// original swaps the palette object alone and there is no IMPACT<n>.RMP in retail data, so the
		// ramp below is deliberately the theater's own.
		byte[]? impactBytes = content.Read("dpl", theater.ImpactPaletteName + ".DPL");
		var impactPalette = impactBytes != null
			? new DynamixPaletteTransformer().Parse(impactBytes) as DynamixPalette
			: null;

		_impactShading = impactPalette != null && _shading != null
			? _shading with { Palette = impactPalette }
			: null;
	}

	/// <summary>Every model built so far, in first-requested order.</summary>
	public IEnumerable<SceneModel> Models => _models.Values.Where(m => m != null)!;

	/// <summary>
	/// The theater's ramp and palette, or null when the ramp did not load. Exposed because the same
	/// pair that colours a flat solid face also carries the zone's fog colour — see
	/// <see cref="Atmosphere"/>.
	/// </summary>
	public SurfaceShading? Shading => _shading;

	/// <summary>
	/// The same pair with the theater's <c>IMPACT&lt;n&gt;.DPL</c> in place of its ordinary palette —
	/// what the whole scene is drawn through while the cockpit damage flash is up. Null when that
	/// palette is missing, in which case the flash simply does not recolour anything. See
	/// docs/retail/rendering/cockpit-canopy-palette.md, "The damage shake".
	/// </summary>
	public SurfaceShading? ImpactShading => _impactShading;

	/// <summary>The mech type's stats, or null when the install has no <c>dat\&lt;name&gt;.DAT</c>.</summary>
	public HercSimDat? MechData(string mechName) {
		if (_mechData.TryGetValue(mechName, out var cached)) {
			return cached;
		}

		byte[]? bytes = _content.Read("dat", mechName + ".DAT");
		var data = bytes != null
			? new HercSimDataTransformer().Parse(bytes) as HercSimDat
			: null;

		_mechData[mechName] = data;
		return data;
	}

	/// <summary>
	/// The mech type's hardpoint list, <c>gl\&lt;NAME&gt;.GL</c> — where its weapons sit, which
	/// cockpit row each owns and which slot of a fit each draws from. Null when the install has none,
	/// in which case the machine is fitted with nothing at all: the fit is addressed through this
	/// list, so without it there is nothing to address.
	/// </summary>
	public GunLayout? MechHardpoints(string mechName) {
		if (_hardpoints.TryGetValue(mechName, out var cached)) {
			return cached;
		}

		byte[]? bytes = _content.Read(GunLayoutFolder, mechName + ".GL");
		var data = bytes != null
			? new GunLayoutTransformer().Parse(bytes) as GunLayout
			: null;

		_hardpoints[mechName] = data;
		return data;
	}

	/// <summary>
	/// A mech or flyer type's hit-sphere model, <c>col\&lt;NAME&gt;.COL</c> — empty when the install
	/// ships none, which on retail data is true of <c>HOVTANK</c> and <c>DROPSHIP</c> and of nothing
	/// else. Shared per type: the model is read in the type's own space and posed per object.
	/// </summary>
	public ColliderNode[] Collision(string typeName) {
		if (_collision.TryGetValue(typeName, out var cached)) {
			return cached;
		}

		var model = CollisionModelReader.Load(_content, typeName);
		_collision[typeName] = model;
		return model;
	}

	/// <summary>
	/// A mech or flyer type's component health record, <c>dmg\&lt;NAME&gt;.DMG</c>. Shared per type
	/// because it is the <i>maxima</i>, not the damage: the damage is per object, in
	/// <see cref="ComponentDamage"/>.
	/// </summary>
	public HercSimDamage? DamageData(string typeName) {
		if (_damageData.TryGetValue(typeName, out var cached)) {
			return cached;
		}

		byte[]? bytes = _content.Read(DamageFolder, typeName + ".DMG");
		var data = bytes != null
			? new HercDamageFileTransformer().Parse(bytes) as HercSimDamage
			: null;

		_damageData[typeName] = data;
		return data;
	}

	/// <summary>
	/// The folder component health records live in — <c>ResourcePath_BuildFolderName(name, "dmg")</c>
	/// in both the mech constructor and the flyer type loader.
	/// </summary>
	private const string DamageFolder = "dmg";

	/// <summary>
	/// A flyer chassis' flight parameters, <c>fm\&lt;NAME&gt;.FM</c>. Only <c>RAZOR</c> and
	/// <c>SKIMMER</c> ship one, and <c>MechType_InitOne</c> (<c>004201a8</c>) only looks for it when
	/// the type's own record sets the flyer flag — so a null here for a walker is the normal case,
	/// not a missing file.
	/// </summary>
	public FlightModel? FlightModelFor(string typeName) {
		if (_flightModels.TryGetValue(typeName, out var cached)) {
			return cached;
		}

		byte[]? bytes = _content.Read(FlightModelFolder, typeName + ".FM");
		var data = bytes != null
			? new FlightModelTransformer().Parse(bytes) as FlightModel
			: null;

		_flightModels[typeName] = data;
		return data;
	}

	/// <summary>The folder flight models live in — <c>ResourcePath_BuildFolderName(name, "fm")</c>.</summary>
	private const string FlightModelFolder = "fm";

	/// <summary>The flyer type's stats, or null — only <c>SKIMMER</c> ships one.</summary>
	public FlyerSimData? FlyerData(string flyerName) {
		if (_flyerData.TryGetValue(flyerName, out var cached)) {
			return cached;
		}

		byte[]? bytes = _content.Read("dat", flyerName + ".DAT");
		var data = bytes != null
			? new FlyerSimDataTransformer().Parse(bytes) as FlyerSimData
			: null;

		_flyerData[flyerName] = data;
		return data;
	}

	/// <summary>
	/// A mech type's animation data, or null when its <c>.DTS</c> is missing or carries no
	/// <c>ANAnimList</c>. Shared per type, as the model is: an animation thread holds only a cursor
	/// into it, so several machines of one type play the same data independently.
	/// </summary>
	public ShapeAnimation? MechAnimation(string mechName) {
		if (_animations.TryGetValue(mechName, out var cached)) {
			return cached;
		}

		var animation = ShapeAnimation.FromModel(LoadMechShape(mechName));
		_animations[mechName] = animation;
		return animation;
	}

	/// <summary>
	/// The model for a mech type, or null when its <c>.DTS</c> is missing or empty.
	///
	/// <para>The chassis' <b>hardpoint attachment slots are left out</b> — see
	/// <see cref="DtsMeshBuilder.AttachmentPartIds"/>. DBSIM overwrites those parts on every frame of
	/// every machine before it draws one, so their shipped geometry appears nowhere in the original;
	/// building them into the mesh stood a flat untextured plate at every hardpoint. The weapon that
	/// belongs there is drawn separately, from <see cref="MechWeapon"/>.</para>
	/// </summary>
	public SceneModel? Mech(string mechName, int rootIndex = 0) {
		string? bankName = MechData(mechName) is { } data
			? HercSimDat.TextureGroupDbaBaseName(data.TextureGroup)
			: null;

		// A crude root's transform ids are root 0's once renumbered, so its parts are placed through
		// root 0's animation list rather than the one the root carries.
		var poseList = rootIndex > 0 && LoadMechShape(mechName)?.Roots is { Count: > 0 } roots
			? ShapeAnimation.FirstAnimList(roots[0])
			: null;

		return Build(mechName + ".DTS", rootIndex, bankName, segmented: true,
			hiddenPartIds: DtsMeshBuilder.AttachmentPartIds(MechHardpoints(mechName)), poseList: poseList);
	}

	/// <summary>
	/// Every root of a machine's shape, finest first — the alternate models
	/// <c>Shape_DrawAtDetailLevel</c> picks between each frame (see <see cref="ShapeDetail"/>).
	/// Empty when the <c>.DTS</c> is missing.
	///
	/// <para>Every root is posed by root 0's one animation, which is what the load-time renumbering
	/// (<see cref="LoadMechShape"/>) makes possible.</para>
	///
	/// <para>The chain stops at the first root that fails to build, because
	/// <see cref="ShapeDetail.SelectRoot"/> walks it by index and a hole would move every root past
	/// it.</para>
	/// </summary>
	public IReadOnlyList<SceneModel> MechDetailRoots(string mechName) {
		int count = LoadMechShape(mechName)?.Roots?.Count ?? 0;
		var roots = new List<SceneModel>(count);

		for (int i = 0; i < count; i++) {
			if (Mech(mechName, i) is not { } root) {
				break;
			}

			roots.Add(root);
		}

		return roots;
	}

	/// <summary>
	/// A machine's own bounding radius in world units — root 0's <c>TSBasePart.Radius</c>, the
	/// <c>shape+8</c> that <c>Shape_DrawAtDetailLevel</c> measures its projected size from. Zero
	/// when the shape is missing.
	///
	/// <para>The radius is taken from root 0 whichever root is being drawn, because the original
	/// restores root 0 into the shape instance after every draw and so measures root 0's every
	/// time.</para>
	/// </summary>
	public int MechShapeRadius(string mechName) =>
		LoadMechShape(mechName)?.Roots is { Count: > 0 } roots && roots[0] is TSBasePart root ? root.Radius : 0;

	/// <summary>
	/// A machine's shape file, with every root after the first renumbered onto root 0's node
	/// numbering the first time it is asked for, as <c>MechType_InitOne</c> does straight after
	/// loading the roots — see <see cref="MechDetailRootRemap"/>. Every mech path goes through here,
	/// so no root is built or read before it is renumbered.
	/// </summary>
	private DynamixThreeSpaceModel? LoadMechShape(string mechName) {
		var model = LoadDts(mechName + ".DTS");
		if (model?.Roots is { } roots && _remappedMechShapes.Add(mechName)
				&& MechData(mechName) is { } data) {
			MechDetailRootRemap.Apply(roots, data.ModelLoDBoneIds);
		}

		return model;
	}

	/// <summary>
	/// The model for a flyer type, or null when the install has no <c>.DTS</c> for it. Split by cell
	/// rather than by node: a flyer loses components like a machine but is drawn rigid, so a
	/// destroyed part has to be able to stop being drawn without the shape animating — see
	/// <see cref="DtsMeshBuilder.BuildCells"/>.
	/// </summary>
	public SceneModel? Flyer(string flyerName) =>
		Build(flyerName + ".DTS", 0, HercSimDat.TextureGroupDbaBaseName(FlyerTextureGroup),
			celled: true);

	/// <summary>
	/// Which texture group every flyer chassis is drawn from — <b>3, the Cybrid mechs' own
	/// <c>ENEMY.DBA</c></b>.
	///
	/// <para>Where a HERC picks its bank per chassis (<c>MechType_InitOne</c> writes
	/// <c>&amp;g_MechTextureGroupSlots + TextureGroup*8</c> into the shape's <c>+0x26</c>), the flyer
	/// type loader (<c>FlyerType_LoadResources</c>, <c>00422ed0</c>) writes a <i>literal</i>
	/// slot address, <c>0x004a9e0e</c>. That is <c>g_MechTextureGroupSlots</c> (<c>004a9df6</c>) plus
	/// <c>3 * 8</c>, so every flyer type shares one bank and it is the enemy one — which makes sense
	/// of a roster that is entirely Cybrid. See docs/retail/rendering/dts-texture-binding.md.</para>
	/// </summary>
	public const short FlyerTextureGroup = 3;

	/// <summary>
	/// The shape a travelling shot is drawn as — a root of <c>dts\BULLETS.DTS</c>, textured from
	/// <c>dba\BULLETS.DBA</c>. Both come from the bullet module's own init (<c>Bullet_LoadResources</c>, <c>0040ade0</c>),
	/// which loads the shape file into <c>DAT_004a9784</c> and binds that one bank to every shape in
	/// it; <paramref name="modelId"/> is the <c>BULLETS.DAT</c> record's first field, which
	/// <c>Bullet_Construct</c> uses as the index into that array.
	///
	/// <para>Retail ships nine roots and the twelve records between them name all nine, but two of
	/// them — roots 2 and 3, which are the <b>three EMP cannons' rounds</b> — are not geometry at
	/// all: they are a <c>TSCellAnimPart</c> of five <c>TSBitmapPart</c>s, a flipbook of billboard
	/// sprites out of the same <c>.DBA</c>. <see cref="DtsMeshBuilder"/> builds triangles and those
	/// roots have none, so their mesh is empty and their <see cref="SceneModel.Sprites"/> is
	/// everything — see <see cref="DtsSpriteBuilder"/>. The plasma round (root 8) is a two-frame cell
	/// animation over real geometry and draws its first frame; every autocannon round is plain static
	/// geometry.</para>
	///
	/// <para>The bank is decoded with index 0 transparent, which only the sprite roots read: the
	/// other seven are <c>TSSolidPoly</c> geometry coloured through the theater ramp and never sample
	/// it at all.</para>
	/// </summary>
	public SceneModel? Bullet(int modelId) =>
		Build(BulletLibraryName, modelId, BulletBankName, transparentBank: true);

	/// <summary>The shape file <c>Bullet_LoadResources</c> (<c>0040ade0</c>) opens, by the literal name <c>bullets</c>.</summary>
	public const string BulletLibraryName = "BULLETS.DTS";

	/// <summary>And the bank it binds to every shape in it, opened by the same literal.</summary>
	public const string BulletBankName = "BULLETS";

	/// <summary>
	/// The shapes a launcher's round is drawn as — a root of <c>dts\ROCKETS.DTS</c>, one entry per
	/// cell of its flipbook, and <b>untextured</b>. <c>Rocket_LoadTypeTable_Unguided</c>
	/// (<c>0040a818</c>) loads the table and the shape file and stops: unlike the bullet module's init
	/// it opens no <c>.DBA</c> and binds no bank to any of the shapes it just read, and there is no
	/// <c>ROCKETS.DBA</c> in the install to open. So a rocket is <c>TSSolidPoly</c> geometry coloured
	/// through the theater's own ramp, the same way a flyer with no bank is.
	///
	/// <para><b>Why a list.</b> A rocket's <see cref="TSCellAnimPart"/>s hold <i>geometry</i>, not
	/// billboards — the flipbook is the exhaust flame, two alternate cones of flat polys at the tail
	/// in the palette's red/orange/white range (indices 109, 94/93, 87/88, 86), sitting beside the
	/// static grey body (index 200). Both retail roots carry one sequence of two cells, and both
	/// cell-anim parts in each read sequence 0 — the same sequence every <c>ROCKETS.DAT</c> record
	/// names. So the record's frame interval really does drive them, and the shape has to be built
	/// once per cell for the flame to move. See <see cref="DtsMeshBuilder.CellFrameCount"/>.</para>
	///
	/// <para><paramref name="modelId"/> is the <c>ROCKETS.DAT</c> record's first field. Retail ships
	/// two roots and the five records name both: root 0 for the four ordinary rockets, root 1 for
	/// <c>BMSL</c>.</para>
	/// </summary>
	/// <returns>The cells in order, or empty when the shape file or the index is missing.</returns>
	public IReadOnlyList<SceneModel> Rocket(int modelId) {
		if (Root(RocketLibraryName, modelId) is not { } root) {
			return Array.Empty<SceneModel>();
		}

		var cells = new List<SceneModel>();
		for (int cell = 0; cell < DtsMeshBuilder.CellFrameCount(root); cell++) {
			if (Build(RocketLibraryName, modelId, bankName: null, cellFrame: cell, leveled: true) is { } model) {
				cells.Add(model);
			}
		}

		return cells;
	}

	/// <summary>The shape file <c>Rocket_LoadTypeTable_Unguided</c> opens, by the literal name <c>rockets</c>.</summary>
	public const string RocketLibraryName = "ROCKETS.DTS";

	/// <summary>
	/// The model a fitted weapon is drawn as, one entry per cell of its muzzle-flash flipbook — a
	/// root of <c>dts\MECHWPNS.DTS</c>, textured from <c>dba\WPNTEX.DBA</c>.
	///
	/// <para><b>The muzzle flash is the weapon's own model.</b> DBSIM spawns no separate effect for
	/// it: the base mount constructor (<c>WeaponMount_CtorBase</c>, <c>0040df30</c>) gives every visibly-mounted hardpoint a
	/// private copy of this shape through <c>WeaponMount_ShapeForMountingCode</c> (<c>0040fab0</c>), and firing steps that copy's
	/// <see cref="TSCellAnimPart"/>s one cell a tick. Cell zero is the gun at rest and the rest are
	/// the flash, as real geometry rather than billboards — see
	/// <see cref="Sim.WeaponMount.FlashCell"/>.</para>
	///
	/// <para>The bank binding is <c>WeaponMount_ShapeForMountingCode</c>'s own <c>shape+0x26 = &amp;DAT_004a9b6c</c>,
	/// which is the atlas <c>Weapons_LoadResourceTables</c> packed <c>wpntex</c> into and hands to
	/// every shape in <c>mechwpn2</c> as well.</para>
	///
	/// <para><paramref name="shapeIndex"/> is one of the four
	/// <see cref="Weapons.WeaponMountTemplate.ModelShapeIndex"/> entries — which one depends on how
	/// the hardpoint hangs off the chassis, so the same gun is a different root on a left-side mount
	/// than on a top one.</para>
	/// </summary>
	/// <returns>The cells in order, or empty when the shape file or the index is missing.</returns>
	public IReadOnlyList<SceneModel> MechWeapon(int shapeIndex) {
		if (Root(MechWeaponLibraryName, shapeIndex) is not { } root) {
			return Array.Empty<SceneModel>();
		}

		var cells = new List<SceneModel>();
		for (int cell = 0; cell < DtsMeshBuilder.CellFrameCount(root); cell++) {
			// Opaque, like a machine's own bank and unlike a billboard's: these are ordinary textured
			// polys and index 0 is a colour in them, not a hole.
			if (Build(MechWeaponLibraryName, shapeIndex, MechWeaponBankName, cellFrame: cell,
					leveled: true) is { } model) {
				cells.Add(model);
			}
		}

		return cells;
	}

	/// <summary>
	/// How long one weapon shape's flipbook is — <see cref="DtsMeshBuilder.CellFrameCount"/> over the
	/// same root, which is what the mounts need and all they need. Zero for a shape the install does
	/// not have; one for a shape with no flipbook at all, which is every pod.
	/// </summary>
	public int MechWeaponCellCount(int shapeIndex) =>
		Root(MechWeaponLibraryName, shapeIndex) is { } root ? DtsMeshBuilder.CellFrameCount(root) : 0;

	/// <summary>The shape file <c>WeaponModels_LoadShapeChunks</c> (<c>0040f998</c>) opens, by the literal name <c>mechwpns</c>.</summary>
	public const string MechWeaponLibraryName = "MECHWPNS.DTS";

	/// <summary>And the bank <c>Weapons_LoadResourceTables</c> binds to every shape in it, by the literal <c>wpntex</c>.</summary>
	public const string MechWeaponBankName = "WPNTEX";

	/// <summary>
	/// The model for a structure type. <see cref="BaseShapeSource.AnimatedLibrary"/> types are a
	/// root of <c>dts\BASES_AN.DTS</c>; <see cref="BaseShapeSource.StaticLibrary"/> types are a
	/// record of <c>dgs\BASES.DGS</c> — see <see cref="BasesDgsTransformer"/> for how that record's
	/// embedded DTS subtree resolves to the same <see cref="TSObject"/> shape either path builds
	/// from. Null only when the install is missing the relevant file or the index is out of range.
	///
	/// <para>Split by cell, so that a collapsing part can be redrawn as its rubble — see
	/// <see cref="DtsMeshBuilder.BuildCells"/> and <see cref="Sim.BaseObject.CellFrames"/> — and with its
	/// ground plane apart from the rest, which is drawn with the ground (<see cref="MeshCell.Ground"/>).</para>
	///
	/// <para>An <see cref="BaseShapeSource.AnimatedLibrary"/> type is split <b>by node as well</b>,
	/// because its shape moves: a radar mast's dish free-runs and an armed tower's turret is seeked
	/// to its aim. Those are the eight types <see cref="BaseType.AnimThreadCount"/> is non-zero for,
	/// and they are exactly the roots of <c>BASES_AN.DTS</c>. A <see cref="MeshSegment"/> carries a
	/// <see cref="CellGate"/> of its own, so the segments are the two splits at once and a caller
	/// drawing them needs the cells for nothing — but both are built, because which one is drawn is
	/// the <i>object</i>'s question (a shape instance with no thread on it has nothing to pose the
	/// nodes with) and the model is shared by every object of the type.</para>
	/// </summary>
	public SceneModel? Base(BaseType type) =>
		type.Source == BaseShapeSource.AnimatedLibrary
			? Build(BaseTypeTable.AnimatedLibraryName, type.ShapeIndex, type.TextureBankName,
				segmented: true, transparentBank: true, celled: true, splitGround: true)
			: BuildFromShapeLibrary(BaseTypeTable.StaticLibraryName, type.ShapeIndex, type.TextureBankName,
				transparentBank: true, celled: true, splitGround: true);

	/// <summary>
	/// The hit geometry a structure type's shape carries, as distinct from the geometry it is drawn
	/// from: the coarse collision volume in its <c>.DGS</c> record and that record's own stated
	/// bounding radius. Both are null/zero for an <see cref="BaseShapeSource.AnimatedLibrary"/> type,
	/// whose shape is an ordinary DTS and has neither — see <see cref="Sim.BaseObject"/> for why
	/// that costs nothing on retail data.
	/// </summary>
	public (int BoundingRadius, ShapeVolume? Volume) GridShapeCollision(BaseType type) =>
		type.Source == BaseShapeSource.AnimatedLibrary
			? (0, null)
			: GridShapeCollision(BaseTypeTable.StaticLibraryName, type.ShapeIndex) ?? (0, null);

	/// <summary>
	/// The same pair for a type's wreck, the <see cref="BaseType.HulkTypeIndex"/> record of
	/// <c>dgs\BHULKS.DGS</c> — what <see cref="Sim.BaseObject"/> is tested against once it shows its
	/// hulk. Null for a type that leaves no wreck, and when the install has no such record.
	/// </summary>
	public (int BoundingRadius, ShapeVolume? Volume)? HulkCollision(BaseType type) =>
		type.HulkTypeIndex < 0 ? null : GridShapeCollision(HulkLibraryName, type.HulkTypeIndex);

	private (int BoundingRadius, ShapeVolume? Volume)? GridShapeCollision(string library, int index) {
		if (LoadShapeLibrary(library)?.Shapes is not { Length: > 0 } shapes
				|| index < 0 || index >= shapes.Length) {
			return null;
		}

		var shape = shapes[index];
		if (!_volumes.TryGetValue((library, index), out var volume)) {
			volume = shape.Collision.IsSolid ? new ShapeVolume(shape.Collision) : null;
			_volumes[(library, index)] = volume;
		}

		return (shape.BoundingRadius, volume);
	}

	/// <summary>
	/// One root of <c>dts\EXPLOS.DTS</c> — an impact effect's shape, a flipbook of billboards out of
	/// whichever <c>dba\EXPLO&lt;n&gt;.DBA</c> its <c>EXPLOS.DAT</c> row names. Both the shape file
	/// and the fifteen banks come from the effect subsystem's own init (<c>Explosion_LoadResources</c>, <c>00407b54</c>), which
	/// writes each bank straight into its shape's bound-bank pointer; this is that binding, expressed
	/// as which atlas the model carries.
	/// </summary>
	public SceneModel? Explosion(int shapeIndex, int textureBankIndex) =>
		Build(ExplosionCatalog.ShapeLibraryName, shapeIndex,
			ExplosionCatalog.TextureBankPrefix + textureBankIndex, transparentBank: true);

	/// <summary>
	/// One root of a debris shape file — <c>dts\{name}_DEB.DTS</c>, or
	/// <see cref="Sim.WeaponMount.DebrisShapeLibraryName"/> for a gun shot off its mount.
	/// <c>Debris_LoadDatabase</c> loads the file and, when its caller supplies one, binds a single bank to
	/// every shape in it: <c>MechType_InitOne</c> passes the chassis' own texture group, so a
	/// machine's wreckage is painted in the machine's colours, while the two shared tables
	/// (<c>DEF_DEB</c> and <c>BASE_DEB</c>) are loaded with no bank at all and draw flat-shaded
	/// through the theater ramp.
	/// </summary>
	public SceneModel? Debris(string shapeLibrary, int shapeIndex, string? bankName) =>
		Build(shapeLibrary, shapeIndex, bankName, leveled: true);

	/// <summary>How many roots a debris shape file has, or zero when the install has none of it.</summary>
	public int ShapeCount(string shapeLibrary) => LoadDts(shapeLibrary)?.Roots?.Count ?? 0;

	/// <summary>
	/// The drop pod in the air — root 0 of <c>dts\METEOR.DTS</c>, textured from
	/// <c>dba\IMPACT.DBA</c>. <c>Meteor_LoadResources</c> (<c>00409a34</c>) loads the shape group
	/// and binds that one bank into every shape in it.
	/// </summary>
	public SceneModel? DropPod() =>
		Build(Sim.MeteorObject.ShapeLibraryName, Sim.MeteorObject.FallingShapeIndex,
			Sim.MeteorObject.TextureBankName);

	/// <summary>
	/// And the pod on the ground — root 1 of the same file, one entry per cell of its opening
	/// flipbook. The original keeps this as a <i>second</i> shape instance on the object
	/// (<c>obj+0x41</c>) and swaps to it the moment the pod lands; see <c>Meteor_Render</c>
	/// (<c>00409cd0</c>).
	/// </summary>
	public IReadOnlyList<SceneModel> DropPodOpening() {
		if (Root(Sim.MeteorObject.ShapeLibraryName, Sim.MeteorObject.OpeningShapeIndex) is not { } root) {
			return Array.Empty<SceneModel>();
		}

		var cells = new List<SceneModel>();
		for (int cell = 0; cell < DtsMeshBuilder.CellFrameCount(root); cell++) {
			if (Build(Sim.MeteorObject.ShapeLibraryName, Sim.MeteorObject.OpeningShapeIndex,
					Sim.MeteorObject.TextureBankName, cellFrame: cell) is { } model) {
				cells.Add(model);
			}
		}

		return cells;
	}

	/// <summary>
	/// One root of the theater's ground-shape set — <c>dts\&lt;set&gt;.DTS</c> with
	/// <c>dba\&lt;set&gt;.DBA</c>, the pair <c>FlatObj_LoadResources</c> (<c>004097a8</c>) loads and
	/// binds into every root — one entry per cell of its sequence-0 flipbook. See
	/// <see cref="Sim.GroundShape"/> and <see cref="TheaterDescriptor.FlatSetName"/>.
	///
	/// <para>Retail's <c>FLAT2</c> has four roots: 0 and 2 single flat solid polys in palette 224, 1
	/// a five-cell ring of solid polys in 96 and 97, and 3 one textured square. The bank is decoded
	/// with index 0 transparent, which changes nothing unless a frame carries that index.</para>
	/// </summary>
	/// <returns>The cells in order, or empty when the set or the root is missing.</returns>
	public IReadOnlyList<SceneModel> GroundShape(string setName, int shapeIndex) {
		string dtsName = setName + ".DTS";
		if (Root(dtsName, shapeIndex) is not { } root) {
			return Array.Empty<SceneModel>();
		}

		var cells = new List<SceneModel>();
		for (int cell = 0; cell < DtsMeshBuilder.CellFrameCount(root); cell++) {
			if (Build(dtsName, shapeIndex, setName, transparentBank: true, cellFrame: cell) is { } model) {
				cells.Add(model);
			}
		}

		return cells;
	}

	/// <summary>
	/// A ground-shape root's own bounding radius in world units — the <c>shape+8</c>
	/// <c>SimObject_GetShapeRadius</c> reads, which the draw's terrain conform probes at. Zero when
	/// the root is missing.
	/// </summary>
	public int GroundShapeRadius(string setName, int shapeIndex) =>
		Root(setName + ".DTS", shapeIndex) is TSBasePart root ? root.Radius : 0;

	/// <summary>
	/// One root of <c>dts\FIRE.DTS</c> — a burning object's looping flipbook of billboards, out of
	/// <c>dba\FIRE0.DBA</c> or <c>FIRE1.DBA</c>. Which of the two is
	/// <c>dat\FIRE.DAT</c>: a four-byte header and then one byte per shape, which
	/// <see cref="Sim.FireEffect"/> documents and <see cref="MissionScene"/> reads.
	/// </summary>
	public SceneModel? Fire(int shapeIndex, int textureBankIndex) =>
		Build(Sim.FireEffect.ShapeLibraryName, shapeIndex,
			Sim.FireEffect.TextureBankPrefix + textureBankIndex, transparentBank: true);

	/// <summary>
	/// One root of <c>dgs\BHULKS.DGS</c> — the wreck a fallen structure is redrawn as, indexed by its
	/// type's <see cref="BaseType.HulkTypeIndex"/>. <c>Base_LoadResources</c> loads the library beside
	/// the two building ones and binds <c>BASETEX</c> to every shape in it, whatever bank the standing
	/// building used.
	///
	/// <para>Split by cell like the building it replaces. The swap keeps the building's cell frames,
	/// so a wreck's cell parts are gated on the structure's own <see cref="Sim.BaseObject.CellFrames"/>,
	/// and the split also carries its <c>TSDetailPart</c> levels, which are chosen per frame under
	/// STRUCTURE DETAIL just as the building's were — see <see cref="DtsMeshBuilder.BuildCells"/>.</para>
	/// </summary>
	public SceneModel? Hulk(int shapeIndex) =>
		BuildFromShapeLibrary(HulkLibraryName, shapeIndex, StructureBankName, transparentBank: true,
			celled: true, splitGround: true);

	/// <summary>The wreck library <c>Base_LoadResources</c> opens, by the literal name <c>bhulks</c>.</summary>
	public const string HulkLibraryName = "BHULKS.DGS";

	/// <summary>And the bank it binds to every shape in it.</summary>
	public const string StructureBankName = "BASETEX";

	/// <summary>How many wrecks that library holds.</summary>
	public int HulkCount => LoadShapeLibrary(HulkLibraryName)?.Shapes?.Length ?? 0;

	private SceneModel? Build(string dtsName, int rootIndex, string? bankName,
			bool segmented = false, bool transparentBank = false, int cellFrame = 0,
			IReadOnlySet<short>? hiddenPartIds = null, bool celled = false, bool leveled = false,
			ANAnimList? poseList = null, bool splitGround = false) {
		string key = cellFrame == 0 ? $"dts\\{dtsName}#{rootIndex}" : $"dts\\{dtsName}#{rootIndex}@{cellFrame}";
		if (_models.TryGetValue(key, out var cached)) {
			return cached;
		}

		var model = BuildFromRoot(key, Root(dtsName, rootIndex), bankName, segmented, transparentBank,
			cellFrame, hiddenPartIds, celled, leveled, poseList, splitGround);
		_models[key] = model;
		return model;
	}

	/// <summary>
	/// How many frames a structure type's idle flipbook has — the modulus
	/// <see cref="Sim.BaseObject.ThinkTick"/> steps <see cref="World.BaseType.AnimCellSequence"/>
	/// round. One for a type that does not animate, which makes the step a no-op.
	///
	/// <para>Both libraries can carry one — on retail data the two types that actually free-run a
	/// flipbook are both static-library shapes — so the root is taken from whichever library the
	/// type selects.</para>
	/// </summary>
	public int BaseAnimCellCount(BaseType type) {
		if (type.AnimCellSequence < 0) {
			return 1;
		}

		if (type.Source == BaseShapeSource.AnimatedLibrary) {
			return DtsMeshBuilder.CellFrameCount(
				Root(BaseTypeTable.AnimatedLibraryName, type.ShapeIndex), type.AnimCellSequence);
		}

		// A .DGS record is itself the TSShape, so the frame counts are on the record, not on its
		// geometry child.
		var sequences = LoadShapeLibrary(BaseTypeTable.StaticLibraryName)?.Shapes is { Length: > 0 } shapes
				&& type.ShapeIndex >= 0 && type.ShapeIndex < shapes.Length
			? shapes[type.ShapeIndex].SequenceList
			: null;

		return DtsMeshBuilder.CellFrameCount(sequences, type.AnimCellSequence);
	}

	/// <summary>
	/// A structure type's animation data, or null for one whose shape carries none — which is every
	/// <see cref="BaseShapeSource.StaticLibrary"/> type, since <c>BASES.DGS</c> holds no
	/// <c>ANAnimList</c> at all. Shared per shape, as a mech type's is.
	/// </summary>
	public ShapeAnimation? BaseAnimation(BaseType type) {
		if (type.Source != BaseShapeSource.AnimatedLibrary) {
			return null;
		}

		string key = $"{BaseTypeTable.AnimatedLibraryName}#{type.ShapeIndex}";
		if (_animations.TryGetValue(key, out var cached)) {
			return cached;
		}

		var animation = ShapeAnimation.FromRoot(Root(BaseTypeTable.AnimatedLibraryName, type.ShapeIndex));
		_animations[key] = animation;
		return animation;
	}

	/// <summary>One root of a shape file, or null when the file or the index is missing.</summary>
	private TSObject? Root(string dtsName, int rootIndex) =>
		LoadDts(dtsName)?.Roots is { Count: > 0 } roots && rootIndex >= 0 && rootIndex < roots.Count
			? roots[rootIndex]
			: null;

	private SceneModel? BuildFromShapeLibrary(string libraryName, int shapeIndex, string? bankName,
			bool transparentBank = false, bool celled = false, bool splitGround = false) {
		string key = $"dgs\\{libraryName}#{shapeIndex}";
		if (_models.TryGetValue(key, out var cached)) {
			return cached;
		}

		TSObject? root = null;
		if (LoadShapeLibrary(libraryName)?.Shapes is { Length: > 0 } shapes
				&& shapeIndex >= 0 && shapeIndex < shapes.Length) {
			root = shapes[shapeIndex].Geometry;
		}

		var model = BuildFromRoot(key, root, bankName, transparentBank: transparentBank, celled: celled,
			splitGround: splitGround);
		_models[key] = model;
		return model;
	}

	private SceneModel? BuildFromRoot(string key, TSObject? root, string? bankName,
			bool segmented = false, bool transparentBank = false, int cellFrame = 0,
			IReadOnlySet<short>? hiddenPartIds = null, bool celled = false, bool leveled = false,
			ANAnimList? poseList = null, bool splitGround = false) {
		if (root == null) {
			return null;
		}

		var atlas = bankName != null ? LoadAtlas(bankName, transparentBank) : null;
		MeshBuild build;
		MeshBuild? ground = null;
		if (splitGround) {
			(build, var groundBuild) = DtsMeshBuilder.BuildRootSplitGround(root, atlas, _shading);
			ground = groundBuild;
		} else {
			build = DtsMeshBuilder.BuildRoot(root, atlas, _shading, cellFrame, hiddenPartIds, poseList);
		}

		// Measured over the whole shape, ground plane included.
		var (min, max) = DtsMeshBuilder.Bounds(ground is { } apart
			? build.Vertices.Concat(apart.Vertices).ToArray()
			: build.Vertices);

		Vector3 extent = max - min;

		// The height comes off the flat mesh whichever way the model ends up being drawn: it describes
		// the shape at rest and undamaged. Each split costs a second pass over the shape, so only the
		// rosters that need one ask: segments for what animates, cells for what damage takes apart
		// without animating, levels for the transient shapes built a cell at a time.
		return new SceneModel(key, build.Vertices, build.TriangleVertexCount, atlas,
			(int)(extent.Y * WorldScale.WorldUnitsPerMeter),
			segmented ? DtsMeshBuilder.BuildSegments(root, atlas, _shading, hiddenPartIds, poseList, splitGround) : Array.Empty<MeshSegment>(),
			DtsSpriteBuilder.Build(root),
			celled ? DtsMeshBuilder.BuildCells(root, atlas, _shading, hiddenPartIds, splitGround)
				: leveled ? DtsMeshBuilder.BuildDetailLevels(root, atlas, _shading, cellFrame)
				: Array.Empty<MeshCell>(),
			build.PointVertexCount,
			root is TSBasePart basePart ? basePart.Radius : 0,
			ground);
	}

	/// <summary>
	/// A mech file's roots are LOD variants of the same machine, drawn one at a time; a library
	/// file's roots (<c>BASES_AN.DTS</c>) are unrelated objects and the caller picks. The files carry
	/// no flag distinguishing the two cases — that knowledge lives here, as it does in the original,
	/// where it is the detail table only <c>Mech_Constructor</c> installs.
	/// </summary>
	private DynamixThreeSpaceModel? LoadDts(string dtsName) {
		if (_files.TryGetValue(dtsName, out var cached)) {
			return cached;
		}

		byte[]? bytes = _content.Read("dts", dtsName);
		DynamixThreeSpaceModel? model = null;

		if (bytes != null) {
			model = new DTSModelTransformer().Parse(bytes) as DynamixThreeSpaceModel;
		}

		_files[dtsName] = model;
		return model;
	}

	/// <summary>
	/// A shape library's records are unrelated structures, one shape each (see
	/// <see cref="BasesDgsTransformer"/>) — the caller picks by index, same as
	/// <c>BASES_AN.DTS</c>'s roots.
	/// </summary>
	private GridShapeLibrary? LoadShapeLibrary(string libraryName) {
		if (_shapeLibraries.TryGetValue(libraryName, out var cached)) {
			return cached;
		}

		byte[]? bytes = _content.Read("dgs", libraryName);
		var library = bytes != null
			? new BasesDgsTransformer().Parse(bytes) as GridShapeLibrary
			: null;

		_shapeLibraries[libraryName] = library;
		return library;
	}

	/// <summary>
	/// <paramref name="transparentIndex0"/> decodes palette index 0 to alpha 0 rather than to an
	/// opaque colour. An explosion frame is a round puff on a field of index 0 and the original's
	/// blit skips that index rather than writing it (docs/retail/rendering/dts-billboards.md, "Brush mode 5
	/// skips palette index 0"), so every sprite bank asks for it.
	///
	/// <para>The <b>structure</b> banks are cutouts too: <c>BASETEX</c> frames 11, 36, 38, 39, 52, 53,
	/// 60, 61, 63, 64 and 65 are 20-73% index 0 each, and they are the lattice girders on a
	/// structure's support towers — drawn opaque they come out as black panels where the original
	/// shows sky through the frame. The original's own switch is per frame rather than per bank: the
	/// loader sets the frame descriptor's <c>+0x12</c> when any texel of the frame is index 0 (docs/retail/rendering/dts-texture-binding.md,
	/// "The frame descriptor table and the span routines (DBSIM)"). A frame with no index 0 in it
	/// draws identically either way, so decoding the whole bank transparent reproduces the original
	/// on this data.</para>
	///
	/// <para>It is still not done for <i>every</i> mesh bank: the mech skins carry a handful of stray
	/// index-0 texels each (9 of 44376 in <c>LIGHT</c>, 7 of 68464 in <c>MEDIUM</c>) that are plainly
	/// paint rather than cutouts, and punching single-pixel holes in a HERC to generalise a rule this
	/// session did not fully trace would be a worse trade than leaving them opaque.</para>
	/// </summary>
	private TextureAtlas? LoadAtlas(string bankName, bool transparentIndex0 = false) {
		// The two decodings of one bank are different images, so they cache apart. No retail bank is
		// asked for both ways; the key keeps that from being an assumption.
		string key = transparentIndex0 ? bankName + "#alpha" : bankName;
		if (_atlases.TryGetValue(key, out var cached)) {
			return cached;
		}

		byte[]? bytes = _content.Read("dba", bankName + ".DBA");
		TextureAtlas? atlas = null;

		if (bytes != null
			&& new DynamixBitmapArrayTransformer().Parse(bytes) is DynamixBitmapArray bank) {
			atlas = TextureAtlas.Build(bank, _palette, transparentIndex0);
		}

		_atlases[key] = atlas;
		return atlas;
	}
}
