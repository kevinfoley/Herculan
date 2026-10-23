using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// Changes which side a group fights on, as one step of <see cref="PointMover"/>'s undo history. The side is the group's
/// and not an object's: block 11's <c>0x6e</c> (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps), which every member
/// spawns with, so changing it for one object changes it for every member of its group.
///
/// <para>Groups are taken by their row-16 GUID, any group the layout has, a conditional one included. The change writes row
/// 16's <see cref="MissionGroup164.Side"/> in the record of the group's GUID that supplies it
/// (<see cref="MissionDocument.SetField"/>, docs/herculan/handoff-editor-document.md#the-tools), which generates <c>script.dat</c> again,
/// and syncs the scene (<see cref="SceneSync"/>), which sets each member's simulation object and placement from the layout.
/// The player's squad, the layout's first group, is left alone: the editor's choice, as a squad that changed sides
/// would leave the player fighting their own.</para>
/// </summary>
internal sealed class SideChanger {
	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;

	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	public SideChanger(MissionDocument source, MissionIndex index, PointMover mover, SceneSync sync) {
		_source = source;
		_index = index;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>Why row-16 GUID <paramref name="group"/>'s side cannot be changed, or null when it can.</summary>
	public string? WhyFixed(short group) =>
		_index.Group(group) is not { } planned ? "Its record is not in the file."
			: planned.Number == 0 ? "The editor leaves the player's squad's team alone."
			: null;

	/// <summary>Puts <paramref name="group"/> on <paramref name="side"/>, unless it is there already or cannot change.</summary>
	public void Set(short group, MissionSide side) {
		if (WhyFixed(group) != null || _index.Group(group)?.Side == side) {
			return;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => _source.SetField(MsnField.GroupSide, group, (short)side));
		} catch (InvalidOperationException ex) {
			Console.Error.WriteLine($"Cannot change group {group}'s side: {ex.Message}");
			return;
		}

		_mover.Record(new RecordsEdit(_sync, change));
	}
}
