using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Changes which side a group fights on, as one step of <see cref="PointMover"/>'s undo history. The side is the group's
/// and not an object's: block 11's <c>0x6e</c> (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps), which every member
/// spawns with, so changing it for one object changes it for every member of its group.
///
/// <para>The document's field, the index's copy (<see cref="MissionIndex.SideOf"/>) and each member's simulation object
/// change together; a member's <see cref="MissionPlacement.Side"/> stays as loaded. The player's squad, group 0, is left
/// alone: the editor's choice, as a squad that changed sides would leave the player fighting their own.</para>
/// </summary>
internal sealed class SideChanger {
	private readonly ScriptDat _document;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;

	public SideChanger(ScriptDat document, MissionIndex index, PointMover mover) {
		_document = document;
		_index = index;
		_mover = mover;
	}

	/// <summary>Why <paramref name="group"/>'s side cannot be changed, or null when it can.</summary>
	public string? WhyFixed(int group) =>
		group == 0 ? "The player's squad is group 0, whose side the editor leaves alone."
			: group < 0 || group >= _document.Groups.Length ? $"Group {group} is not in the file."
			: null;

	/// <summary>Puts <paramref name="group"/> on <paramref name="side"/>, unless it is there already or cannot change.</summary>
	public void Set(int group, MissionSide side) {
		if (WhyFixed(group) != null || _index.SideOf(group) == side) {
			return;
		}

		var edit = new SideEdit(this, group, _document.Groups[group].Side, (short)side);
		edit.Redo();
		_mover.Record(edit);
	}

	// Writes a side as the file holds it, and reads it as the loader does: 1 is Cybrid, anything else human.
	private void Apply(int group, short raw) {
		_document.Groups[group].Side = raw;
		var side = raw == (short)MissionSide.Cybrid ? MissionSide.Cybrid : MissionSide.Human;
		_index.SetSide(group, side);
		foreach (var member in _index.GroupMembers[group]) {
			member.Object.Side = side;
		}
	}

	// The field as it was, kept raw so an undo puts back an unset side as unset.
	private sealed record SideEdit(SideChanger Changer, int Group, short Before, short After) : IEditorEdit {
		public void Undo() => Changer.Apply(Group, Before);

		public void Redo() => Changer.Apply(Group, After);
	}
}
