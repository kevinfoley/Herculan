using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Adds a herc outside the player's squad to the open mission, as one step of <see cref="PointMover"/>'s undo history,
/// through <see cref="HercGroups"/>'s record edits: in a new group of its own, standing on a route of one waypoint where it
/// was put and hunting the player's squad, or joining the selected herc group. The scene is then synced with the document
/// (<see cref="SceneSync"/>), which builds the herc's object for the record, and the herc is
/// selected (docs/herculan/handoff-editor-roster.md#hercs).
///
/// <para>It comes with the fit a new herc of its type gets (<see cref="MachineFitter.NewHercFit"/>), as a chassis change gives
/// it.</para>
/// </summary>
internal sealed class HercPlacer {
	private const int MechRow = 12;
	private const int GroupBlock = 11;

	private readonly ScriptDat _document;
	private readonly MissionDocument _source;
	private readonly MissionScene _scene;
	private readonly GeneratedIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private readonly MachineFitter _fitter;
	private readonly UnitTypeNames _names;

	/// <param name="document">The generated <c>script.dat</c>, <paramref name="source"/>'s, which the scene was loaded from.</param>
	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	/// <param name="names">The <c>nam\MECHS.NAM</c> type names, which a ghost's model is found by.</param>
	public HercPlacer(ScriptDat document, MissionDocument source, MissionScene scene, GeneratedIndex index, PointMover mover,
			SceneSync sync, SelectionState selection, MachineFitter fitter, UnitTypeNames names) {
		_document = document;
		_source = source;
		_scene = scene;
		_index = index;
		_mover = mover;
		_sync = sync;
		_selection = selection;
		_fitter = fitter;
		_names = names;
	}

	/// <summary>
	/// Why no herc can be added, or null when one can: block 7 is full with the squad's
	/// machines counted (<see cref="HercGroups.WhyNoAdd"/>).
	/// </summary>
	public string? WhyNoAdd => HercGroups.WhyNoAdd(_source, SquadSize);

	/// <summary>
	/// Adds a herc of <c>MECHS.NAM</c> type <paramref name="typeIndex"/> on <paramref name="side"/> at
	/// (<paramref name="x"/>, <paramref name="y"/>), in a group of its own or, given <paramref name="joinGroup"/>, as a
	/// member of that herc group; and selects it. Answers why it could not, or null when it did.
	/// </summary>
	public string? Add(int typeIndex, MissionSide side, int x, int y, int? joinGroup = null) {
		if (WhyNoAdd is { } refused) {
			return refused;
		}

		if (_fitter.NewHercFit(typeIndex) is not var (weapons, ammo)) {
			return HercGroups.IsCybrid(typeIndex) ? $"There is no herc type {typeIndex}." : $"The install has no stock fit for chassis {typeIndex}.";
		}

		var source = _source;
		var herc = HercGroups.Machine((short)typeIndex, weapons, ammo);
		int squad = SquadSize;
		short mech = -1;
		if (joinGroup is { } joined) {
			if (WhyNotJoin(joined) is { } reason) {
				return reason;
			}

			short group = source.GuidOf(GroupBlock, joined);
			return Commit(typeIndex, () => HercGroups.Join(source, group, herc, squad, out mech), () => mech);
		}

		return Commit(typeIndex, () => HercGroups.Create(source, herc, (short)side, x, y, squad, out mech, out _), () => mech);
	}

	/// <summary>Why a herc cannot be added to group <paramref name="group"/>, by block-11 index, or null when it can.</summary>
	public string? WhyNotJoin(int group) =>
		group < 0 || group >= _document.Groups.Length || _source.GuidOf(GroupBlock, group) == -1 ? $"Group {group} is not in the file."
			: HercGroups.WhyNoJoin(_source, _source.GuidOf(GroupBlock, group));

	/// <summary>
	/// The herc <see cref="Add"/> would add of <paramref name="typeIndex"/> on <paramref name="side"/>, built the same way
	/// but in no roster slot or group and recorded nowhere, for <see cref="DropPreview"/> to draw; null for a type the
	/// install has nothing for.
	/// </summary>
	public SceneObject? Ghost(int typeIndex, MissionSide side) =>
		_fitter.NewHercFit(typeIndex) is var (weapons, ammo)
			? _scene.CreateObject(new MissionPlacement(MissionUnitKind.Mech, typeIndex, _names[typeIndex], -1, -1, Vec3i.Zero, 0,
				weapons, ammo, Side: side, OutOfActionReport: OutOfActionReport.None))
			: null;

	// The machines player.mec brings, which share block 7's slots, as the mission check counts them.
	private int SquadSize => _index.Mission.Placements.Count(placement => placement.IsPlayerLance);

	// Makes an add's change and syncs the scene with it, then records it as one step and selects the object built for
	// the herc's row-12 GUID; or, when none was built, puts the records back and says why.
	private string? Commit(int typeIndex, Func<MsnEdit> add, Func<short> mech) {
		var source = _source;
		MsnEdit change;
		try {
			change = _sync.Run(add);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		short added = mech();
		bool generated = source.ExportedIndex(MechRow, added) != null;
		if (!generated || _index.Scene.ObjectOf(new ObjectKey(MechRow, added)) == null) {
			_sync.Undo(change);
			return !generated
				? "The added herc was not generated; nothing was added."
				: $"The install has no herc type {typeIndex}.";
		}

		_mover.Record(new RecordsEdit(_sync, change));
		_selection.Current = new ObjectSelection(MechRow, added);
		return null;
	}
}
