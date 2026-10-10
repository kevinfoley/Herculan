using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Scene;

/// <summary>
/// What a <see cref="MissionScene"/> draws with: every model it needs, built up front because nothing that
/// appears mid-mission has anywhere to load one from, the terrain mesh and bank, and the theater's atmosphere and
/// shading. <see cref="Build"/> runs once the simulation is set up, since it is the machines' fits and the
/// structures' types that say which shapes are needed, and writes what the simulation times or sizes by a shape
/// back into it: the impact effects' frame counts, the debris shapes' radii, the fires' loop lengths and the drop
/// pod's opening.
/// </summary>
public sealed class MissionModels {
	private MissionModels(IReadOnlyList<SceneModel> all, MeshVertex[] terrainMesh, TerrainTextureBank? terrainBank,
			IReadOnlyDictionary<int, SceneModel> bullets, IReadOnlyDictionary<int, SceneModel> explosions,
			IReadOnlyDictionary<int, IReadOnlyList<SceneModel>> rockets,
			IReadOnlyDictionary<int, IReadOnlyList<SceneModel>> mechWeapons, Atmosphere atmosphere,
			SurfaceRampTable? shadeRamps, PaletteRampTable? paletteRamp, ImpactFlash? impactFlash,
			IReadOnlyDictionary<string, IReadOnlyList<SceneModel?>> debris, IReadOnlyList<SceneModel?> fires,
			IReadOnlyDictionary<int, SceneModel> hulks, SceneModel? dropPod, IReadOnlyList<SceneModel> dropPodOpening,
			IReadOnlyList<IReadOnlyList<SceneModel>> groundShapes) {
		All = all;
		TerrainMesh = terrainMesh;
		TerrainBank = terrainBank;
		Atmosphere = atmosphere;
		GroundShapes = groundShapes;
		ShadeRamps = shadeRamps;
		PaletteRamp = paletteRamp;
		ImpactFlash = impactFlash;
		Bullets = bullets;
		Explosions = explosions;
		Rockets = rockets;
		MechWeapons = mechWeapons;
		Debris = debris;
		Fires = fires;
		Hulks = hulks;
		DropPod = dropPod;
		DropPodOpening = dropPodOpening;
	}

	/// <summary>How far this zone is visible and what it fades into — see <see cref="Scene.Atmosphere"/>.</summary>
	public Atmosphere Atmosphere { get; }

	/// <summary>
	/// The theater's shaded-surface colours, or null when its palette carries no ramp table. A host
	/// hands this to <see cref="SceneRenderer.SetShadeRamps"/> once after loading — it is what makes
	/// a HERC or a structure the colour the original draws it. See <see cref="SurfaceRampTable"/>.
	/// </summary>
	public SurfaceRampTable? ShadeRamps { get; }

	/// <summary>
	/// The theater's palette at every shade row, or null when its ramp or palette did not load. A
	/// host hands this to <see cref="SceneRenderer.SetPaletteRamp"/> once after loading, and must then
	/// bind indexed atlases — see <see cref="PaletteRampTable"/>.
	/// </summary>
	public PaletteRampTable? PaletteRamp { get; }

	/// <summary>
	/// The same three, rebuilt against the theater's damage-flash palette, or null when that palette
	/// is missing — see <see cref="Scene.ImpactFlash"/>.
	/// </summary>
	public ImpactFlash? ImpactFlash { get; }

	/// <summary>The distinct models the scene draws with — upload each of these once.</summary>
	public IReadOnlyList<SceneModel> All { get; }

	/// <summary>Terrain triangles in render space, ready to upload.</summary>
	public MeshVertex[] TerrainMesh { get; }

	/// <summary>
	/// The terrain's packed texture bank, or null when the theater's <c>.DBA</c> could not be loaded
	/// — in which case <see cref="TerrainMesh"/>'s vertices are all flagged untextured and draw
	/// the untextured fill — see <see cref="TerrainMeshBuilder"/>.
	/// </summary>
	public TerrainTextureBank? TerrainBank { get; }

	/// <summary>
	/// The shape each travelling shot is drawn as, keyed by the <c>PROJ.DAT</c> subtype id that
	/// spawned it — the same id <see cref="SimWorld.Bullets"/> is indexed by. Built up front, from
	/// every record in that table, because a shot that appears mid-flight has nowhere to load a model
	/// from; the original loads the same nine shapes once at startup for the same reason.
	/// </summary>
	public IReadOnlyDictionary<int, SceneModel> Bullets { get; }

	/// <summary>
	/// The shape each impact effect is drawn as, keyed by the <c>EXPLOS.DAT</c> shape index its type
	/// row names — one root of <c>dts\EXPLOS.DTS</c> each, textured from whichever
	/// <c>dba\EXPLO&lt;n&gt;.DBA</c> that row's own second field selects. Built up front for the same
	/// reason <see cref="Bullets"/> is: an effect appears at the instant of impact and has
	/// nowhere to load anything from, and the original loads all twenty once at startup.
	/// </summary>
	public IReadOnlyDictionary<int, SceneModel> Explosions { get; }

	/// <summary>
	/// The shapes each launcher round is drawn as, keyed by the <c>PROJ.DAT</c> subtype id that fired
	/// it — the same arrangement <see cref="Bullets"/> has, over a separate table and a separate
	/// shape file. The two key spaces overlap (both start at subtype 0) and mean different things, so
	/// they are deliberately not one dictionary.
	///
	/// <para>The value is a <b>list</b> because a rocket's flipbook is geometry: entry <c>i</c> is the
	/// shape with its exhaust flame on cell <c>i</c>, and a round in flight picks by
	/// <see cref="Rocket.AnimationFrame"/>. See <see cref="SceneModelLibrary.Rocket"/>.</para>
	/// </summary>
	public IReadOnlyDictionary<int, IReadOnlyList<SceneModel>> Rockets { get; }

	/// <summary>
	/// The weapon models the machines on this field are fitted with, keyed by
	/// <see cref="Sim.WeaponMount.ModelShapeIndex"/> and holding one entry per cell of the shape's
	/// muzzle-flash flipbook — see <see cref="SceneModelLibrary.MechWeapon"/>. A mount draws the cell
	/// its own <see cref="Sim.WeaponMount.FlashCell"/> names, at
	/// <see cref="Sim.WeaponMount.ModelFrame"/>.
	/// </summary>
	public IReadOnlyDictionary<int, IReadOnlyList<SceneModel>> MechWeapons { get; }

	/// <summary>
	/// The shapes wreckage is drawn as, keyed by the shape file a piece names
	/// (<see cref="Sim.DebrisObject.ShapeLibrary"/>) and indexed by its root. Built up front from
	/// every table this mission can reach — the two shared ones and one per HERC chassis on the field
	/// — because a piece appears at the instant something is destroyed and has nowhere to load
	/// anything from.
	///
	/// <para>An entry can be null: a table may name a root its shape file does not have, and a
	/// missing shape is a piece that is simulated and not drawn rather than a reason to refuse the
	/// throw.</para>
	/// </summary>
	public IReadOnlyDictionary<string, IReadOnlyList<SceneModel?>> Debris { get; }

	/// <summary>
	/// The four roots of <c>dts\FIRE.DTS</c>, in order — a burning object's flipbook of billboards.
	/// Indexed by <see cref="Sim.FireEffect.ShapeIndex"/>.
	/// </summary>
	public IReadOnlyList<SceneModel?> Fires { get; }

	/// <summary>
	/// The wreck each structure type leaves behind, keyed by its
	/// <see cref="BaseType.HulkTypeIndex"/> — a root of <c>dgs\BHULKS.DGS</c>, drawn in place of the
	/// building once <see cref="Sim.BaseObject.ShowingHulk"/> is set. Only the types on this field
	/// that state one are built.
	/// </summary>
	public IReadOnlyDictionary<int, SceneModel> Hulks { get; }

	/// <summary>
	/// The drop pod in the air — root 0 of <c>dts\METEOR.DTS</c>. Null when the install has no such
	/// file, which leaves a pod that is simulated and not drawn, as a missing debris root does.
	/// </summary>
	public SceneModel? DropPod { get; }

	/// <summary>
	/// And the pod on the ground, one entry per cell of its opening flipbook —
	/// <see cref="Sim.MeteorObject.AnimationFrame"/> picks. Its <i>length</i> is load-bearing as well
	/// as its contents: it is what ends the animation, and so when the pod hands its group over.
	/// </summary>
	public IReadOnlyList<SceneModel> DropPodOpening { get; }

	/// <summary>
	/// The theater's ground-shape set, one entry per root and one model per cell of that root's
	/// flipbook — <see cref="Sim.GroundShape.ShapeIndex"/> picks the root and
	/// <see cref="Sim.GroundShape.Frame"/> the cell. A root the set lacks is an empty list, and the
	/// shape is simulated and not drawn.
	/// </summary>
	public IReadOnlyList<IReadOnlyList<SceneModel>> GroundShapes { get; }

	/// <summary>
	/// Builds the scene's models once its simulation is set up, binding the shape-derived figures into
	/// <paramref name="world"/> as it goes.
	/// </summary>
	/// <param name="groundShapeModels">The theater's ground-shape set, which the simulation needed before any object joined it.</param>
	internal static MissionModels Build(GameContent content, TheaterDescriptor theater, TerrainMaterialTable materials,
			SceneModelLibrary models, SimWorld world, IReadOnlyList<SceneObject> objects,
			IReadOnlyList<IReadOnlyList<SceneModel>> groundShapeModels) {
		var terrain = world.Terrain;
		var bullets = world.Bullets;
		var explosions = world.Explosions;
		var rockets = world.Rockets;
		var debris = world.Debris;

		var terrainBank = TerrainTextureBank.Load(content, theater, materials);

		// Terrain is lit here, after the flattening above has settled the heights it is lit from, the
		// way the original relights the grid at the end of the same pass -- see TerrainMeshBuilder.
		// The same theater ramp that colours a flat solid face supplies the brightness curve the
		// baked shade bytes are read through.
		var terrainMesh = TerrainMeshBuilder.Build(terrain, terrainBank, models.Shading);

		var bulletModels = new Dictionary<int, SceneModel>();
		for (int subtype = 0; bullets != null && subtype < bullets.Count; subtype++) {
			if (bullets.Record(subtype) is { } record && models.Bullet(record.ModelId) is { } model) {
				bulletModels[subtype] = model;
			}
		}

		var rocketModels = new Dictionary<int, IReadOnlyList<SceneModel>>();
		for (int subtype = 0; rockets != null && subtype < rockets.Count; subtype++) {
			if (rockets.Record(subtype) is { } record && models.Rocket(record.ModelId) is { Count: > 0 } cells) {
				rocketModels[subtype] = cells;
			}
		}

		var explosionModels = new Dictionary<int, SceneModel>();
		var explosionFrames = new List<int>();
		for (int shapeIndex = 0; explosions != null && shapeIndex < explosions.ShapeCount; shapeIndex++) {
			var shape = explosions.Shape(shapeIndex);
			var model = shape != null ? models.Explosion(shapeIndex, shape.TextureBankIndex) : null;

			if (model != null) {
				explosionModels[shapeIndex] = model;
			}

			explosionFrames.Add(model?.Sprites.Length ?? 0);
		}

		explosions?.BindFrameCounts(explosionFrames);

		// The wreckage shapes: the two shared tables, plus one per HERC chassis on the field. Each
		// table's shapes come out of its own .DTS under the same base name, and only a chassis' are
		// textured -- MechType_InitOne binds that chassis' own bank to every shape in its table and
		// the two shared loads bind none.
		var debrisModels = new Dictionary<string, IReadOnlyList<SceneModel?>>(
			StringComparer.OrdinalIgnoreCase);

		void LoadDebrisShapes(string tableName, string? bankName) {
			string library = tableName + EffectPools.ShapeLibrarySuffix;
			if (debrisModels.ContainsKey(library)) {
				return;
			}

			int count = models.ShapeCount(library);
			var shapes = new SceneModel?[count];
			var radii = new int[count];
			for (int i = 0; i < count; i++) {
				shapes[i] = models.Debris(library, i, bankName);
				radii[i] = shapes[i]?.ShapeRadius ?? 0;
			}

			debrisModels[library] = shapes;
			world.Effects.BindDebrisShapeRadii(library, radii);
		}

		if (debris != null) {
			LoadDebrisShapes(DebrisDatabase.DefaultName, null);
			LoadDebrisShapes(DebrisDatabase.StructureName, null);
		}

		foreach (var placed in objects) {
			if (placed.Object is MechObject machine
					&& machine.Type.DebrisTableName is { Length: > 0 } table
					&& debris?.Database(table) != null) {
				LoadDebrisShapes(table,
					HercSimDat.TextureGroupDbaBaseName(machine.Type.Data.TextureGroup));
			}
		}

		// A gun shot off its mount is thrown as its own model out of a second weapon library, which is
		// not a debris table and so is loaded on its own terms -- one shape per mount shape the roster
		// carries, the same set MechWeaponModels covers.
		{
			int count = models.ShapeCount(WeaponMount.DebrisShapeLibraryName);
			var shapes = new SceneModel?[count];
			var radii = new int[count];
			for (int i = 0; i < count; i++) {
				shapes[i] = models.Debris(WeaponMount.DebrisShapeLibraryName, i,
					SceneModelLibrary.MechWeaponBankName);
				radii[i] = shapes[i]?.ShapeRadius ?? 0;
			}

			debrisModels[WeaponMount.DebrisShapeLibraryName] = shapes;
			world.Effects.BindDebrisShapeRadii(WeaponMount.DebrisShapeLibraryName, radii);
		}

		// And how long each root of FIRE.DTS runs, which is what times a burning object's loop. The
		// bank each root draws from is dat\FIRE.DAT: a four-byte header and then one byte per shape.
		byte[]? fireBanks = content.Read(DebrisDatabase.ResourceFolder, FireEffect.BankTableResource);

		// The fire shapes, and how long each one's loop is.
		int fireShapeCount = models.ShapeCount(FireEffect.ShapeLibraryName);
		var fireModels = new SceneModel?[fireShapeCount];
		var fireFrames = new int[fireShapeCount];
		for (int i = 0; i < fireShapeCount; i++) {
			int bank = fireBanks != null && FireEffect.BankTableHeaderLength + i < fireBanks.Length
				? fireBanks[FireEffect.BankTableHeaderLength + i]
				: 0;

			fireModels[i] = models.Fire(i, bank);
			fireFrames[i] = fireModels[i]?.Sprites.Length ?? 0;
		}

		world.Effects.BindFireShapeFrames(fireFrames);

		// And the wrecks the structures on this field leave behind.
		var hulkModels = new Dictionary<int, SceneModel>();
		foreach (var placed in objects) {
			if (placed.Object is BaseObject structure && structure.Type.HulkTypeIndex >= 0
					&& !hulkModels.ContainsKey(structure.Type.HulkTypeIndex)
					&& models.Hulk(structure.Type.HulkTypeIndex) is { } hulk) {
				hulkModels[structure.Type.HulkTypeIndex] = hulk;
			}
		}

		// The drop pod, loaded up front for the reason the debris shapes are: a pod appears the moment
		// a mission action fires and has nowhere to load anything from. Meteor_LoadResources runs at
		// startup in the original, not per mission, which is the same "always there" arrangement.
		var dropPodModel = models.DropPod();
		var dropPodOpening = models.DropPodOpening();
		world.BindDropPodFrameCount(dropPodOpening.Count);

		// One flipbook per weapon shape the roster actually carries, built after the machines are
		// spawned because it is their fits that say which shapes those are. Several mounts share a
		// shape freely: the cell each one shows is its own, the geometry is not.
		var mechWeaponModels = new Dictionary<int, IReadOnlyList<SceneModel>>();
		foreach (var placed in objects) {
			if (placed.Object is not MechObject fitted) {
				continue;
			}

			foreach (var mount in fitted.Weapons.Mounts) {
				if (mount.ModelShapeIndex < 0 || mechWeaponModels.ContainsKey(mount.ModelShapeIndex)) {
					continue;
				}

				if (models.MechWeapon(mount.ModelShapeIndex) is { Count: > 0 } cells) {
					mechWeaponModels[mount.ModelShapeIndex] = cells;
				}
			}
		}

		return new MissionModels(models.Models.ToArray(), terrainMesh, terrainBank, bulletModels, explosionModels,
			rocketModels, mechWeaponModels, Atmosphere.From(terrain, models.Shading, theater.File),
			SurfaceRampTable.Build(models.Shading), PaletteRampTable.Build(models.Shading),
			models.ImpactShading is { } impact
				? new ImpactFlash(SurfaceRampTable.Build(impact), PaletteRampTable.Build(impact),
					Atmosphere.From(terrain, impact, theater.File))
				: null,
			debrisModels, fireModels, hulkModels, dropPodModel, dropPodOpening, groundShapeModels);
	}
}
