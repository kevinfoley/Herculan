using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// The open mission's about file (<see cref="MissionAbout"/>), which Save writes beside its <c>.MSN</c> and Export Mission
/// Bundle puts in the bundle. It is no part of the <c>.MSN</c>, but a change to it is a step of <see cref="PointMover"/>'s
/// undo history all the same, so that it marks the mission unsaved and undoes as any other edit does.
///
/// <para>The edit runs when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class MissionAboutEditor {
	private readonly PointMover _mover;
	private MissionAbout? _requested;

	public MissionAboutEditor(PointMover mover) {
		_mover = mover;
	}

	/// <summary>What the about file says now.</summary>
	public MissionAbout About { get; private set; } = MissionAbout.Blank;

	/// <summary>Takes <paramref name="about"/> as what the mission was opened with, as no step of the undo history.</summary>
	public void Load(MissionAbout about) => About = about;

	/// <summary>Asks for the about file to say <paramref name="about"/>.</summary>
	public void Request(MissionAbout about) => _requested = about;

	/// <summary>Makes the edit asked for since the last call, if any and if it changes anything.</summary>
	public void RunRequest() {
		if (_requested is not { } about) {
			return;
		}

		_requested = null;
		Set(about);
	}

	/// <summary>Makes the about file say <paramref name="about"/> at once, as one step of the undo history, unless it already does.</summary>
	public void Set(MissionAbout about) {
		if (about == About) {
			return;
		}

		var edit = new AboutEdit(this, About, about);
		About = about;
		_mover.Record(edit);
	}

	private sealed class AboutEdit(MissionAboutEditor editor, MissionAbout before, MissionAbout after) : IEditorEdit {
		public void Undo() => editor.About = before;

		public void Redo() => editor.About = after;
	}
}
