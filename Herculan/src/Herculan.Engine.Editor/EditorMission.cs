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
/// One opened mission and everything the editor builds over it on the CPU: the scene, the cross-reference
/// index, the overlays, the picker, the two side panels and the move, add, delete, route, trigger area, action, timer, condition, presence, order, objective, text, theater and campaign state tools, all sharing one
/// selection. An edit that adds or removes records builds the CPU state again in place (<see cref="Rebuild"/>), and a
/// change of theater or time of day dresses the scene in its world again in place (<see cref="Theater"/>). Opening another
/// mission builds a new one of these rather than resetting this one; <see cref="MissionDrawables"/> is its GPU half.
/// </summary>
internal sealed class EditorMission {
	private EditorMission(GameContent content, string name, ScriptDat document, MissionDocument? source, MissionScene scene,
			MissionIndex index, TerrainMaterialTable materials, MissionSquad? squad, Func<GameContent>? shellContent,
			MissionLaunchMode? mode, string handoffDirectory) {
		Name = name;
		Document = document;
		Source = source;
		Scene = scene;
		Index = index;

		var drape = new DrapedLines(scene.World.Terrain);
		Overlay = new MissionOverlay(index, drape);
		Picker = new ScenePicker(scene, index, Overlay);
		GroupOverlay = new GroupOverlay(index, drape, Picker);
		Outliner = new MissionOutliner(index, new MissionLint(index, document, source?.Provenance.Lines.Count ?? 0), Selection);
		Mover = new PointMover(document, source, scene, index, Picker, Overlay, materials);
		Gizmo = new MoveGizmo(Selection, Mover);
		Rebuild = new MissionRebuild(content, document, source, scene, index, Overlay, Picker, Mover, Outliner, Selection, squad);
		Mover.FullRebuild = Rebuild;
		Placer = new StructurePlacer(document, source, scene, index, Mover, Rebuild, Selection);
		Sides = new SideChanger(document, source, index, Mover);
		Deleter = new ObjectDeleter(document, source, index, Mover, Rebuild);
		Fitter = new MachineFitter(document, source, Mover, Rebuild, shellContent);
		Squad = new SquadEditor(document, source, index, Mover, Rebuild, Fitter, Deleter);
		Theater = new TheaterChanger(document, source, scene, index, Mover, Rebuild);
		Campaign = new CampaignState(source, Rebuild, Mover, Theater, mode, handoffDirectory);
		Routes = new RouteEditor(source, index, Mover, Rebuild, Selection, Deleter);
		Placing = new RoutePlacing(Routes, index, drape);
		Areas = new AreaEditor(source, index, Mover, Rebuild, Selection, Deleter);
		AreaPlacing = new AreaPlacing(Areas, drape);
		Placing.Blocked = () => AreaPlacing.Active;
		AreaPlacing.Blocked = () => Placing.Active;
		Hercs = new HercPlacer(document, source, scene, index, Mover, Rebuild, Selection, Fitter, UnitTypeNames.LoadMechs(content));
		Flyers = new FlyerPlacer(document, source, scene, index, Mover, Rebuild, Selection, UnitTypeNames.LoadFlyers(content));
		Vehicles = new VehiclePlacer(document, source, index, Mover, Rebuild, Selection, Placer);
		Orders = new OrderEditor(document, source, Mover, Rebuild);
		Objectives = new ObjectiveEditor(source, Mover, Rebuild, Selection);
		Texts = new MissionTextEditor(source, Mover, Rebuild);
		Actions = new ActionEditor(source, Mover, Rebuild, Selection, Deleter);
		Timers = new TimerEditor(source, Mover, Rebuild, Selection);
		Conditions = new ConditionEditor(source, index, Mover, Rebuild, Selection, Deleter);
		Presence = new PresenceEditor(source, Mover, Rebuild, Selection, Deleter);
		Outliner.Placing = Placing;
		Outliner.AreaPlacing = AreaPlacing;
		Outliner.Objectives = Objectives;
		Outliner.Actions = Actions;
		Outliner.Timers = Timers;
		Outliner.Conditions = Conditions;
		Properties = new PropertiesPanel(index, document, Selection, Mover, Sides, Deleter,
			new SquadMachinePanel(Fitter, Squad, squad),
			new MissionView(() => Name, () => document.ZoneIndex, Theater, TheaterNames.Labels(content),
				new CampaignStateView(Campaign), new MissionTextView(Texts)),
			Routes, Placing, Areas, AreaPlacing, Orders, Objectives, Actions, Timers, Conditions, Presence, SimStrings.Load(content));
	}

	/// <summary>
	/// What the window title calls the mission: the <c>.MSN</c> it was generated from, without its extension, which a
	/// Save writes and a Save As renames; or the file opened.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// The <c>script.dat</c> <see cref="Scene"/> was loaded from: <see cref="Source"/>'s generated file, whose blocks each
	/// edit's generation replaces, or a plain <c>script.dat</c> as parsed. It keeps every record as written, including what
	/// loading resolves away — a ref past the end of its block, a slot DBSIM never reads.
	/// </summary>
	public ScriptDat Document { get; }

	/// <summary>
	/// The mission as its <c>.MSN</c> and <c>.ENG</c>, which every edit writes; null for a plain <c>script.dat</c>, which
	/// opens read-only (docs/herculan/handoff-editor-authoring.md#holding-the-msn-as-the-document).
	/// </summary>
	public MissionDocument? Source { get; }

	public MissionScene Scene { get; }

	/// <summary>The mission as the index last built it: as loaded, or as generated after an edit that added or removed records.</summary>
	public Mission Mission => Index.Mission;

	/// <summary>The mission's own features, cross-referenced once.</summary>
	public MissionIndex Index { get; }

	public SelectionState Selection { get; } = new();

	public MissionOverlay Overlay { get; }

	public ScenePicker Picker { get; }

	public GroupOverlay GroupOverlay { get; }

	public MissionOutliner Outliner { get; }

	public PropertiesPanel Properties { get; }

	/// <summary>The edits made to <see cref="Source"/>, and their undo history.</summary>
	public PointMover Mover { get; }

	public MoveGizmo Gizmo { get; }

	/// <summary>Builds the CPU state again after an edit that adds or removes records of <see cref="Source"/>.</summary>
	public MissionRebuild Rebuild { get; }

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

	public SideChanger Sides { get; }

	/// <summary>Deletes objects from <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public ObjectDeleter Deleter { get; }

	/// <summary>Changes the squad's chassis and weapon fits in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public MachineFitter Fitter { get; }

	/// <summary>Adds and removes the player's squadmates in <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public SquadEditor Squad { get; }

	/// <summary>Changes the mission's theater and time of day in <see cref="Source"/>, reloading the scene's world, as steps of <see cref="Mover"/>'s undo history.</summary>
	public TheaterChanger Theater { get; }

	/// <summary>Sets the campaign flags and draws <see cref="Source"/> is shown with, outside the undo history.</summary>
	public CampaignState Campaign { get; }

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
	/// Opens the handoff at <paramref name="scriptPath"/>. Given <paramref name="source"/>, the mission it holds as its
	/// <c>.MSN</c>, its generated <c>script.dat</c> and <c>mission.str</c> are written over the handoff's and the scene is
	/// loaded from them; without one the <c>script.dat</c> there opens read-only. <paramref name="dataDirectory"/> is where
	/// the simulator's settings are when they are not beside the script — see <see cref="MissionScene.Load"/>.
	/// <paramref name="squad"/> is which Hercs the mission's squad pilots, which builds the handoff's <c>player.mec</c> again after
	/// an edit, and <paramref name="shellContent"/> VSHELL's mount, which holds the shell's chassis and weapon tables. Throws
	/// <see cref="MissingHandoffFileException"/> as <see cref="MissionLoader.Load"/> does. <paramref name="mode"/> is how the
	/// mission was opened, which Draw again opens it by again (<see cref="CampaignState"/>).
	/// </summary>
	public static EditorMission Open(GameContent content, string scriptPath, string name, string? dataDirectory = null,
			MissionDocument? source = null, MissionSquad? squad = null, Func<GameContent>? shellContent = null,
			MissionLaunchMode? mode = null) {
		ScriptDat document;
		if (source != null) {
			File.WriteAllBytes(scriptPath, source.ScriptBytes);
			File.WriteAllBytes(CaseInsensitivePath.Combine(Path.GetDirectoryName(scriptPath) ?? ".", MissionLoader.TextFileName),
				source.MissionText);
			document = source.Script;
		} else {
			document = MissionLoader.ReadScript(scriptPath);
		}

		var scene = MissionScene.Load(content, scriptPath, dataDirectory, document);
		foreach (var sceneObject in scene.Objects) {
			sceneObject.Object.Position = PointFollowers.Settled(scene.World.Terrain, sceneObject.Object, sceneObject.Object.Position);
		}

		var index = new MissionIndex(scene, document, SquadMessages.LoadCommand(content, scene.Mission.Header.TrainingMissionNumber));
		// The scene's own copy is not kept; a base pad's tile is sized by its material when the ground is levelled again.
		return new EditorMission(content, name, document, source, scene, index, TerrainMaterialTable.Load(content), squad,
			shellContent, mode, Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? ".");
	}
}
