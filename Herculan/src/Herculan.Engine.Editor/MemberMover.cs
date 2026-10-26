using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Editor;

/// <summary>
/// Moves a herc, flyer, structure or ground vehicle outside the player's squad into another group, as one step of
/// <see cref="PointMover"/>'s undo history, through <see cref="MemberMoves"/>'s record edits on the mission's <c>.MSN</c>
/// (docs/herculan/handoff-editor-roster.md#moving-to-another-group). A structure that needs a point of its own to stay put is
/// given one where its object stands. The edit builds the editor's state again (<see cref="SceneSync"/>), as it changes
/// which group places the object, and waits for the frame's ImGui to be rendered (<see cref="RunRequest"/>).
/// </summary>
internal sealed class MemberMover {
	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	public MemberMover(MissionDocument source, MissionIndex index, PointMover mover, SceneSync sync) {
		_source = source;
		_index = index;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>Why the last move run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why <paramref name="placed"/> cannot move into row-16 group <paramref name="group"/>, or null when it can.</summary>
	public string? WhyNoMove(SceneObject placed, short group) =>
		_index.KeyOf(placed) is not { } key ? "Not found."
			: MemberMoves.WhyNoMove(_source, key.Row, key.Guid, group, BaseObject.IsGroundVehicle);

	/// <summary>Asks for <paramref name="placed"/> to move into row-16 group <paramref name="group"/>.</summary>
	public void Request(SceneObject placed, short group) => _requested = () => {
		if (_index.KeyOf(placed) is not { } key) {
			return "Not found.";
		}

		var at = placed.Object.Position;
		MsnEdit change;
		try {
			change = _sync.Run(() => MemberMoves.Move(_source, key.Row, key.Guid, group, BaseObject.IsGroundVehicle, (at.X, at.Y, at.Z)));
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_mover.Record(new RecordsEdit(_sync, change));
		return null;
	};

	/// <summary>Makes the move asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}
}
