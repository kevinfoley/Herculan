using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io;
using Herculan.Engine.Content;
using Herculan.Engine.Scene;
using Herculan.Engine.Shell;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// One opened mission and everything the editor builds over it on the CPU: the scene, its objects placed from the
/// <c>.MSN</c>'s layout (<see cref="Index"/>), the overlays, the picker, the two side panels, the Mission Settings window and the move, add, delete, fit, type, starting condition, heading, base pad, route, trigger area, trigger, countdown, condition, presence, random table, order, objective, text and theater tools, all sharing one
/// selection. Every edit, undo and redo syncs the scene and the indices with the document in place (<see cref="Sync"/>), and a
/// change of theater or time of day dresses the scene in its world again in place (<see cref="Theater"/>). Opening another
/// mission builds a new one of these rather than resetting this one; <see cref="MissionDrawables"/> is its GPU half.
/// </summary>
internal sealed class EditorMission {
	private EditorMission(GameContent content, string name, MissionDocument source, MissionScene scene,
			PlacementTables tables, MissionIndex sceneIndex, TerrainMaterialTable materials,
			MissionSquad squad, Func<GameContent> shellContent, CybridWeapons cybrid, ZoneCatalog zones, LoadoutPresets presets) {
		Name = name;
		Source = source;
		Scene = scene;
		Index = sceneIndex;
		PlayerSquad = squad;
		Names = new MissionNames(sceneIndex, RosterTypeNames.Load(content));

		var drape = new DrapedLines(scene.World.Terrain);
		Overlay = new MissionOverlay(sceneIndex, drape);
		Picker = new ScenePicker(scene, sceneIndex, Overlay);
		GroupOverlay = new GroupOverlay(sceneIndex, drape, Picker);
		Outliner = new MissionOutliner(source, sceneIndex, Names, Selection);
		Sync = new SceneSync(source, scene, tables, sceneIndex, Overlay, Picker, Outliner, Selection);
		Mover = new PointMover(source, scene, sceneIndex, Sync, Overlay, materials);
		Gizmo = new MoveGizmo(Selection, Mover);
		Placer = new StructurePlacer(source, scene, sceneIndex, Mover, Sync, Selection, tables.Bases);
		Sides = new SideChanger(source, sceneIndex, Mover, Sync);
		Deleter = new ObjectDeleter(source, sceneIndex, Mover, Sync);
		GroupDeleter = new GroupDeleter(source, sceneIndex, Mover, Sync, Deleter);
		Members = new MemberMover(source, sceneIndex, Mover, Sync);
		Fitter = new MachineFitter(source, Mover, Sync, shellContent, cybrid);
		Presets = new LoadoutPresetView(Fitter, presets);
		Squad = new SquadEditor(source, Mover, Sync, Fitter, Deleter);
		Theater = new TheaterChanger(source, scene, sceneIndex, Mover, Sync);
		Routes = new RouteEditor(source, sceneIndex, Mover, Sync, Selection, Deleter);
		Placing = new RoutePlacing(Routes, sceneIndex, drape);
		Areas = new AreaEditor(source, sceneIndex, Mover, Sync, Selection, Deleter);
		AreaPlacing = new AreaPlacing(Areas, drape);
		Placing.Blocked = () => AreaPlacing.Active;
		AreaPlacing.Blocked = () => Placing.Active;
		Hercs = new HercPlacer(source, scene, sceneIndex, Mover, Sync, Selection, Fitter, UnitTypeNames.LoadMechs(content));
		Flyers = new FlyerPlacer(source, scene, sceneIndex, Mover, Sync, Selection, UnitTypeNames.LoadFlyers(content));
		Vehicles = new VehiclePlacer(source, sceneIndex, Mover, Sync, Selection, Placer);
		Orders = new OrderEditor(source, Mover, Sync);
		Objectives = new ObjectiveEditor(source, Mover, Sync, Selection);
		Texts = new MissionTextEditor(source, Mover, Sync);
		Debrief = new DebriefEditor(source, Mover, Sync, () => squad.PlayerMachine == PlayerMachineSource.CareerSquad);
		Actions = new ActionEditor(source, Mover, Sync, Selection);
		Timers = new TimerEditor(source, Mover, Sync, Selection);
		Conditions = new ConditionEditor(source, sceneIndex, Names, Mover, Sync, Selection, Deleter);
		Presence = new PresenceEditor(source, sceneIndex, Mover, Sync, Deleter,
			() => squad.PlayerMachine == PlayerMachineSource.CareerSquad);
		TableEditor = new RandomTableEditor(source, sceneIndex, Mover, Sync, Selection);
		Types = new RosterTypeEditor(source, Mover, Sync);
		StartingConditions = new StartingConditionEditor(source, Mover, Sync);
		RadarSettings = new RadarSettingEditor(source, Mover, Sync);
		Headings = new HeadingEditor(source, Mover, Sync);
		Pads = new BasePadEditor(source, sceneIndex, tables.Bases, Mover, Sync);
		Formations = new FormationEditor(source, sceneIndex, tables, Mover, Sync);
		UnusedPoints = new UnusedPointRemover(source, Mover, Sync);
		Outliner.Placing = Placing;
		Outliner.AreaPlacing = AreaPlacing;
		Outliner.Objectives = Objectives;
		Outliner.Actions = Actions;
		Outliner.Timers = Timers;
		Outliner.Conditions = Conditions;
		var randomTables = new RandomTables(source, sceneIndex, Names, squad);
		Outliner.Tables = randomTables;
		PlayableIn = new PlayableInEditor(source, Mover, Sync);
		About = new MissionAboutEditor(Mover);
		Settings = new MissionSettingsWindow(() => Name, () => zones.Describe(Zone), Theater, TheaterNames.Labels(content),
			PlayableIn, About, new MissionTextView(Texts), new DebriefView(Debrief));
		var catalog = new UnitTypeCatalog(content);
		Properties = new PropertiesPanel(sceneIndex, Selection, source, Mover, Sides, Deleter, Members,
			new SquadMachinePanel(Fitter, Squad, squad, Presets), new RosterFitView(Fitter, Types, TableEditor, catalog, Presets),
			Routes, Placing, Areas, AreaPlacing, Orders, Objectives, Actions, Timers, Conditions, Presence, TableEditor,
			StartingConditions, RadarSettings, Headings, randomTables, new TriggerMessages(content), Names, catalog, tables.Bases, Types, Fitter, Placer, Hercs, Flyers,
			Pads, Formations, GroupDeleter);
	}

	/// <summary>
	/// What the window title calls the mission: the <c>.MSN</c> it was generated from, without its extension, which a
	/// Save writes and a Save As renames.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// The mission as its <c>.MSN</c> and <c>.ENG</c>, which every edit writes.
	/// </summary>
	public MissionDocument Source { get; }

	public MissionScene Scene { get; }

	/// <summary>Which Hercs the mission's squad pilots, which builds the handoff's <c>player.mec</c> for File &gt; Export.</summary>
	public MissionSquad PlayerSquad { get; }

	/// <summary>The zone the mission is played in: the launch's header word (<see cref="MissionDocument.LaunchHeader"/>).</summary>
	public short Zone => Source.LaunchHeader[ZoneCatalog.ZoneWord];

	/// <summary>
	/// The mission's own zone, which Save writes beside its <c>.MSN</c> (<see cref="SavedMissions.Write"/>) and Export Mission
	/// Bundle into its bundle, or null when it plays on the archive's zone <see cref="Zone"/>.
	/// </summary>
	public ZoneFiles? OwnZone { get; set; }

	/// <summary>The scene's objects and the mission's groups, routes and trigger areas, as the <c>.MSN</c>'s layout places them, by GUID.</summary>
	public MissionIndex Index { get; }

	/// <summary>What the panels, the outliner and the Toolbox name the mission's records by.</summary>
	public MissionNames Names { get; }

	public SelectionState Selection { get; } = new();

	public MissionOverlay Overlay { get; }

	public ScenePicker Picker { get; }

	public GroupOverlay GroupOverlay { get; }

	public MissionOutliner Outliner { get; }

	public PropertiesPanel Properties { get; }

	/// <summary>The Mission Settings window, which Edit &gt; Mission Settings opens.</summary>
	public MissionSettingsWindow Settings { get; }

	/// <summary>The edits made to <see cref="Source"/>, and their undo history.</summary>
	public PointMover Mover { get; }

	public MoveGizmo Gizmo { get; }

	/// <summary>Syncs the scene and the indices with <see cref="Source"/> after every edit, undo and redo.</summary>
	public SceneSync Sync { get; }

	/// <summary>Adds structures to <see cref="Source"/>, a structure group's chosen place included, as steps of <see cref="Mover"/>'s undo history.</summary>
	public StructurePlacer Placer { get; }

	/// <summary>Adds hercs outside the player's squad to <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public HercPlacer Hercs { get; }

	/// <summary>Adds flyers to <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public FlyerPlacer Flyers { get; }

	/// <summary>Adds ground vehicles to <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public VehiclePlacer Vehicles { get; }

	/// <summary>Edits groups' order slots in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public OrderEditor Orders { get; }

	/// <summary>Removes the positions nothing in <see cref="Source"/> uses, as a step of <see cref="Mover"/>'s undo history.</summary>
	public UnusedPointRemover UnusedPoints { get; }

	/// <summary>Creates, edits and deletes objectives in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public ObjectiveEditor Objectives { get; }

	/// <summary>Edits the objective, briefing and intelligence lines of <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public MissionTextEditor Texts { get; }

	/// <summary>Edits the debrief text of <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public DebriefEditor Debrief { get; }

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

	/// <summary>Creates, edits and deletes random tables in <see cref="Source"/>, and sets which one an object picks from, as steps of <see cref="Mover"/>'s undo history.</summary>
	public RandomTableEditor TableEditor { get; }

	public SideChanger Sides { get; }

	/// <summary>Deletes objects from <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public ObjectDeleter Deleter { get; }

	/// <summary>Deletes a group whole, its members with it.</summary>
	public GroupDeleter GroupDeleter { get; }

	/// <summary>Moves objects into other groups of <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public MemberMover Members { get; }

	/// <summary>Changes hercs' chassis and weapon fits in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public MachineFitter Fitter { get; }

	/// <summary>The loadout preset choice of a HERC's view, and its prompts, which take the keyboard while one is up.</summary>
	public LoadoutPresetView Presets { get; }

	/// <summary>Changes the types of flyers, structures and ground vehicles in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public RosterTypeEditor Types { get; }

	/// <summary>Sets the starting condition of hercs, structures and ground vehicles in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public StartingConditionEditor StartingConditions { get; }

	/// <summary>Turns the radar of hercs on and off in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public RadarSettingEditor RadarSettings { get; }

	/// <summary>Marks <see cref="Source"/> as meant for HERCULAN alone or for retail too, as steps of <see cref="Mover"/>'s undo history.</summary>
	public PlayableInEditor PlayableIn { get; }

	/// <summary>Edits the mission's about file, kept beside its <c>.MSN</c>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public MissionAboutEditor About { get; }

	/// <summary>Sets the own headings of structures, ground vehicles and groups in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public HeadingEditor Headings { get; }

	/// <summary>Turns structure groups' base pads on and off in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public BasePadEditor Pads { get; }

	/// <summary>Sets groups' formations in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public FormationEditor Formations { get; }

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
	public string Title(bool unsaved) => $"HERCULAN Mission Editor — {Name}{(unsaved ? "*" : "")}, zone {Zone}";

	/// <summary>
	/// Opens the handoff at <paramref name="scriptPath"/> as <paramref name="source"/>, the mission it holds as its
	/// <c>.MSN</c>: the document's generated <c>script.dat</c> and <c>mission.str</c> are written over the handoff's, the
	/// world is loaded for the mission generated from them (<see cref="MissionScene.LoadWorld"/>), and the objects are built
	/// for the records the document's layout places (<see cref="LayoutPlacement"/>) and stood on the ground as the game's
	/// load stands its own (<see cref="MissionScene.Settle"/>). That write and load happen once, here; an edit writes nothing
	/// and loads nothing. <paramref name="dataDirectory"/> is where the simulator's
	/// settings are when they are not beside the script — see <see cref="MissionScene.Load"/>.
	/// <paramref name="squad"/> is which Hercs the mission's squad pilots, which builds the <c>player.mec</c> File &gt; Export
	/// writes, <paramref name="shellContent"/> VSHELL's mount, which holds the shell's chassis and weapon tables,
	/// <paramref name="cybrid"/> what a Cybrid's hardpoints offer and <paramref name="zones"/> the install's zones with the
	/// retail missions that use each, both built once per editor run, and <paramref name="presets"/> the loadout presets, read once
	/// per editor run, or with none an empty set kept in memory only. Throws
	/// <see cref="MissingHandoffFileException"/> as <see cref="MissionLoader.Load"/> does.
	/// </summary>
	public static EditorMission Open(GameContent content, string scriptPath, string name, string dataDirectory,
			MissionDocument source, MissionSquad squad, Func<GameContent> shellContent, CybridWeapons cybrid, ZoneCatalog zones,
			LoadoutPresets? presets = null) {
		File.WriteAllBytes(scriptPath, source.ScriptBytes);
		File.WriteAllBytes(CaseInsensitivePath.Combine(Path.GetDirectoryName(scriptPath) ?? ".", MissionLoader.TextFileName),
			source.MissionText);
		var generated = MissionLoader.Load(content, source.Script, scriptPath);
		var scene = MissionScene.LoadWorld(content, generated, dataDirectory);
		var tables = PlacementTables.Load(content);
		var plan = LayoutPlacement.Place(source.Layout, tables);
		var objects = SceneSync.Create(scene, source, plan);
		var sceneIndex = new MissionIndex(plan, objects);
		scene.Settle(sceneIndex.Objects, sceneIndex.BasePads);
		foreach (var sceneObject in sceneIndex.Objects) {
			sceneObject.Object.Position = GroundStanding.Settled(scene.World.Terrain, sceneObject.Object, sceneObject.Object.Position);
		}

		// The scene's own copy is not kept; a base pad's tile is sized by its material when the ground is levelled again.
		return new EditorMission(content, name, source, scene, tables, sceneIndex,
			TerrainMaterialTable.Load(content), squad, shellContent, cybrid, zones, presets ?? new LoadoutPresets());
	}
}
