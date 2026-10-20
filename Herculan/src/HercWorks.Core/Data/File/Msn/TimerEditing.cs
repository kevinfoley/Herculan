using HercWorks.Core.Data.File.Msn.Script;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Creates, edits and deletes a <see cref="MissionDocument"/>'s action timers, row 11, as record edits. The record's layout
/// is docs/retail/formats/msn-mission-file.md#row-11-field-decode--the-timer-record-dat_00470662-30-bytesrecord, and what a
/// timer does docs/retail/simulation/mission-deployment.md#action-timers--actiontimer_tick-004230a4.
///
/// <list type="bullet">
/// <item>A repeated row-11 GUID overlays field by field (<c>Msn_MergeRow11</c> (<c>00416768</c>)), the delay's unset value
/// being 0, so a field is written through the record that supplies it (<see cref="MissionDocument.SetField"/>), and an
/// unset value into every record of the GUID. Row 11 draws no variant.</item>
/// <item>The delay is in seconds, 0 to <see cref="MaxDelay"/>: <c>ActionTimer_SetDelay</c> (<c>004679c0</c>) sign-extends the
/// stored word and shifts it left 11 into a 32-bit countdown, so a larger stored word reads as negative, and
/// <c>Timer_CountDown</c> clamps a countdown below 0 to 0, which fires on the first tick it counts. A delay of 0 does that
/// too.</item>
/// <item>The fired slots are kept filled from slot 0, the editor's choice: <c>ActionTimer_Tick</c> (<c>004230a4</c>) fires
/// every set slot of the ten, so a gap changes nothing in DBSIM.</item>
/// <item>Nothing references a timer: the generator resolves no field of any row into row 11
/// (<c>MissionGenerator.ReadRows</c>, every <c>Ref</c> read), and no <c>script.dat</c> field indexes block 6, so a delete
/// removes every row-11 record of the GUID with nothing to refuse it.</item>
/// </list>
/// </summary>
public static class TimerEditing {
	private const int TimerRow = 11;
	private const int ActionRow = 10;
	private const int ActionBlock = 5;

	/// <summary>The fired slots a record carries.</summary>
	public const int Slots = 10;

	/// <summary>The longest delay that counts down, in seconds (the class summary): about 9.1 hours.</summary>
	public const short MaxDelay = short.MaxValue;

	/// <summary>A new timer's delay, 10 seconds: 49 of the 72 retail records (<c>HercWorks.Query values --row 11 --field Delay</c>).</summary>
	public const short NewDelay = 10;

	/// <summary>
	/// Appends a new timer, answering its GUID in <paramref name="guid"/>: unconditioned and its dead word <c>0x04</c>
	/// <c>-1</c>, as all 72 retail records; <see cref="NewDelay"/>; started by no action, so it runs from mission start, and
	/// firing nothing until an action is added, the editor's choice.
	/// </summary>
	public static MsnEdit Create(MissionDocument document, out short guid) {
		short created = -1;
		var change = document.Change(() => document.Append(TimerRow, new ActionTimer30 {
			ConditionRef = -1, Unk04 = -1, PrimaryActionRef = -1, Delay = NewDelay,
			SequenceRefs = Enumerable.Repeat((short)-1, Slots).ToArray(),
		}, out created));
		guid = created;
		return change;
	}

	/// <summary>Row-11 GUID <paramref name="timer"/> as generated, throwing for one no record exports.</summary>
	public static ScriptActionTimer Exported(MissionDocument document, short timer) =>
		timer != -1 && document.ExportedIndex(TimerRow, timer) is { } index
			? document.Script.ActionTimers[index]
			: throw new InvalidOperationException($"Timer {timer} is not in this preview.");

	/// <summary>The row-10 GUID of the action that starts <paramref name="timer"/> as generated, or <c>-1</c> when it runs from mission start.</summary>
	public static short Start(MissionDocument document, short timer) => ActionGuid(document, Exported(document, timer).PrimaryActionRef);

	/// <summary>The row-10 GUIDs in <paramref name="timer"/>'s ten fired slots as generated, <c>-1</c> for an empty one.</summary>
	public static short[] Fired(MissionDocument document, short timer) =>
		Exported(document, timer).SequenceRefs.Select(action => ActionGuid(document, action)).ToArray();

	/// <summary>How many fired slots are filled, from slot 0 to the last filled one.</summary>
	public static int Filled(short[] fired) => Array.FindLastIndex(fired, action => action != -1) + 1;

	/// <summary>Sets the action that starts <paramref name="timer"/>, a row-10 GUID, or <c>-1</c> for none: it then runs from mission start.</summary>
	public static MsnEdit SetStart(MissionDocument document, short timer, short action) {
		Exported(document, timer);
		CheckAction(document, action, none: true);
		return document.Change(() => document.SetField(MsnField.TimerStart, timer, action));
	}

	/// <summary>Sets <paramref name="timer"/>'s delay to <paramref name="seconds"/>, 0 to <see cref="MaxDelay"/>.</summary>
	public static MsnEdit SetDelay(MissionDocument document, short timer, int seconds) {
		if (seconds is < 0 or > MaxDelay) {
			throw new ArgumentOutOfRangeException(nameof(seconds), seconds, $"A delay is 0 to {MaxDelay} seconds.");
		}

		Exported(document, timer);
		return document.Change(() => document.SetField(MsnField.TimerDelay, timer, (short)seconds));
	}

	/// <summary>Why <paramref name="action"/> cannot be added to <paramref name="timer"/>'s fired actions, or null when it can.</summary>
	public static string? WhyNoAddFired(MissionDocument document, short timer, short action = -1) {
		var fired = Fired(document, timer);
		return Filled(fired) >= Slots ? $"All {Slots} slots are filled."
			: action != -1 && fired.Contains(action) ? "It fires that action already."
			: null;
	}

	/// <summary>Adds row-10 action <paramref name="action"/> after <paramref name="timer"/>'s last fired one.</summary>
	public static MsnEdit AddFired(MissionDocument document, short timer, short action) {
		CheckAction(document, action, none: false);
		if (WhyNoAddFired(document, timer, action) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		int slot = Filled(Fired(document, timer));
		return document.Change(() => document.SetField(MsnField.TimerFired(slot), timer, action));
	}

	/// <summary>Puts row-10 action <paramref name="action"/> in <paramref name="timer"/>'s filled slot <paramref name="slot"/>.</summary>
	public static MsnEdit SetFired(MissionDocument document, short timer, int slot, short action) {
		var fired = Fired(document, timer);
		if (slot < 0 || slot >= Filled(fired)) {
			throw new InvalidOperationException($"There is no action in slot {slot}.");
		}

		CheckAction(document, action, none: false);
		if (fired.Where((_, at) => at != slot).Contains(action)) {
			throw new InvalidOperationException("It fires that action already.");
		}

		return document.Change(() => document.SetField(MsnField.TimerFired(slot), timer, action));
	}

	/// <summary>Removes the action in <paramref name="timer"/>'s slot <paramref name="slot"/>, moving every later one up a slot.</summary>
	public static MsnEdit RemoveFired(MissionDocument document, short timer, int slot) {
		var fired = Fired(document, timer);
		int last = Filled(fired) - 1;
		if (slot < 0 || slot > last || fired[slot] == -1) {
			throw new InvalidOperationException($"There is no action in slot {slot}.");
		}

		return document.Change(() => {
			for (int at = slot; at < last; at++) {
				document.SetField(MsnField.TimerFired(at), timer, fired[at + 1]);
			}

			document.SetField(MsnField.TimerFired(last), timer, -1);
		});
	}

	/// <summary>Deletes row-11 GUID <paramref name="timer"/>: every record of the GUID, whatever its condition.</summary>
	public static MsnEdit Delete(MissionDocument document, short timer) => document.Change(() => document.Remove(TimerRow, timer));

	// The GUID of the row-10 record that created block-5 action <action>, or -1.
	private static short ActionGuid(MissionDocument document, short action) =>
		action >= 0 ? document.GuidOf(ActionBlock, action) : (short)-1;

	private static void CheckAction(MissionDocument document, short action, bool none) {
		if (action == -1 ? !none : document.ExportedIndex(ActionRow, action) == null) {
			throw new InvalidOperationException(action == -1 ? "No action was given." : "That action is not in this preview.");
		}
	}
}
