using HercWorks.Core.Data.Struct;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Settings;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;
using System.Numerics;

namespace Herculan.Engine.Scene;

/// <summary>
/// The alternate whole-shape models one object can be drawn as, and what the choice between them is
/// measured against — see <see cref="Render.ShapeDetail"/>. Only a machine has one.
/// </summary>
/// <param name="Roots">
/// Every root of the shape, finest first. <c>Roots[0]</c> is the same model as
/// <see cref="SceneObject.Model"/>.
/// </param>
/// <param name="ShapeRadius">
/// Root 0's own <c>TSBasePart.Radius</c> in world units — the radius the original measures projected
/// size from, whichever root is currently drawn.
/// </param>
public sealed record ShapeDetailChain(IReadOnlyList<SceneModel> Roots, int ShapeRadius);

/// <summary>One placed object, paired with the shared model it draws with.</summary>
/// <param name="Object">The simulation object, which owns the authoritative position and heading.</param>
/// <param name="Model">Its shared model, or null when its type has none the engine can build yet.</param>
/// <param name="Placement">The mission record it came from, kept for diagnostics and tooling.</param>
/// <param name="Detail">
/// Its LOD chain, or null for everything that has none — which is everything but a machine, since
/// only <c>Mech_Constructor</c> installs a detail table on an object.
/// </param>
public sealed record SceneObject(SimObject Object, SceneModel? Model, MissionPlacement Placement,
	ShapeDetailChain? Detail = null);

/// <summary>
/// Assembles a playable scene from a real mission: the zone and theater the mission names, its
/// terrain, and one simulation object per unit the mission places — see
/// docs/herculan/planning.md, "Milestone 4".
///
/// <para>Nothing here is configured by hand. The zone, the theater and its variant come out of
/// <c>script.dat</c>'s header; the units, their types, positions and headings come out of its
/// rosters and activation records (see <see cref="MissionLoader"/> for the rule and the RE behind
/// it); the player's own lance comes out of <c>player.mec</c>. The previous milestone's single
/// hardcoded mech at the middle of the zone is gone.</para>
///
/// <para>Everything is CPU-side on purpose. It loads files, builds a <see cref="SimWorld"/> and
/// produces vertex arrays, but touches no GL, so a host uploads the meshes once it has a context and
/// a headless caller (a test, a future editor's data pass) can build the same scene with no window
/// at all.</para>
/// </summary>
public sealed class MissionScene {
	private MissionScene(Mission mission, SimWorld world, FlyCameraObject camera,
			IReadOnlyList<SceneObject> objects, TheaterDescriptor theater, SceneObject? playerObject,
			BeamAppearance? beams, MissionModels models) {
		Beams = beams;
		Mission = mission;
		World = world;
		Camera = camera;
		Objects = objects;
		PlayerObject = playerObject;
		Theater = theater;
		Models = models;

		if (playerObject?.Object is MechObject pilot) {
			Targeting = new TargetSelection(world, pilot);
		}
	}

	/// <summary>The mission this scene was built from.</summary>
	public Mission Mission { get; }

	public SimWorld World { get; }

	/// <summary>The observer camera, itself a simulation object (see <see cref="FlyCameraObject"/>).</summary>
	public FlyCameraObject Camera { get; }

	/// <summary>Every placed object, in spawn order.</summary>
	public IReadOnlyList<SceneObject> Objects { get; }

	/// <summary>
	/// The machine the player pilots, or null when the mission has no <c>player.mec</c> beside it.
	/// It is an ordinary placed object; the only thing that distinguishes it is that the host feeds
	/// it <see cref="MechObject.Controls"/>.
	/// </summary>
	public SceneObject? PlayerObject { get; }

	/// <summary>The player's HERC, or null when there is no player or it has no mech model.</summary>
	public MechObject? PlayerMech => PlayerObject?.Object as MechObject;

	/// <summary>
	/// The player's target selection, or null with no player machine to select from — see
	/// <see cref="TargetSelection"/> for why it is a peer of the machine rather than part of it. It
	/// lives on the scene because the original's does too: it belongs to the cockpit, which is built
	/// once per mission alongside everything else here.
	/// </summary>
	public TargetSelection? Targeting { get; private set; }

	/// <summary>The theater descriptor this scene was built against — it names the terrain bank and the palette.</summary>
	public TheaterDescriptor Theater { get; }

	/// <summary>
	/// Beam widths, colours and the shared cross-section, or null when either resource is missing —
	/// in which case beams still fire and still do damage, they just are not drawn. Loaded here
	/// because its colours are palette indices and the theater owns the palette.
	/// </summary>
	public BeamAppearance? Beams { get; }

	/// <summary>
	/// What the scene draws with: every model, the terrain mesh and bank, and the theater's atmosphere and
	/// shading — see <see cref="MissionModels"/>.
	/// </summary>
	public MissionModels Models { get; }

	/// <summary>How many placed objects have no model the engine can build yet.</summary>
	public int UnmodelledCount => Objects.Count(o => o.Model == null);

	/// <summary>
	/// Loads the mission at <paramref name="scriptPath"/> and everything it needs.
	/// </summary>
	/// <param name="dataDirectory">Where the simulator's settings are, when not beside the script.</param>
	public static MissionScene Load(GameContent content, string scriptPath, string? dataDirectory = null) {
		var mission = MissionLoader.Load(content, scriptPath);

		var materials = TerrainMaterialTable.Load(content);
		var theater = TheaterDescriptor.Load(content, mission.Header.TheaterIndex, mission.Header.TheaterVariant);

		// The one generator the whole session rolls on, at DBSIM's own starting state — the terrain
		// material pass below is a randomised load-time pass in the original, and it draws from the
		// same generator every later roll comes out of, so the same instance goes on to the world. The
		// zone's roll is the first draw on it, as in the original — see
		// docs/retail/simulation/random-generator.md#the-simulations-draws.
		var random = new SimRandom();

		// How far this mission draws is a player setting, not a property of the zone — see
		// TerrainDetail. The simulator keeps it beside the script it was handed, so this looks for it
		// in the same folder unless told otherwise, and falls back to the highest setting when there is
		// nothing to read.
		int detail = TerrainDetail.LevelFrom(dataDirectory ?? Path.GetDirectoryName(scriptPath));
		var terrain = TerrainZoneLoader.Load(content, mission.Header.ZoneIndex, materials, random,
			detailLevel: detail);

		// The travelling-projectile table, loaded once at startup as Bullet_LoadResources (0040ade0) loads it, and given
		// to the world because a shot in flight is simulation state before it is anything visual.
		var bullets = BulletCatalog.Load(content.Read(BulletCatalog.ResourceFolder, BulletCatalog.TableResource));

		// The impact-effect table, loaded once at startup as Explosion_LoadResources (00407b54) loads it. Its shapes are
		// built below, and the frame counts they yield go back into the catalog — an effect's life is
		// one pass of its own flipbook, so the simulation cannot time it without them.
		var explosions = ExplosionCatalog.Load(
			content.Read(ExplosionCatalog.ResourceFolder, ExplosionCatalog.TableResource));

		// And the launcher table, loaded once at startup as Rocket_LoadTypeTable_Unguided loads it.
		var rockets = RocketCatalog.Load(
			content.Read(RocketCatalog.ResourceFolder, RocketCatalog.TableResource));

		// And the beam table, which Beam_LoadResourceTables (0040b6e0) loads with the same startup
		// pass. The world gets it because an ELF tracer's geometry is built from it at fire time,
		// not at draw time.
		var beams = BeamAppearance.Load(content, theater.PaletteName);

		// The debris tables, headed by DEF_DEB. Debris_LoadResources loads that one at startup and
		// Base_LoadResources loads BASE_DEB beside it; a chassis' own table is loaded with its type,
		// which here means as the roster asks for it.
		var debris = DebrisCatalog.Load(content);
		debris?.Database(DebrisDatabase.StructureName);

		var world = new SimWorld(terrain, bullets, explosions, rockets, beams, random, debris) {
			// The shell chose these three before it wrote the script — see SimWorld.Difficulty.
			Difficulty = mission.Header.Difficulty,
			UnlimitedAmmunition = mission.Header.UnlimitedAmmunition,
			PlayerInvulnerable = mission.Header.PlayerInvulnerable,
			Theater = mission.Header.TheaterIndex,
			Tweaks = TweakSettings.Current,
			IsV110 = content.IsV110
		};
		world.Mission.LoadCounters(mission.Counters);
		var models = new SceneModelLibrary(content, theater);
		var baseTypes = BaseTypeTable.Load(content);

		// The theater's ground-shape set, which FlatObj_LoadResources loads at the end of
		// World_LoadTheater -- before a single object exists, which matters here: a HERC takes its
		// shadows as it joins the world below, and each is built with its root's radius.
		var groundShapeModels = new IReadOnlyList<SceneModel>[models.ShapeCount(theater.FlatSetName + ".DTS")];
		var groundShapeRadii = new int[groundShapeModels.Length];
		var groundShapeFrames = new int[groundShapeModels.Length];
		for (int i = 0; i < groundShapeModels.Length; i++) {
			groundShapeModels[i] = models.GroundShape(theater.FlatSetName, i);
			groundShapeRadii[i] = models.GroundShapeRadius(theater.FlatSetName, i);
			groundShapeFrames[i] = groundShapeModels[i].Count;
		}

		world.BindGroundShapes(groundShapeRadii, groundShapeFrames);

		// The structure hit-sphere table, read straight after the type table as Base_LoadResources
		// reads it, and sized by it: BASECOL.DAT carries no count of its own.
		var baseCollision = BaseCollisionTable.Load(content, baseTypes.Count);

		// The simulator loads both weapon tables once, at startup, not per machine — see WeaponCatalog.
		var weapons = WeaponCatalog.Load(
			content.Read(WeaponCatalog.ResourceFolder, WeaponCatalog.TemplateResource),
			content.Read(WeaponCatalog.ResourceFolder, WeaponCatalog.ProjectileResource));

		// And the transport's weapon slots, which Base_LoadResources reads between BFORMS.DAT and
		// BASES.DAT. Every transport in the mission shares the one table.
		var transportArmament = TransportArmament.Load(
			content.Read(TransportArmament.ResourceFolder, TransportArmament.SlotResource), weapons);

		var objects = new List<SceneObject>(mission.Placements.Count);
		SceneObject? playerObject = null;
		foreach (var placement in mission.Placements) {
			var spawned = Spawn(placement, models, baseTypes, baseCollision, weapons, transportArmament,
				world.Random, world.Tweaks);
			if (spawned == null) {
				continue;
			}

			if (ReferenceEquals(placement, mission.Player)) {
				playerObject = spawned;
				if (spawned.Object is MechObject playerMech) {
					playerMech.IsPlayer = true;
				}
			}

			// A structure registers its footprint with the terrain before it is settled onto it, as both
			// of the original's base-placement paths do. Nothing has moved yet -- the flattening
			// happens once, below, after every structure has had its say.
			if (spawned.Object is BaseObject structure) {
				terrain.MarkStructureFootprint(structure.Position.X, structure.Position.Y,
					structure.ShapeRadius);
			}

			// Settling an object onto the terrain before it joins the world is a placement step, not
			// a simulation step -- a mech's own tick now walks it, which is not what spawning wants.
			spawned.Object.Position = new Vec3i(spawned.Object.Position.X, spawned.Object.Position.Y,
				spawned.Object switch {
					FlyerObject => spawned.Object.Position.Z,
					MechObject mech => MechSpawnHeight(terrain, mech,
						placement.GroundPoint ?? spawned.Object.Position),
					_ => terrain.HeightAtWorld(spawned.Object.Position.X, spawned.Object.Position.Y)
				});

			world.Add(spawned.Object);
			objects.Add(spawned);
		}

		// The group records. DBSim_BuildGroupRecord builds one per block-11 entry and attaches every
		// object that entry placed, in placement order -- so the group's first member, which the AI
		// reads as its leader, is the first one the mission listed. The AI is driven from these and
		// not from the object list: see MissionGroup.
		// The mission's actions first: a group's record names one as its arrival gate, so the states
		// have to exist before any group does. DBSim_LoadScriptDat builds the array in its own pass,
		// ahead of the spawn pass, for the same reason.
		var actions = new MissionActionState[mission.Actions.Count];
		for (int i = 0; i < actions.Length; i++) {
			actions[i] = new MissionActionState(mission.Actions[i]);
		}

		world.Mission.SetActions(actions);

		// And the timers that activate them. A timer's own refs are resolved here rather than in the loader
		// for the same reason an order's subject is: the states have to exist first.
		var timers = new MissionActionTimerState[mission.ActionTimers.Count];
		for (int i = 0; i < timers.Length; i++) {
			var record = mission.ActionTimers[i];
			var sequence = new MissionActionState?[MissionActionTimer.SequenceSlots];
			for (int slot = 0; slot < sequence.Length && slot < record.SequenceRefs.Count; slot++) {
				sequence[slot] = ActionAt(actions, record.SequenceRefs[slot]);
			}

			timers[i] = new MissionActionTimerState(record,
				ActionAt(actions, record.PrimaryActionRef), sequence);
		}

		world.Mission.SetActionTimers(timers);

		var groups = new Dictionary<int, MissionGroup>();
		foreach (var placed in objects) {
			int index = placed.Placement.GroupIndex;
			if (!groups.TryGetValue(index, out var group)) {
				group = new MissionGroup(index, KindOfGroup(mission, index), placed.Object.Side,
					OrdersOf(mission, index), ActionAt(actions, DeploymentActionOf(mission, index)));
				if (index >= 0 && index < mission.GroupOutOfActionReports.Count) {
					group.OutOfActionReport = mission.GroupOutOfActionReports[index];
				}

				groups.Add(index, group);
				world.AddGroup(group);
			}

			group.Add(placed.Object);

			if (placed.Object is MechObject machine) {
				machine.InstallInitialBehaviour();
			}
		}

		BindOrderSubjects(groups, objects);
		BindOrderActions(groups, actions);
		BindActionSubjects(actions, groups, objects);

		// The mission's objectives, on the same second pass and for the same reason: an objective
		// names a group or a roster slot, and neither exists until every group has been built. The
		// bounding box the two boundary statuses test is block 1's own extent, which the loader has
		// already read.
		world.Mission.SetObjectives(BuildObjectives(mission, groups, objects));
		world.Mission.Box = MissionBox.Of(mission.Coordinates);

		// Each object's own two actions -- the one it fires when an enemy closes on it and the one it
		// fires when it dies. DBSim_SpawnMissionObjects resolves both as it builds the object; here
		// they wait for the action states, which are built above.
		foreach (var placed in objects) {
			placed.Object.PilotIndex = placed.Placement.PilotIndex;
			placed.Object.EngagementAction = ActionAt(actions, placed.Placement.EngagementActionRef);
			placed.Object.DefeatAction = ActionAt(actions, placed.Placement.DefeatActionRef);
			placed.Object.OutOfActionReport = placed.Placement.OutOfActionReport ?? OutOfActionReport.None;

			// And the condition the mission says a machine starts in, which for anything under 80% means
			// it spawns already damaged -- or, under 20%, already a wreck. DBSim_SpawnMissionObjects
			// makes this call in the same place, immediately after resolving the two actions, and the
			// order matters for the wreck grade: writing a leg off can fire the death gate, and the
			// defeat action has to be attached before it can go off. A structure's was applied by its
			// constructor, as Base_Construct applies it.
			if (placed.Object is MechObject spawned) {
				spawned.ApplyStartingCondition(world, placed.Placement.StartingCondition);
			}
		}

		world.PlayerMech = playerObject?.Object as MechObject;

		// Sim_InitMissionSession builds the player's cockpit, whose weapon rows each seed a charge bar from
		// their mount, and runs one Player_PerFrameCockpitUpdate before the first tick.
		world.PlayerMech?.Weapons.BuildGauges();
		world.PlayerMech?.Weapons.PushGaugeStates();

		// Each base group that carries a formation layout repaints the ground it stands on with that
		// formation's own material, which is what puts a base on a marked concrete pad instead of on
		// open terrain, and marks the pad's own cells for the levelling below. The original does
		// this per group as it builds the group record; both effects only accumulate, so running
		// them here -- after every structure has marked its own footprint, before the one flattening
		// pass -- lands in the same place.
		foreach (var pad in mission.BasePads) {
			terrain.PaintFormationPad(pad.Anchor.X, pad.Anchor.Y, pad.Layout.MaterialIndex,
				materials[pad.Layout.MaterialIndex].BlockShift, pad.Layout.Dimension,
				pad.Layout.Map);
		}

		// The whole roster is down, so the ground can now be levelled under each structure and every
		// normal and diagonal rebuilt over it -- the point DBSim_SpawnMissionObjects runs the same
		// pass. See HeightGrid.FlattenStructureFootprints for what a zone's single-sample ground vehicle
		// marks turn into.
		terrain.FlattenStructureFootprints();

		// And the structures are re-settled onto the terrain they just changed, which is the loop the
		// original runs over its own base list the moment the flattening returns. Only structures:
		// machines were settled before the pass and the original does not revisit them either, since
		// a walking machine re-queries the ground every tick anyway.
		foreach (var placed in objects) {
			if (placed.Object is BaseObject structure) {
				structure.Position = new Vec3i(structure.Position.X, structure.Position.Y,
					terrain.HeightAtWorld(structure.Position.X, structure.Position.Y));
			}
		}

		var camera = new FlyCameraObject { Position = CameraStart(mission, terrain) };
		world.Add(camera);

		var sceneModels = MissionModels.Build(content, theater, materials, models, world, objects, groundShapeModels);
		return new MissionScene(mission, world, camera, objects, theater, playerObject, beams, sceneModels);
	}

	/// <summary>
	/// Where one node of an animating object's shape stands, in render space: the node's own posed
	/// transform followed by the object's shape-to-world one. A caller draws each
	/// <see cref="MeshSegment"/> with the matrix for its own transform id, which is what makes a
	/// machine's legs move and a radar mast's dish turn.
	///
	/// <para>This is <c>Shape_DrawAtDetailLevel</c> (<c>004033e4</c>) and
	/// <c>TSGroup_BindNodeTransform</c> (<c>00476014</c>) between them, and it is the same pair for
	/// every class: the draw slot installs the object's model transform and hands the whole shape to
	/// the shape instance's own render, which composes each group's node transform in front of it.
	/// The structure classes install that draw unchanged — the machine is the one that overrides it,
	/// to splice its hardpoints first.</para>
	///
	/// <para>One thing differs from <see cref="TransformOf"/>, and it is the simulation being let
	/// through rather than approximated: the object's own transform is
	/// <see cref="SimObject.WorldFrame"/>, so a machine's lean over sloping ground comes with it,
	/// where the rigid path has only a heading rotation.</para>
	/// </summary>
	public static Matrix4x4 PosedTransformOf(SimObject subject, int transformId) {
		var world = WorldScale.ToRenderMatrix(subject.WorldFrame);
		return transformId < 0
			? world
			: WorldScale.ToRenderMatrix(subject.NodeTransform(transformId)) * world;
	}

	/// <summary>
	/// A group's order slots, or ten empty ones for a block-11 record the mission carries no orders
	/// for. See <see cref="MissionOrder"/>.
	/// </summary>
	private static IReadOnlyList<MissionOrder?> OrdersOf(Mission mission, int groupIndex) =>
		groupIndex >= 0 && groupIndex < mission.GroupOrders.Count
			? mission.GroupOrders[groupIndex]
			: new MissionOrder?[MissionOrder.Slots];

	/// <summary>
	/// Resolves every order's subject once all the objects exist, which is why it is a second pass:
	/// an order routinely names a group built after its own. An order naming a roster slot nothing
	/// placed, or a group that is waiting to deploy and so is not in the world at all, resolves to
	/// nothing — see <see cref="MissionGroup.BindOrderSubject"/>.
	/// </summary>
	private static void BindOrderSubjects(Dictionary<int, MissionGroup> groups,
			List<SceneObject> objects) {
		var bySlot = new Dictionary<(MissionUnitKind, int), SimObject>();
		foreach (var placed in objects) {
			bySlot[(placed.Placement.Kind, placed.Placement.SlotIndex)] = placed.Object;
		}

		foreach (var group in groups.Values) {
			for (int slot = 0; slot < group.Orders.Count; slot++) {
				if (group.Orders[slot] is not { } order
						|| order.SubjectKind == MissionOrderSubject.None || order.SubjectRef < 0) {
					continue;
				}

				if (order.SubjectKind == MissionOrderSubject.Group) {
					group.BindOrderSubject(slot, groups.GetValueOrDefault(order.SubjectRef), null);
					continue;
				}

				var kind = order.SubjectKind switch {
					MissionOrderSubject.Mech => MissionUnitKind.Mech,
					MissionOrderSubject.Flyer => MissionUnitKind.Flyer,
					_ => MissionUnitKind.Base
				};

				group.BindOrderSubject(slot, null, bySlot.GetValueOrDefault((kind, order.SubjectRef)));
			}
		}
	}

	/// <summary>
	/// Points each order slot at the action it hangs on — the order record's own <c>+0x12</c>, which
	/// is a different action from the group's arrival gate and resolved on the same second pass as
	/// the order subjects.
	/// </summary>
	private static void BindOrderActions(Dictionary<int, MissionGroup> groups,
			MissionActionState[] actions) {
		foreach (var group in groups.Values) {
			for (int slot = 0; slot < group.Orders.Count; slot++) {
				if (group.Orders[slot] is { } order) {
					group.BindOrderAction(slot, ActionAt(actions, order.ActionRef));
				}
			}
		}
	}

	/// <summary>
	/// Resolves each action's own <c>+0x36</c> target, which is what trigger types 7-10 test the
	/// position of. The type says which roster the ref indexes, exactly as an order's discriminator
	/// does; types 0-6 name no target and their ref is left unread.
	/// </summary>
	private static void BindActionSubjects(MissionActionState[] actions,
			Dictionary<int, MissionGroup> groups, List<SceneObject> objects) {
		var bySlot = new Dictionary<(MissionUnitKind, int), SimObject>();
		foreach (var placed in objects) {
			bySlot[(placed.Placement.Kind, placed.Placement.SlotIndex)] = placed.Object;
		}

		foreach (var action in actions) {
			int reference = action.Record.TargetRef;
			if (reference < 0) {
				continue;
			}

			switch (action.Record.Type) {
				case MissionAction.SubjectTargetMech:
					action.TargetObject = bySlot.GetValueOrDefault((MissionUnitKind.Mech, reference));
					break;
				case MissionAction.SubjectTargetFlyer:
					action.TargetObject = bySlot.GetValueOrDefault((MissionUnitKind.Flyer, reference));
					break;
				case MissionAction.SubjectTargetBase:
					action.TargetObject = bySlot.GetValueOrDefault((MissionUnitKind.Base, reference));
					break;
				case MissionAction.SubjectTargetGroup:
					action.TargetGroup = groups.GetValueOrDefault(reference);
					break;
			}
		}
	}

	/// <summary>
	/// Block 12's records turned into runtime state, with each one's subject resolved the way the
	/// order and action subjects above are. An objective naming a roster slot nothing placed, or a
	/// group the mission does not have, resolves to nothing and its condition simply never comes
	/// true — which for a mandatory record means the mission cannot be completed, exactly as the
	/// original's own null subject would behave once it stopped crashing.
	/// </summary>
	private static MissionObjectives BuildObjectives(Mission mission,
			Dictionary<int, MissionGroup> groups, List<SceneObject> objects) {
		var bySlot = new Dictionary<(MissionUnitKind, int), SimObject>();
		foreach (var placed in objects) {
			bySlot[(placed.Placement.Kind, placed.Placement.SlotIndex)] = placed.Object;
		}

		var states = new MissionObjectiveState[mission.Objectives.Count];

		for (int i = 0; i < states.Length; i++) {
			var record = mission.Objectives[i];
			var state = new MissionObjectiveState(record);

			if (record.SubjectRef >= 0) {
				if (record.SubjectKind == MissionObjectiveSubject.Group) {
					state.SubjectGroup = groups.GetValueOrDefault(record.SubjectRef);
				} else {
					var kind = record.SubjectKind switch {
						MissionObjectiveSubject.Mech => MissionUnitKind.Mech,
						MissionObjectiveSubject.Flyer => MissionUnitKind.Flyer,
						_ => MissionUnitKind.Base
					};

					state.SubjectObject = bySlot.GetValueOrDefault((kind, record.SubjectRef));
				}
			}

			states[i] = state;
		}

		var briefing = new int[mission.ObjectiveTextRefs.Count];
		for (int i = 0; i < briefing.Length; i++) {
			briefing[i] = mission.ObjectiveTextRefs[i];
		}

		return new MissionObjectives(states) {
			ObjectiveTextRefs = briefing,
			ObjectiveType = mission.Header.ObjectiveType
		};
	}

	private static MissionActionState? ActionAt(MissionActionState[] actions, int reference) =>
		reference >= 0 && reference < actions.Length ? actions[reference] : null;

	private static int DeploymentActionOf(Mission mission, int groupIndex) =>
		groupIndex >= 0 && groupIndex < mission.GroupDeploymentActions.Count
			? mission.GroupDeploymentActions[groupIndex]
			: -1;

	private static MissionUnitKind KindOfGroup(Mission mission, int groupIndex) =>
		groupIndex >= 0 && groupIndex < mission.GroupKinds.Count
			? mission.GroupKinds[groupIndex]
			: MissionUnitKind.Mech;

	/// <summary>
	/// Model-to-world transform for one placed object, in render space: heading rotation, then its
	/// world position. Nothing else — the shape's own origin is where the original stands it — except
	/// for a <see cref="FlyerObject"/>, whose whole attitude comes through.
	///
	/// <para>The rotation sign is the simulation's, not the camera's. A HERC's forward vector is
	/// <c>(-sin h, cos h)</c> in world XY — that falls out of <c>BuildEulerRotationMatrixQ14</c>'s
	/// Z-only matrix and the row-vector transform, and it is the same sense
	/// <see cref="MissionLoader"/>'s formation spread rotates in. <see cref="Camera"/>'s yaw runs
	/// the other way, so anything attaching a camera to an object's heading negates it; this
	/// transform must not.</para>
	///
	/// <para><b>No bounding-box lift</b>, deliberately: a shape's origin is already its ground contact
	/// point, so raising an object by its mesh's lowest point sinks the one shape authored off the
	/// ground. See docs/retail/formats/dgs-hd0-notes.md, "Shape origin".</para>
	/// </summary>
	public static Matrix4x4 TransformOf(SceneObject sceneObject) =>
		// The object's whole attitude, not its heading alone: SimObject_InstallModelTransform
		// (00401fe4) builds the model transform every draw installs out of the object's euler
		// TRIPLE at obj+0xc, for a structure the same as for a machine. A heading-only rotation
		// draws a Cybrid flyer flat through every turn it makes, and a ground vehicle level over
		// every hill it drives up.
		WorldScale.ToRenderMatrix(sceneObject.Object.WorldFrame);

	/// <summary>
	/// A mech's spawn height — <c>Mech_AttachToGroup</c> (<c>00417aa8</c>): the ground under
	/// <paramref name="groundPoint"/> plus the chassis' ride height, and <see cref="FlyerSpawnLift"/>
	/// more for a flyer. See docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 5.
	/// </summary>
	private static int MechSpawnHeight(HeightGrid terrain, MechObject mech, Vec3i groundPoint) =>
		terrain.HeightAtWorld(groundPoint.X, groundPoint.Y) + mech.Type.RideHeight
			+ (mech.Type.IsFlyer ? FlyerSpawnLift : 0);

	/// <summary>
	/// How much higher than a walker a flyer chassis (the RAZOR) starts: <c>Mech_AttachToGroup</c>'s
	/// literal, added when <c>typeRec+0x50</c> is set.
	/// </summary>
	private const int FlyerSpawnLift = 5000;

	/// <summary>
	/// Builds and positions the simulation object for one placement. Returns null when the placement
	/// names a type the install has nothing at all for — an out-of-range index in a hand-edited
	/// mission — since an object with no identity has nothing to stand in for. A known type whose
	/// <i>model</i> cannot be built still spawns: it is really there, and the scene reports it.
	/// </summary>
	private static SceneObject? Spawn(MissionPlacement placement, SceneModelLibrary models,
			BaseTypeTable baseTypes, BaseCollisionTable baseCollision, WeaponCatalog? weapons,
			TransportArmament? transportArmament, SimRandom random, TweakSettings tweaks) {
		var (simObject, model, detail) = Create(placement, models, baseTypes, baseCollision, weapons,
			transportArmament, random, tweaks);
		if (simObject == null) {
			return null;
		}

		simObject.Position = placement.Position;
		simObject.Heading = placement.Heading;
		simObject.Side = placement.Side;

		if (simObject is FlyerObject aircraft) {
			// Its FFORMS.DAT station, which a wingman re-reads every tick it holds formation.
			aircraft.FormationOffset = placement.FlyerFormationOffset;
		}

		if (simObject is BaseObject structure) {
			// Its BFORMS.DAT station. Only a mobile ground vehicle reads it again after the
			// spread that placed it, and only while it is following a leader.
			structure.FormationOffset = placement.FormationOffset;
		}

		if (simObject is MechObject machine) {
			// The three per-machine AI settings DBSim_SpawnMissionObjects copies out of the block-7
			// record. The formation offset is the one the spread already used; a follower needs it
			// again every tick it holds station on its leader.
			machine.CruiseSpeed = placement.AiCruiseSpeed;
			machine.RadarOrder = placement.AiRadarActive;
			machine.FormationOffset = placement.FormationOffset;

			// A squad machine's condition from player.mec, written in after the constructor has
			// configured its loadout -- DBSim_SpawnMissionObjects calls Mech_ConfigureLoadout
			// (004258a2) before Mech_ApplySquadCondition (004258de), so the shield array and the
			// reactor rate are worked out from a pristine machine and the condition only reaches the
			// shield array at the first hit's recompute.
			if (placement.SquadCondition is { } condition) {
				machine.Damage?.ApplySquadCondition(condition);
			}
		}

		// The original's hover-height substitution, applied at spawn because that is where it
		// happens in Flyer_AttachToGroup (00421ee8) — see FlyerObject.DefaultHoverHeight.
		if (simObject is FlyerObject && placement.Position.Z == 0) {
			simObject.Position = new Vec3i(
				placement.Position.X, placement.Position.Y, FlyerObject.DefaultHoverHeight);
		}

		return new SceneObject(simObject, model, placement, detail);
	}

	/// <summary>
	/// One object's own component health, or null when the install ships no <c>.DMG</c> for its type.
	/// The record is shared per type and the damage is per object, which is the split the original
	/// has: the maxima live in the loaded file and the three arrays are allocated in the constructor.
	/// </summary>
	private static ComponentDamage? ComponentDamageFor(SceneModelLibrary models, string typeName,
			int componentCount, int dependentCount, SimRandom random) =>
		models.DamageData(typeName) is { } data
			? new ComponentDamage(data, componentCount, dependentCount, random)
			: null;

	private static (SimObject? Object, SceneModel? Model, ShapeDetailChain? Detail) Create(
			MissionPlacement placement,
			SceneModelLibrary models, BaseTypeTable baseTypes, BaseCollisionTable baseCollision,
			WeaponCatalog? weapons, TransportArmament? transportArmament, SimRandom random,
			TweakSettings tweaks) {
		switch (placement.Kind) {
			case MissionUnitKind.Mech: {
				if (placement.TypeName == null || models.MechData(placement.TypeName) is not { } simData) {
					return (null, null, null);
				}

				// Every root, not just the one that is drawn at the moment: which of them that is
				// changes with the distance to the eye, every frame — see Render.ShapeDetail.
				var roots = models.MechDetailRoots(placement.TypeName);
				var model = roots.Count > 0 ? roots[0] : null;
				return (
					new MechObject(placement.TypeName, simData, model?.ShapeRadius ?? 0,
						new MechLoadout(
							placement.WeaponRefs.Select(id => (int)id).ToArray(),
							placement.WeaponSecondary),
						models.MechAnimation(placement.TypeName),
						models.MechHardpoints(placement.TypeName),
						weapons,
						models.Collision(placement.TypeName),
						ComponentDamageFor(models, placement.TypeName,
							ComponentDamage.MechComponentCount, ComponentDamage.MechDependentCount, random),
						models.MechWeaponCellCount,
						models.FlightModelFor(placement.TypeName)),
					model,
					roots.Count > 1
						? new ShapeDetailChain(roots, models.MechShapeRadius(placement.TypeName))
						: null);
			}

			case MissionUnitKind.Flyer: {
				if (placement.TypeName == null) {
					return (null, null, null);
				}

				var model = models.Flyer(placement.TypeName);
				return (
					new FlyerObject(placement.TypeName, models.FlyerData(placement.TypeName),
						model?.ShapeRadius ?? 0,
						models.Collision(placement.TypeName),
						ComponentDamageFor(models, placement.TypeName,
							ComponentDamage.FlyerComponentCount, ComponentDamage.FlyerDependentCount, random),
						models.FlightModelFor(placement.TypeName) is { } fm
							? new FlightModelRecord(fm)
							: null) {
						// The two PROJ.DAT rows the flyer AI names by literal — see FlyerObject.AttackRun.
						GunProjectile = weapons?.ProjectileAt(FlyerObject.GunProjectileIndex),
						MissileProjectile = weapons?.Lookup(
							ProjectileType.Rocket, FlyerObject.MissileSubtype)
					},
					model, null);
			}

			case MissionUnitKind.Base: {
				if (baseTypes[placement.TypeIndex] is not { } type) {
					return (null, null, null);
				}

				var model = models.Base(type);
				var (boundingRadius, volume) = models.GridShapeCollision(type);
				return (
					new BaseObject(type, volume, baseCollision[type.Index], boundingRadius,
						models.BaseAnimCellCount(type), models.BaseAnimation(type),
						placement.StartingCondition, models.HulkCollision(type),
						tweaks.GetSettingValue(TweakSettingDefinitions.SmootherTurretMovement)) {
						// The two PROJ.DAT rows the armed structure's tick names by literal, the same
						// way the flyer AI does — see BaseObject.ArmedThinkTick.
						GunProjectile = weapons?.ProjectileAt(BaseObject.GunProjectileIndex),
						MissileProjectile = weapons?.Lookup(
							ProjectileType.Rocket, BaseObject.MissileSubtype),
						TransportArmament = transportArmament
					},
					model, null);
			}

			default:
				return (null, null, null);
		}
	}

	/// <summary>
	/// Puts the camera where the mission starts: a short distance behind and above the machine the
	/// player pilots, so the opening view is the one the mission was authored around. With no
	/// <c>player.mec</c> to say what that is, it falls back to the first placed object, and to the
	/// middle of the zone if the mission places nothing at all.
	/// </summary>
	private static Vec3i CameraStart(Mission mission, HeightGrid terrain) {
		var target = mission.Player?.Position
			?? mission.Placements.FirstOrDefault()?.Position
			?? new Vec3i((int)(terrain.WorldWidth / 2), (int)(terrain.WorldHeight / 2), 0);

		var eye = new Vec3i(target.X, target.Y - 6000, 0);
		return new Vec3i(eye.X, eye.Y, terrain.HeightAtWorld(eye.X, eye.Y) + 3000);
	}
}
