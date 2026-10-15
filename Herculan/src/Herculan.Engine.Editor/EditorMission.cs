using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Content;
using Herculan.Engine.Scene;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// One opened mission and everything the editor builds over it on the CPU: the scene, the cross-reference
/// index, the overlays, the picker, the two side panels and the move and add tools, all sharing one selection. Opening another mission
/// builds a new one of these rather than resetting this one; <see cref="MissionDrawables"/> is its GPU half.
/// </summary>
internal sealed class EditorMission {
	private EditorMission(string name, ScriptDat document, MissionScene scene, MissionIndex index,
			TerrainMaterialTable materials) {
		Name = name;
		Document = document;
		Scene = scene;
		Index = index;

		var drape = new DrapedLines(scene.World.Terrain);
		Overlay = new MissionOverlay(index, drape);
		Picker = new ScenePicker(scene, index, Overlay);
		GroupOverlay = new GroupOverlay(index, drape, Picker);
		Outliner = new MissionOutliner(index, new MissionLint(index, document), Selection);
		Mover = new PointMover(document, scene, index, Picker, Overlay, materials);
		Gizmo = new MoveGizmo(Selection, Mover);
		Placer = new StructurePlacer(document, scene, index, Picker, Mover, Selection);
		Properties = new PropertiesPanel(index, Selection, Mover);
	}

	/// <summary>What the window title calls the mission: the <c>.MSN</c> it was generated from, or the file opened.</summary>
	public string Name { get; }

	/// <summary>
	/// The file as parsed, which <see cref="Scene"/> was loaded from: what an edit changes and a save writes. It keeps
	/// every record as written, including what loading resolves away — a ref past the end of its block, a slot DBSIM
	/// never reads.
	/// </summary>
	public ScriptDat Document { get; }

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

	/// <summary>The edits made to <see cref="Document"/>, and their undo history.</summary>
	public PointMover Mover { get; }

	public MoveGizmo Gizmo { get; }

	/// <summary>Adds structures to <see cref="Document"/>, as steps of <see cref="Mover"/>'s undo history.</summary>
	public StructurePlacer Placer { get; }

	public string WindowTitle => $"HERCULAN Mission Editor — {Name}, zone {Mission.Header.ZoneIndex}";

	/// <summary>
	/// Opens the handoff at <paramref name="scriptPath"/>. <paramref name="dataDirectory"/> is where the
	/// simulator's settings are when they are not beside the script — see <see cref="MissionScene.Load"/>. Throws
	/// <see cref="MissingHandoffFileException"/> as <see cref="MissionLoader.Load"/> does.
	/// </summary>
	public static EditorMission Open(GameContent content, string scriptPath, string name, string? dataDirectory = null) {
		var document = MissionLoader.ReadScript(scriptPath);
		var scene = MissionScene.Load(content, scriptPath, dataDirectory, document);
		foreach (var sceneObject in scene.Objects) {
			sceneObject.Object.Position = PointFollowers.Settled(scene.World.Terrain, sceneObject.Object, sceneObject.Object.Position);
		}

		var index = new MissionIndex(scene, SquadMessages.LoadCommand(content, scene.Mission.Header.TrainingMissionNumber));
		// The scene's own copy is not kept; a base pad's tile is sized by its material when the ground is levelled again.
		return new EditorMission(name, document, scene, index, TerrainMaterialTable.Load(content));
	}
}
