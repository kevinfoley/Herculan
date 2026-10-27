using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Editor;

/// <summary>
/// Sets or takes away the own heading of a structure, a ground vehicle or a group (<see cref="OwnHeadings.Set"/>), as one step
/// of <see cref="PointMover"/>'s undo history each; the records that name another heading under other conditions are left as
/// they are. Each edit builds the editor's state again (<see cref="SceneSync"/>), which turns the object where it stands, or
/// a group's formation and every member that faces with it.
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class HeadingEditor {
	private const int GroupRow = 16;

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	public HeadingEditor(MissionDocument source, PointMover mover, SceneSync sync) {
		_source = source;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>The mission as its <c>.MSN</c>, whose conditions the views read the other versions' headings under.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why row <paramref name="row"/>'s GUID <paramref name="guid"/>'s heading cannot be set, or null when it can.</summary>
	public string? WhyFixed(int row, short guid) =>
		_source.Layout.Entry(row, guid) != null ? null
			: row == GroupRow ? "That group is not in the mission." : EditorToolbox.ObjectGone(row);

	/// <summary>Why row <paramref name="row"/>'s GUID <paramref name="guid"/>'s heading cannot be taken away, or null when it can.</summary>
	public string? WhyNoNone(int row, short guid) => WhyFixed(row, guid) ?? OwnHeadings.WhyNoNone(_source, row, guid);

	/// <summary>
	/// Asks for row <paramref name="row"/>'s GUID <paramref name="guid"/> to face <paramref name="degrees"/>, or with null to
	/// have no heading of its own.
	/// </summary>
	public void Request(int row, short guid, short? degrees) => _requested = () => {
		if (WhyFixed(row, guid) is { } reason) {
			return reason;
		}

		if (OwnHeadings.Degrees(_source, row, guid) == degrees) {
			return null;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => OwnHeadings.Set(_source, row, guid, degrees));
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
