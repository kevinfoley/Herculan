using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// Edit &gt; "Remove unused positions": removes every position nothing uses (<see cref="UnusedPoints"/>), as one step of
/// <see cref="PointMover"/>'s undo history, which brings the Positions count of the viewport's readout down
/// (<see cref="RosterCountsOverlay"/>).
///
/// <para>The edit runs when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class UnusedPointRemover {
	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private bool _requested;

	public UnusedPointRemover(MissionDocument source, PointMover mover, SceneSync sync) {
		_source = source;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>How many positions the edit would remove now.</summary>
	public int Count => UnusedPoints.Find(_source).Count;

	/// <summary>Asks for the unused positions to be removed.</summary>
	public void Request() => _requested = true;

	/// <summary>Removes the unused positions, if asked to since the last call and there are any.</summary>
	public void RunRequest() {
		if (!_requested) {
			return;
		}

		_requested = false;
		if (Count == 0) {
			return;
		}

		var source = _source;
		_mover.Record(new RecordsEdit(_sync, _sync.Run(() => UnusedPoints.Remove(source))));
	}
}
