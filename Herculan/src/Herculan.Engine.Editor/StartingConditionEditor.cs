using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Editor;

/// <summary>
/// Sets the starting condition of a herc outside the player's squad or of a structure or ground vehicle, in its GUID's base
/// (<see cref="StartingConditions.Set"/>), as one step of <see cref="PointMover"/>'s undo history each; the records that set
/// it under other conditions are left as they are. Each edit builds the editor's state again (<see cref="SceneSync"/>), which
/// builds a new object for the record, as a structure placed collapsed is built as its wreck.
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class StartingConditionEditor {
	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	public StartingConditionEditor(MissionDocument source, PointMover mover, SceneSync sync) {
		_source = source;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>The mission as its <c>.MSN</c>, whose conditions the view reads the other versions' values under.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why the roster record <paramref name="key"/>'s starting condition cannot be set, or null when it can.</summary>
	public string? WhyFixed(ObjectKey key) =>
		_source.Layout.Entry(key.Row, key.Guid) == null ? EditorToolbox.ObjectGone(key.Row) : StartingConditions.WhyFixed(_source, key.Row, key.Guid);

	/// <summary>Asks for the roster record <paramref name="key"/>'s starting condition to be <paramref name="value"/> per cent.</summary>
	public void Request(ObjectKey key, short value) => _requested = () => {
		if (WhyFixed(key) is { } reason) {
			return reason;
		}

		if (StartingConditions.Current(_source, key.Row, key.Guid) == value) {
			return null;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => StartingConditions.Set(_source, key.Row, key.Guid, value));
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
