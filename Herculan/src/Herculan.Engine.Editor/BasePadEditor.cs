using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Turns a structure group's base pad on or off, as one step of <see cref="PointMover"/>'s undo history: row 16's
/// <see cref="MissionGroup164.PaintsGround"/>, 1 or 0, written to the record of the group's GUID that supplies it
/// (<see cref="MissionDocument.SetField"/>), as <see cref="SideChanger"/> writes a group's side; 0 is the field's unset value,
/// so turning it off clears it in every record of the GUID. The edit builds the editor's state again (<see cref="SceneSync"/>),
/// which snaps the group's point into its tile, paints and levels the pad, or takes them away
/// (docs/retail/formats/script-dat.md#base-formation-terrain). Only a structure group outside the player's squad whose
/// formation has a pad layout (<see cref="BaseFormationTable.LayoutFor"/>) is offered it: a flag on any other changes nothing.
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class BasePadEditor {
	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly BaseFormationTable _formations;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private Func<string?>? _requested;

	public BasePadEditor(MissionDocument source, MissionIndex index, BaseFormationTable formations, PointMover mover, SceneSync sync) {
		_source = source;
		_index = index;
		_formations = formations;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Why row-16 GUID <paramref name="group"/>'s base pad cannot be turned on or off, or null when it can.</summary>
	public string? WhyFixed(short group) =>
		_index.Group(group) is not { } planned ? EditorToolbox.GroupGone
			: planned.IsSquad || planned.Kind != MissionUnitKind.Base ? "Only a group of structures lays a base pad."
			: _formations.LayoutFor(planned.Setup.FormationId) == null ? "Its formation has no pad to lay."
			: null;

	/// <summary>Asks for row-16 GUID <paramref name="group"/> to lay its base pad, or with false not to.</summary>
	public void Request(short group, bool lays) => _requested = () => {
		if (WhyFixed(group) is { } reason) {
			return reason;
		}

		if (_index.Group(group)?.Setup.PaintsGround == lays) {
			return null;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => _source.SetField(MsnField.GroupPaintsGround, group, (short)(lays ? 1 : 0)));
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
