using Herculan.Engine.Content;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The cockpit's two message ports — the computer's and the squad's comm channel — the coarse clock they run on,
/// and the power-up announcement the computer's port carries. The audio stack speaks what they post: see
/// <see cref="Audio.GameAudio"/>, which the simulation's posts reach them through.
///
/// <para>Neither port has the original's view-change gate (the view manager's <c>+0x1c</c>): it covers
/// the one frame before retail's in-frame slide, and the pan and glance here have no such frame. See
/// docs/retail/simulation/cockpit-messages.md, "The port".</para>
/// </summary>
public sealed class MessagePorts {
	/// <summary>
	/// How long after the power-up begins the computer announces the result — the original's own
	/// <c>200 &lt; elapsed</c> against <c>Time_GetCoarseTicks</c>, whose unit is 16 ms. It lands
	/// inside <c>start3</c>'s five seconds rather than after them.
	/// </summary>
	public static readonly TimeSpan PowerUpAnnounceDelay = TimeSpan.FromMilliseconds(200 * 16);

	/// <summary>
	/// <c>Time_GetCoarseTicks</c>' unit, which is what <see cref="MessagePort"/> counts in:
	/// <c>GetTickCount() &gt;&gt; 4</c>, so 16 ms of wall time.
	/// </summary>
	public const double CoarseTickSeconds = 0.016;

	private MechObject? _pilot;
	private TimeSpan _powerUpAnnounceIn = TimeSpan.MinValue;
	private double _messageTicks;

	/// <param name="messages">The computer's messages, or null when <c>SYSTEM.STR</c> would not load.</param>
	public MessagePorts(SystemMessages? messages) {
		Computer = new MessagePort(messages);
	}

	/// <summary>
	/// <c>Time_GetCoarseTicks</c> as this session has counted it — the same clock the message port
	/// runs on, and the one the cockpit's power-up animations are timed against. Exposed because the
	/// compass's wind-up is stamped and ramped in it; see <see cref="HeadingTapeSweep"/>.
	/// </summary>
	public long CoarseTicks => (long)_messageTicks;

	/// <summary>
	/// The computer's message port. It owns the on-screen ticker as much as the speech; the simulation
	/// reaches it through <see cref="Sim.ISoundSink.Say"/>, which knows nothing about either half. A
	/// renderer reads <see cref="MessagePort.Ticker"/> from it; see <see cref="MessageTickerLayout"/>.
	/// </summary>
	public MessagePort Computer { get; }

	/// <summary>
	/// The pilot and squad channel — the three comm boxes, their queue and the portraits they play.
	/// Null until <see cref="AttachSquad"/> is called, because which pilots are in the boxes is a
	/// per-mission fact; a post to it before then is simply dropped.
	/// </summary>
	public SquadCommChannel? Squad { get; private set; }

	/// <summary>
	/// Hands this the mission's comm boxes. From here on <see cref="Update"/> runs the channel on the
	/// port's own clock, so it stops with everything else across a suspend.
	/// </summary>
	public void AttachSquad(SquadCommChannel squad) {
		ArgumentNullException.ThrowIfNull(squad);
		Squad = squad;
	}

	/// <summary>
	/// Stops both message ports' clock and nothing else — what a modal panel does. The original's
	/// <c>AlertPanel_Enter</c> (<c>00454630</c>) and <c>AlertPanel_Leave</c> (<c>004548ac</c>) pause
	/// and resume the two ports and leave the sound alone, so an effect already playing plays out
	/// and a line already on screen keeps the rest of its display time for after the panel.
	/// <see cref="Suspended"/> is the lost window's.
	/// </summary>
	public bool Paused { get; set; }

	/// <summary>
	/// Stops the clock for a lost window, which is where the original pauses both ports beside
	/// <c>Sound_SuspendAll</c> (<c>Sim_Suspend</c>, <c>0045f0b8</c>; the sound half is
	/// <see cref="Audio.GameAudio.Suspend"/>). A line already on screen keeps the rest of its display time
	/// for after the pause.
	/// </summary>
	public bool Suspended { get; set; }

	/// <summary>
	/// <c>Cockpit_PowerUpSound</c> (<c>004328cc</c>)'s message half: the player takes
	/// <paramref name="pilot"/>, whose destruction disables the ports, and a walking machine's power-up
	/// announces itself <see cref="PowerUpAnnounceDelay"/> later. A flyer's does not; see
	/// <see cref="Audio.GameAudio.PowerUp"/>.
	/// </summary>
	public void PowerUp(MechObject pilot) {
		_pilot = pilot;
		_powerUpAnnounceIn = pilot.Type.IsFlyer ? TimeSpan.MinValue : PowerUpAnnounceDelay;
	}

	/// <summary>
	/// Leaves the cockpit for good: the pending announcement is dropped and the computer's port forgets
	/// everything it was holding.
	/// </summary>
	public void LeaveCockpit() {
		_powerUpAnnounceIn = TimeSpan.MinValue;
		_pilot = null;
		Computer.Clear();
	}

	/// <summary>
	/// Per-frame service: the power-up announcement, then both ports on the coarse clock. Call once a
	/// frame, beside <see cref="Audio.GameAudio.Update"/>.
	/// </summary>
	/// <param name="elapsed">Wall time since the last call.</param>
	public void Update(TimeSpan elapsed) {
		AnnouncePowerUp(elapsed);

		// The port's clock is Time_GetCoarseTicks' wall time, not the simulation's, and it stops while
		// suspended or paused — which is what the original's own pause pair (MessagePort_Pause,
		// 00435b58 / MessagePort_Resume, 00435b80) achieves by shifting every deadline forward by
		// however long the pause lasted.
		if (!Suspended && !Paused) {
			_messageTicks += elapsed.TotalSeconds / CoarseTickSeconds;
		}

		Computer.PilotDisabled = _pilot is { Destroyed: true };
		Computer.Update((long)_messageTicks);

		// The squad channel runs on the same clock: it is the second instance of the same port, and
		// its comm boxes count their static in the same coarse ticks.
		if (Squad is { } squad) {
			squad.Port.PilotDisabled = Computer.PilotDisabled;
			squad.Update((long)_messageTicks);
		}
	}

	/// <summary>
	/// <c>Cockpit_PowerUpTick</c> (<c>00432924</c>)'s tail — the cockpit's power-up sequence announcing itself once
	/// <see cref="PowerUpAnnounceDelay"/> has passed since the sequence began.
	///
	/// <para>Which line is <see cref="PowerUpFindsDamage"/>'s.</para>
	/// </summary>
	private void AnnouncePowerUp(TimeSpan elapsed) {
		if (_powerUpAnnounceIn == TimeSpan.MinValue) {
			return;
		}

		_powerUpAnnounceIn -= elapsed;
		if (_powerUpAnnounceIn > TimeSpan.Zero) {
			return;
		}

		_powerUpAnnounceIn = TimeSpan.MinValue;
		Computer.Post(PowerUpFindsDamage(_pilot)
			? SystemMessages.PowerUpDamaged
			: SystemMessages.PowerUpNominal);
	}

	/// <summary>
	/// Whether the power-up announces <see cref="SystemMessages.PowerUpDamaged"/>: true when any of
	/// the first <see cref="PowerUpCheckedInternals"/> internals reads any damage at all, which is
	/// <c>Cockpit_PowerUpTick</c>'s test as docs/retail/simulation/cockpit-messages.md, "Posters", derives it.
	///
	/// <para>The original's reading (<c>Mech_ReadEntryDamage</c>, <c>0041b514</c>) takes a zero
	/// maximum as fully damaged where <see cref="ComponentDamage.DependentPercent"/> reads it as 0;
	/// no chassis that announces has one among these slots.</para>
	/// </summary>
	private static bool PowerUpFindsDamage(MechObject? pilot) {
		if (pilot?.Damage is not { } damage) {
			return false;
		}

		for (int slot = 0; slot < PowerUpCheckedInternals; slot++) {
			if (damage.DependentPercent(slot) != 0) {
				return true;
			}
		}

		return false;
	}

	/// <summary>How many internals the power-up checks, from slot 0 — the original's literal 10.</summary>
	private const int PowerUpCheckedInternals = 10;
}
