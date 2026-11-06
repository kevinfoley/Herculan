namespace Herculan.Engine.Editor;

/// <summary>
/// One step of the undo history (<see cref="EditHistory"/>): a move, an add, a delete, a side change, any edit
/// (<see cref="RecordsEdit"/>), or a theater change. Each puts the <c>.MSN</c> back as the step found or left it
/// (<see cref="MissionDocument.Undo"/>) and syncs the scene with it (<see cref="SceneSync"/>); the history then levels the
/// ground.
/// </summary>
internal interface IEditorEdit {
	void Undo();

	void Redo();
}

/// <summary>
/// The open mission's undo history: every edit made anywhere in the editor, in the order it was made, which Undo and Redo
/// step through, and whether the document has changed since it was last saved or opened (<see cref="Dirty"/>). An undo
/// back to the step on top when it was saved counts as unchanged. After every step recorded, undone or redone, the ground
/// under the structures is levelled where they now stand (<see cref="TerrainLeveller.Relevel"/>).
///
/// <para>The saved state is held as the undo list's depth and its top step. Equal depth and the same top step mean the
/// same list: steps leave the undo list only from the top, and a step comes back onto it only by a redo, which a new
/// step's clearing of the redo list rules out once anything under it has changed.</para>
/// </summary>
internal sealed class EditHistory {
	private readonly List<IEditorEdit> _undo = new();
	private readonly List<IEditorEdit> _redo = new();
	private readonly Action _stepped;
	private int _savedDepth;
	private IEditorEdit? _savedTop;

	/// <param name="stepped">Run after every step recorded, undone or redone: the levelling of the ground.</param>
	public EditHistory(Action stepped) {
		_stepped = stepped;
	}

	public bool CanUndo => _undo.Count > 0;

	public bool CanRedo => _redo.Count > 0;

	/// <summary>Whether the steps applied differ from those applied when <see cref="MarkSaved"/> was last called, or since the start.</summary>
	public bool Dirty => _undo.Count != _savedDepth || !ReferenceEquals(Top, _savedTop);

	private IEditorEdit? Top => _undo.Count > 0 ? _undo[^1] : null;

	/// <summary>Adds an edit already applied as one step, which ends whatever could be redone.</summary>
	public void Record(IEditorEdit edit) {
		_undo.Add(edit);
		_redo.Clear();
		_stepped();
	}

	/// <summary>Undoes the step on top of the undo list, when there is one, and moves it onto the redo list.</summary>
	public void Undo() {
		if (Move(_undo, _redo) is { } edit) {
			edit.Undo();
			_stepped();
		}
	}

	/// <summary>Redoes the step on top of the redo list, when there is one, and moves it back onto the undo list.</summary>
	public void Redo() {
		if (Move(_redo, _undo) is { } edit) {
			edit.Redo();
			_stepped();
		}
	}

	/// <summary>Takes the steps applied now as the document's saved state.</summary>
	public void MarkSaved() {
		_savedDepth = _undo.Count;
		_savedTop = Top;
	}

	/// <summary>Takes the document as never saved, a new mission's state: changed whatever is undone, until <see cref="MarkSaved"/>.</summary>
	public void MarkNeverSaved() => _savedDepth = -1;

	/// <summary>Whether the document has not been saved since <see cref="MarkNeverSaved"/>, so no file holds it.</summary>
	public bool NeverSaved => _savedDepth == -1;

	private static IEditorEdit? Move(List<IEditorEdit> from, List<IEditorEdit> to) {
		if (from.Count == 0) {
			return null;
		}

		var edit = from[^1];
		from.RemoveAt(from.Count - 1);
		to.Add(edit);
		return edit;
	}
}
