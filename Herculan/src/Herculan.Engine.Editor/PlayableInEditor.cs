using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Mission Settings window's "Playable in" choice: whether the mission is meant for HERCULAN alone
/// (<see cref="MissionDocument.SetHerculanOnly"/>), which frees it of the limits retail alone has, as one step of
/// the <see cref="EditHistory">undo history</see>.
///
/// <para>The edit runs when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class PlayableInEditor {
	private readonly MissionDocument _source;
	private readonly EditHistory _history;
	private readonly SceneSync _sync;
	private bool? _requested;

	public PlayableInEditor(MissionDocument source, EditHistory history, SceneSync sync) {
		_source = source;
		_history = history;
		_sync = sync;
	}

	/// <inheritdoc cref="MissionLayout.HerculanOnly"/>
	public bool HerculanOnly => _source.Layout.HerculanOnly;

	/// <inheritdoc cref="MissionDocument.WhyNoHerculanOnly"/>
	public string? WhyFixed => _source.WhyNoHerculanOnly;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Asks for the mission to be meant for HERCULAN alone, or for retail too.</summary>
	public void Request(bool herculanOnly) => _requested = herculanOnly;

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is not { } herculanOnly) {
			return;
		}

		_requested = null;
		Failure = null;
		if (herculanOnly == HerculanOnly) {
			return;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => _source.SetHerculanOnly(herculanOnly));
		} catch (InvalidOperationException ex) {
			Failure = ex.Message;
			return;
		}

		_history.Record(new RecordsEdit(_sync, change));
	}
}
