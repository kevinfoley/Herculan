using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io;
using Herculan.Engine.Content;
using Herculan.Engine.Scene;
using Herculan.Engine.Shell;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// One opened mission and everything the editor builds over it on the CPU: the scene, its objects placed from the
/// <c>.MSN</c>'s layout (<see cref="Index"/>), the generated mission's cross-reference (<see cref="Generated"/>), the overlays, the picker, the two side panels and the move, add, delete, fit, type, route, trigger area, trigger, countdown, condition, presence, variant, order, objective, text and theater tools, all sharing one
/// selection. Every edit, undo and redo syncs the scene and the indices with the document in place (<see cref="Sync"/>), and a
/// change of theater or time of day dresses the scene in its world again in place (<see cref="Theater"/>). Opening another
/// mission builds a new one of these rather than resetting this one; <see cref="MissionDrawables"/> is its GPU half.
/// </summary>
internal sealed class EditorMission {
	private EditorMission(GameContent content, string name, ScriptDat document, MissionDocument source, MissionScene scene,
			PlacementTables tables, MissionIndex sceneIndex, GeneratedIndex index, MissionLint lint, TerrainMaterialTable materials,
			MissionSquad squad, Func<GameContent> shellContent) {
		Name = name;
		Document = document;
		Source = source;
		Scene = scene;
		Index = sceneIndex;
		Generated = index;
		Names = new MissionNames(sceneIndex, RosterTypeNames.Load(content));

		var drape = new DrapedLines(scene.World.Terrain);
		Overlay = new MissionOverlay(sceneIndex, drape);
		Picker = new ScenePicker(scene, sceneIndex, Overlay);
		GroupOverlay = new GroupOverlay(sceneIndex, drape, Picker);
		Outliner = new MissionOutliner(source, sceneIndex, Names, lint, Selection);
		Sync = new SceneSync(content, source, scene, tables, sceneIndex, index, Overlay, Picker, Outliner, Selection, squad);
		Mover = new PointMover(source, scene, sceneIndex, Sync, Overlay, materials);
		Gizmo = new MoveGizmo(Selection, Mover);
		Placer = new StructurePlacer(document, source, scene, index, Mover, Sync, Selection);
		Sides = new SideChanger(document, source, index, Mover, Sync);
		Deleter = new ObjectDeleter(document, source, index, Mover, Sync);
		Fitter = new MachineFitter(document, source, Mover, Sync, shellContent);
		Squad = new SquadEditor(document, source, index, Mover, Sync, Fitter, Deleter);
		Theater = new TheaterChanger(document, source, scene, index, Mover, Sync);
		Routes = new RouteEditor(source, index, Mover, Sync, Selection, Deleter);
		Placing = new RoutePlacing(Routes, index, drape);
		Areas = new AreaEditor(source, index, Mover, Sync, Selection, Deleter);
		AreaPlacing = new AreaPlacing(Areas, drape);
		Placing.Blocked = () => AreaPlacing.Active;
		AreaPlacing.Blocked = () => Placing.Active;
		Hercs = new HercPlacer(document, source, scene, index, Mover, Sync, Selection, Fitter, UnitTypeNames.LoadMechs(content));
		Flyers = new FlyerPlacer(document, source, scene, index, Mover, Sync, Selection, UnitTypeNames.LoadFlyers(content));
		Vehicles = new VehiclePlacer(document, source, index, Mover, Sync, Selection, Placer);
		Orders = new OrderEditor(source, Mover, Sync);
		Objectives = new ObjectiveEditor(source, Mover, Sync, Selection);
		Texts = new MissionTextEditor(source, Mover, Sync);
		Actions = new ActionEditor(source, Mover, Sync, Selection);
		Timers = new TimerEditor(source, Mover, Sync, Selection);
		Conditions = new ConditionEditor(source, index, Names, Mover, Sync, Selection, Deleter);
		Presence = new PresenceEditor(source, sceneIndex, Mover, Sync, Deleter);
		Variants = new VariantEditor(source, Mover, Sync, Selection);
		Types = new RosterTypeEditor(source, Mover, Sync);
		Outliner.Placing = Placing;
		Outliner.AreaPlacing = AreaPlacing;
		Outliner.Objectives = Objectives;
		Outliner.Actions = Actions;
		Outliner.Timers = Timers;
		Outliner.Conditions = Conditions;
		var choices = new RandomChoices(source, sceneIndex, Names, squad);
		Outliner.Choices = choices;
		Properties = new PropertiesPanel(index, Selection, Mover, Sides, Deleter,
			new SquadMachinePanel(Fitter, Squad, squad), new RosterFitView(Fitter, Types, Variants, choices, new UnitTypeCatalog(content)),
			new MissionView(() => Name, () => document.ZoneIndex, Theater, TheaterNames.Labels(content),
				new MissionTextView(Texts)),
			Routes, Placing, Areas, AreaPlacing, Orders, Objectives, Actions, Timers, Conditions, Presence, Variants,
			choices, new TriggerMessages(content), Names, SimStrings.Load(content));
	}

	/// <summary>
	/// What the window title calls the mission: the <c>.MSN</c> it was generated from, without its extension, which a
	/// Save writes and a Save As renames.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// The <c>script.dat</c> <see cref="Scene"/> was loaded from: <see cref="Source"/>'s generated file, whose blocks each
	/// edit's generation replaces. It keeps every record as written, including what
	/// loading resolves away — a ref past the end of its block, a slot DBSIM never reads.
	/// </summary>
	public ScriptDat Document { get; }

	/// <summary>
	/// The mission as its <c>.MSN</c> and <c>.ENG</c>, which every edit writes.
	/// </summary>
	public MissionDocument Source { get; }

	public MissionScene Scene { get; }

	/// <summary>The mission as generated after the last edit, which the views read.</summary>
	public Mission Mission => Generated.Mission;

	/// <summary>The scene's objects and the mission's groups, routes and trigger areas, as the <c>.MSN</c>'s layout places them, by GUID.</summary>
	public MissionIndex Index { get; }

	/// <summary>The generated mission's features, cross-referenced by block index, which the views read.</summary>
	public GeneratedIndex Generated { get; }

	/// <summary>What the panels, the outliner and the Toolbox name the mission's records by.</summary>
	public MissionNames Names { get; }

	public SelectionState Selection { get; } = new();

	public MissionOverlay Overlay { get; }

	public ScenePicker Picker { get; }

	public GroupOverlay GroupOverlay { get; }

	public MissionOutliner Outliner { get; }

	public PropertiesPanel Properties { get; }

	/// <summary>The edits made to <see cref="Source"/>, and their undo history.</summary>
	public PointMover Mover { get; }

	public MoveGizmo Gizmo { get; }

	/// <summary>Syncs the scene and the indices with <see cref="Source"/> after every edit, undo and redo.</summary>
	public SceneSync Sync { get; }

	/// <summary>Adds structures to <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public StructurePlacer Placer { get; }

	/// <summary>Adds hercs outside the player's squad to <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public HercPlacer Hercs { get; }

	/// <summary>Adds flyers to <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public FlyerPlacer Flyers { get; }

	/// <summary>Adds ground vehicles to <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public VehiclePlacer Vehicles { get; }

	/// <summary>Edits groups' order slots in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public OrderEditor Orders { get; }

	/// <summary>Creates, edits and deletes objectives in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public ObjectiveEditor Objectives { get; }

	/// <summary>Edits the objective, briefing and intelligence lines of <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public MissionTextEditor Texts { get; }

	/// <summary>
	/// Creates, edits and deletes actions in <see cref="Source"/>, and wires them into groups and objects, as steps of
	/// <see cref="Mover"/>'s undo history.
	/// </summary>
	public ActionEditor Actions { get; }

	/// <summary>Creates, edits and deletes action timers in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public TimerEditor Timers { get; }

	/// <summary>Creates, edits and deletes conditions in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public ConditionEditor Conditions { get; }

	/// <summary>Sets which campaign states a record of <see cref="Source"/> is present in, as steps of <see cref="Mover"/>'s undo history.</summary>
	public PresenceEditor Presence { get; }

	/// <summary>Creates, edits and deletes variant tables in <see cref="Source"/>, and sets which one an object draws from, as steps of <see cref="Mover"/>'s undo history.</summary>
	public VariantEditor Variants { get; }

	public SideChanger Sides { get; }

	/// <summary>Deletes objects from <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public ObjectDeleter Deleter { get; }

	/// <summary>Changes hercs' chassis and weapon fits in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public MachineFitter Fitter { get; }

	/// <summary>Changes the types of flyers, structures and ground vehicles in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public RosterTypeEditor Types { get; }

	/// <summary>Adds and removes the player's squadmates in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public SquadEditor Squad { get; }

	/// <summary>Changes the mission's theater and time of day in <see cref="Source"/>, reloading the scene's world, as steps of <see cref="Mover"/>'s undo history.</summary>
	public TheaterChanger Theater { get; }

	/// <summary>Creates, edits and deletes routes in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public RouteEditor Routes { get; }

	/// <summary>The route-placing mode, which takes the view's clicks while it is on.</summary>
	public RoutePlacing Placing { get; }

	/// <summary>Creates, reshapes and deletes trigger areas in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public AreaEditor Areas { get; }

	/// <summary>The area-placing mode, which takes the view's presses and releases while it is on.</summary>
	public AreaPlacing AreaPlacing { get; }

	/// <summary>Whether a route or a trigger area is being placed, while which the view picks and moves nothing.</summary>
	public bool PlacingAny => Placing.Active || AreaPlacing.Active;

	/// <summary>The window title, the name marked <c>*</c> when <paramref name="unsaved"/> changes are held.</summary>
	public string Title(bool unsaved) => $"HERCULAN Mission Editor — {Name}{(unsaved ? "*" : "")}, zone {Mission.Header.ZoneIndex}";

	/// <summary>
	/// Opens the handoff at <paramref name="scriptPath"/> as <paramref name="source"/>, the mission it holds as its
	/// <c>.MSN</c>: the document's generated <c>script.dat</c> and <c>mission.str</c> are written over the handoff's, the
	/// world is loaded for the mission generated from them (<see cref="MissionScene.LoadWorld"/>), and the objects are built
	/// for the records the document's layout places (<see cref="LayoutPlacement"/>) and stood on the ground as the game's
	/// load stands its own (<see cref="MissionScene.Settle"/>). <paramref name="dataDirectory"/> is where the simulator's
	/// settings are when they are not beside the script — see <see cref="MissionScene.Load"/>.
	/// <paramref name="squad"/> is which Hercs the mission's squad pilots, which builds the handoff's <c>player.mec</c> again after
	/// an edit, and <paramref name="shellContent"/> VSHELL's mount, which holds the shell's chassis and weapon tables. Throws
	/// <see cref="MissingHandoffFileException"/> as <see cref="MissionLoader.Load"/> does.
	/// </summary>
	public static EditorMission Open(GameContent content, string scriptPath, string name, string dataDirectory,
			MissionDocument source, MissionSquad squad, Func<GameContent> shellContent) {
		File.WriteAllBytes(scriptPath, source.ScriptBytes);
		File.WriteAllBytes(CaseInsensitivePath.Combine(Path.GetDirectoryName(scriptPath) ?? ".", MissionLoader.TextFileName),
			source.MissionText);
		var document = source.Script;
		var generated = MissionLoader.Load(content, document, scriptPath);
		var scene = MissionScene.LoadWorld(content, generated, dataDirectory);
		var tables = PlacementTables.Load(content);
		var plan = LayoutPlacement.Place(source.Layout, tables);
		var objects = SceneSync.Create(scene, source, plan);
		var sceneIndex = new MissionIndex(plan, objects);
		scene.Settle(sceneIndex.Objects, sceneIndex.BasePads);
		foreach (var sceneObject in sceneIndex.Objects) {
			sceneObject.Object.Position = GroundStanding.Settled(scene.World.Terrain, sceneObject.Object, sceneObject.Object.Position);
		}

		var index = new GeneratedIndex(generated, document, new GeneratedRecords(source), sceneIndex);
		// The scene's own copy is not kept; a base pad's tile is sized by its material when the ground is levelled again.
		return new EditorMission(content, name, document, source, scene, tables, sceneIndex, index,
			new MissionLint(source, sceneIndex, MissionLint.SquadOf(generated)), TerrainMaterialTable.Load(content), squad, shellContent);
	}
}
