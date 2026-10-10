using Herculan.Engine.Content;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// Which halves of a message channel are live — options 3 and 2 of
/// <see cref="SimulatorPreferences"/>, which the preferences screen's COMPUTER MESSAGE and PILOT
/// MESSAGE rows write. Three values, not four: the row offers no OFF. See
/// docs/retail/simulation/cockpit-messages.md, "The computer's messages".
/// </summary>
public enum MessageChannelMode {
	/// <summary>Text drawn, nothing spoken. The port tests <c>!= 0</c> before it speaks.</summary>
	TextOnly = 0,

	/// <summary>Spoken, nothing drawn. The port tests <c>!= 1</c> before it draws.</summary>
	VoiceOnly = 1,

	/// <summary>Both, which is every other value the byte can hold.</summary>
	TextAndVoice = 2,
}

/// <summary>
/// What the cockpit's message ticker is showing this frame, or <see cref="HasText"/> false when it is
/// showing nothing. <see cref="MessagePort"/> republishes it every <see cref="MessagePort.Update"/>.
/// </summary>
/// <param name="Text">The line, truncated to <see cref="MessagePort.TextLimit"/> characters.</param>
/// <param name="ScrollTicks">
/// Coarse ticks since the line went up, which is what its x is a function of — see
/// <see cref="MessagePort.ScrollUnitsPerInterval"/>. Meaningless when <paramref name="Centered"/>.
/// </param>
/// <param name="Centered">
/// True for the one message that does not scroll. The paint tests the message id directly
/// (<c>id == 0x36</c>, <c>TRANSFERRING DATA</c>) and centres that one in the box; the same id is what
/// arms the blink, so the two travel together.
/// </param>
/// <param name="Visible">
/// False on the dark half of a blinking message's cycle. A message that does not blink is always
/// visible while it is up.
/// </param>
public readonly record struct MessageTicker(string? Text, long ScrollTicks, bool Centered, bool Visible) {
	/// <summary>Whether there is anything to draw at all.</summary>
	public bool HasText => !string.IsNullOrEmpty(Text);
}

/// <summary>
/// The cockpit's message port — the object at <c>view+0x20b</c> that queues what the computer has to
/// say, decides when to say it, scrolls it across the front window and reads it aloud. Roughly thirty
/// functions at <c>00434e50</c>-<c>00436fd0</c> in DBSIM; the derivation is in
/// docs/retail/simulation/cockpit-messages.md, "The computer's messages".
///
/// <para><b>Both halves, one object.</b> Text and speech are not two features that happen to agree:
/// the port shows a line and speaks it in the same call, and the timings that decide how long it
/// stays up come from the same <c>SYSTEM.STR</c> attribute bytes either way. That is why the
/// preference is one three-way setting rather than two checkboxes — see
/// <see cref="MessageChannelMode"/> — and why turning the text off still runs the whole lifecycle,
/// drawing nothing (the original's <c>+0x4d2</c> suppression flag).</para>
///
/// <para><b>What this type does not do.</b> It holds no rect and no font, and it draws nothing: it
/// publishes <see cref="Ticker"/> and something else turns that into pixels
/// (<see cref="MessageTickerLayout"/> and the overlay renderer). Speech and the alert tone leave by
/// <see cref="Speak"/> and <see cref="AlertTone"/> rather than by calling into
/// <see cref="Audio.ComputerVoice"/>, so the port stays testable with no device attached — the same
/// split <see cref="Herculan.Engine.Sim.SimWorld"/> and <see cref="ISoundSink"/> draw.</para>
///
/// <para>The original keeps a second instance of the same class for the pilot and squad channel, at
/// <c>view+0x207</c>, which is <see cref="SquadMessagePort"/> here.</para>
/// </summary>
public sealed class MessagePort {
	/// <summary>
	/// Messages the queue holds — the vector <c>MessagePort_QueueInit</c> (<c>00434e50</c>) builds, ten records of <c>0x31</c>
	/// bytes. Posting into a full queue drops the last, which is the lowest-priority one.
	/// </summary>
	public const int Capacity = 10;

	/// <summary>
	/// Coarse ticks one unit of an attribute timing is worth: <c>MessagePort_Enqueue</c> (<c>00434e8c</c>) multiplies each of
	/// the four by <c>0x3c</c>. At <c>Time_GetCoarseTicks</c>' 16 ms that is 0.96 s, so the authored
	/// numbers read as seconds.
	/// </summary>
	public const int TicksPerTimingUnit = 0x3c;

	/// <summary>
	/// How long the same message id is swallowed for after being shown — <c>MessagePort_Show</c> (<c>00436abc</c>)'s
	/// <c>300</c>, about 4.8 s. A swallowed repeat refreshes the window rather than leaving it, so a
	/// stream of them stays silent for as long as it keeps coming.
	/// </summary>
	public const int RepeatSuppressionTicks = 300;

	/// <summary>Characters of a line that reach the screen — <c>MessagePort_SetText</c> (<c>00436a0c</c>)'s <c>strncpy(.., 0x50)</c>.</summary>
	public const int TextLimit = 0x50;

	/// <summary>
	/// Units the text travels left per <see cref="TicksPerTimingUnit"/> ticks, in the <c>.GAU</c>'s own
	/// 320-wide space — <c>MessageTicker_ScrollText</c> (<c>00436f70</c>)'s <c>0x23 &lt;&lt; XCoordShift</c>, so about 73 device pixels a
	/// second in the 640-wide cockpit this engine draws.
	/// </summary>
	public const int ScrollUnitsPerInterval = 0x23;

	/// <summary>
	/// Coarse-tick bit the blinking message's on and off phases come from —
	/// <c>Time_GetCoarseTicks() &amp; 0x20</c>, so a little over half a second each way.
	/// </summary>
	public const int BlinkTickBit = 0x20;

	private readonly List<Entry> _queue = new(Capacity);

	private long _now;

	private Entry? _current;
	private bool _activated;
	private bool _ready;
	private bool _cancel;
	private bool _shown;
	private bool _suppressed;
	private bool _blink;
	private string _text = string.Empty;
	private long _shownAt;
	private long _lastShownTicks;
	private int _lastShownId = -1;

	/// <param name="messages">The parsed <c>SYSTEM.STR</c>, or null to run with nothing to say.</param>
	public MessagePort(SystemMessages? messages) => Messages = messages;

	/// <summary>The message set posts are looked up in, or null when <c>SYSTEM.STR</c> would not load.</summary>
	public SystemMessages? Messages { get; }

	/// <summary>Which halves of this channel are live. The computer's own preference.</summary>
	public MessageChannelMode Mode { get; set; } = MessageChannelMode.TextAndVoice;

	/// <summary>
	/// Whether the machine whose cockpit this is has been destroyed — the original's
	/// <c>LocalPlayerMech + 0x99</c>. Nothing new goes up while it is set and whatever is up comes down
	/// at once — but posting still works, so a queue built while the machine was dying is intact if it
	/// somehow is not.
	/// </summary>
	public bool PilotDisabled { get; set; }

	/// <summary>
	/// Whether the external view is up — the cockpit view manager's <c>+0x14</c> reading 4, which
	/// <c>MessagePort_Show</c> (<c>00436abc</c>) tests. A line that goes up while it is set takes the
	/// <see cref="MessageChannelMode.VoiceOnly"/> path: no text and no alert tone for as long as that
	/// line is up, back in the cockpit included, while its timings run and it is still spoken. See
	/// docs/retail/simulation/cockpit-messages.md, "The port".
	/// </summary>
	public bool ExternalView { get; set; }

	/// <summary>What the ticker is showing, republished by every <see cref="Update"/>.</summary>
	public MessageTicker Ticker { get; private set; }

	/// <summary>Whether a line is up and being drawn.</summary>
	public bool IsShowing => _shown && !_suppressed;

	/// <summary>Messages waiting behind the one that is up, the current one included.</summary>
	public int QueueLength => _queue.Count;

	/// <summary>
	/// Raised when a message goes up and the channel's voice half is live, with the flat
	/// <c>SYSTEM.STR</c> id. <c>MessagePort_Show</c> (<c>00436abc</c>)'s tail, which builds the <c>CVM</c> filename from the
	/// message's own clip number and plays it at volume 100.
	/// </summary>
	public event Action<int>? Speak;

	/// <summary>
	/// Raised with a sound catalog id when a message goes up and the display half is live — the
	/// switch at the end of <c>MessagePort_Show</c> (<c>00436abc</c>). See <see cref="AlertToneFor"/>.
	/// </summary>
	public event Action<int>? AlertTone;

	/// <summary>
	/// Posts a message — the vtable call the simulation makes on the port,
	/// <see cref="ISoundSink.Say"/>'s destination. Queued by priority (attribute byte 2, zero
	/// throughout the retail file), and a post into a full queue drops the lowest-priority entry to
	/// make room.
	/// </summary>
	/// <param name="messageId">The flat <c>SYSTEM.STR</c> id — see <see cref="SystemMessages"/>.</param>
	/// <param name="subject">
	/// The object the message is about, carried at the record's <c>+0x02</c> and compared by
	/// <see cref="Withdraw"/> so that two machines' worth of the same message are separate entries.
	/// The computer's own lines all post without one.
	/// </param>
	public void Post(int messageId, object? subject = null) {
		if (Messages?[messageId] is not { } message) {
			return;
		}

		var attributes = message.Attributes;
		byte Attribute(int index) => index < attributes.Length ? attributes[index] : (byte)0;

		var entry = new Entry {
			Id = messageId,
			Subject = subject,
			Text = message.Text,
			VoiceClip = message.VoiceClip,
			Priority = Attribute(SystemMessages.PriorityAttribute),

			// The two display times stay durations until the message is shown, at which point the tick
			// it went up is added in; the two delays are absolute from the moment it is posted.
			MinTime = Attribute(SystemMessages.MinDisplayAttribute) * TicksPerTimingUnit,
			MaxTime = Attribute(SystemMessages.MaxDisplayAttribute) * TicksPerTimingUnit,
			MinWait = Attribute(SystemMessages.MinDelayAttribute) * TicksPerTimingUnit + _now,
			MaxWait = Attribute(SystemMessages.MaxDelayAttribute) * TicksPerTimingUnit + _now,
		};

		if (_queue.Count == Capacity) {
			_queue.RemoveAt(_queue.Count - 1);
		}

		int at = _queue.FindIndex(queued => entry.Priority < queued.Priority);
		_queue.Insert(at < 0 ? _queue.Count : at, entry);
	}

	/// <summary>
	/// Withdraws a posted message — <c>MessagePort_Withdraw</c> (<c>00435ac8</c>). <c>Mech_ToggleRadarMode</c> withdraws both
	/// radar lines before posting the one the mode just became, so flipping twice quickly announces
	/// where it ended up rather than reading out the sequence.
	///
	/// <para>The one message left alone is the current one while it is activated, due and waiting to
	/// go up; this then returns false. The show clears that latch, so a line already on screen is
	/// cancelled and comes down on the next <see cref="Update"/>. See docs/retail/simulation/cockpit-messages.md,
	/// "The port".</para>
	/// </summary>
	/// <returns>Whether anything was withdrawn.</returns>
	public bool Withdraw(int messageId, object? subject = null) {
		if (_current is { } current && current.Id == messageId && Equals(current.Subject, subject)) {
			if (_activated) {
				return false;
			}

			_cancel = true;
			return true;
		}

		int at = _queue.FindIndex(queued => queued.Id == messageId && Equals(queued.Subject, subject));
		if (at < 0) {
			return false;
		}

		_queue.RemoveAt(at);
		return true;
	}

	/// <summary>
	/// Runs the port for one frame at <paramref name="coarseTicks"/> — <c>MessagePort_Tick</c> (<c>00435610</c>), plus the
	/// latch its paint entry points set on the way in. Call once a frame, before anything reads
	/// <see cref="Ticker"/>.
	///
	/// <para>The clock is <c>Time_GetCoarseTicks</c>' wall time, not simulation time, so a caller that
	/// pauses should stop advancing it rather than keep counting — which is exactly what the
	/// original's own pause pair (<c>MessagePort_Pause</c> (<c>00435b58</c>) / <c>MessagePort_Resume</c> (<c>00435b80</c>)) achieves by shifting every
	/// deadline forward by the paused duration.</para>
	/// </summary>
	public void Update(long coarseTicks) {
		_now = coarseTicks;

		// The paint side's own latch: every frame the port is painted, an activated message becomes
		// ready. It is what puts one frame between a message coming due and going up.
		if (_activated) {
			_ready = true;
		}

		// Anything queued behind the current message whose window has closed is dropped unshown. The
		// walk stops short of index 0, which is the message being handled.
		for (int i = _queue.Count - 1; i > 0; i--) {
			if (_queue[i].MaxWait < _now) {
				_queue.RemoveAt(i);
			}
		}

		_current ??= _queue.Count > 0 ? _queue[0] : null;

		if (_current is { } message) {
			if (!_ready && !_cancel) {
				if (message.MaxWait < _now) {
					// Never got its turn inside its own window.
					Dequeue();
					_shown = false;
					_suppressed = false;
				} else if (message.MinWait < _now && !_activated) {
					_activated = true;
					_ready = false;

					// The original also runs up to two registered begin callbacks here (+0x4b9, filled
					// by MessagePort_AddBeginCallback (004355a8)). Only the pilot channel registers any — the comm box's, which
					// start that speaker's clip and portrait — so on the computer's port there is
					// nothing to fire, and no hook is invented for one.
				}
			} else if (!_activated || _cancel) {
				bool overrun = _shown && message.MaxTime < _now;

				// Held for its minimum, and something else is waiting: yield to it. This is the whole
				// of the port's preemption — a message with the screen to itself keeps it until its
				// maximum, and gives it up at its minimum only when there is a successor.
				bool yield = _shown && message.MinTime < _now && _queue.Count > 1;

				if (_cancel || overrun || yield || PilotDisabled) {
					Hide();
					Dequeue();
					_activated = false;
				}
			} else if (Show()) {
				message.MinTime += _now;
				message.MaxTime += _now;
				_activated = false;
			}
		}

		Ticker = _shown && !_suppressed && _text.Length > 0
			? new MessageTicker(_text, _now - _shownAt, _blink, !_blink || (_now & BlinkTickBit) != 0)
			: default;
	}

	/// <summary>Takes everything down and forgets it — leaving the cockpit, or the mission ending.</summary>
	public void Clear() {
		_queue.Clear();
		_current = null;
		_activated = false;
		_ready = false;
		_cancel = false;
		_lastShownId = -1;
		Hide();
		Ticker = default;
	}

	/// <summary>
	/// The catalog id a message announces itself with — the jump table <c>MessagePort_Show</c> (<c>00436abc</c>) ends on.
	/// Only two ids get anything but the console tone, and both are damage: the general internal-damage
	/// line and the imminent-structural-failure one get <c>strcfail</c>, and the five that report a
	/// destroyed system or a failing shield get the warning whoop.
	///
	/// <para>Several ids the table lists separately resolve to the same tone its default arm gives, so
	/// the switch is wider than its behaviour.</para>
	/// </summary>
	public static int AlertToneFor(int messageId) => messageId switch {
		0 or 0x13 => SoundId.StructuralFailure,
		0x0c or 0x0f or 0x10 or 0x14 or 0x15 => SoundId.WarningWhoop,
		_ => SoundId.ScannerActive,
	};

	/// <summary>
	/// <c>MessagePort_Show</c> (<c>00436abc</c>) — puts the current message up, or swallows it as a repeat. Returns false
	/// when it was swallowed, which leaves the cancel latch set so the next tick drops it.
	/// </summary>
	private bool Show() {
		if (_current is not { } message || PilotDisabled) {
			return false;
		}

		if (Mode != MessageChannelMode.VoiceOnly && !ExternalView) {
			if (message.Id == _lastShownId && _now < _lastShownTicks + RepeatSuppressionTicks) {
				_shown = false;
				_cancel = true;
				_lastShownTicks = _now;
				return false;
			}

			_lastShownTicks = _now;
			_lastShownId = message.Id;

			_text = message.Text.Length > TextLimit ? message.Text[..TextLimit] : message.Text;
			_shownAt = _now;
			_blink = message.Id == SystemMessages.TransferringData;
			_shown = true;
			_suppressed = false;

			AlertTone?.Invoke(AlertToneFor(message.Id));
		} else {
			// The lifecycle still runs with the text off, or with the external view up, so the timings
			// behave the same either way; only the drawing and the tone are skipped, and the repeat
			// window is neither tested nor refreshed.
			_shown = true;
			_suppressed = true;
		}

		if (Mode != MessageChannelMode.TextOnly) {
			Speak?.Invoke(message.Id);
		}

		return true;
	}

	/// <summary><c>MessagePort_Erase</c> (<c>00436fd0</c>) — takes the line down and erases its box.</summary>
	private void Hide() {
		_shown = false;
		_suppressed = false;
		_blink = false;
		_text = string.Empty;
	}

	private void Dequeue() {
		if (_queue.Count > 0) {
			_queue.RemoveAt(0);
		}

		_current = null;
		_cancel = false;
		_ready = false;
	}

	/// <summary>One queued message — the <c>0x31</c>-byte record <c>MessagePort_Enqueue</c> (<c>00434e8c</c>) fills.</summary>
	private sealed class Entry {
		public int Id;
		public object? Subject;
		public string Text = string.Empty;
		public int VoiceClip;
		public int Priority;

		/// <summary>Record <c>+0x1c</c>: how long the line must stay up before it will yield.</summary>
		public long MinTime;

		/// <summary>Record <c>+0x20</c>: how long it may stay up at most.</summary>
		public long MaxTime;

		/// <summary>Record <c>+0x24</c>: the tick before which it will not be shown.</summary>
		public long MinWait;

		/// <summary>Record <c>+0x28</c>: the tick after which it is dropped unshown.</summary>
		public long MaxWait;
	}
}
