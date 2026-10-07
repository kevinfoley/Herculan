namespace HercWorks.Core.Data.Struct.Herc;

/// <summary>
/// The 13 external facets of a HERC's status block, by index. The shell names and prices them in six
/// groups (cockpit, each torso, chassis, each leg), not one by one. See
/// <c>docs/retail/formats/save-games.md#the-66-byte-status-block</c>.
/// </summary>
public sealed class HercExternals {
	public static readonly HercExternals CockpitFront = new(0, "Cockpit Front");
	public static readonly HercExternals CockpitRear = new(1, "Cockpit Rear");
	public static readonly HercExternals TorsoLeftFront = new(2, "Left Torso Front");
	public static readonly HercExternals TorsoRightFront = new(3, "Right Torso Front");
	public static readonly HercExternals TorsoLeftRear = new(4, "Left Torso Rear");
	public static readonly HercExternals TorsoRightRear = new(5, "Right Torso Rear");
	public static readonly HercExternals Chassis = new(6, "Chassis");
	public static readonly HercExternals LegLeftTop = new(7, "Leg Left Thigh");
	public static readonly HercExternals LegRightTop = new(8, "Leg Right Thigh");
	public static readonly HercExternals LegLeftMid = new(9, "Leg Left Calf");
	public static readonly HercExternals LegRightMid = new(10, "Leg Right Calf");
	public static readonly HercExternals LegLeftFoot = new(11, "Leg Left Foot");
	public static readonly HercExternals LegRightFoot = new(12, "Leg Right Foot");

	private static readonly IReadOnlyList<HercExternals> All = new[]
	{
		CockpitFront, CockpitRear, TorsoLeftFront, TorsoRightFront, TorsoLeftRear, TorsoRightRear,
		Chassis, LegLeftTop, LegRightTop, LegLeftMid, LegRightMid, LegLeftFoot, LegRightFoot
	};

	private static readonly Dictionary<short, HercExternals> ById = All.ToDictionary(e => e.Id);

	public short Id { get; }
	public string Label { get; }

	private HercExternals(short id, string label) {
		Id = id;
		Label = label;
	}

	public static HercExternals? GetById(short id) => ById.GetValueOrDefault(id);

	public static IReadOnlyList<HercExternals> Values() => All;

	public static HercExternals? GetByName(string name) =>
		All.FirstOrDefault(e => string.Equals(name, e.Label, StringComparison.OrdinalIgnoreCase));
}
