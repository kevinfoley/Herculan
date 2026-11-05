using System.Globalization;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Es1.Messages;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.Sound;
using Herculan.Es1.View;

namespace Herculan.Es1.Hdd;

/// <summary>
/// One of the Heads-Down Display's three comm-box gauges, its <c>0x14a</c> bytes from the display's <c>+0xf6</c>
/// (docs/es1/simulation/heads-down-display.md#the-comm-boxes): the squadmate's pilot, its six labels, bank and frame
/// offsets, which <see cref="Load"/> sets; the line it speaks (<see cref="BeginMessage"/>) and its step of the state
/// machine (<see cref="Service"/>); and its four paints, idle (<see cref="PaintIdle"/>), static
/// (<see cref="PaintStatic"/>), the face (<see cref="PaintPilotFrame"/>) and the death scream
/// (<see cref="PaintScream"/>).
///
/// <para>The display and the slot are kept here, as the paints that take (display, slot) in retail reach them.</para>
/// </summary>
public sealed class Es1HddCommBox {
	/// <summary>The labels, <c>+0x00</c> to <c>+0x14</c>: the name, <c>STATUS:</c>, the condition, <c>OBJECTIVE:</c>, the order line, and a sixth whose text nothing sets.</summary>
	public const int NameLabel = 0, StatusLabel = 1, ConditionLabel = 2, ObjectiveLabel = 3, OrderLabel = 4, LabelCount = 6;

	/// <summary>The offset pairs at <c>+0x3d</c>, and the entries <c>ofs\pilot&lt;p&gt;</c> is read for.</summary>
	public const int OffsetPairs = 30, OffsetEntries = 27;

	/// <summary>The frames of <c>dba\static</c> <see cref="PaintStatic"/> steps through.</summary>
	public const int StaticFrames = 5;

	/// <summary>The fill of the video rect behind the idle labels and the face, and of the sixth label.</summary>
	public const byte IdleFill = 0x3f, CornerFill = 0x3b;

	/// <summary>The states of <see cref="State"/>: idle, static before the face, the face talking, static after it.</summary>
	public const int Idle = 0, StaticBefore = 1, Talking = 2, StaticAfter = 3;

	/// <summary>The clock counts of static before the face talks and after it, a third of a second.</summary>
	public const uint StaticCounts = 0x14;

	/// <summary>The message id of the death scream, <c>%</c>, which talks through <see cref="PaintScream"/>.</summary>
	public const byte ScreamMessage = 0x25;

	/// <summary>The scream's face, its clock counts before the first flip, and its least counts and random bound after each.</summary>
	public const int ScreamFrame = 0x1b, ScreamFirstCounts = 5, ScreamLeastCounts = 5, ScreamCountsBound = 0x14;

	/// <summary>The full bank's first head frame, of three, which <see cref="PaintPilotFrame"/> draws under the mouth.</summary>
	public const int FirstHeadFrame = 24, HeadFrames = 3;

	/// <summary>The low-memory bank's head frame, and the offset pair it is drawn at.</summary>
	public const int LowMemoryHeadFrame = 6, LowMemoryHeadPair = 24;

	/// <summary>
	/// <c>HddLowMemFrameMap</c> (<c>00094494</c>): the low-memory bank's frame for each of the 28 portrait frames, the
	/// image's values.
	/// </summary>
	private static readonly short[] LowMemoryFrameMap =
		[1, 0, 1, 4, 2, 4, 5, 4, 2, 4, 4, 3, 3, 3, 4, 5, 5, 2, 4, 4, 4, 1, 2, 4, 6, 6, 6, 7];

	/// <summary>
	/// <c>STRINGS0.STR</c>'s groups the idle paint reads (docs/es1/formats/str-strings.md#the-groups): 24 the five
	/// conditions, 29 <c>STATUS:</c> and <c>OBJECTIVE:</c>, 34 the order lines.
	/// </summary>
	public const int ConditionGroup = 24, CaptionGroup = 29, OrderGroup = 34;

	/// <summary>
	/// <c>HddStaticFramePos</c> (<c>000b15fc</c>), the point the static is blitted at in the box's clip, which
	/// <c>HddDisplay_StaticInit</c> (<c>0003edac</c>) zeroes and nothing else writes.
	/// </summary>
	private static readonly (int X, int Y) StaticFramePos = (0, 0);

	/// <summary>The label rows below the box's top, from the second label to the fifth.</summary>
	private static readonly int[] LabelRows = [16, 24, 32, 40];

	private readonly Es1Label?[] _labels = new Es1Label?[LabelCount];
	private readonly (int X, int Y)[] _offsets = new (int X, int Y)[OffsetPairs];

	/// <summary>
	/// <c>CommBox_ElementCtor</c> (<c>ES1DBSIM 0003cab8</c>)(gauge), from the display's <c>__vec_ctor</c>; the
	/// constructor's own work is the rects' and the offset pairs' constructors.
	/// </summary>
	internal Es1HddCommBox(Es1HddDisplay display, int slot) {
		Display = display;
		Slot = slot;
	}

	/// <summary>The display the box is one of.</summary>
	public Es1HddDisplay Display { get; }

	/// <summary>The box's slot, 0 to 2, its squadmate's in <see cref="Es1Cockpit.SquadmateMachines"/>.</summary>
	public int Slot { get; }

	/// <summary><c>+0x00</c>–<c>+0x14</c>, the six labels by <see cref="NameLabel"/> and the rest; none until <see cref="Load"/>.</summary>
	public IReadOnlyList<Es1Label?> Labels => _labels;

	/// <summary><c>+0x1c</c>, a byte <see cref="Es1HddDisplay"/>'s end callback clears; what reads it is Open (docs/es1/simulation/heads-down-display.md#open).</summary>
	public byte Byte1C { get; internal set; }

	/// <summary><c>+0x18</c>, the pilot's bank, <c>dba\pilot&lt;p&gt;</c> or the low-memory <c>pilot&lt;p&gt;s</c>; none when it is missing.</summary>
	public IReadOnlyList<DynamixBitmap>? Bank { get; private set; }

	/// <summary><c>+0x1d</c>, the video rect: the box's button's rect inset by one pixel, which the display's repaint sets.</summary>
	public Es1Rect VideoRect { get; set; }

	/// <summary><c>+0x2d</c>, the marker rect, the <c>.GAU</c>'s, which the box's button paints in highlight mode 1.</summary>
	public Es1Rect MarkerRect { get; set; }

	/// <summary><c>+0x3d</c>, the 30 offset pairs, pair <i>k</i> where entry <i>k</i> of <c>ofs\pilot&lt;p&gt;</c> puts it.</summary>
	public IReadOnlyList<(int X, int Y)> Offsets => _offsets;

	/// <summary><c>+0x12d</c>, the id of the line the box last began, its low byte.</summary>
	public byte MessageId { get; private set; }

	/// <summary><c>+0x12e</c>, the static's frame, 0 to 4.</summary>
	public byte StaticFrame { get; private set; }

	/// <summary><c>+0x12f</c>, the speech slot of the line the box last began, none when its load failed.</summary>
	public Es1SpeechSlot? SpeechSlot { get; private set; }

	/// <summary><c>+0x133</c>, set for the full bank and clear for the low-memory one.</summary>
	public bool FullBank { get; private set; }

	/// <summary><c>+0x135</c>, the portrait number, the pilot index / 3.</summary>
	public short Portrait { get; private set; }

	/// <summary><c>+0x137</c>, the pilot's name, none until <see cref="Load"/> sets it, and none for a portrait past the names.</summary>
	public string? Name { get; set; }

	/// <summary><c>+0x13b</c>, the state, <see cref="Idle"/> to <see cref="StaticAfter"/>.</summary>
	public int State { get; set; }

	/// <summary><c>+0x13f</c>, the state as the state machine last left it.</summary>
	public int PreviousState { get; private set; }

	/// <summary><c>+0x143</c>, the clock count at which the static ends, or the scream next flips.</summary>
	public uint Deadline { get; internal set; }

	/// <summary><c>+0x147</c>, the comms-out latch, set for a destroyed squadmate.</summary>
	public bool CommsOutLatch { get; set; }

	/// <summary><c>+0x148</c> and <c>+0x149</c>, the scream's phase and its previous value.</summary>
	public (byte Phase, byte Previous) Scream { get; private set; }

	/// <summary>
	/// <c>HddGauge_LoadPilotFrames</c> (<c>0003dab4</c>)'s gauge, on <paramref name="pilot"/>, the pilot after the first
	/// box's override (docs/es1/simulation/heads-down-display.md#who-is-in-it--hddgauge_loadpilotframes-0003dab4): the
	/// portrait <i>p</i> (<see cref="Es1Cockpit.PilotPortraitOf"/>) and the name, entry <i>p</i> of
	/// <see cref="Es1HddDisplay.PilotNames"/>; the pilot channel's table for the slot,
	/// <see cref="Messages.Es1MessagePort.SystemMessagesIndex"/> with the voice bank of <i>p</i>
	/// (<see cref="Es1Cockpit.PilotVoiceBankOf"/>); the 27 entries of <c>ofs\pilot&lt;p&gt;</c>, each an index <i>k</i>
	/// and the pair for <see cref="Offsets"/>[<i>k</i>]; the bank <c>dba\pilot&lt;p&gt;</c> with <see cref="FullBank"/>
	/// set, or under <see cref="Es1Preferences.LowMemory"/> <c>pilot&lt;p&gt;s</c> with it clear; the six labels on the
	/// box's button's rect {<i>x0</i>, <i>y0</i>, <i>x1</i>, <i>y1</i>}, centered, <i>h</i> the cell height of
	/// <c>cpred</c>: the name in <c>cpred</c> on the slot's color (<see cref="Es1Cockpit.SquadSlotColor"/>) over
	/// <i>x0</i> + 2 to <i>x1</i> − 2 from <i>y0</i> + 4, four over <i>x0</i> to <i>x1</i> from <i>y0</i> + 16, 24, 32
	/// and 40 in <c>cpblue</c>, <c>cpred</c>, <c>cpblue</c> and <c>cpred</c> with no fill, each <i>h</i> tall, and the
	/// sixth in <c>cpred</c> on <see cref="CornerFill"/> over <i>x0</i> to <i>x0</i> + 10 and <i>y1</i> − 10 to
	/// <i>y1</i>; and the states, the static frame, the latch and the scream's phases cleared.
	///
	/// <para>A missing <c>ofs\</c> file leaves the pairs at (0, 0) and a missing bank leaves none, as a missing
	/// <c>dba\pbuttons</c> blits nothing, where retail asserts on each; an entry's index outside the 30 pairs, which
	/// retail writes past them, is not taken, an engine guard. A portrait past the names' first group gives no name,
	/// where retail's walk goes on into the next group's header (docs/es1/simulation/cockpit-messages.md#the-string-files).</para>
	/// </summary>
	internal void Load(short pilot) {
		var cockpit = Display.Cockpit;
		short portrait = (short)Es1Cockpit.PilotPortraitOf(pilot);
		Portrait = portrait;
		var names = Display.PilotNames?.Group(0);
		Name = names != null && portrait >= 0 && portrait < names.Count ? names[portrait].Text : null;
		cockpit.PilotChannel.SystemMessagesIndex((byte)Es1Cockpit.PilotVoiceBankOf(portrait), (sbyte)Slot);

		string file = "PILOT" + portrait;
		if (Display.Resources?.Invoke("ofs", file + ".OFS") is { } bytes
				&& new PilotOffsetFileTransformer().Parse(bytes) is { Entries: { } entries }) {
			foreach (var entry in entries.Take(OffsetEntries)) {
				if (entry.Index >= 0 && entry.Index < OffsetPairs) {
					_offsets[entry.Index] = (entry.X, entry.Y);
				}
			}
		}

		FullBank = !cockpit.Preferences.LowMemory;
		Bank = Display.Resources?.Invoke("dba", file + (FullBank ? "" : "S") + ".DBA") is { } bank
			? new DynamixBitmapArrayTransformer().Parse(bank)?.Images
			: null;

		var red = Display.Font(Es1CockpitViews.CpRed);
		var blue = Display.Font(Es1CockpitViews.CpBlue);
		int h = red?.CellHeight ?? 0;
		var rect = Display.Buttons[Es1HddDisplay.FirstCommBoxButton + Slot].Rect;
		_labels[NameLabel] = Label(red, new Es1Rect(rect.X0 + 2, rect.Y0 + 4, rect.X1 - 2, rect.Y0 + 4 + h),
			Es1Cockpit.SquadSlotColor(Slot));
		for (int i = 0; i < LabelRows.Length; i++) {
			int top = rect.Y0 + LabelRows[i];
			_labels[StatusLabel + i] = Label(i % 2 == 0 ? blue : red, new Es1Rect(rect.X0, top, rect.X1, top + h), 0);
		}

		_labels[LabelCount - 1] = Label(red, new Es1Rect(rect.X0, rect.Y1 - 10, rect.X0 + 10, rect.Y1), CornerFill);
		PreviousState = 0;
		State = 0;
		StaticFrame = 0;
		CommsOutLatch = false;
		Scream = (0, 0);

		static Es1Label Label(HudFont? font, Es1Rect rect, int fill) {
			var label = new Es1Label(font) { FillColor = fill };
			label.SetRect(Es1Label.Centered, rect);
			return label;
		}
	}

	/// <summary>
	/// <c>HddGauge_PaintIdle</c> (<c>0003df60</c>)(display, slot) (docs/es1/simulation/heads-down-display.md#the-three-paints):
	/// while the display's screen rect is visible, for a squadmate not destroyed, the video rect filled
	/// <see cref="IdleFill"/> and the five labels written: the name, <c>STATUS:</c>, the condition of
	/// <see cref="ConditionIndex"/>, <c>OBJECTIVE:</c> and the order line of <see cref="Es1Mech.SquadOrderLineIndex"/>;
	/// for a destroyed one, <see cref="PaintStatic"/>. With no squadmate in the slot, which a box with a name always has,
	/// nothing, an engine guard.
	/// </summary>
	public void PaintIdle() {
		var cockpit = Display.Cockpit;
		if (!cockpit.RectCornerVisible(Display.ScreenRect) || cockpit.SquadmateMachines[Slot] is not { } machine) {
			return;
		}

		if (machine.Destroyed) {
			PaintStatic();
			return;
		}

		var canvas = cockpit.PushCanvasContext();
		canvas.FillRect(VideoRect.X0, VideoRect.Y0, VideoRect.X1, VideoRect.Y1, IdleFill);
		_labels[NameLabel]?.SetText(canvas, Name);
		_labels[StatusLabel]?.SetText(canvas, Display.Text(CaptionGroup, 0));
		_labels[ConditionLabel]?.SetText(canvas, Display.Text(ConditionGroup, ConditionIndex(machine)));
		_labels[ObjectiveLabel]?.SetText(canvas, Display.Text(CaptionGroup, 1));
		_labels[OrderLabel]?.SetText(canvas, Display.Text(OrderGroup, machine.SquadOrderLineIndex()));
	}

	/// <summary>
	/// <c>HddGauge_PaintStatic</c> (<c>0003e400</c>)(display, gauge): while the display's screen rect is visible, frame
	/// <see cref="StaticFrame"/> of <see cref="Es1HddDisplay.StaticBank"/> blitted at <see cref="StaticFramePos"/> in the
	/// box's clip (<see cref="PushVideoClip"/>), the frame stepped on and wrapped at <see cref="StaticFrames"/>, and the
	/// name written again. With no bank or frame, which retail reads unchecked, nothing is blitted, an engine guard.
	/// </summary>
	public void PaintStatic() {
		var cockpit = Display.Cockpit;
		if (!cockpit.RectCornerVisible(Display.ScreenRect)) {
			return;
		}

		var bank = Display.StaticBank;
		PushVideoClip().Blit(bank != null && StaticFrame < bank.Count ? bank[StaticFrame] : null, StaticFramePos.X,
			StaticFramePos.Y);
		StaticFrame++;
		if (StaticFrame == StaticFrames) {
			StaticFrame = 0;
		}

		_labels[NameLabel]?.SetText(cockpit.PushCanvasContext(), Name);
	}

	/// <summary>
	/// <c>CommBox_BeginMessage</c> (<c>0003e060</c>)(display, gauge, <paramref name="record"/>)
	/// (docs/es1/simulation/heads-down-display.md#starting-a-line): the id's low byte into <see cref="MessageId"/>; the
	/// suffix <c>_</c>, the id in decimal with a 0 before it below 10, and the attribute byte 1 in decimal, unsigned,
	/// with a 0 before it below 100 and another below 10; and <see cref="Es1VoiceLines.LoadLine"/> with the script
	/// <c>P</c> + (<c>'A'</c> + the portrait) + suffix and the voice <c>P</c> + <see cref="Es1Cockpit.PilotVoiceBankOf"/>
	/// of the portrait + suffix, so <c>PA_01000</c> and <c>P1_01000</c> for line 1, byte 0, of portrait 0. The slot goes
	/// to <see cref="SpeechSlot"/>; answers whether there is one.
	/// </summary>
	internal bool BeginMessage(Es1QueuedMessage record) {
		ushort id = (ushort)record.Id;
		MessageId = (byte)id;
		byte attribute = record.Attribute1;
		string suffix = "_" + (id < 10 ? "0" : "") + id.ToString(CultureInfo.InvariantCulture)
			+ (attribute < 100 ? "0" : "") + (attribute < 10 ? "0" : "")
			+ attribute.ToString(CultureInfo.InvariantCulture);
		string voice = "P" + Es1Cockpit.PilotVoiceBankOf(Portrait).ToString(CultureInfo.InvariantCulture) + suffix;
		string script = "P" + (char)(byte)('A' + Portrait) + suffix;
		var cockpit = Display.Cockpit;
		SpeechSlot = cockpit.Globals.VoiceLines.LoadLine(script, voice, cockpit.Globals.Clock.Clock, Display.Resources);
		return SpeechSlot != null;
	}

	/// <summary>
	/// The box's step of <c>HddDisplay_ServiceCommBoxes</c> (<c>0003e574</c>) on the clock count <paramref name="now"/>
	/// (docs/es1/simulation/heads-down-display.md#the-state-machine--hdddisplay_servicecommboxes-0003e574), then
	/// <see cref="PreviousState"/> = the state:
	///
	/// <list type="bullet">
	/// <item><see cref="Idle"/>: while the previous state is not idle or <see cref="Es1HddDisplay.IdleRepaintPending"/>
	/// is set, <see cref="PaintIdle"/> and the video rect copied to the page on the screen
	/// (<see cref="Es1Cockpit.DrawToCockpit"/> with 1), the flag set when that copied nothing.</item>
	/// <item><see cref="StaticBefore"/>: <see cref="PaintStatic"/> until <paramref name="now"/> reaches
	/// <see cref="Deadline"/> with the comms-out latch clear, then <see cref="Talking"/> at once.</item>
	/// <item><see cref="Talking"/>: on entry the pilot channel's ready latch and <see cref="Es1VoiceLines.Start"/> of the
	/// slot, the frame −1 when that answers false and otherwise the slot's, and for <see cref="ScreamMessage"/> the
	/// deadline <paramref name="now"/> + <see cref="ScreamFirstCounts"/>; after entry the slot's frame. At −1 the
	/// channel's cancelled latch; then for the scream the latch and <see cref="StaticBefore"/>, and otherwise
	/// <see cref="StaticAfter"/> with the deadline <paramref name="now"/> + <see cref="StaticCounts"/> and the latch
	/// when the squadmate is destroyed; and <see cref="PaintStatic"/>. Otherwise <see cref="PaintScream"/> for the
	/// scream and <see cref="PaintPilotFrame"/> of the frame for any other line.</item>
	/// <item><see cref="StaticAfter"/>: <see cref="PaintStatic"/> until <paramref name="now"/> reaches the deadline,
	/// then idle at once.</item>
	/// </list>
	///
	/// <para>The pilot channel is the display's cockpit's, <c>CockpitView_GetSquadMessagePort</c>'s. With no slot, which
	/// a box only talks with once its load succeeded, or no squadmate in the slot, which retail reads unchecked, the
	/// frame is −1 and the latch is left, engine guards.</para>
	/// </summary>
	internal void Service(uint now) {
		switch (State) {
			case Idle:
				ServiceIdle();
				break;
			case StaticBefore:
				if (Deadline <= now && !CommsOutLatch) {
					State = Talking;
					ServiceTalking(now);
				} else {
					PaintStatic();
				}

				break;
			case Talking:
				ServiceTalking(now);
				break;
			case StaticAfter:
				if (Deadline <= now) {
					State = Idle;
					ServiceIdle();
				} else {
					PaintStatic();
				}

				break;
		}

		PreviousState = State;
	}

	/// <summary>
	/// <c>HddGauge_PaintPilotFrame</c> (<c>0003e1cc</c>)(display, <paramref name="frame"/>, gauge)
	/// (docs/es1/simulation/heads-down-display.md#the-three-paints): while the display's screen rect is visible, the
	/// video rect filled <see cref="IdleFill"/>; in the box's clip (<see cref="PushVideoClip"/>), with
	/// <see cref="FullBank"/> the bank's frame <see cref="FirstHeadFrame"/> + the presentation generator's draw
	/// (<c>Math_RandomNext</c> on <c>000b5c48</c>), sign-extended and taken modulo <see cref="HeadFrames"/> unsigned, at
	/// its own offset pair, then the frame at its own pair; without it, the bank's <see cref="LowMemoryHeadFrame"/> at
	/// pair <see cref="LowMemoryHeadPair"/>, then <see cref="LowMemoryFrameMap"/>[frame] at the frame's pair; and the
	/// name written again. A frame or pair past the bank, the pairs or the map, which retail reads unchecked, blits
	/// nothing, an engine guard.
	/// </summary>
	public void PaintPilotFrame(short frame) {
		var cockpit = Display.Cockpit;
		if (!cockpit.RectCornerVisible(Display.ScreenRect)) {
			return;
		}

		cockpit.PushCanvasContext().FillRect(VideoRect.X0, VideoRect.Y0, VideoRect.X1, VideoRect.Y1, IdleFill);
		var clip = PushVideoClip();
		if (FullBank) {
			int head = FirstHeadFrame + (int)((uint)(int)cockpit.Globals.PresentationRandom.Next() % HeadFrames);
			Blit(clip, head, head);
			Blit(clip, frame, frame);
		} else {
			Blit(clip, LowMemoryHeadFrame, LowMemoryHeadPair);
			Blit(clip, frame >= 0 && frame < LowMemoryFrameMap.Length ? LowMemoryFrameMap[frame] : -1, frame);
		}

		_labels[NameLabel]?.SetText(cockpit.PushCanvasContext(), Name);
	}

	/// <summary>
	/// <c>HddGauge_PaintScream</c> (<c>0003e348</c>)(display, gauge), on the clock count <paramref name="now"/>
	/// (docs/es1/simulation/heads-down-display.md#the-three-paints): while the display's screen rect is visible, at phase
	/// 0 <see cref="PaintPilotFrame"/> of <see cref="ScreamFrame"/>, the phase 1 once <paramref name="now"/> is past
	/// <see cref="Deadline"/>; at phase 1 <see cref="PaintStatic"/>, the phase back to 0 once past it; on a phase change
	/// the deadline <paramref name="now"/> + the greater of <see cref="ScreamLeastCounts"/> and the presentation
	/// generator's draw below <see cref="ScreamCountsBound"/> (<c>Math_RandomBelow</c> on <c>000b5c48</c>); and the phase
	/// kept as the previous one.
	/// </summary>
	public void PaintScream(uint now) {
		if (!Display.Cockpit.RectCornerVisible(Display.ScreenRect)) {
			return;
		}

		var (phase, previous) = Scream;
		if (phase == 0) {
			PaintPilotFrame(ScreamFrame);
			if (Deadline < now) {
				phase++;
			}
		} else if (phase == 1) {
			PaintStatic();
			if (Deadline < now) {
				phase--;
			}
		}

		if (phase != previous) {
			uint counts = (uint)Display.Cockpit.Globals.PresentationRandom.NextBelow(ScreamCountsBound);
			Deadline = now + Math.Max(counts, (uint)ScreamLeastCounts);
		}

		Scream = (phase, phase);
	}

	/// <summary>The idle state's step: the idle paint and its copy while the box has just come idle or a copy is pending.</summary>
	private void ServiceIdle() {
		if (PreviousState != Idle || Display.IdleRepaintPending) {
			PaintIdle();
			Display.IdleRepaintPending = !Display.Cockpit.DrawToCockpit(VideoRect, 1);
		}
	}

	/// <summary>The talking state's step (see <see cref="Service"/>).</summary>
	private void ServiceTalking(uint now) {
		var cockpit = Display.Cockpit;
		var channel = cockpit.PilotChannel;
		short frame;
		if (PreviousState == State) {
			frame = SpeechSlot?.Frame ?? -1;
		} else {
			channel.Ready = true;
			frame = SpeechSlot is { } slot && cockpit.Globals.VoiceLines.Start(slot) ? slot.Frame : (short)-1;
			if (MessageId == ScreamMessage) {
				Deadline = now + ScreamFirstCounts;
			}
		}

		if (frame == -1) {
			channel.Cancelled = true;
			if (MessageId == ScreamMessage) {
				CommsOutLatch = true;
				State = StaticBefore;
			} else {
				State = StaticAfter;
				Deadline = now + StaticCounts;
				if (!cockpit.Globals.Sound.IsPlaying(Sound.Es1Sound.StaticRow)) {
					cockpit.Globals.Sound.Play(Sound.Es1Sound.StaticRow);
				}

				if (cockpit.SquadmateMachines[Slot] is { Destroyed: true }) {
					CommsOutLatch = true;
				}
			}

			PaintStatic();
		} else if (MessageId == ScreamMessage) {
			PaintScream(now);
		} else {
			PaintPilotFrame(frame);
		}
	}

	/// <summary>Frame <paramref name="index"/> of the bank at offset pair <paramref name="pair"/> in <paramref name="clip"/>, or nothing past either.</summary>
	private void Blit(Es1PanelCanvas clip, int index, int pair) {
		if (Bank is { } bank && index >= 0 && index < bank.Count && pair >= 0 && pair < OffsetPairs) {
			clip.Blit(bank[index], _offsets[pair].X, _offsets[pair].Y);
		}
	}

	/// <summary>
	/// <c>HddGauge_ConditionIndex</c> (<c>0003ded8</c>)(display, <paramref name="machine"/>): of
	/// <see cref="Es1Mech.ReadDamageReadouts"/>, the mean <i>m</i> of readings 1 to 19, truncating, and
	/// (<c>0x100</c> − <i>m</i>) × 100 ≫ 8: 90 or more 0 (<c>OK</c>), 74 or more 1, 51 or more 2, 1 or more 3, else 4
	/// (<c>DESTROYED</c>), an entry of group 24.
	/// </summary>
	public static int ConditionIndex(Es1Mech machine) {
		var readings = machine.ReadDamageReadouts();
		int sum = 0;
		for (int i = 1; i <= 19; i++) {
			sum += readings[i];
		}

		int percent = (0x100 - sum / 19) * 100 >> 8;
		return percent >= 90 ? 0 : percent >= 74 ? 1 : percent >= 51 ? 2 : percent >= 1 ? 3 : 4;
	}

	/// <summary>
	/// <c>CommBox_PushVideoClip</c> (<c>0003e790</c>)(gauge): the canvas context pushed, and the clip context
	/// <c>CommBox_ClipContext</c> (<c>000b15f8</c>) answered with its rect the video rect less the canvas context's
	/// origin, its own origin (0, 0), clip mode 1 and the translation set, as <c>HddDisplay_Ctor</c> leaves it. Here the
	/// context is made at each push and handed to what draws, so <c>CommBox_PopVideoClip</c> (<c>0003e828</c>),
	/// <c>Cockpit_PopRenderContext</c>, has nothing to put back.
	/// </summary>
	internal Es1PanelCanvas PushVideoClip() {
		var canvas = Display.Cockpit.PushCanvasContext();
		var origin = canvas.Origin;
		return new Es1PanelCanvas(canvas.Display, VideoRect.Offset(-origin.X, -origin.Y));
	}
}
