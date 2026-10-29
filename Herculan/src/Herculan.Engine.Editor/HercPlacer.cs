using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Adds a herc outside the player's squad to the open mission, as one step of <see cref="PointMover"/>'s undo history,
/// through <see cref="HercGroups"/>'s record edits: in a new group of its own, standing on a route of one waypoint where it
/// was put and hunting the player's squad, or joining the selected herc group, or in a chosen member slot of a herc group
/// from the group's view (<see cref="RequestJoin"/>). The scene is then synced with the document
/// (<see cref="SceneSync"/>), which builds the herc's object for the record, and the herc is
/// selected (docs/herculan/editor-design.md#new-units).
///
/// <para>It gets its chassis's standard weapons (<see cref="MachineFitter.NewHercFit"/>), as a chassis change gives them.</para>
/// </summary>
internal sealed class HercPlacer {
	private const int MechRow = 12;

	private readonly MissionDocument _source;
	private readonly MissionScene _scene;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly SelectionState _selection;
	private readonly MachineFitter _fitter;
	private readonly UnitTypeNames _names;
	private Func<string?>? _requested;

	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	/// <param name="index">The layout's groups and objects.</param>
	/// <param name="names">The <c>nam\MECHS.NAM</c> type names, which a ghost's model is found by.</param>
	public HercPlacer(MissionDocument source, MissionScene scene, MissionIndex index, PointMover mover,
			SceneSync sync, SelectionState selection, MachineFitter fitter, UnitTypeNames names) {
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
	/// Why no herc can be added, or null when one can: block 7 is full in some version of the mission with the squad's
	/// machines counted (<see cref="HercGroups.WhyNoAdd"/>).
	/// </summary>
	public string? WhyNoAdd => HercGroups.WhyNoAdd(_source);

	/// <summary>
	/// Adds a herc of <c>MECHS.NAM</c> type <paramref name="typeIndex"/> on <paramref name="side"/> at
	/// (<paramref name="x"/>, <paramref name="y"/>), in a group of its own or, given <paramref name="joinGroup"/>, as a
	/// member of that herc group; and selects it. Answers why it could not, or null when it did.
	/// </summary>
	public string? Add(int typeIndex, MissionSide side, int x, int y, short? joinGroup = null) {
		if (WhyNoAdd is { } refused) {
			return refused;
		}

		if (_fitter.NewHercFit(typeIndex) is not var (weapons, ammo)) {
			return HercGroups.IsCybrid(typeIndex) ? $"There is no HERC type {typeIndex}." : $"The install has no stock fit for chassis {typeIndex}.";
		}

		var source = _source;
		var herc = HercGroups.Machine((short)typeIndex, weapons, ammo);
		short mech = -1;
		if (joinGroup is { } joined) {
			if (WhyNotJoin(joined) is { } reason) {
				return reason;
			}

			return Commit(typeIndex, () => HercGroups.Join(source, joined, herc, out mech), () => mech);
		}

		return Commit(typeIndex, () => HercGroups.Create(source, herc, (short)side, x, y, out mech, out _), () => mech);
	}

	/// <summary>Why a herc cannot be added to group <paramref name="group"/>, by its row-16 GUID, or null when it can.</summary>
	public string? WhyNotJoin(short group) =>
		_index.Group(group) == null ? EditorToolbox.GroupGone : HercGroups.WhyNoJoin(_source, group);

	/// <summary>Why the last slot join run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>
	/// Why no herc can be put in member slot <paramref name="slot"/> of <paramref name="group"/>, by its row-16 GUID, or null
	/// when one can: block 7 is full (<see cref="WhyNoAdd"/>), or the slot cannot be filled (<see cref="HercGroups.WhyNoJoinSlot"/>).
	/// </summary>
	public string? WhyNotFillSlot(short group, int slot) =>
		WhyNoAdd ?? (_index.Group(group) == null ? EditorToolbox.GroupGone : HercGroups.WhyNoJoinSlot(_source, group, slot));

	/// <summary>
	/// Asks for a herc of type <paramref name="typeIndex"/>, with the fit a new herc of it gets, to be added in member slot
	/// <paramref name="slot"/> of herc group <paramref name="group"/>. Its record names no point of its own, so it stands on
	/// the slot's formation spot (docs/retail/formats/script-dat.md#placement--the-actual-rule); it is not selected, so the
	/// group's view stays. The edit runs when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.
	/// </summary>
	public void RequestJoin(short group, int slot, int typeIndex) => _requested = () => JoinSlot(typeIndex, group, slot);

	/// <summary>Makes the slot join asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	private string? JoinSlot(int typeIndex, short group, int slot) {
		if (WhyNotFillSlot(group, slot) is { } reason) {
			return reason;
		}

		if (_fitter.NewHercFit(typeIndex) is not var (weapons, ammo)) {
			return HercGroups.IsCybrid(typeIndex) ? $"There is no HERC type {typeIndex}." : $"The install has no stock fit for chassis {typeIndex}.";
		}

		var source = _source;
		var herc = HercGroups.Machine((short)typeIndex, weapons, ammo);
		short mech = -1;
		return Commit(typeIndex, () => HercGroups.JoinSlot(source, group, slot, herc, out mech), () => mech, select: false);
	}

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

	// Makes an add's change and syncs the scene with it, then records it as one step and, when <select>, selects the object
	// built for the herc's row-12 GUID; or, when none was built, puts the records back and says why.
	private string? Commit(int typeIndex, Func<MsnEdit> add, Func<short> mech, bool select = true) {
		MsnEdit change;
		try {
			change = _sync.Run(add);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		short added = mech();
		if (_index.ObjectOf(new ObjectKey(MechRow, added)) == null) {
			_sync.Undo(change);
			return $"The install has no HERC type {typeIndex}.";
		}

		_mover.Record(new RecordsEdit(_sync, change));
		if (select) {
			_selection.Current = new ObjectSelection(MechRow, added);
		}

		return null;
	}
}
