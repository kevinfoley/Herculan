using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The pilot and squad channel — the cockpit view's second message port, at <c>view+0x207</c>. The
/// computer's own is <see cref="MessagePort"/> at <c>view+0x20b</c>; the two are instances of the
/// same queue with different show behaviour, which in the original is a vtable and here is two
/// classes. See docs/retail/formats/cockpit-messages.md, "The port".
///
/// <para>What differs from the computer's channel:</para>
/// <list type="bullet">
/// <item>a post names a <b>speaker</b> — the squadmate saying it — because the filename, the portrait
/// and the comm box that plays it all come off that machine's own pilot;</item>
/// <item>the message set is per speaker: <c>str\PILOT&lt;bank&gt;.STR</c>, chosen by the pilot's voice
/// bank, and an id there can have several recordings, one of which is rolled for
/// (<see cref="SquadMessages.Pick"/>). A post with no speaker takes <c>COMMAND0.STR</c> instead;</item>
/// <item>it runs <b>begin</b> and <b>end</b> callbacks. Only this channel registers any
/// (<c>MessagePort_AddBeginCallback</c>, <c>004355a8</c>): <c>CommBox_OnMessageBegin</c>
/// (<c>0044b4ec</c>) starts that speaker's clip and portrait, and <c>CommBox_OnMessageEnd</c>
/// (<c>0044b5c0</c>) closes it. That pair is what <see cref="SquadCommChannel"/> is.</item>
/// </list>
///
/// <para>The lifecycle itself is the shared base's, <c>MessagePort_Tick</c> (<c>00435610</c>), on the
/// same four latches and the same four timings out of the entry's attribute bytes — see
/// <see cref="MessagePort"/>, which documents them against the computer's file.</para>
/// </summary>
public sealed class SquadMessagePort {
	/// <inheritdoc cref="MessagePort.Capacity"/>
	public const int Capacity = MessagePort.Capacity;

	/// <inheritdoc cref="MessagePort.TicksPerTimingUnit"/>
	public const int TicksPerTimingUnit = MessagePort.TicksPerTimingUnit;

	private readonly Func<int, SquadMessages?> _catalogs;
	private readonly SimRandom? _random;
	private readonly List<Queued> _queue = new(Capacity);

	private long _now;
	private Queued? _current;
	private bool _activated;
	private bool _ready;
	private bool _cancel;
	private bool _shown;

	/// <param name="catalogs">
	/// The message set for a comm-box slot — the pilot's voice bank resolved to a
	/// <c>PILOT&lt;bank&gt;.STR</c>, or the speakerless set for
	/// <see cref="SquadCommChannel.NoSpeaker"/>. Null for a slot with nobody in it.
	/// </param>
	/// <param name="random">
	/// The generator the variant roll draws on — pass the world's
	/// <see cref="Sim.SimWorld.PresentationRandom"/>.
	/// </param>
	/// <param name="training">Whether this is the training mission's port class — see <see cref="Training"/>.</param>
	public SquadMessagePort(Func<int, SquadMessages?> catalogs, SimRandom? random = null, bool training = false) {
		_catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
		_random = random;
		Training = training;
	}

	/// <summary>
	/// Whether this is the training mission's port class (vtable <c>0049baa8</c>) rather than the
	/// ordinary one (<c>0049bad4</c>). Its post (<c>TrainingMessagePort_Post</c>, <c>004362e4</c>) takes an id's first entry
	/// without rolling and whatever the subject, and its paint joins that entry with the ones after
	/// it into one word-wrapped block — see <see cref="Queued.Sentences"/> and
	/// <see cref="TrainingMessageLayout"/>.
	/// </summary>
	public bool Training { get; }

	/// <summary>Whether the channel is drawn and spoken. The preferences screen's PILOT MESSAGE setting.</summary>
	public MessageChannelMode Mode { get; set; } = MessageChannelMode.TextAndVoice;

	/// <inheritdoc cref="MessagePort.PilotDisabled"/>
	public bool PilotDisabled { get; set; }

	/// <summary>The attribute byte behind <see cref="Queued.ShowsWithoutCommBox"/>.</summary>
	public const int ShowsWithoutCommBoxAttribute = 7;

	/// <summary>Messages waiting, the one that is up included.</summary>
	public int QueueLength => _queue.Count;

	/// <summary>The line currently up, or null when the channel is quiet.</summary>
	public Queued? Current => _shown ? _current : null;

	/// <summary>
	/// Raised as a message comes due, before it goes up — the port's begin callbacks. The comm box
	/// starts its static, its clip and its portrait here.
	/// </summary>
	public event Action<Queued>? Begin;

	/// <summary>Raised as it comes down, on the matching end hook.</summary>
	public event Action<Queued>? End;

	/// <summary>
	/// Raised as a line goes up — the show. The training port starts its <c>TM&lt;n&gt;_</c> clip
	/// here, at the end of its paint; the ordinary port's voice belongs to the comm box instead.
	/// </summary>
	public event Action<Queued>? Shown;

	/// <summary>
	/// Posts what <paramref name="slot"/>'s squadmate has to say — <c>Ai_PostSquadMessage</c>
	/// (<c>00420a98</c>) through the port's vtable slot 0. The id names a line in that pilot's own
	/// message set, and the recording is rolled among that id's variants here rather than at play
	/// time, so the portrait script and the clip agree.
	/// </summary>
	public void Post(int messageId, int slot, object? speaker = null) {
		var catalog = _catalogs(slot);
		IReadOnlyList<SquadMessages.Entry> sentences = Training
			? catalog?.Instruction(messageId) ?? Array.Empty<SquadMessages.Entry>()
			: Array.Empty<SquadMessages.Entry>();

		SquadMessages.Entry? picked = Training
			? sentences.Count > 0 ? sentences[0] : null
			: catalog?.Pick(messageId, _random);

		if (picked is not { } message) {
			return;
		}

		var attributes = message.Attributes;
		byte Attribute(int index) => index < attributes.Length ? attributes[index] : (byte)0;

		var entry = new Queued {
			Id = message.Id,
			Variant = message.Variant,
			Slot = slot,
			Speaker = speaker,
			Text = message.Text,
			Sentences = Training ? sentences.Select(s => s.Text).ToArray() : new[] { message.Text },
			ShowsWithoutCommBox = Attribute(ShowsWithoutCommBoxAttribute) != 0,
			Priority = Attribute(SystemMessages.PriorityAttribute),
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
	/// Runs the port for one frame at <paramref name="coarseTicks"/> — <c>MessagePort_Tick</c>. Call
	/// once a frame, before <see cref="SquadCommChannel"/>'s own update.
	/// </summary>
	public void Update(long coarseTicks) {
		_now = coarseTicks;

		// The port's own update sets the ready latch only for a line that needs no comm box: any due
		// line on the training port (TrainingMessagePort_Update, 004365d0), and on the ordinary one only a line whose attribute
		// byte 7 is set (PilotMessagePort_Update, 004361cc). A squadmate's line waits for its box —
		// see MarkReady.
		if (_activated && _current is { } due && (Training || due.ShowsWithoutCommBox)) {
			_ready = true;
		}

		for (int i = _queue.Count - 1; i > 0; i--) {
			if (_queue[i].MaxWait < _now) {
				_queue.RemoveAt(i);
			}
		}

		_current ??= _queue.Count > 0 ? _queue[0] : null;

		if (_current is not { } message) {
			return;
		}

		if (!_ready && !_cancel) {
			if (message.MaxWait < _now) {
				// Never got its turn inside its own window.
				Dequeue();
				_shown = false;
			} else if (message.MinWait < _now && !_activated) {
				_activated = true;
				_ready = false;
				Begin?.Invoke(message);
			}
		} else if (!_activated || _cancel) {
			bool overrun = _shown && message.MaxTime < _now;
			bool yield = _shown && message.MinTime < _now && _queue.Count > 1;

			if (_cancel || overrun || yield || PilotDisabled) {
				if (_shown) {
					End?.Invoke(message);
				}

				_shown = false;
				Dequeue();
				_activated = false;
			}
		} else {
			// The show. The ordinary port's (PilotMessagePort_Speak, 00435d9c) draws one line over the
			// canopy; a squadmate's voice is not started here but by the comm box, which opens the clip
			// beside its portrait script so the two stay in step.
			_shown = true;
			_activated = false;
			message.MinTime += _now;
			message.MaxTime += _now;
			Shown?.Invoke(message);
		}
	}

	/// <summary>
	/// <c>MessagePort_MarkReady</c> (<c>00435b14</c>) — lets <paramref name="message"/> go up on the
	/// next <see cref="Update"/>, if it is the current line and due. The comm box calls it as the
	/// speaker's portrait starts talking.
	/// </summary>
	public void MarkReady(Queued? message) {
		if (message != null && ReferenceEquals(message, _current) && _activated) {
			_ready = true;
		}
	}

	/// <summary>
	/// <c>MessagePort_Cancel</c> (<c>00435b38</c>) — takes the current line down on the next
	/// <see cref="Update"/>, when <paramref name="message"/> is it or is null. The comm box calls it
	/// as the portrait's script runs out; a line the port has already taken down is not current, so
	/// the call does nothing.
	/// </summary>
	public void Cancel(Queued? message) {
		if (message == null || ReferenceEquals(message, _current)) {
			_cancel = true;
		}
	}

	/// <summary>
	/// <c>MessagePort_Withdraw</c> (<c>00435ac8</c>) on this port, with the same rules as
	/// <see cref="MessagePort.Withdraw"/>: the match is on the id and the speaker, the current line is
	/// cancelled unless it is due and waiting to go up, and any other match is removed from the queue.
	/// A cancelled line that is up comes down on the next <see cref="Update"/>, through
	/// <see cref="End"/>, so its comm box closes as it would at the end of the line.
	/// </summary>
	/// <returns>Whether anything was withdrawn.</returns>
	public bool Withdraw(int messageId, object? speaker) {
		if (_current is { } current && current.Id == messageId && ReferenceEquals(current.Speaker, speaker)) {
			if (_activated) {
				return false;
			}

			_cancel = true;
			return true;
		}

		int at = _queue.FindIndex(queued => queued.Id == messageId && ReferenceEquals(queued.Speaker, speaker));
		if (at < 0) {
			return false;
		}

		_queue.RemoveAt(at);
		return true;
	}

	/// <summary>Takes everything down and forgets it — leaving the cockpit, or the mission ending.</summary>
	public void Clear() {
		_queue.Clear();
		_current = null;
		_activated = false;
		_ready = false;
		_cancel = false;
		_shown = false;
	}

	private void Dequeue() {
		if (_queue.Count > 0) {
			_queue.RemoveAt(0);
		}

		_current = null;
		_cancel = false;
		_ready = false;
	}

	/// <summary>One queued line: which pilot says it, which recording, and the four timings.</summary>
	public sealed class Queued {
		/// <summary>The <c>PILOT&lt;bank&gt;.STR</c> message id — attribute byte 0.</summary>
		public int Id;

		/// <summary>Which recording of it — attribute byte 1, and the last digit of both filenames.</summary>
		public int Variant;

		/// <summary>Which comm box is speaking, or <see cref="SquadCommChannel.NoSpeaker"/>.</summary>
		public int Slot;

		/// <summary>Record <c>+0x02</c>: the machine the line is about, which here is the speaker.</summary>
		public object? Speaker;

		/// <summary>The line itself.</summary>
		public string Text = string.Empty;

		/// <summary>
		/// Everything the paint lays out: <see cref="Text"/> alone on the ordinary port, and on the
		/// training port the instruction's every sentence, <see cref="Text"/> first.
		/// </summary>
		public IReadOnlyList<string> Sentences = Array.Empty<string>();

		/// <summary>
		/// Record <c>+0x2c</c>, attribute byte 7 — whether the ordinary port readies the line itself
		/// rather than waiting for a comm box. Set on every <c>COMMAND0.STR</c> line, clear on every
		/// squadmate's.
		/// </summary>
		public bool ShowsWithoutCommBox;

		/// <inheritdoc cref="SystemMessages.PriorityAttribute"/>
		public int Priority;

		/// <summary>Record <c>+0x1c</c>.</summary>
		public long MinTime;

		/// <summary>Record <c>+0x20</c>.</summary>
		public long MaxTime;

		/// <summary>Record <c>+0x24</c>.</summary>
		public long MinWait;

		/// <summary>Record <c>+0x28</c>.</summary>
		public long MaxWait;
	}
}
