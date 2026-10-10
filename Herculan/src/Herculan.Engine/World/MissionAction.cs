using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>
/// What shape a <see cref="MissionTriggerArea"/> is — the resolved block-4 record's type flag at
/// <c>+0x00</c>, which is the only thing that distinguishes the two.
/// </summary>
public enum MissionTriggerShape {
	/// <summary>An axis-aligned box between two block-1 coordinates, with Z ignored.</summary>
	Box = 0,

	/// <summary>A ground-plane radius about one block-1 coordinate.</summary>
	Circle = 1
}

/// <summary>
/// One trigger area of a <see cref="MissionAction"/> — the 10-byte record
/// <c>TriggerArea_Resolve</c> (<c>00423358</c>) builds from a <c>script.dat</c> block-4 entry and
/// <c>TriggerArea_ContainsPoint</c> (<c>004233a4</c>) tests a position against.
///
/// <para>The original stores resolved <i>pointers</i> into the block-1 coordinate array; the points
/// are copied here, which is the same thing for a table nothing writes to.</para>
/// </summary>
/// <param name="Shape">The record's type flag.</param>
/// <param name="A">The block-1 coordinate at <c>+0x02</c>: one corner of the box, or the centre.</param>
/// <param name="B">The other corner, for <see cref="MissionTriggerShape.Box"/> only.</param>
/// <param name="Radius">
/// The record's literal times ten, for <see cref="MissionTriggerShape.Circle"/> only — the scale
/// factor is <c>TriggerArea_Resolve</c>'s own, applied once at load rather than per test.
/// </param>
/// <param name="Reference">
/// The block-4 record it was resolved from, or -1 when not built from a file. The simulation never
/// reads it; tooling uses it to tell apart two actions sharing one area from two areas that
/// happen to have the same shape.
/// </param>
public readonly record struct MissionTriggerArea(
	MissionTriggerShape Shape,
	Vec3i A,
	Vec3i B,
	int Radius,
	int Reference = -1) {

	/// <summary>
	/// <c>TriggerArea_ContainsPoint</c> (<c>004233a4</c>) — whether a position is inside the area.
	///
	/// <para>The box test is <b>strict on both sides and flat</b>: a position exactly on an edge is
	/// outside, and Z is not looked at, so a box catches anything standing over its footprint however
	/// high. The circle test is the ground-plane distance, which is the sim's sqrt-free magnitude and
	/// so overshoots a true diagonal by up to about 12%, as the original's 2D ranges do.</para>
	/// </summary>
	public bool Contains(Vec3i point) {
		if (Shape != MissionTriggerShape.Box) {
			return SimMath.FastMagnitude2D(point.X - A.X, point.Y - A.Y) < Radius;
		}

		return System.Math.Min(A.X, B.X) < point.X && point.X < System.Math.Max(A.X, B.X)
			&& System.Math.Min(A.Y, B.Y) < point.Y && point.Y < System.Math.Max(A.Y, B.Y);
	}
}

/// <summary>
/// One mission action — the 58-byte runtime record <c>DBSim_LoadScriptDat</c> (<c>00424308</c>)
/// builds from a <c>script.dat</c> block-5 entry, minus the one field that is runtime state (the
/// fired flag, which lives on <see cref="Sim.MissionActionState"/>).
///
/// <para>An action is a <b>trigger plus a consequence</b>. <see cref="Type"/> says whose position is
/// offered to <see cref="Areas"/> every frame; the first area that catches its subject fires the
/// action, once and for good. What firing then means is read by whoever is watching: a mission group
/// waiting to arrive reads <see cref="Verb"/> for how it turns up, and a group working its orders
/// moves to the next one. See docs/retail/simulation/mission-deployment.md.</para>
///
/// <para><b>The field-to-offset mapping is the load pass's read order</b>, which is the only place
/// it is stated: two shorts into <c>+0x00</c>/<c>+0x02</c>, sixteen bytes of block-4 refs into a
/// stack buffer, twenty bytes into <c>+0x0c</c>, twenty more into <c>+0x20</c>, ten bytes read and
/// dropped, then <c>+0x34</c> and <c>+0x36</c>. See docs/retail/formats/script-dat.md.</para>
/// </summary>
/// <param name="Type">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptAction.Type"/>
/// See <see cref="Sim.MissionTriggers.Evaluate"/> for the table.
/// </param>
/// <param name="Verb">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptAction.Verb"/>
/// See <see cref="Sim.MissionGroup.DeploymentCheck"/>.
/// </param>
/// <param name="Areas">
/// The block-4 refs at file offset <c>+0x04</c>, resolved. <b>The list stops at the first negative
/// ref</b>, not at the eighth slot: the load pass counts up to the first <c>-1</c> and allocates
/// exactly that many, so a populated slot behind a gap is never tested.
/// </param>
/// <param name="CounterRefs">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptAction.CounterRefs"/>
/// </param>
/// <param name="CounterOps">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptAction.CounterOps"/>
/// <see cref="CounterIncrement"/> and <see cref="CounterClear"/> are the two that do anything; any
/// other value does nothing.
/// </param>
/// <param name="MessageId">
/// Record <c>+0x34</c>, <b>already decremented</b> — the load pass subtracts one from the stored
/// value, so this is the index the message port is handed, and <c>-1</c> means the action says
/// nothing. It names a <c>str\COMMAND0.STR</c> line, not a <c>data\mission.str</c> one — see
/// docs/retail/simulation/mission-deployment.md.
/// </param>
/// <param name="TargetRef">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptAction.TargetRef"/>
/// Carried as the raw ref the file states. The original resolves it to a pointer during the spawn
/// pass; <see cref="Sim.MissionActionState"/> holds the resolved form.
/// </param>
public sealed record MissionAction(
	short Type,
	short Verb,
	IReadOnlyList<MissionTriggerArea> Areas,
	IReadOnlyList<short> CounterRefs,
	IReadOnlyList<short> CounterOps,
	short MessageId,
	short TargetRef) {

	/// <summary>Block-4 refs an action record can name.</summary>
	public const int AreaSlots = 8;

	/// <summary>Counter slots an action record carries.</summary>
	public const int CounterSlots = 10;

	/// <summary>The <see cref="CounterOps"/> value that adds one to the named counter.</summary>
	public const short CounterIncrement = 6;

	/// <summary>The <see cref="CounterOps"/> value that zeroes it.</summary>
	public const short CounterClear = 5;

	/// <summary>Trigger subject: the player's own machine.</summary>
	public const short SubjectPlayer = 0;

	/// <summary>Trigger subject: every member of the player's group.</summary>
	public const short SubjectPlayerGroup = 1;

	/// <summary>Trigger subject: every member of every deployed human group.</summary>
	public const short SubjectHumanGroups = 2;

	/// <summary>Trigger subject: every member of every deployed Cybrid group.</summary>
	public const short SubjectCybridGroups = 3;

	/// <summary>Trigger subject: every member of every deployed mech group.</summary>
	public const short SubjectMechGroups = 4;

	/// <summary>Trigger subject: every member of every deployed flyer group.</summary>
	public const short SubjectFlyerGroups = 5;

	/// <summary>Trigger subject: every member of every deployed base group.</summary>
	public const short SubjectBaseGroups = 6;

	/// <summary>Trigger subject: the action's own resolved target object (a mech).</summary>
	public const short SubjectTargetMech = 7;

	/// <summary>Trigger subject: the action's own resolved target object (a flyer).</summary>
	public const short SubjectTargetFlyer = 8;

	/// <summary>Trigger subject: the action's own resolved target object (a base).</summary>
	public const short SubjectTargetBase = 9;

	/// <summary>Trigger subject: every member of the action's own resolved target group.</summary>
	public const short SubjectTargetGroup = 10;

	/// <summary>Arrival verb: drop pod, dropped anywhere in the half-circle ahead of the player.</summary>
	public const short VerbPodWide = 2;

	/// <summary>Arrival verb: drop pod, dropped close to the player's own heading.</summary>
	public const short VerbPodNarrow = 3;

	/// <summary>Arrival verb: walk on from behind the player.</summary>
	public const short VerbWalkBehind = 4;

	/// <summary>Arrival verb: walk on from ahead of the player.</summary>
	public const short VerbWalkAhead = 5;
}

/// <summary>
/// One <b>action timer</b> — a <c>script.dat</c> block-6 record. It names a primary action, a
/// delay, and up to ten actions to activate when that delay runs out.
///
/// <para><c>ActionTimer_Tick</c> (<c>004230a4</c>), walked every frame from <c>Sim_MainTick</c>,
/// counts the delay down only while the primary action has activated — or unconditionally when the
/// record names no primary. So a timer is either "N seconds into the mission, do this" or "N
/// seconds after that happened, do this", and chaining two of them is how a mission staggers a
/// sequence: <c>script6.dat</c> has action 1 arm a 92-second timer that activates action 2, which
/// arms a 123-second timer that activates action 3.</para>
///
/// <para><b>This is the second of four ways an action activates</b>, and the reason an action
/// carrying no trigger area of its own is ordinary rather than dead. The other three are its own areas, an
/// object being engaged, and an object being destroyed — see
/// <see cref="Sim.MissionActionState"/>.</para>
/// </summary>
/// <param name="PrimaryActionRef">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptActionTimer.PrimaryActionRef"/>
/// A ref outside block 5 is read as <c>-1</c>.
/// </param>
/// <param name="Delay">
/// How long the timer runs, in <see cref="Numerics.SimMath.TickDelta"/>'s timer unit. The file
/// states it in <see cref="DelayShift"/>-bit units, which <c>FUN_004679c0</c> converts on the way
/// in.
/// </param>
/// <param name="SequenceRefs"><inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptActionTimer.SequenceRefs"/></param>
public sealed record MissionActionTimer(int PrimaryActionRef, int Delay, IReadOnlyList<short> SequenceRefs) {
	/// <summary>Actions one timer can activate.</summary>
	public const int SequenceSlots = 10;

	/// <summary>
	/// What the file's stored delay is shifted by to reach the simulation's timer unit —
	/// <c>FUN_004679c0</c>'s own <c>value &lt;&lt; 11</c>. 2048 counts is 1000 ms exactly, so the
	/// on-disk unit is one second and a stored 30 is half a minute.
	/// </summary>
	public const int DelayShift = 11;

	/// <summary>
	/// What the timer is reloaded with once it expires — <c>ActionTimer_Tick</c>'s literal 30000,
	/// through the same shift, which is about eight hours. It does run again on
	/// that schedule; it just has nothing left to do, because every action it names has already
	/// activated and <c>Action_Activate</c> is one-shot.
	/// </summary>
	public const int SpentReload = 30000 << DelayShift;
}
