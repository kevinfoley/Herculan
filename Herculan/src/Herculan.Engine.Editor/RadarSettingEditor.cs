using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Editor;

/// <summary>
/// Turns the radar of a herc outside the player's squad on or off, in its GUID's base (<see cref="RadarSettings.Set"/>), as one
/// step of <see cref="PointMover"/>'s undo history each; the records that set it under other conditions are left as they are.
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class RadarSettingEditor {
	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	public RadarSettingEditor(MissionDocument source, PointMover mover, SceneSync sync) {
		_source = source;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>The mission as its <c>.MSN</c>, whose conditions the view reads the other versions' settings under.</summary>
	public MissionDocument Source => _source;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why herc GUID <paramref name="guid"/>'s radar setting cannot be set, or null when it can.</summary>
	public string? WhyFixed(short guid) =>
		_source.Layout.Entry(12, guid) == null ? EditorToolbox.ObjectGone(12) : RadarSettings.WhyFixed(_source, guid);

	/// <summary>Asks for herc GUID <paramref name="guid"/>'s radar to be on or off.</summary>
	public void Request(short guid, bool on) => _requested = () => {
		if (WhyFixed(guid) is { } reason) {
			return reason;
		}

		if (RadarSettings.Current(_source, guid) == on) {
			return null;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => RadarSettings.Set(_source, guid, on));
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
