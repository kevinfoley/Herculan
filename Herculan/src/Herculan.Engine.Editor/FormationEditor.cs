using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// One place of a formation other than the leader's: its unrotated offset from the leader, x across and y ahead, and how far a
/// structure standing there is turned from the group's heading (<see cref="BaseFormationTable.HeadingNudgeFor"/>), 0 for a
/// herc or flyer formation.
/// </summary>
internal readonly record struct FormationPlace(int X, int Y, short Turn);

/// <summary>
/// Sets a group's formation, as one step of <see cref="PointMover"/>'s undo history: row 16's
/// <see cref="MissionGroup164.FormationId"/>, written to the record of the group's GUID that supplies it
/// (<see cref="MissionDocument.SetField"/>), as <see cref="BasePadEditor"/> writes the pad flag. A herc group picks from
/// <c>dat\MFORMS.DAT</c>, a flyer group from <c>dat\FFORMS.DAT</c> and a structure group from <c>dat\BFORMS.DAT</c>
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 7). The edit builds the editor's state again
/// (<see cref="SceneSync"/>), which stands each member without a position of its own on its new place.
///
/// <para>A structure group that lays a base pad stops laying it, in the same step, when it moves to a formation with no pad
/// layout (<see cref="BaseFormationTable.LayoutFor"/>), which <see cref="BasePadEditor"/> offers no pad for. The player's
/// squad is not offered a formation: the briefing map stands it in its first order's formation instead
/// (docs/retail/shell/mission-map.md#the-squads-positions), which this does not change.</para>
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class FormationEditor {
	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly PlacementTables _tables;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	public FormationEditor(MissionDocument source, MissionIndex index, PlacementTables tables, PointMover mover, SceneSync sync) {
		_source = source;
		_index = index;
		_tables = tables;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why row-16 GUID <paramref name="group"/>'s formation cannot be set, or null when it can.</summary>
	public string? WhyFixed(short group) =>
		_index.Group(group) is not { } planned ? EditorToolbox.GroupGone
			: planned.IsSquad ? "Not set here for the player's squad: the briefing map stands it in its first order's formation."
			: null;

	/// <summary>How many formations a group of <paramref name="kind"/> picks from.</summary>
	public int Count(MissionUnitKind kind) => kind switch {
		MissionUnitKind.Mech => _tables.Mechs.Count,
		MissionUnitKind.Flyer => _tables.Flyers.Count,
		_ => _tables.Bases.Count,
	};

	/// <summary>
	/// Every place of formation <paramref name="formation"/> for a group of <paramref name="kind"/> after the leader's, in
	/// order: place 2 first. Empty for a formation outside the table.
	/// </summary>
	public IReadOnlyList<FormationPlace> Places(MissionUnitKind kind, int formation) {
		var places = new List<FormationPlace>();
		for (int member = 1; ; member++) {
			FormationPlace? place = kind switch {
				MissionUnitKind.Mech => _tables.Mechs.OffsetFor(formation, member) is { } herc ? new(herc.X, herc.Y, 0) : null,
				MissionUnitKind.Flyer => _tables.Flyers.OffsetFor(formation, member) is { } flyer ? new(flyer.X, flyer.Y, 0) : null,
				_ => _tables.Bases.OffsetFor(formation, member) is { } structure ? new(structure.X, structure.Y, structure.HeadingNudge) : null,
			};
			if (place is not { } found) {
				return places;
			}

			places.Add(found);
		}
	}

	/// <summary>Whether formation <paramref name="formation"/> of a group of <paramref name="kind"/> has a base pad to lay.</summary>
	public bool HasPad(MissionUnitKind kind, int formation) =>
		kind == MissionUnitKind.Base && _tables.Bases.LayoutFor(formation) != null;

	/// <summary>Asks for row-16 GUID <paramref name="group"/> to stand in formation <paramref name="formation"/>.</summary>
	public void Request(short group, short formation) => _requested = () => {
		if (WhyFixed(group) is { } reason) {
			return reason;
		}

		var planned = _index.Group(group)!;
		if (planned.Setup.FormationId == formation) {
			return null;
		}

		bool dropPad = planned.Setup.PaintsGround && planned.Kind == MissionUnitKind.Base && !HasPad(planned.Kind, formation);
		MsnEdit change;
		try {
			change = _sync.Run(() => _source.Change(() => {
				_source.SetField(MsnField.GroupFormation, group, formation);
				if (dropPad) {
					_source.SetField(MsnField.GroupPaintsGround, group, 0);
				}
			}));
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		return null;
	};

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}
}
