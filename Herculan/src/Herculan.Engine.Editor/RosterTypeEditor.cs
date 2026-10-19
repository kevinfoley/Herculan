using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Editor;

/// <summary>
/// Changes the type of a flyer, a structure or a ground vehicle outside the player's squad, a row-13 or row-14 record of the
/// <c>.MSN</c>, as one step of <see cref="PointMover"/>'s undo history each, through <see cref="RosterFit.Write"/>: in the
/// variant alternative the record draws its type from when asked and the table is one the editor edits, so every record
/// drawing it changes, else in the record that supplies it (docs/herculan/handoff-editor-roster.md#type-and-fit). A herc's chassis is <see cref="MachineFitter"/>'s. Each edit builds the editor's state again
/// (<see cref="MissionRebuild"/>), which builds a new object for the record, as its type differs.
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered. A plain
/// <c>script.dat</c> edits nothing.</para>
/// </summary>
internal sealed class RosterTypeEditor {
	private readonly MissionDocument? _source;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private Func<string?>? _requested;

	public RosterTypeEditor(MissionDocument? source, PointMover mover, MissionRebuild rebuild) {
		_source = source;
		_mover = mover;
		_rebuild = rebuild;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why roster row <paramref name="row"/>'s GUID <paramref name="guid"/>'s type cannot be changed, or null when it can.</summary>
	public string? WhyFixed(int row, short guid) =>
		_mover.WhyReadOnly ?? (_source!.ExportedIndex(row, guid) == null ? "It is not in the mission as shown." : null);

	/// <summary>
	/// The variant alternative a type change of roster row <paramref name="row"/>'s GUID <paramref name="guid"/> asked to write
	/// the alternative changes (<see cref="RosterFit.DrawnAlternative"/>); null when its type is its own, or it cannot change.
	/// </summary>
	public MsnRecordId? DrawnAlternative(int row, short guid) =>
		WhyFixed(row, guid) == null ? RosterFit.DrawnAlternative(_source!, guid, [MsnField.RosterType(row)]) : null;

	/// <summary>
	/// Asks for roster row <paramref name="row"/>'s GUID <paramref name="guid"/> to become type <paramref name="type"/>, written
	/// to the alternative it draws its type from when <paramref name="alternative"/>.
	/// </summary>
	public void RequestType(int row, short guid, int type, bool alternative) => _requested = () => {
		if (WhyFixed(row, guid) is { } reason) {
			return reason;
		}

		var source = _source!;
		var fields = new[] { (MsnField.RosterType(row), (short)type) };
		if (!RosterFit.Differs(source, guid, fields)) {
			return null;
		}

		MsnEdit change;
		try {
			change = _rebuild.Run(() => RosterFit.Write(source, guid, fields, alternative, _ => false));
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_rebuild, change));
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
