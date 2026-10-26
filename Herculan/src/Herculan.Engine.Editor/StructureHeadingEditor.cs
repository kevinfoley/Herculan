using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Editor;

/// <summary>
/// Sets or takes away the own heading of a structure or ground vehicle (<see cref="StructureHeadings.Set"/>), as one step of
/// <see cref="PointMover"/>'s undo history each; the records that name another heading under other conditions are left as they
/// are. Each edit builds the editor's state again (<see cref="SceneSync"/>), which turns the object where it stands.
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class StructureHeadingEditor {
	private const int StructureRow = 14;

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	public StructureHeadingEditor(MissionDocument source, PointMover mover, SceneSync sync) {
		_source = source;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>The mission as its <c>.MSN</c>, whose conditions the view reads the other versions' headings under.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why row-14 GUID <paramref name="guid"/>'s heading cannot be set, or null when it can.</summary>
	public string? WhyFixed(short guid) => _source.Layout.Entry(StructureRow, guid) == null ? EditorToolbox.ObjectGone(StructureRow) : null;

	/// <summary>Asks for row-14 GUID <paramref name="guid"/> to face <paramref name="degrees"/>, or with null its group's way.</summary>
	public void Request(short guid, short? degrees) => _requested = () => {
		if (WhyFixed(guid) is { } reason) {
			return reason;
		}

		if (StructureHeadings.Degrees(_source, guid) == degrees) {
			return null;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => StructureHeadings.Set(_source, guid, degrees));
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
