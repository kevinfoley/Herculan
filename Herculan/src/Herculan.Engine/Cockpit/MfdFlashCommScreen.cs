using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Ai;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The MFD's FLASH COMM page (<c>MfdFlashCommScreen_Ctor</c>, <c>0043f5d8</c>) — six order rows, one
/// of them selected, and XMIT. Derivation: docs/retail/formats/mfd.md and
/// docs/retail/simulation/ai-squadmates.md.
///
/// <para><b>Six positions, not six orders.</b> Each row names one of two verbs from
/// <c>STRINGS0.STR</c> group 0: its own index, or that index <b>+ 3</b> when the row's state byte
/// (<c>screen+0x2c + row</c>) has bit 1 set. Only rows 4 and 5 ever toggle — <c>MfdFlashComm_ToggleRowVariant</c> (<c>0043f9d0</c>)
/// flips the bit after a transmission and returns immediately for any other row — so the page reads
/// <c>SCAN FOR HOSTILES</c>/<c>EMCON</c> and <c>FIRE AT WILL</c>/<c>HOLD YOUR FIRE</c> and the first
/// four rows are fixed.</para>
///
/// <para><b>Two selections, not one.</b> The row the page draws lives in the display's shared state
/// block (<c>display+0xb1</c>, <see cref="SelectedRow"/>); the row XMIT actually sends lives on the
/// screen (<c>screen+0x32</c>, <see cref="Row"/>). The paint copies the first into the second every
/// time FLASH COMM is repainted, which is what <see cref="Sync"/> is. They come apart for exactly one
/// input: an <c>[Alt]</c> hotkey pressed while another MFD screen is up transmits from the screen's
/// own row without the display's ever moving, because <c>MfdFlashComm_SelectRow</c> (<c>00447130</c>) refuses to write the shared
/// block outside mode 1.</para>
///
/// <para><b>Availability is dead in retail.</b> Bit 0 of a row's state byte greys it out and takes it
/// out of the <c>[,]</c>/<c>[.]</c> walk, but the two functions that set and clear it
/// (<c>FUN_0043fa14</c>, <c>FUN_0043f9f4</c>) have no callers in the image, so every row is always
/// available. It is modelled because the paint and the walk both read it.</para>
/// </summary>
/// <summary>
/// What the FLASH COMM page draws, snapshotted for the renderer: which row the cursor is on, and for
/// each row the verb it currently names and whether the squad can take it.
/// </summary>
/// <param name="SelectedRow">The display's own row — <c>display+0xb1</c>.</param>
/// <param name="Verbs">Each row's resolved <c>STRINGS0.STR</c> group 0 index.</param>
/// <param name="Available">Each row's availability bit, clear meaning the row draws in <c>CPOFF</c>.</param>
public readonly record struct MfdFlashCommState(
	int SelectedRow,
	IReadOnlyList<int> Verbs,
	IReadOnlyList<bool> Available) {

	/// <summary>The page as it looks before anything has been selected — row 0, all six available.</summary>
	public static MfdFlashCommState Default { get; } = new(0,
		Enumerable.Range(0, MfdLayout.FlashCommRowCount).ToArray(),
		Enumerable.Repeat(true, MfdLayout.FlashCommRowCount).ToArray());

	/// <summary>Row <paramref name="row"/>'s verb, or -1 when the snapshot does not cover it.</summary>
	public int Verb(int row) => Verbs is { } verbs && row >= 0 && row < verbs.Count ? verbs[row] : -1;

	/// <summary>Whether row <paramref name="row"/> can be taken. A row outside the snapshot is not.</summary>
	public bool CanTake(int row) => Available is { } available && row >= 0 && row < available.Count && available[row];
}

public sealed class MfdFlashCommScreen {
	/// <summary>How many rows the page lists — the constructor's own loop bound.</summary>
	public const int RowCount = MfdLayout.FlashCommRowCount;

	/// <summary>
	/// What a row's verb steps by when its state bit is set — <c>MfdFlashComm_SelectedVerb</c>
	/// (<c>0043f998</c>).
	/// </summary>
	public const int ToggledVerbStep = 3;

	/// <summary>Bit 0 of a row's state byte: the squad cannot take this order.</summary>
	private const byte UnavailableBit = 1;

	/// <summary>Bit 1: the row is showing its second verb.</summary>
	private const byte ToggledBit = 2;

	private readonly byte[] _state = new byte[RowCount];

	private int _row;

	/// <summary>
	/// The row the display's shared state block holds — what the page draws its plate and its
	/// <c>CPYLW</c> row on. <c>MfdDisplay_SetSharedRow</c> (<c>0044707c</c>) is the only writer.
	/// </summary>
	public int SelectedRow { get; private set; }

	/// <summary>
	/// The screen's own row, which is what <see cref="Transmit"/> resolves. Normally equal to
	/// <see cref="SelectedRow"/>; see the class remarks for the one input that separates them.
	/// </summary>
	public int Row => _row;

	/// <summary>Whether the squad can currently take row <paramref name="row"/>'s order.</summary>
	public bool Available(int row) =>
		row >= 0 && row < RowCount && (_state[row] & UnavailableBit) == 0;

	/// <summary>Whether row <paramref name="row"/> is showing its second verb.</summary>
	public bool Toggled(int row) =>
		row >= 0 && row < RowCount && (_state[row] & ToggledBit) != 0;

	/// <summary>
	/// The <c>STRINGS0.STR</c> group 0 index row <paramref name="row"/> currently names —
	/// <c>MfdFlashComm_SelectedVerb</c>'s arithmetic for an arbitrary row.
	/// </summary>
	public int VerbOf(int row) =>
		row < 0 || row >= RowCount ? 0 : row + (Toggled(row) ? ToggledVerbStep : 0);

	/// <summary>The verb <see cref="Transmit"/> would send — the screen's own row, resolved.</summary>
	public int SelectedVerb => VerbOf(_row);

	/// <summary>
	/// <c>MfdFlashCommScreen_Paint</c> (<c>0043f7a4</c>)'s first act: the paint copies the display's row onto the screen. Call at
	/// the top of any frame FLASH COMM is the current screen.
	/// </summary>
	public void Sync() => _row = SelectedRow;

	/// <summary>
	/// <c>FUN_0043f9b8</c> — moves the screen's own row. The display's row moves with it only when
	/// FLASH COMM is up, which is <c>MfdFlashComm_SelectRow</c>'s mode test.
	/// </summary>
	public void Select(int row, bool flashCommIsUp) {
		if (row < 0 || row >= RowCount) {
			return;
		}

		_row = row;
		if (flashCommIsUp) {
			SelectedRow = row;
		}
	}

	/// <summary>
	/// <c>FUN_00447014</c> and <c>FUN_00447048</c> — <c>[.]</c> and <c>[,]</c>, which walk the
	/// display's row forward and back past any row the squad cannot take. Both wrap, and both write
	/// the shared block directly rather than going through <see cref="Select"/>.
	///
	/// <para>The walk is a do-while with no guard against every row being unavailable; nothing in
	/// retail ever makes one so, and this stops after a full lap rather than spinning.</para>
	/// </summary>
	public void StepRow(int direction) {
		int row = SelectedRow;
		for (int i = 0; i < RowCount; i++) {
			row += direction < 0 ? -1 : 1;
			if (row == RowCount) {
				row = 0;
			} else if (row < 0) {
				row = RowCount - 1;
			}

			if (Available(row)) {
				break;
			}
		}

		SelectedRow = row;
		_row = row;
	}

	/// <summary>
	/// <c>MfdFlashComm_Transmit</c> (<c>00447220</c>) — resolves the screen's row to a verb, writes it
	/// into the shared order record and broadcasts it to the whole of the player's group. On
	/// acceptance, <c>MfdFlashComm_ToggleRowVariant</c> (<c>0043f9d0</c>) flips the row's second-verb bit, so a taken <c>SCAN FOR
	/// HOSTILES</c> leaves the row reading <c>EMCON</c>.
	/// </summary>
	/// <returns>Whether anyone took the order.</returns>
	public bool Transmit(SimWorld world, MissionGroup? group) {
		ArgumentNullException.ThrowIfNull(world);

		bool accepted = SquadOrders.Broadcast(world, group,
			new SquadOrderMessage((SquadCommand)SelectedVerb));

		// MfdFlashComm_ToggleRowVariant (0043f9d0)'s own bound: it returns immediately unless the row is 4 or 5.
		if (accepted && _row is >= ToggleableFirstRow and <= ToggleableLastRow) {
			_state[_row] ^= ToggledBit;
		}

		return accepted;
	}

	/// <summary>
	/// Which rows can show a second verb. <c>MfdFlashComm_ToggleRowVariant</c> (<c>0043f9d0</c>)'s guard is
	/// <c>row - 4 &lt;= 1</c> unsigned, so rows 4 and 5 and nothing else.
	/// </summary>
	public const int ToggleableFirstRow = 4;

	/// <inheritdoc cref="ToggleableFirstRow"/>
	public const int ToggleableLastRow = 5;

	/// <summary>What the renderer needs this frame.</summary>
	public MfdFlashCommState Snapshot() {
		var verbs = new int[RowCount];
		var available = new bool[RowCount];
		for (int i = 0; i < RowCount; i++) {
			verbs[i] = VerbOf(i);
			available[i] = Available(i);
		}

		return new MfdFlashCommState(SelectedRow, verbs, available);
	}

	/// <summary>
	/// The row a click at <paramref name="deviceX"/>, <paramref name="deviceY"/> — measured from the
	/// inset origin — lands on, or -1 for none. <c>MfdFlashComm_HandleListClick</c> hit-tests the six row rects
	/// inclusively on all four edges and does nothing outside them.
	/// </summary>
	public static int RowAt(float deviceX, float deviceY) {
		var rows = MfdLayout.FlashCommRows;
		if (deviceX < rows.X0 || deviceX > rows.X1) {
			return -1;
		}

		for (int i = 0; i < RowCount; i++) {
			float y0 = rows.Y0 + i * rows.RowHeight;
			if (deviceY >= y0 && deviceY <= y0 + rows.RowHeight) {
				return i;
			}
		}

		return -1;
	}
}
