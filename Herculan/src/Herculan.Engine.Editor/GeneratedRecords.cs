using HercWorks.Core.Data.File.Msn;

namespace Herculan.Engine.Editor;

/// <summary>
/// Where the record a selection names stands in the generated <c>script.dat</c> the Properties views still read, and
/// back: a group, trigger area, action, timer or route by its GUID of row 16, 9, 10, 11 or 8, at its block-11, 4, 5, 6
/// or 3 index, and an objective's row-17 index from its block-12 index through provenance. Null where the generation
/// has no such record, one whose condition fails as the editor opened the mission, whose view then says it is not shown
/// in the editor (docs/herculan/handoff-editor-viewing.md#selection-and-panels).
///
/// <para>The selections a view or link makes from a block index are built here too (<see cref="GroupAt"/> and the
/// rest), from the GUID of the record that created the generated record (<see cref="MissionDocument.GuidOf"/>).</para>
/// </summary>
internal sealed class GeneratedRecords {
	private const int RouteBlock = 3;
	private const int AreaBlock = 4;
	private const int ActionBlock = 5;
	private const int TimerBlock = 6;
	private const int GroupBlock = 11;

	/// <summary>Why an editor refuses an edit of a record the generation has none for, which no view shows.</summary>
	public const string NotShown = "It is not shown in the editor.";

	/// <summary>
	/// What a view says of an object or a group the scene shows and the generation has none for, which the views, reading
	/// the generated file, cannot show; and why its editors refuse it.
	/// </summary>
	public const string Absent = "Absent from the mission as opened, so this view cannot show it yet.";

	private readonly MissionDocument _source;

	public GeneratedRecords(MissionDocument source) => _source = source;

	public int? Group(short guid) => _source.ExportedIndex(GroupBlock + 5, guid);

	public int? Area(short guid) => _source.ExportedIndex(AreaBlock + 5, guid);

	public int? Action(short guid) => _source.ExportedIndex(ActionBlock + 5, guid);

	public int? Timer(short guid) => _source.ExportedIndex(TimerBlock + 5, guid);

	public int? Route(short guid) => _source.ExportedIndex(RouteBlock + 5, guid);

	/// <summary>The GUID of the record that created block-11 group <paramref name="group"/>, or <c>-1</c>.</summary>
	public short GroupGuid(int group) => _source.GuidOf(GroupBlock, group);

	public short AreaGuid(int area) => _source.GuidOf(AreaBlock, area);

	public short ActionGuid(int action) => _source.GuidOf(ActionBlock, action);

	public short TimerGuid(int timer) => _source.GuidOf(TimerBlock, timer);

	public short RouteGuid(int route) => _source.GuidOf(RouteBlock, route);

	public GroupSelection GroupAt(int group) => new(GroupGuid(group));

	public AreaSelection AreaAt(int area) => new(AreaGuid(area));

	public ActionSelection ActionAt(int action) => new(ActionGuid(action));

	public TimerSelection TimerAt(int timer) => new(TimerGuid(timer));

	public RouteSelection RouteAt(int route, int waypoint = -1) => new(RouteGuid(route), waypoint);

	/// <summary>Block-12 objective <paramref name="objective"/>'s row-17 record, or index <c>-1</c> for none, which no record has.</summary>
	public ObjectiveSelection ObjectiveAt(int objective) => new(ObjectiveEditing.RecordOf(_source, objective) ?? -1);
}
