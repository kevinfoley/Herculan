using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Moves placed objects: it gives an object's roster record a block-1 point of its own in the document
/// (<see cref="ScriptPointRefs.Place"/>), stands the simulation object where the simulator would stand it, and keeps
/// an undo history. A roster record's own point wins over its group's, so the object alone moves
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 2).
///
/// <para>The height is the spawn pass's (rule 5 there): only a flyer's Z is read from the point, so only a flyer's is
/// the mission's to set, and the point written for anything else carries the height the simulator gives it
/// (<see cref="Standing"/>).</para>
///
/// <para>The terrain is not rebuilt: a structure moved off the ground levelled for it stands on the unlevelled ground
/// where it lands, until the mission is opened again.</para>
/// </summary>
internal sealed class ObjectMover {
	private readonly ScriptDat _document;
	private readonly HeightGrid _terrain;
	private readonly ScenePicker _picker;
	private readonly MissionOverlay _overlay;
	private readonly List<Edit> _undo = new();
	private readonly List<Edit> _redo = new();

	public ObjectMover(ScriptDat document, MissionScene scene, ScenePicker picker, MissionOverlay overlay) {
		_document = document;
		_terrain = scene.World.Terrain;
		_picker = picker;
		_overlay = overlay;
		LoadedBox = MissionBox.Of(scene.Mission.Coordinates);
	}

	/// <summary>Raised with each object whose simulation object has moved, for whatever draws it.</summary>
	public event Action<SceneObject>? Moved;

	/// <summary>The mission box as the mission was opened, which a point outside of grows.</summary>
	public MissionBox LoadedBox { get; }

	public bool CanUndo => _undo.Count > 0;

	public bool CanRedo => _redo.Count > 0;

	/// <summary>
	/// Why <paramref name="sceneObject"/> cannot be given a point of its own, or null when it can. The player's squad
	/// is not in the rosters: each <c>player.mec</c> entry is placed from block 11's record 0
	/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 6).
	/// </summary>
	public string? WhyFixed(SceneObject sceneObject) {
		var placement = sceneObject.Placement;
		if (placement.IsPlayerLance) {
			return "The player's squad stands at group 0's point; it has no roster record of its own to move.";
		}

		return RecordRef(placement.Kind, placement.SlotIndex) == null
			? $"Its {placement.Kind.ToString().ToLowerInvariant()} roster slot {placement.SlotIndex} is not in the file."
			: null;
	}

	/// <summary>Whether a point's Z is the mission's to set for <paramref name="sceneObject"/>: only a flyer's is.</summary>
	public static bool HasOwnHeight(SceneObject sceneObject) => sceneObject.Object is FlyerObject;

	/// <summary>
	/// Where the simulator stands <paramref name="sceneObject"/> for a point at (<paramref name="x"/>,
	/// <paramref name="y"/>, <paramref name="z"/>): <paramref name="z"/> is read for a flyer alone.
	/// </summary>
	public Vec3i Standing(SceneObject sceneObject, int x, int y, int z) => sceneObject.Object switch {
		FlyerObject => new Vec3i(x, y, z == 0 ? FlyerObject.DefaultHoverHeight : z),
		MechObject mech => new Vec3i(x, y, MissionScene.MechSpawnHeight(_terrain, mech, new Vec3i(x, y, 0))),
		_ => new Vec3i(x, y, _terrain.HeightAtWorld(x, y))
	};

	/// <summary>
	/// Stands <paramref name="sceneObject"/> at <paramref name="position"/> for the eye alone, as a drag does before it
	/// lets go: the document is unchanged until <see cref="Commit"/>.
	/// </summary>
	public void Preview(SceneObject sceneObject, Vec3i position) => Stand(sceneObject, position);

	/// <summary>
	/// Gives <paramref name="sceneObject"/>'s record a point of its own at (<paramref name="x"/>, <paramref name="y"/>,
	/// <paramref name="z"/>) and stands it there, as one step of the undo history. <paramref name="before"/> is where
	/// it stood before any <see cref="Preview"/> of this move.
	/// </summary>
	public void Commit(SceneObject sceneObject, Vec3i before, int x, int y, int z) {
		var placement = sceneObject.Placement;
		if (WhyFixed(sceneObject) != null) {
			return;
		}

		var position = Standing(sceneObject, x, y, z);
		var from = new State(Snapshot(_document.Coordinates), RecordRef(placement.Kind, placement.SlotIndex)!.Value, before);
		short reference;
		try {
			reference = ScriptPointRefs.Place(_document, from.Reference, position.X, position.Y, position.Z);
		} catch (InvalidOperationException ex) {
			Console.Error.WriteLine($"Cannot move {MissionText.Describe(sceneObject)}: {ex.Message}");
			Stand(sceneObject, before);
			return;
		}

		SetRecordRef(placement.Kind, placement.SlotIndex, reference);
		var to = new State(Snapshot(_document.Coordinates), reference, position);

		_undo.Add(new Edit(sceneObject, from, to));
		_redo.Clear();
		Stand(sceneObject, position);
		_overlay.SetBox(MissionBox.Of(Points()));
	}

	public void Undo() => Step(_undo, _redo, edit => edit.Before);

	public void Redo() => Step(_redo, _undo, edit => edit.After);

	private void Step(List<Edit> from, List<Edit> to, Func<Edit, State> state) {
		if (from.Count == 0) {
			return;
		}

		var edit = from[^1];
		from.RemoveAt(from.Count - 1);
		to.Add(edit);

		var target = state(edit);
		_document.Coordinates = Snapshot(target.Coordinates);
		SetRecordRef(edit.Object.Placement.Kind, edit.Object.Placement.SlotIndex, target.Reference);
		Stand(edit.Object, target.Position);
		_overlay.SetBox(MissionBox.Of(Points()));
	}

	private void Stand(SceneObject sceneObject, Vec3i position) {
		sceneObject.Object.Position = position;
		_picker.Move(sceneObject);
		Moved?.Invoke(sceneObject);
	}

	private List<Vec3i> Points() => _document.Coordinates.Select(point => new Vec3i(point.X, point.Y, point.Z)).ToList();

	private short? RecordRef(MissionUnitKind kind, int slot) => kind switch {
		MissionUnitKind.Mech when slot >= 0 && slot < _document.Mechs.Length => _document.Mechs[slot].PositionRef,
		MissionUnitKind.Flyer when slot >= 0 && slot < _document.Flyers.Length => _document.Flyers[slot].PositionRef,
		MissionUnitKind.Base when slot >= 0 && slot < _document.Bases.Length => _document.Bases[slot].PositionRef,
		_ => null
	};

	private void SetRecordRef(MissionUnitKind kind, int slot, short reference) {
		switch (kind) {
			case MissionUnitKind.Mech:
				_document.Mechs[slot].PositionRef = reference;
				break;
			case MissionUnitKind.Flyer:
				_document.Flyers[slot].PositionRef = reference;
				break;
			case MissionUnitKind.Base:
				_document.Bases[slot].PositionRef = reference;
				break;
		}
	}

	// Block 1 is copied whole for each step: a few hundred points at most, and a copy cannot be changed by a later
	// edit's write in place.
	private static ScriptCoordinate[] Snapshot(ScriptCoordinate[] points) =>
		Array.ConvertAll(points, point => new ScriptCoordinate { X = point.X, Y = point.Y, Z = point.Z });

	private sealed record State(ScriptCoordinate[] Coordinates, short Reference, Vec3i Position);

	private sealed record Edit(SceneObject Object, State Before, State After);
}
