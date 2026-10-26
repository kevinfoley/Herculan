namespace Herculan.Engine.Editor;

/// <summary>
/// The editor's undo and redo lists, and whether the document has changed since it was last saved or opened
/// (<see cref="Dirty"/>). An undo back to the step on top when it was saved counts as unchanged.
///
/// <para>The saved state is held as the undo list's depth and its top step. Equal depth and the same top step mean the
/// same list: steps leave the undo list only from the top, and a step comes back onto it only by a redo, which a new
/// step's clearing of the redo list rules out once anything under it has changed.</para>
/// </summary>
internal sealed class EditHistory {
	private readonly List<IEditorEdit> _undo = new();
	private readonly List<IEditorEdit> _redo = new();
	private int _savedDepth;
	private IEditorEdit? _savedTop;

	public bool CanUndo => _undo.Count > 0;

	public bool CanRedo => _redo.Count > 0;

	/// <summary>Whether the steps applied differ from those applied when <see cref="MarkSaved"/> was last called, or since the start.</summary>
	public bool Dirty => _undo.Count != _savedDepth || !ReferenceEquals(Top, _savedTop);

	private IEditorEdit? Top => _undo.Count > 0 ? _undo[^1] : null;

	/// <summary>Adds a step already applied, which ends whatever could be redone.</summary>
	public void Add(IEditorEdit edit) {
		_undo.Add(edit);
		_redo.Clear();
	}

	/// <summary>The step to undo, moved onto the redo list, or null when there is none.</summary>
	public IEditorEdit? TakeUndo() => Move(_undo, _redo);

	/// <summary>The step to redo, moved back onto the undo list, or null when there is none.</summary>
	public IEditorEdit? TakeRedo() => Move(_redo, _undo);

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
