using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Es2.Editor;

/// <summary>
/// Changes the type of a flyer, a structure or a ground vehicle outside the player's squad, a row-13 or row-14 record of the
/// <c>.MSN</c>, as one step of the <see cref="EditHistory">undo history</see> each, through <see cref="RosterFit.Write"/>: in the
/// template of the option being edited when the record picks its type from a table the editor edits
/// (<see cref="LoadoutView"/>), so every record picking that option changes, else in the record that supplies it (docs/herculan/editor-design.md#new-units). A herc's chassis is <see cref="MachineFitter"/>'s. Each edit builds the editor's state again
/// (<see cref="SceneSync"/>), which builds a new object for the record, as its type differs.
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class RosterTypeEditor {
	private readonly MissionDocument _source;
	private readonly EditHistory _history;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	public RosterTypeEditor(MissionDocument source, EditHistory history, SceneSync sync) {
		_source = source;
		_history = history;
		_sync = sync;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>
	/// Why a roster row's GUID's type cannot be changed, or null when it can: any
	/// record the layout has, one the mission as opened leaves out included.
	/// </summary>
	/// <param name="row">The roster row.</param>
	/// <param name="guid">The GUID.</param>
	public string? WhyFixed(int row, short guid) =>
		_source.Layout.Entry(row, guid) == null ? EditorToolbox.ObjectGone(row) : null;

	/// <summary>
	/// The template of the option the layout shows a roster row's GUID with that
	/// its type is copied from (<see cref="RosterFit.PickedTemplate"/>); null when its type is its own, or it cannot change.
	/// </summary>
	/// <param name="row">The roster row.</param>
	/// <param name="guid">The GUID.</param>
	public MsnRecordId? PickedTemplate(int row, short guid) =>
		WhyFixed(row, guid) == null ? RosterFit.PickedTemplate(_source, guid, [MsnField.RosterType(row)]) : null;

	/// <summary>
	/// A roster row's GUID's type, as a load picking an option, a
	/// template of its table, makes it, or as the layout shows it for no option (<see cref="RosterFit.Current"/>).
	/// </summary>
	/// <param name="row">The roster row.</param>
	/// <param name="guid">The GUID.</param>
	/// <param name="option">The option, or null for none.</param>
	public int Type(int row, short guid, MsnRecordId? option) => RosterFit.Current(_source, MsnField.RosterType(row), guid, option);

	/// <summary>
	/// Asks for a roster row's GUID to become a type, written
	/// to the template of the option being edited when a template supplies its type.
	/// </summary>
	/// <param name="row">The roster row.</param>
	/// <param name="guid">The GUID.</param>
	/// <param name="type">The type.</param>
	/// <param name="option">The option being edited, or null.</param>
	public void RequestType(int row, short guid, int type, MsnRecordId? option = null) => _requested = () => {
		if (WhyFixed(row, guid) is { } reason) {
			return reason;
		}

		var source = _source;
		var fields = new[] { (MsnField.RosterType(row), (short)type) };
		if (!RosterFit.Differs(source, guid, fields, option)) {
			return null;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => RosterFit.Write(source, guid, fields, option, _ => false));
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		_history.Record(new RecordsEdit(_sync, change));
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
