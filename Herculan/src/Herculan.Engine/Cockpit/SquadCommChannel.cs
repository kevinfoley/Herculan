using Herculan.Engine.Content;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// What one squad comm box is doing — <c>gauge+0x13b</c>, the four states
/// <c>HddDisplay_ServiceCommBoxes</c> (<c>0044b5f8</c>) switches on.
/// </summary>
public enum CommBoxState {
	/// <summary>Nobody is talking: the box shows the pilot's name, condition and objective.</summary>
	Idle = 0,

	/// <summary>Static, before the picture comes up. Held for <see cref="SquadCommChannel.StaticTicks"/>.</summary>
	OpeningStatic = 1,

	/// <summary>The portrait, animated by the message's <c>.SNC</c> script while its clip plays.</summary>
	Talking = 2,

	/// <summary>Static again, for the same time, before the box goes back to idle.</summary>
	ClosingStatic = 3
}

/// <summary>
/// One frame of whatever the transmitting squadmate's video box is showing, published for a display
/// that is not the box itself to draw — the block at <c>hddDisplay+0x766</c> that both comm-box
/// paints write and <c>MfdDisplay_Update</c> (<c>00446328</c>) reads.
///
/// <para>Only one transmission is published at a time: <c>CommBox_OnMessageBegin</c> claims the block
/// only while it is empty, so a second squadmate talking over the first stays in their own box and
/// never reaches the MFD.</para>
/// </summary>
/// <param name="Bank">The sprite bank the frame is in — a <c>PILOT&lt;n&gt;</c> or <c>STATIC</c>.</param>
/// <param name="Frame">Which frame of it.</param>
/// <param name="OffsetX">
/// Where to draw it relative to the display's own origin, from the portrait's <c>.OFS</c> table.
/// These are the bank's own 320-wide pixels and are added raw, while the frame is blitted doubled.
/// Static publishes (0, 0).
/// </param>
/// <param name="OffsetY"><inheritdoc cref="OffsetX"/></param>
/// <param name="Name">The speaker's name, for the caption plate.</param>
/// <param name="NameColorId">
/// The <c>COLORS.DAT</c> id the plate is filled with — the speaker's own comm-box colour,
/// <see cref="HudColorTable.PilotColorId"/>, the same colour their marker uses on the map.
/// </param>
/// <param name="ShowName">
/// Whether the caption is drawn. The MFD writes it only while the box is <see cref="CommBoxState.Talking"/>,
/// so static comes up unlabelled.
/// </param>
public readonly record struct SquadTransmission(
	string Bank,
	int Frame,
	int OffsetX,
	int OffsetY,
	string Name,
	int NameColorId,
	bool ShowName);

/// <summary>
/// The three squad comm boxes and the transmission they publish — <c>CommBox_OnMessageBegin</c>
/// (<c>0044b4ec</c>) and the per-frame service loop <c>HddDisplay_ServiceCommBoxes</c> (<c>0044b5f8</c>) that runs their state machine.
/// Derivation: docs/retail/simulation/heads-down-display.md, docs/retail/simulation/cockpit-messages.md and
/// docs/retail/simulation/audio.md.
///
/// <para>A reply from a squadmate reaches this through <see cref="SquadMessagePort"/>: the port decides
/// when the line is due, and its begin callback puts that pilot's box into
/// <see cref="CommBoxState.OpeningStatic"/> with the <c>whitenz</c> hiss under it. Twenty coarse ticks
/// later the portrait comes up and its <c>.SNC</c> script drives the frames until it runs out; twenty
/// more of static and the box is idle again. That is the whole of what the MFD's FLASH COMM page shows
/// when an order is received.</para>
///
/// <para><b>This type draws nothing and plays nothing.</b> It raises <see cref="Speak"/> for the clip
/// and <see cref="Hiss"/> for the static, the same split <see cref="MessagePort"/> makes, so it runs
/// with no device attached.</para>
/// </summary>
public sealed class SquadCommChannel {
	/// <summary>How many comm boxes there are.</summary>
	public const int SlotCount = 3;

	/// <summary>
	/// Coarse ticks of static on either side of the picture — <c>CommBox_OnMessageBegin</c>'s
	/// <c>now + 0x14</c> going in and the same going out, about a third of a second each.
	/// </summary>
	public const int StaticTicks = 0x14;

	/// <summary>Frames the <c>static</c> bank holds, cycled one per paint.</summary>
	public const int StaticFrameCount = 5;

	/// <summary>The bank name the static comes from.</summary>
	public const string StaticBank = "STATIC";

	/// <summary>
	/// The death scream, <c>AAAAAAARRGHH!</c> — the one message id the service loop
	/// (<c>HddDisplay_ServiceCommBoxes</c>) singles out, by testing the box's <c>+0x12d</c> copy of it against
	/// <c>'%'</c>. It gets its own picture (<see cref="ScreamFrame"/> flickering with static) and its
	/// own ending: the box latches comms-out and stays on static for good.
	/// </summary>
	public const int ScreamMessageId = 0x25;

	/// <summary>
	/// The portrait frame the scream shows — <c>HddGauge_PaintScream</c> (<c>0044b31c</c>)'s literal <c>0x1b</c>, the bank's
	/// last frame, which no <c>.SNC</c> script reaches.
	/// </summary>
	public const int ScreamFrame = 0x1b;

	/// <summary>
	/// Shortest time either half of the scream's flicker lasts, in coarse ticks — both the first
	/// frame's <c>now + 5</c> and the floor on each later <c>Math_RandomBelow(0x14)</c>.
	/// </summary>
	public const int ScreamMinTicks = 5;

	/// <summary>The bound of that roll.</summary>
	public const int ScreamRollBound = 0x14;

	private readonly Box[] _boxes = new Box[SlotCount];
	private readonly PilotRoster? _roster;
	private readonly GameContent _content;
	private readonly Dictionary<int, SquadMessages?> _catalogs = new();
	private readonly SquadMessages? _command;
	private readonly string _headquarters;
	private readonly Numerics.SimRandom? _random;

	private long _now;
	private int _speakingSlot = -1;

	/// <summary>
	/// The slot a line with no squadmate behind it is queued under — <c>Squad_IndexOf</c>'s answer
	/// for a null subject. It opens no comm box and draws in the computer's black and red.
	/// </summary>
	public const int NoSpeaker = -1;

	/// <summary>
	/// <c>STRINGS0.STR</c> group holding the name a speakerless line is signed with — <c>HQ</c>, the
	/// one entry at <c>DAT_004d1430</c> that <c>PilotMessagePort_GetHqName</c> (<c>004342b8</c>) hands the composer.
	/// </summary>
	public const int HeadquartersNameGroup = 8;

	/// <param name="content">The mounted archives, for the portrait scripts and the message sets.</param>
	/// <param name="roster">The pilot roster, or null when <c>PILOTS.STR</c> would not load.</param>
	/// <param name="random">
	/// The generator the variant roll, the scream's flicker and the portrait paint draw on — pass the
	/// world's <see cref="Sim.SimWorld.PresentationRandom"/>.
	/// </param>
	/// <param name="trainingMission">
	/// The mission's training number, which picks both the speakerless set (<c>COMMAND&lt;n&gt;.STR</c>)
	/// and the port class — see <see cref="SquadMessagePort.Training"/>.
	/// </param>
	public SquadCommChannel(GameContent content, PilotRoster? roster, Numerics.SimRandom? random = null,
			int trainingMission = 0) {
		_content = content ?? throw new ArgumentNullException(nameof(content));
		_roster = roster;
		_random = random;
		TrainingMission = trainingMission;

		for (int i = 0; i < _boxes.Length; i++) {
			_boxes[i] = new Box();
		}

		_command = SquadMessages.LoadCommand(content, trainingMission);
		_headquarters = SimStrings.Load(content)?.Text(HeadquartersNameGroup, 0) ?? string.Empty;

		Port = new SquadMessagePort(CatalogFor, random, training: trainingMission != 0);
		Port.Begin += BeginMessage;
		Port.End += EndMessage;
	}

	/// <summary>
	/// The queue in front of these boxes — the cockpit view's second message port. A squadmate's
	/// reply is posted to it and it decides when the box opens.
	/// </summary>
	public SquadMessagePort Port { get; }

	/// <summary>The mission's training number, 0 for an ordinary mission.</summary>
	public int TrainingMission { get; }

	/// <summary>
	/// Raised when a portrait starts, with the speaker's voice bank, the message id and the variant —
	/// the clip <c>CommBox_BeginMessage</c> opens beside the script.
	/// </summary>
	public event Action<int, int, int>? Speak;

	/// <summary>
	/// Raised when static starts, with <see cref="SoundId.CommStatic"/>. The original tests
	/// whether the hiss is already playing before starting it again, which the sink does not need to
	/// know about — a listener that plays it should make the same test.
	/// </summary>
	public event Action<int>? Hiss;

	/// <summary>What the transmitting box is showing this frame, or null when nobody is transmitting.</summary>
	public SquadTransmission? Transmission { get; private set; }

	/// <summary>Which slot owns the published transmission, or -1 for none.</summary>
	public int SpeakingSlot => _speakingSlot;

	/// <summary>What slot <paramref name="slot"/>'s box is doing.</summary>
	public CommBoxState State(int slot) =>
		slot >= 0 && slot < SlotCount ? _boxes[slot].State : CommBoxState.Idle;

	/// <summary>
	/// Seats a pilot in a comm box — <c>HddGauge_LoadPilotFrames</c>' own resolution of a machine's
	/// pilot index into a name, a portrait bank and a voice bank. A slot with nobody in it takes a
	/// negative index and paints nothing.
	/// </summary>
	public void Seat(int slot, int pilot, object? machine = null) {
		if (slot < 0 || slot >= SlotCount) {
			return;
		}

		var box = _boxes[slot];
		box.Machine = machine;
		box.Pilot = pilot;
		box.Portrait = pilot < 0 ? -1 : PilotRoster.PortraitOf(pilot);
		box.VoiceBank = box.Portrait < 0 ? 0 : PilotRoster.VoiceBankOf(box.Portrait);
		box.Name = (pilot < 0 ? null : _roster?.Name(pilot)) ?? string.Empty;
		box.State = CommBoxState.Idle;
		box.PreviousState = CommBoxState.Idle;
		box.CommsOut = false;
		box.ScreamStatic = false;
		box.ScreamWasStatic = false;
	}

	/// <summary>Whether slot <paramref name="slot"/> has a pilot in it.</summary>
	public bool Occupied(int slot) => slot >= 0 && slot < SlotCount && _boxes[slot].Portrait >= 0;

	/// <summary>Which voice bank slot <paramref name="slot"/> speaks with, or 0 when it is empty.</summary>
	public int VoiceBank(int slot) => slot >= 0 && slot < SlotCount ? _boxes[slot].VoiceBank : 0;

	/// <summary>
	/// The name in slot <paramref name="slot"/>, or empty when it is not seated — the box's own
	/// <c>+0x137</c>, which <c>HddGauge_Name</c> (<c>0044b900</c>) hands out and which the message composer
	/// (<c>PilotMessagePort_ComposeLine</c>, <c>00435d0c</c>) puts in front of the line the squadmate speaks. <see cref="NoSpeaker"/>
	/// answers <c>HQ</c>, the composer's fallback for a record naming no object.
	/// </summary>
	public string Name(int slot) =>
		slot == NoSpeaker ? _headquarters
		: slot >= 0 && slot < SlotCount ? _boxes[slot].Name : string.Empty;

	/// <summary>
	/// Whether the machine in slot <paramref name="slot"/> is destroyed — the machine's own flag,
	/// which <c>HddGauge_PaintIdle</c> reads to hand an idle box to the static paint, and which the
	/// service loop reads as a message ends to latch the box's comms out. The owner of the machines
	/// keeps it in step each frame.
	///
	/// <para>It is <b>not</b> the comms-out latch itself (<c>gauge+0x147</c>): the service loop sets
	/// that when a message ends with the machine destroyed or when the death scream ends. Retail's
	/// <c>HddDisplay_Update</c> also sets it for a destroyed squadmate during an external-view
	/// transition, which this class does not model — docs/retail/simulation/heads-down-display.md#the-death-scream.
	/// The difference matters for the scream, which is posted as the machine is destroyed and
	/// would never get past the opening static if the latch followed the flag.</para>
	/// </summary>
	public void SetDestroyed(int slot, bool destroyed) {
		if (slot >= 0 && slot < SlotCount) {
			_boxes[slot].Destroyed = destroyed;
		}
	}

	/// <summary>
	/// <c>Ai_PostSquadMessage</c> (<c>00420a98</c>) — what a squadmate says, addressed by the machine
	/// saying it. A machine that is not one of the three seated here has no box and is dropped, which
	/// is <c>Squad_IndexOf</c>'s own -1 arm.
	/// </summary>
	public void Post(int messageId, object speaker) {
		int slot = SlotOf(speaker);
		if (slot >= 0) {
			Port.Post(messageId, slot, speaker);
		}
	}

	/// <summary>
	/// Posts a line with no subject — a mission action's message, which <c>Action_Activate</c>
	/// (<c>00423430</c>) sends with a null <c>+0x02</c>. The port's post (<c>PilotMessagePort_Post</c>,
	/// <c>00435c48</c>) takes such an id from <c>COMMAND0.STR</c> rather than any pilot's set, and a
	/// training mission's from its own <c>COMMAND&lt;n&gt;.STR</c>. No box opens for it, because the
	/// comm box's begin callback resolves the null subject to no slot.
	/// </summary>
	public void PostUnattributed(int messageId) => Port.Post(messageId, NoSpeaker);

	/// <summary><c>Squad_IndexOf</c> — which box <paramref name="machine"/> talks in, or -1.</summary>
	public int SlotOf(object? machine) {
		if (machine == null) {
			return -1;
		}

		for (int slot = 0; slot < SlotCount; slot++) {
			if (ReferenceEquals(_boxes[slot].Machine, machine)) {
				return slot;
			}
		}

		return -1;
	}

	/// <summary>
	/// The message set a box speaks from: its pilot's voice bank resolved to a
	/// <c>PILOT&lt;bank&gt;.STR</c>, read once and kept. Null for an empty box.
	/// <see cref="NoSpeaker"/> takes the speakerless set, and so does every slot on the training
	/// port, whose post never looks at the subject.
	/// </summary>
	private SquadMessages? CatalogFor(int slot) {
		if (slot == NoSpeaker || Port.Training) {
			return _command;
		}

		int bank = VoiceBank(slot);
		if (bank == 0) {
			return null;
		}

		if (!_catalogs.TryGetValue(bank, out var catalog)) {
			_catalogs[bank] = catalog = SquadMessages.Load(_content, bank);
		}

		return catalog;
	}

	/// <summary>
	/// <c>CommBox_OnMessageBegin</c> — the port's begin callback. Opens the message's clip and its
	/// portrait script together, starts the opening static, and claims the published transmission if
	/// nothing else already holds it.
	/// </summary>
	public void BeginMessage(SquadMessagePort.Queued message) {
		ArgumentNullException.ThrowIfNull(message);

		int slot = message.Slot;
		if (slot < 0 || slot >= SlotCount || _boxes[slot].Portrait < 0) {
			return;
		}

		// CommBox_BeginMessage fails when Voice_Acquire cannot open the recording; the callback then
		// readies and cancels the line together, so it is dropped unshown and the box stays as it was.
		// This engine opens the clip separately, so a portrait script that will not load stands in for
		// that failure.
		var script = SncScript.Load(_content, _boxes[slot].Portrait, message.Id, message.Variant);
		if (script == null) {
			Port.MarkReady(message);
			Port.Cancel(message);
			return;
		}

		var box = _boxes[slot];
		box.Message = message;
		box.MessageId = message.Id;
		box.Variant = message.Variant;
		box.Script = script;
		box.Deadline = _now + StaticTicks;
		box.State = CommBoxState.OpeningStatic;
		Hiss?.Invoke(SoundId.CommStatic);

		// First speaker wins the block, and holds it until their own box goes quiet.
		if (_speakingSlot < 0) {
			_speakingSlot = slot;
		}
	}

	/// <summary>
	/// <c>CommBox_OnMessageEnd</c> — the matching end hook. It does not cut the box off: the picture
	/// runs to the end of its own script whatever the port does with the line. The other direction
	/// does hold: the script ending takes the line down, if it is still up.
	/// </summary>
	public void EndMessage(SquadMessagePort.Queued message) {
	}

	/// <summary>
	/// Runs every box for one frame at <paramref name="coarseTicks"/> — the service loop's own state
	/// machine, minus the painting. Call once a frame, after <see cref="SquadMessagePort.Update"/>.
	/// </summary>
	public void Update(long coarseTicks) {
		_now = coarseTicks;
		Port.Update(coarseTicks);

		for (int slot = 0; slot < SlotCount; slot++) {
			Step(slot);
		}

		Publish();
	}

	/// <summary>Takes every box down — leaving the cockpit, or the mission ending.</summary>
	public void Clear() {
		Port.Clear();
		foreach (var box in _boxes) {
			box.State = CommBoxState.Idle;
			box.PreviousState = CommBoxState.Idle;
			box.Script = null;
		}

		_speakingSlot = -1;
		Transmission = null;
	}

	private void Step(int slot) {
		var box = _boxes[slot];
		if (box.Portrait < 0) {
			return;
		}

		switch (box.State) {
			case CommBoxState.Idle:
				// An idle box is still painting if its machine is destroyed: HddGauge_PaintIdle hands
				// that box straight to HddGauge_PaintStatic, and that paint advances the cycle every
				// time it runs.
				if (box.Destroyed) {
					AdvanceStatic(box);
				}

				break;

			case CommBoxState.OpeningStatic:
				if (_now >= box.Deadline && !box.CommsOut) {
					box.State = CommBoxState.Talking;
					goto case CommBoxState.Talking;
				}

				AdvanceStatic(box);
				break;

			case CommBoxState.Talking:
				if (box.PreviousState != CommBoxState.Talking) {
					// Entering the state is what starts both halves, which is why the clip and the
					// script are opened in one call and started in one place. It is also what lets the
					// line over the canopy go up (MessagePort_MarkReady from HddDisplay_ServiceCommBoxes).
					box.ScriptStartedAt = _now;
					Port.MarkReady(box.Message);
					Speak?.Invoke(box.VoiceBank, box.MessageId, box.Variant);

					if (box.MessageId == ScreamMessageId) {
						box.Deadline = _now + ScreamMinTicks;
					}
				}

				int frame = box.Script?.Frame(_now - box.ScriptStartedAt) ?? SncScript.Finished;
				if (frame == SncScript.Finished) {
					// And the script running out is what takes it down (MessagePort_Cancel).
					Port.Cancel(box.Message);

					if (box.MessageId == ScreamMessageId) {
						// No closing static and no hiss: the box latches comms-out and drops back to
						// the opening-static state, which the latch then holds for good.
						box.CommsOut = true;
						box.State = CommBoxState.OpeningStatic;
					} else {
						box.State = CommBoxState.ClosingStatic;
						box.Deadline = _now + StaticTicks;
						Hiss?.Invoke(SoundId.CommStatic);

						// A message that ends with its speaker's machine destroyed latches too.
						if (box.Destroyed) {
							box.CommsOut = true;
						}
					}

					AdvanceStatic(box);
				} else if (box.MessageId == ScreamMessageId) {
					StepScream(box);
				} else {
					box.PortraitFrame = Math.Clamp(frame, 0, PilotRoster.TalkingFrameCount - 1);
					PaintPortraitDraw();
				}

				break;

			case CommBoxState.ClosingStatic:
				if (_now >= box.Deadline) {
					box.State = CommBoxState.Idle;
				} else {
					AdvanceStatic(box);
				}

				break;
		}

		box.PreviousState = box.State;

		// The service loop drops the published transmission when the box that holds it goes quiet, or
		// when that pilot's comms are out.
		if (_speakingSlot == slot && (box.State == CommBoxState.Idle || box.CommsOut)) {
			_speakingSlot = -1;
		}
	}

	/// <summary>
	/// <c>HddGauge_PaintScream</c> (<c>0044b31c</c>) — the scream's picture, in place of the script's frames. It flips between
	/// <see cref="ScreamFrame"/> and static each time the deadline passes, and every flip draws a new
	/// deadline <c>max(5, Math_RandomBelow(0x14))</c> ticks on, so the face breaks up irregularly for
	/// as long as the recording runs.
	/// </summary>
	private void StepScream(Box box) {
		if (!box.ScreamStatic) {
			box.PortraitFrame = ScreamFrame;
			PaintPortraitDraw();
			if (box.Deadline < _now) {
				box.ScreamStatic = true;
			}
		} else {
			AdvanceStatic(box);
			if (box.Deadline < _now) {
				box.ScreamStatic = false;
			}
		}

		if (box.ScreamStatic != box.ScreamWasStatic) {
			int roll = _random?.NextBelow(ScreamRollBound) ?? 0;
			box.Deadline = _now + Math.Max(ScreamMinTicks, roll);
		}

		box.ScreamWasStatic = box.ScreamStatic;
	}

	/// <summary>
	/// The draw <c>HddGauge_PaintPilotFrame</c> (<c>0044b120</c>) makes on every portrait paint and
	/// throws away. It moves nothing on screen, only the generator the scream's roll and every sound
	/// and message variant draw on after it — see docs/retail/simulation/heads-down-display.md#the-three-paints.
	/// </summary>
	private void PaintPortraitDraw() => _random?.Next();

	private static void AdvanceStatic(Box box) {
		box.StaticFrame++;
		if (box.StaticFrame >= StaticFrameCount) {
			box.StaticFrame = 0;
		}
	}

	/// <summary>
	/// What slot <paramref name="slot"/>'s own box on the Heads-Down Display is showing, or null when
	/// it is showing the idle five labels instead. The service loop's paint dispatch, as a value:
	/// <see cref="CommBoxState.Talking"/> gives the portrait frame and its <c>.OFS</c> offsets,
	/// either static state gives the <c>STATIC</c> bank's current frame, and idle gives null — except
	/// for a destroyed squadmate's box, which <c>HddGauge_PaintIdle</c> hands straight on to
	/// <c>HddGauge_PaintStatic</c> rather than labelling (<see cref="SetDestroyed"/>). The death
	/// scream alternates <see cref="ScreamFrame"/> with static while it plays.
	///
	/// <para>The name is carried on the record for both: the two video paints refresh the box's name
	/// label and nothing else, so the plate survives the picture going over the rest of the box.</para>
	/// </summary>
	public SquadTransmission? Video(int slot) {
		if (slot < 0 || slot >= SlotCount) {
			return null;
		}

		var box = _boxes[slot];
		if (box.Portrait < 0 || (box.State == CommBoxState.Idle && !box.Destroyed)) {
			return null;
		}

		return Describe(box, slot);
	}

	private void Publish() {
		if (_speakingSlot < 0) {
			Transmission = null;
			return;
		}

		Transmission = Describe(_boxes[_speakingSlot], _speakingSlot);
	}

	/// <summary>One box's current picture — shared by the MFD's published transmission and the box's
	/// own paint on the Heads-Down Display, which draw the same frame from the same two banks.</summary>
	private SquadTransmission Describe(Box box, int slot) {
		bool talking = box.State == CommBoxState.Talking;

		// The scream's static half is drawn by HddGauge_PaintStatic while the box is still in its
		// talking state, and that paint publishes the state too — so the caption stays up over it.
		bool portrait = talking && !(box.MessageId == ScreamMessageId && box.ScreamStatic);

		// The scream's frame is past the 27 entries the .OFS loader reads, so its offset pair is two
		// fields of the zero-allocated display object nothing is found writing: (0, 0), which is
		// what Offset answers for a frame the table does not cover.
		var (offsetX, offsetY) = portrait
			? _roster?.Offset(box.Portrait, box.PortraitFrame) ?? (0, 0)
			: (0, 0);

		return new SquadTransmission(
			portrait ? PilotRoster.BankName(box.Portrait) : StaticBank,
			portrait ? box.PortraitFrame : box.StaticFrame,
			offsetX, offsetY,
			box.Name,
			HudColorTable.PilotColorId(slot),
			talking);
	}

	/// <summary>One comm box's own state — the <c>0x14e</c>-byte gauge, minus its labels and rects.</summary>
	private sealed class Box {
		public object? Machine;
		public int Pilot = -1;
		public int Portrait = -1;
		public int VoiceBank;
		public string Name = string.Empty;
		public CommBoxState State;
		public CommBoxState PreviousState;
		public SquadMessagePort.Queued? Message;
		public long Deadline;

		/// <summary><c>gauge+0x147</c> — the comms-out latch, set here by the service loop — see <see cref="SetDestroyed"/>.</summary>
		public bool CommsOut;

		/// <summary>The machine's own destroyed flag, mirrored — see <see cref="SetDestroyed"/>.</summary>
		public bool Destroyed;
		public int MessageId;
		public int Variant;
		public SncScript? Script;
		public long ScriptStartedAt;
		public int StaticFrame;
		public int PortraitFrame;

		/// <summary>
		/// <c>gauge+0x148</c> — which half of the scream's flicker is up. Never reset, as in the
		/// original, where a box screams at most once.
		/// </summary>
		public bool ScreamStatic;

		/// <summary><c>gauge+0x149</c> — the same, as of the last paint.</summary>
		public bool ScreamWasStatic;
	}
}
