using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io;
using Herculan.Engine.Content;
using Herculan.Engine.Scene;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// One opened mission and everything the editor builds over it on the CPU: the scene, the cross-reference
/// index, the overlays, the picker, the two side panels and the move, add and delete tools, all sharing one selection. Opening another mission
/// builds a new one of these rather than resetting this one; <see cref="MissionDrawables"/> is its GPU half.
/// </summary>
internal sealed class EditorMission {
	private EditorMission(string name, ScriptDat document, MissionDocument? source, MissionScene scene, MissionIndex index,
			TerrainMaterialTable materials) {
		Name = name;
		Document = document;
		Source = source;
		Scene = scene;
		Index = index;

		var drape = new DrapedLines(scene.World.Terrain);
		Overlay = new MissionOverlay(index, drape);
		Picker = new ScenePicker(scene, index, Overlay);
		GroupOverlay = new GroupOverlay(index, drape, Picker);
		Outliner = new MissionOutliner(index, new MissionLint(index, document), Selection);
		Mover = new PointMover(document, source, scene, index, Picker, Overlay, materials);
		Gizmo = new MoveGizmo(Selection, Mover);
		Placer = new StructurePlacer(document, source, scene, index, Picker, Mover, Selection);
		Sides = new SideChanger(document, source, index, Mover);
		Deleter = new ObjectDeleter(document, index, Picker, Mover, Selection);
		Properties = new PropertiesPanel(index, Selection, Mover, Sides, Deleter);
	}

	/// <summary>What the window title calls the mission: the <c>.MSN</c> it was generated from, or the file opened.</summary>
	public string Name { get; }

	/// <summary>
	/// The <c>script.dat</c> <see cref="Scene"/> was loaded from: <see cref="Source"/>'s generated file, whose blocks each
	/// edit's generation replaces, or a plain <c>script.dat</c> as parsed. It keeps every record as written, including what
	/// loading resolves away — a ref past the end of its block, a slot DBSIM never reads.
	/// </summary>
	public ScriptDat Document { get; }

	/// <summary>
	/// The mission as its <c>.MSN</c> and <c>.ENG</c>, which every edit writes; null for a plain <c>script.dat</c>, which
	/// opens read-only (docs/herculan/handoff-editor-mission-editing.md#holding-the-msn-as-the-document).
	/// </summary>
	public MissionDocument? Source { get; }

	public MissionScene Scene { get; }

	public Mission Mission => Scene.Mission;

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

	/// <summary>Adds structures to <see cref="Source"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public StructurePlacer Placer { get; }

	public SideChanger Sides { get; }

	/// <summary>Deletes objects, as steps of <see cref="Mover"/>'s undo history; refuses every delete until it is ported to <see cref="Source"/>.</summary>
	public ObjectDeleter Deleter { get; }

	public string WindowTitle => $"HERCULAN Mission Editor — {Name}, zone {Mission.Header.ZoneIndex}";

	/// <summary>
	/// Opens the handoff at <paramref name="scriptPath"/>. Given <paramref name="source"/>, the mission it holds as its
	/// <c>.MSN</c>, its generated <c>script.dat</c> and <c>mission.str</c> are written over the handoff's and the scene is
	/// loaded from them; without one the <c>script.dat</c> there opens read-only. <paramref name="dataDirectory"/> is where
	/// the simulator's settings are when they are not beside the script — see <see cref="MissionScene.Load"/>. Throws
	/// <see cref="MissingHandoffFileException"/> as <see cref="MissionLoader.Load"/> does.
	/// </summary>
	public static EditorMission Open(GameContent content, string scriptPath, string name, string? dataDirectory = null,
			MissionDocument? source = null) {
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

		var index = new MissionIndex(scene, SquadMessages.LoadCommand(content, scene.Mission.Header.TrainingMissionNumber));
		// The scene's own copy is not kept; a base pad's tile is sized by its material when the ground is levelled again.
		return new EditorMission(name, document, source, scene, index, TerrainMaterialTable.Load(content));
	}
}
