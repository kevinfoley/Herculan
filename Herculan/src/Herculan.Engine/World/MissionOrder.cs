using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>
/// What kind of thing a <see cref="MissionOrder"/> names — the order record's <c>+0x0c</c>
/// discriminator, which <c>Mission_ResolveRefByKind</c> (<c>00425348</c>) uses to turn the ref
/// beside it into a pointer.
/// </summary>
public enum MissionOrderSubject {
	/// <summary>The order names nothing.</summary>
	None = -1,

	/// <summary>Another mission group, by block-11 record index.</summary>
	Group = 0,

	/// <summary>One HERC, by block-7 roster slot.</summary>
	Mech = 1,

	/// <summary>One flyer, by block-8 roster slot.</summary>
	Flyer = 2,

	/// <summary>One structure, by block-9 roster slot.</summary>
	Base = 3
}

/// <summary>
/// One of the ten orders a mission group works through — the 22-byte record
/// <c>DBSim_SpawnMissionObjects</c> (<c>004253d8</c>) builds from one <c>script.dat</c> block-10
/// entry, with each of its refs resolved. See docs/formats/script-dat.md, "Block 10 in memory", for
/// the layout and docs/simulation/ai-goals.md for what each verb means;
/// <see cref="Sim.MissionGroup"/> is what runs them.
///
/// <para>The two record fields with no reader found — the order's own point and the short beside the
/// verb (docs/simulation/ai-goals.md#open) — are not carried here.</para>
/// </summary>
/// <param name="Verb">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptOrder.Verb"/>
/// Kept as the raw number rather than turned into an enum that would need a name for each of its
/// two readers' roles.
/// </param>
/// <param name="SubjectKind">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptOrder.SubjectKind"/>
/// A value the enum does not name is read as <see cref="MissionOrderSubject.None"/>.
/// </param>
/// <param name="SubjectRef">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptOrder.SubjectRef"/>
/// Carried before resolution.
/// </param>
/// <param name="Route">
/// Record <c>+0x08</c> resolved to block-1 points. Only slot 0's is ever installed as the group's
/// route; see <see cref="Sim.MissionGroup.Route"/>.
/// </param>
/// <param name="RouteRef">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptOrder.RouteRef"/>
/// Carried <i>unresolved</i>, which the AI never wants but the mission objective layer does: a "get
/// there" objective names the order it is about by the route that order runs on rather than by
/// slot. See <see cref="Sim.MissionGroup.OrderCompletedForRoute"/>.
/// </param>
/// <param name="ActionRef">
/// <inheritdoc cref="HercWorks.Core.Data.File.Msn.Script.ScriptOrder.ActionRef"/>
/// A ref outside block 5 is read as <c>-1</c>. See <see cref="Sim.MissionGroup.AiTick"/>.
/// </param>
public sealed record MissionOrder(
	short Verb,
	MissionOrderSubject SubjectKind,
	int SubjectRef,
	IReadOnlyList<Vec3i> Route,
	int ActionRef,
	int RouteRef = -1) {

	/// <summary>Whether the order names an action at all.</summary>
	public bool GatedOnAction => ActionRef >= 0;

	/// <summary>Hunt down what the order names — installs <c>search/destroy</c>.</summary>
	public const short VerbSearchDestroy = 0;

	/// <summary>Installs <c>ramming</c>.</summary>
	public const short VerbRam = 1;

	/// <summary>Installs <c>guarding</c>.</summary>
	public const short VerbGuard = 2;

	/// <summary>Installs <c>patrolling</c>.</summary>
	public const short VerbPatrol = 3;

	/// <summary>Installs <c>sleeping</c>.</summary>
	public const short VerbSleep = 4;

	/// <summary>Installs <c>travelling</c>, or <c>bulldog travel</c> on a chassis that never shipped.</summary>
	public const short VerbTravel = 5;

	/// <summary>Installs <c>following</c>.</summary>
	public const short VerbFollow = 6;

	/// <summary>
	/// The verb <c>Mech_AiSelectBehaviour</c> substitutes for a null order slot. It matches no case
	/// in the state map, in <c>Group_IsOrderComplete</c>, or in
	/// <see cref="Sim.Ai.AiTargeting.SelectTarget"/>'s designation test.
	/// </summary>
	public const short VerbNone = 0x0b;

	/// <summary>How many orders a block-11 record can name.</summary>
	public const int Slots = 10;
}
