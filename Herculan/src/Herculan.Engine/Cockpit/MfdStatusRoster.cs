using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The STATUS screen's squad roster — the machines F1's SELECT button walks, held at
/// <c>MfdDisplay+0x308</c> with its cursor at <c>+0x318</c> and its count at <c>+0x31c</c>.
/// docs/retail/formats/mfd.md#the-subject owns the evidence; F1's subject is <see cref="Subject"/>, read
/// through <see cref="MfdStatusSubject.For"/> as F5's selection is.
/// </summary>
public sealed class MfdStatusRoster {
	private readonly SimObject?[] _entries;

	/// <summary>
	/// <c>MfdDisplay_SetStatusRoster</c> (<c>00447294</c>): the cockpit's own machine first, then the
	/// squadmates <c>Cockpit_LoadSquadmatePilots</c> seated in the comm boxes, in box order. The roster
	/// is built once per mission and never pruned, so a destroyed squadmate keeps its entry.
	/// </summary>
	/// <param name="own">The machine being flown — <c>CockpitViewInstance+0x203</c>.</param>
	/// <param name="squadmates">The seated comm-box machines, slot 0 first.</param>
	public MfdStatusRoster(SimObject? own, IEnumerable<SimObject?> squadmates) {
		_entries = squadmates.Prepend(own).ToArray();
	}

	/// <summary>How many entries the roster holds: the squadmates plus the player's own machine.</summary>
	public int Count => _entries.Length;

	/// <summary>
	/// The entry the screen shows. It starts on the player's own machine, entry 0, because the cursor
	/// field is never written before the first step and the display is allocated zeroed.
	/// </summary>
	public int Cursor { get; private set; }

	/// <summary>The machine at <see cref="Cursor"/>.</summary>
	public SimObject? Subject => _entries[Cursor];

	/// <summary>
	/// <c>MfdDisplay_StepStatusSubject</c> (<c>00446f9c</c>) — SELECT on F1: one entry on, back to the
	/// player's own machine past the last squadmate.
	/// </summary>
	public void Step() {
		Cursor++;
		if (Cursor == Count) {
			Cursor = 0;
		}
	}
}
