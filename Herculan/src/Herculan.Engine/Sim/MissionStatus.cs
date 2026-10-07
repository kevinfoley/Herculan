namespace Herculan.Engine.Sim;

/// <summary>
/// How the mission stands, as <c>Mission_Status</c> (<c>004135e8</c>) answers it once a poll. The
/// numbers are the original's own and are load-bearing twice over: they index the table at
/// <c>0049935c</c> that says which of them are worth interrupting the player for, and they select
/// the alert panel <c>StatusAlertPanel_Ctor</c> (<c>00455934</c>) builds.
///
/// <para><b>Nothing here is decided by the objectives alone.</b> Every conclusive value is gated on
/// the player being clear of hostiles as well — see <see cref="MissionObjectives.IsClearOfThreats"/>
/// — which is why a mission whose objectives are all met does not end while something is still
/// shooting at the player. See docs/retail/simulation/mission-objectives.md.</para>
/// </summary>
public enum MissionStatus {
	/// <summary>Not yet evaluated. Not one of the original's values; it is the <c>-1</c> the poll returns.</summary>
	None = -1,

	/// <summary>The player's machine is destroyed.</summary>
	PlayerDestroyed = 2,

	/// <summary>The player's machine is immobilised — <c>mech+0xa4</c>, legs gone.</summary>
	PlayerImmobilised = 3,

	/// <summary>
	/// Neither complete nor failed, and the player is still in contact. Announces nothing.
	/// </summary>
	InProgressEngaged = 4,

	/// <summary>Neither complete nor failed, and nothing is on the player. Announces nothing.</summary>
	InProgress = 5,

	/// <summary>A failure condition has come true, and the player is clear. <c>MISSION FAILED</c>.</summary>
	Failed = 6,

	/// <summary>
	/// The player has left the mission box. <c>APPROACHING MISSION ZONE BOUNDARY</c>.
	/// </summary>
	LeavingMissionZone = 7,

	/// <summary>
	/// The player is <see cref="MissionObjectives.RulesOfEngagementMargin"/> beyond the mission box.
	/// <c>RULES OF ENGAGEMENT VIOLATED. MISSION ABORTED.</c>
	/// </summary>
	RulesOfEngagementViolated = 8,

	/// <summary>
	/// Every mandatory objective is satisfied, no failure condition is, and the player is clear.
	/// <c>MISSION SUCCESSFUL</c>.
	/// </summary>
	Complete = 9,

	/// <summary>
	/// The objectives have decided the mission one way or the other, but the player is still in
	/// contact, so it is not announced. Both <see cref="Complete"/> and <see cref="Failed"/> collapse
	/// to this while a hostile is aware of the player and near enough — the one status that says
	/// "finish disengaging first".
	/// </summary>
	DecidedButEngaged = 10,

	/// <summary>
	/// Not one of <c>Mission_Status</c>'s answers: <c>Sim_MainTick</c> raises it in place of
	/// <see cref="PlayerImmobilised"/> when the outnumbered damage it deals first
	/// (<see cref="MechObject.ApplyGroupOutnumberedDamage"/>) destroys the player.
	/// </summary>
	ImmobilisedThenDestroyed = 18
}
