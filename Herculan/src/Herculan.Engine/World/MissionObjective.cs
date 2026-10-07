using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>
/// What kind of thing a <see cref="MissionObjective"/> is about — the record's <c>+0x04</c>
/// discriminator, resolved by <c>Mission_ResolveRefByKind</c> (<c>00425348</c>) exactly as an
/// order's and an action's are.
/// </summary>
public enum MissionObjectiveSubject {
	/// <summary>A whole mission group, by block-11 record index.</summary>
	Group = 0,

	/// <summary>One HERC, by block-7 roster slot.</summary>
	Mech = 1,

	/// <summary>One flyer, by block-8 roster slot.</summary>
	Flyer = 2,

	/// <summary>One structure, by block-9 roster slot.</summary>
	Base = 3
}

/// <summary>
/// One <b>mission objective</b> — a <c>script.dat</c> block-12 record, which
/// <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) reads 54 bytes at a time into a 76-byte
/// runtime record and <c>Mission_EvaluateObjectives</c> (<c>00413280</c>) walks once per poll.
///
/// <para>An objective is a <b>question about one subject</b> plus what answering it does. The
/// question is <see cref="ConditionCode"/> asked of <see cref="SubjectKind"/>/<see cref="SubjectRef"/>;
/// the answer is read two different ways depending on <see cref="Required"/>, and the first time it
/// comes back true the record's ten counter slots are applied and the record latches.</para>
///
/// <para><b>The field-to-offset mapping is the spawn pass's read order</b>, which is the only
/// statement of it: seven shorts, then ten counter refs, then ten operations. See
/// docs/retail/simulation/mission-objectives.md.</para>
/// </summary>
/// <param name="Required"><inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptObjective.Required"/></param>
/// <param name="ConditionCode">Record <c>+0x02</c> — which question is asked. See the constants below.</param>
/// <param name="SubjectKind"><inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptObjective.SubjectKind"/></param>
/// <param name="SubjectRef">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptObjective.SubjectRef"/>
/// Carried before resolution.
/// </param>
/// <param name="Point">
/// Record <c>+0x08</c> resolved to a block-1 coordinate, or null. Carried because the record carries
/// it; no reader is found (see docs/retail/simulation/mission-objectives.md#open).
/// </param>
/// <param name="RouteRef">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptObjective.RouteRef"/>
/// It is the only field <see cref="ConditionOrderComplete"/> uses besides the subject.
/// </param>
/// <param name="TextRef">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptObjective.TextRef"/>
/// The failure alert prints it and the lines after it, <see cref="TextLines"/> in all.
/// </param>
/// <param name="CounterRefs">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptObjective.CounterRefs"/>
/// Written when the objective is satisfied, to the same 1,000-short array <c>Action_Activate</c>
/// writes; see <see cref="Sim.SimWorld.MissionCounters"/>.
/// </param>
/// <param name="CounterOps">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptObjective.CounterOps"/>
/// <b>The operation codes are not the action record's</b> — see the four constants below.
/// </param>
public sealed record MissionObjective(
	short Required,
	short ConditionCode,
	MissionObjectiveSubject SubjectKind,
	int SubjectRef,
	Vec3i? Point,
	int RouteRef,
	int TextRef,
	IReadOnlyList<short> CounterRefs,
	IReadOnlyList<short> CounterOps) {

	/// <summary>Counter slots one objective record carries.</summary>
	public const int CounterSlots = 10;

	/// <summary>
	/// Consecutive <c>data\mission.str</c> lines an objective's description spans. The alert that
	/// prints it has a fourth line, which the spawn pass fills with the empty string at
	/// <c>0049a870</c> rather than from the file.
	/// </summary>
	public const int TextLines = 3;

	/// <summary><see cref="Required"/>'s one meaningful value: this objective has to be met.</summary>
	public const short Mandatory = 1;

	/// <summary>Counter operation: set the counter to one.</summary>
	public const short CounterSet = 4;

	/// <summary>Counter operation: zero it.</summary>
	public const short CounterClear = 5;

	/// <summary>Counter operation: add one.</summary>
	public const short CounterIncrement = 6;

	/// <summary>Counter operation: subtract one.</summary>
	public const short CounterDecrement = 7;

	/// <summary>
	/// The subject group has finished the order that runs <see cref="RouteRef"/> — an object subject
	/// asks it of the group that object belongs to.
	/// </summary>
	public const short ConditionOrderComplete = 0;

	/// <summary>
	/// The subject is gone: a group written off (<c>Group_ConditionTier</c> at or past its side's
	/// threshold), or an object destroyed or immobilised.
	/// </summary>
	public const short ConditionLost = 1;

	/// <summary>
	/// The subject is <b>clear</b> — not written off, and nothing hostile is aware of it and near it.
	/// See <see cref="Sim.MissionObjectives.IsClearOfThreats"/>. This is the escort objective.
	/// </summary>
	public const short ConditionClear = 2;

	/// <summary>The player has completed a data link. Reads the player's machine, not the subject.</summary>
	public const short ConditionDataLink = 3;

	/// <summary><see cref="ConditionDataLink"/> again — the evaluator's two arms are identical.</summary>
	public const short ConditionDataLinkAlso = 4;

	/// <summary>The subject has been engaged — <c>obj+0x9e</c>, or any member of a group.</summary>
	public const short ConditionEngaged = 6;

	/// <summary>The subject is disarmed — <c>obj+0xa5</c>, or <b>every</b> member of a group.</summary>
	public const short ConditionDisarmed = 7;

	/// <summary>The subject has <i>not</i> been engaged: <see cref="ConditionEngaged"/> negated.</summary>
	public const short ConditionUnengaged = 8;

	/// <summary>The player has not completed a data link.</summary>
	public const short ConditionNoDataLink = 9;

	/// <summary><see cref="ConditionNoDataLink"/> again, on the same terms as 3 and 4.</summary>
	public const short ConditionNoDataLinkAlso = 10;
}
