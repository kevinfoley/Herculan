namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// One cockpit mouse record on an input tape, <c>CockpitMouseQueue_Push</c>'s own 14-byte layout, the same in both
/// games' tapes (<see cref="InputTape"/>, <see cref="Es1InputTape"/>): a position in the game's screen space, the
/// held-button mask and the queue's clock when the event was queued. ES2's push is DBSIM <c>00453034</c>, ES1's
/// ES1DBSIM <c>00044830</c> (docs/es1/simulation/cockpit-mouse.md#the-callback-and-the-queue).
/// </summary>
public class TapeMouseEvent {
	public int X { get; set; }
	public int Y { get; set; }

	/// <summary>Bit 0 the left button, bit 1 the right, and in ES1's queue bit 2 the middle.</summary>
	public ushort Buttons { get; set; }

	/// <summary>
	/// The clock when the event was queued: ES2's <c>Time_GetCoarseTicks</c>, 16 ms units; ES1's the clock
	/// <c>000981c4</c>, 60 counts a second.
	/// </summary>
	public int Time { get; set; }
}
