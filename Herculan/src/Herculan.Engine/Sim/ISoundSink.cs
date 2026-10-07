using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// What the simulation talks to when something makes a noise. <see cref="Audio.SoundDirector"/> is the
/// real implementation; a world with none attached simply runs silent.
///
/// <para>The simulation holds no audio state of its own and never asks a question back — it says
/// "this happened, here", exactly as DBSIM's own call sites do, and every rule about whether that is
/// audible, how loud, and where in the stereo field belongs to the director. That keeps
/// <see cref="SimWorld"/> tickable by a headless test or a mission editor with no device
/// present, which is the same split docs/herculan/planning.md draws for rendering.</para>
/// </summary>
public interface ISoundSink {
	/// <summary>
	/// Plays a catalog id with no position — a cockpit tone, which is in the player's ears wherever
	/// the machine is. <c>Sound_Play</c> (<c>0046272c</c>).
	/// </summary>
	void Play(int id);

	/// <summary>
	/// Plays a catalog id at a world point. <c>Sound_PlayAt</c> (<c>004627dc</c>).
	/// </summary>
	/// <param name="id">The catalog id.</param>
	/// <param name="position">Where it is, in world units.</param>
	/// <param name="reach"><inheritdoc cref="Audio.SoundDirector.PlayAt" path="/param[@name='reach']"/></param>
	/// <param name="source"><inheritdoc cref="Audio.SoundDirector.PlayAt" path="/param[@name='source']"/></param>
	void PlayAt(int id, Vec3i position, SoundReach? reach = null, object? source = null);

	/// <summary>Stops a catalog id. <c>Sound_Stop</c> (<c>004629c0</c>).</summary>
	void Stop(int id);

	/// <summary>
	/// Moves a sound that is already running. <c>Sound_UpdatePosition</c> (<c>00462878</c>) — what
	/// the looping engine hum and the flamer use to follow their machine.
	/// </summary>
	/// <param name="id">The catalog id.</param>
	/// <param name="position">Where it now is, in world units.</param>
	/// <param name="reach"><inheritdoc cref="Audio.SoundDirector.UpdatePosition" path="/param[@name='reach']"/></param>
	/// <param name="source"><inheritdoc cref="Audio.SoundDirector.UpdatePosition" path="/param[@name='source']"/></param>
	void MoveTo(int id, Vec3i position, SoundReach? reach = null, object? source = null);

	/// <summary>
	/// Sets a running sound's playback rate, 16.16 with <c>0x10000</c> as its recorded pitch —
	/// <c>Sound_SetPitch</c> (<c>00463010</c>). The flyer's engine hum is the one thing that varies
	/// it continuously; the cockpit power-up sets it once.
	/// </summary>
	void SetPitch(int id, int rate);

	/// <summary>
	/// Posts one of the cockpit computer's messages by its flat <c>SYSTEM.STR</c> id — the vtable
	/// call the original makes on the cockpit's message port, <c>view+0x20b</c>. See
	/// <see cref="Content.SystemMessages"/> for the ids and <see cref="Audio.ComputerVoice"/> for what
	/// becomes of one.
	/// </summary>
	void Say(int messageId);

	/// <summary>
	/// Posts what one squadmate has to say, on the cockpit's <i>other</i> message port
	/// (<c>view+0x207</c>) — <c>Ai_PostSquadMessage</c> (<c>00420a98</c>). The id names a line in that
	/// pilot's own <c>PILOT&lt;bank&gt;.STR</c>, and the machine saying it is what picks the comm box,
	/// the portrait and the recorded voice. See <see cref="Cockpit.SquadCommChannel"/>.
	/// </summary>
	void SquadSay(int messageId, object speaker);

	/// <summary>
	/// Posts a line on the same port with no speaker — a mission action's message, which
	/// <c>Action_Activate</c> (<c>00423430</c>) queues. The id names a <c>COMMAND0.STR</c> line, signed
	/// <c>HQ</c>. See <see cref="Cockpit.SquadCommChannel.PostUnattributed"/>.
	/// </summary>
	void CommandSay(int messageId);

	/// <summary>
	/// Withdraws a posted message that has not been said yet — <c>MessagePort_Withdraw</c> (<c>00435ac8</c>), which the radar
	/// toggle uses on both of its own lines before posting the one it wants.
	/// </summary>
	void Unsay(int messageId);

	/// <summary>
	/// Withdraws a line from the pilot-and-squad port by id and by the machine it is about — the same
	/// <c>MessagePort_Withdraw</c> on <c>view+0x207</c>, which <c>Squad_SendOrderToSlot</c>
	/// (<c>00431610</c>) uses on the squadmate it has just addressed. See
	/// <see cref="Cockpit.SquadMessagePort.Withdraw"/>.
	/// </summary>
	void SquadUnsay(int messageId, object? speaker);
}
