using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;

namespace Herculan.Engine.Editor;

/// <summary>
/// Adds and removes the player's squadmates, group 0's members after the player's machine, as one step of
/// <see cref="PointMover"/>'s undo history each, through <see cref="SquadRoster"/>'s record edits; the editor's state is
/// then built again (<see cref="MissionRebuild"/>), which builds the handoff's <c>player.mec</c> again first
/// (docs/herculan/handoff-editor-mission-editing.md#in-place, "The player's squad").
///
/// <para>A new squadmate is the player's machine's chassis as the mission holds it now, with that chassis's stock fit
/// (<see cref="MachineFitter.StockFit"/>), as a Herc Type choice gives it. An add is refused past
/// <see cref="SquadRoster.MaxSquadmates"/> and past DBSIM's block-7 slots (<see cref="ScriptDatLint.MechSlotCap"/>),
/// which the squad's machines share with the roster, counted as the mission check counts them, the add taking one more of
/// each.</para>
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered, since the rebuild
/// changes what the panels list.</para>
/// </summary>
internal sealed class SquadEditor {
	private readonly ScriptDat _document;
	private readonly MissionDocument? _source;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private readonly MachineFitter _fitter;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;

	public SquadEditor(ScriptDat document, MissionDocument? source, MissionIndex index, PointMover mover,
			MissionRebuild rebuild, MachineFitter fitter, ObjectDeleter deleter) {
		_document = document;
		_source = source;
		_index = index;
		_mover = mover;
		_rebuild = rebuild;
		_fitter = fitter;
		_deleter = deleter;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Group 0's member slots holding the squadmates, in squad order: slot 1 up to the first unset one.</summary>
	public IEnumerable<int> Squadmates => Enumerable.Range(1, SquadRoster.Positions(_document) - 1);

	/// <summary>Why a squadmate cannot be added, or null when one can.</summary>
	public string? WhyNoAdd {
		get {
			if (_mover.WhyReadOnly is { } readOnly) {
				return readOnly;
			}

			if (SquadRoster.WhyNoAdd(_source!) is { } reason) {
				return reason;
			}

			int squad = _index.Mission.Placements.Count(placement => placement.IsPlayerLance);
			if (_document.Mechs.Length + squad + 2 > ScriptDatLint.MechSlotCap) {
				return $"The mission already has {_document.Mechs.Length} hercs plus a squad of {squad}, and DBSIM holds "
					+ $"{ScriptDatLint.MechSlotCap} in all.";
			}

			return _fitter.SquadSlot(0) is { } player && _fitter.Record(player) is { } record && _fitter.StockFit(record.TypeIndex) == null
				? $"The install has no stock fit for chassis {record.TypeIndex}."
				: null;
		}
	}

	/// <summary>
	/// Why the squadmate in group 0's member slot <paramref name="member"/> cannot be removed, or null when it can, with
	/// what names or needs it.
	/// </summary>
	public (string? Fixed, DeleteNamers Names, bool OwnActions) WhyNoRemove(int member) {
		if (_mover.WhyReadOnly is { } readOnly) {
			return (readOnly, DeleteNamers.None, false);
		}

		if (SquadRoster.Check(_source!, member) is not var (_, check)) {
			return ("Its record is not one of its own: group 0 names it again, or it is all the group holds.", DeleteNamers.None, false);
		}

		var names = _deleter.Resolve(_source!, check.Namers);
		return (null, names, check.FiredActions.Count > 0);
	}

	/// <summary>Asks for a squadmate to be added after the last.</summary>
	public void RequestAdd() => _requested = Add;

	/// <summary>Asks for the squadmate in group 0's member slot <paramref name="member"/> to be removed.</summary>
	public void RequestRemove(int member) => _requested = () => Remove(member);

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	private string? Add() {
		if (WhyNoAdd is { } reason) {
			return reason;
		}

		var player = _fitter.Record(_fitter.SquadSlot(0) ?? -1);
		if (player == null || _fitter.StockFit(player.TypeIndex) is not var (weapons, guidance)) {
			return "Group 0 names no player's machine to take the chassis of.";
		}

		var source = _source!;
		return Commit(() => SquadRoster.Add(source, SquadRoster.Machine(player.TypeIndex, weapons, guidance), out _));
	}

	private string? Remove(int member) {
		var (reason, names, ownActions) = WhyNoRemove(member);
		if (reason != null) {
			return reason;
		}

		if (names.Any || ownActions) {
			return "The mission names or needs it; its view says what.";
		}

		var source = _source!;
		return Commit(() => SquadRoster.Remove(source, member));
	}

	// Makes the change, rebuilding from it, and records it as one step of the history.
	private string? Commit(Func<MsnEdit> edit) {
		MsnEdit change;
		try {
			change = _rebuild.Run(edit);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_rebuild, change));
		return null;
	}
}
