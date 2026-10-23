using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;

namespace Herculan.Engine.Editor;

/// <summary>
/// The player's squad as the layout holds it, by GUID (<see cref="SquadRoster"/>), and its squadmates added and removed, as
/// one step of <see cref="PointMover"/>'s undo history each, through <see cref="SquadRoster"/>'s record edits; the editor's
/// state is then built again (<see cref="SceneSync"/>) (docs/herculan/handoff-editor-roster.md#the-players-squad).
///
/// <para>A new squadmate is the player's HERC's chassis as the mission holds it now, with that chassis's stock fit
/// (<see cref="MachineFitter.StockFit"/>), as a Herc Type choice gives it. An add is refused past
/// <see cref="SquadRoster.MaxSquadmates"/> and past DBSIM's block-7 slots (<see cref="ScriptDatLint.MechSlotCap"/>),
/// which the squad's machines share with the roster, counted over every version of the mission
/// (<see cref="HercGroups.WhyNoRoom"/>), the add taking one more of each: a row-12 GUID and a squad position.</para>
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered, since the rebuild
/// changes what the panels list.</para>
/// </summary>
internal sealed class SquadEditor {
	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly MachineFitter _fitter;
	private readonly ObjectDeleter _deleter;
	private Func<string?>? _requested;

	public SquadEditor(MissionDocument source, PointMover mover, SceneSync sync, MachineFitter fitter, ObjectDeleter deleter) {
		_source = source;
		_mover = mover;
		_sync = sync;
		_fitter = fitter;
		_deleter = deleter;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>The row-12 GUID of the player's HERC, group 0's first member, or null when it names none.</summary>
	public short? Player => SquadRoster.Member(_source.Layout, 0) is var guid and not -1 ? guid : null;

	/// <summary>
	/// The squadmates' row-12 GUIDs, in squad order: group 0's member slots from 1 up to the first unset one. Squadmate
	/// <c>k</c>, from 1, is the <c>k</c>th.
	/// </summary>
	public IReadOnlyList<short> Squadmates {
		get {
			var layout = _source.Layout;
			return Enumerable.Range(1, SquadRoster.Positions(layout) - 1).Select(slot => SquadRoster.Member(layout, slot)).ToList();
		}
	}

	/// <summary>Why a squadmate cannot be added, or null when one can.</summary>
	public string? WhyNoAdd {
		get {
			if (SquadRoster.WhyNoAdd(_source) is { } reason) {
				return reason;
			}

			if (HercGroups.WhyNoRoom(_source.Layout, 2) is { } full) {
				return full;
			}

			return Player is { } player && _fitter.Record(player) is { } record && _fitter.StockFit(record.TypeIndex) == null
				? $"The install has no stock fit for chassis {record.TypeIndex}."
				: null;
		}
	}

	/// <summary>
	/// Why squadmate <paramref name="number"/>, from 1, cannot be removed, or null when it can, with what references or
	/// needs it.
	/// </summary>
	public (string? Fixed, DeleteNamers Names, bool OwnActions) WhyNoRemove(int number) {
		if (SquadRoster.Check(_source, number) is not var (_, check)) {
			return ("Its record is also another squad member's, or it is the only HERC the squad has.", DeleteNamers.None, false);
		}

		var names = _deleter.Resolve(_source, check.Namers);
		return (null, names, check.FiredActions.Count > 0);
	}

	/// <summary>Asks for a squadmate to be added after the last.</summary>
	public void RequestAdd() => _requested = Add;

	/// <summary>Asks for squadmate <paramref name="number"/>, from 1, to be removed.</summary>
	public void RequestRemove(int number) => _requested = () => Remove(number);

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

		if (Player is not { } player || _fitter.Record(player) is not { } herc || _fitter.StockFit(herc.TypeIndex) is not var (weapons, guidance)) {
			return "The player's squad has no HERC for the player, whose chassis a new squadmate takes.";
		}

		var source = _source;
		return Commit(() => SquadRoster.Add(source, SquadRoster.Machine(herc.TypeIndex, weapons, guidance), out _));
	}

	private string? Remove(int number) {
		var (reason, names, ownActions) = WhyNoRemove(number);
		if (reason != null) {
			return reason;
		}

		if (names.Any || ownActions) {
			return "The mission references or needs it; its view says what.";
		}

		var source = _source;
		return Commit(() => SquadRoster.Remove(source, number));
	}

	// Makes the change, rebuilding from it, and records it as one step of the history.
	private string? Commit(Func<MsnEdit> edit) {
		MsnEdit change;
		try {
			change = _sync.Run(edit);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		return null;
	}
}
