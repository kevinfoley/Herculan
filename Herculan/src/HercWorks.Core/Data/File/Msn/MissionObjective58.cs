namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #17 (58 bytes/record) — a mission objective, <c>script.dat</c> block 12: a condition the
/// simulation tests about a subject, the failure text, and the mission counters it writes. No GUID;
/// nothing names one. See docs/retail/formats/msn-mission-file.md, "Row #17 field decode", and
/// docs/retail/simulation/mission-objectives.md.
/// </summary>
public class MissionObjective58 {
	/// <summary>0x00 — condition ref. Not exported to <c>script.dat</c>.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x00 / 2;

	/// <summary>
	/// 0x02 — <b>1 when the objective must be satisfied</b> for the mission to be a success, and the
	/// first unsatisfied one supplies the failure text. <b>Anything else makes it a failure
	/// condition</b>, and the mission is lost the moment it comes back true. There is no third
	/// reading — the evaluator's own test is <c>== 1</c>.
	/// </summary>
	public short Required { get; set; }
	public const int RequiredWord = 0x02 / 2;

	/// <summary>0x04 — the condition code the objective tests: the simulator has cases for 0-4 and 6-10; retail files use 0-4, 6 and 7. See docs/retail/simulation/mission-objectives.md, "What each condition asks".</summary>
	public short ConditionCode { get; set; }

	/// <summary>0x06 — what <see cref="SubjectRef"/> names: 0 a group (row #16), 1 a mech (#12), 2 a flyer (#13), 3 a base (#14).</summary>
	public short SubjectKind { get; set; }
	public const int SubjectKindWord = 0x06 / 2;

	/// <summary>0x08 — the objective's subject, per <see cref="SubjectKind"/>.</summary>
	public short SubjectRef { get; set; }
	public const int SubjectRefWord = 0x08 / 2;

	/// <summary>0x0A — ref into row #6 (<see cref="MapPoint22"/>); no reader is found (see docs/retail/simulation/mission-objectives.md#open).</summary>
	public short PointRef { get; set; }
	public const int PointRefWord = 0x0A / 2;

	/// <summary>
	/// 0x0C — ref into row #8 (<see cref="WaypointGroup"/>): the waypoint group condition 0 asks about.
	/// It names which of the subject group's ten orders has to be finished, matched by the route that
	/// order runs on.
	/// </summary>
	public short RouteRef { get; set; }
	public const int RouteRefWord = 0x0C / 2;

	/// <summary>0x0E — the failure text, an <c>.ENG</c> id: the first of three consecutive lines.</summary>
	public short TextRef { get; set; }
	public const int TextRefWord = 0x0E / 2;

	/// <summary>0x10 — how many of <see cref="Pairs"/> are filled, from the front. Not exported to <c>script.dat</c>.</summary>
	public short PairCount { get; set; }

	/// <summary>0x12-0x39 — the mission counters the objective writes, unused slots <c>-1</c>/<c>-1</c>.</summary>
	public CounterPair[] Pairs { get; set; } = new CounterPair[10];
	public const int PairsWord = 0x12 / 2;
}

/// <summary>One (counter ref, operation) pair of <see cref="MissionObjective58.Pairs"/> — 4 bytes on disk.</summary>
public class CounterPair {
	public short CounterRef { get; set; } = -1;
	public short Op { get; set; } = -1;
}
