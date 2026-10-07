namespace HercWorks.Core.Data.File.Sav;

/// <summary>One slot in <see cref="SaveSlotDirectory"/>: the file it names and how it is presented.</summary>
public class SaveSlotEntry {
	/// <summary>The save's filename, without a folder — <c>GAME_0.SAV</c> through <c>GAME_T.SAV</c>.</summary>
	public string FileName { get; set; } = string.Empty;

	/// <summary>
	/// What the save screen's slot row shows, carrying its own <c>"N. "</c> prefix — the numbering is
	/// stored, not generated. A slot the player has never written holds the prefix alone, which the
	/// reader completes; see <see cref="SaveSlotDirectory"/>.
	/// </summary>
	public string Label { get; set; } = string.Empty;

	/// <summary>
	/// Whether the slot holds a save. It gates the whole save screen: the detail panel is filled only
	/// for a slot with this set, RESTORE is enabled only for one, and the summary scan skips the rest.
	/// </summary>
	public bool InUse { get; set; }
}

/// <summary>
/// FILE - [root]/SAV/GAMEFILE.STR — the slot directory, and not itself a save: twelve filenames and
/// twelve display labels, read by <c>GameFileStr_Read</c> (<c>0040ddc8</c>) and written by <c>GameFileStr_Write</c> (<c>0040df4b</c>).
///
/// <para>Slots 0-9 are the player-named saves, slot 10 the campaign autosave and slot 11 the training
/// autosave; the last two are one slot from the caller's side, chosen between by the campaign/training
/// mode flag. Both counts are written as a literal 12, so the slot count is fixed in code and this
/// file cannot widen it.</para>
///
/// <para>See <c>docs/retail/formats/save-games.md</c> for the byte layout and for why the leading length
/// field measures the physical file rather than the payload.</para>
/// </summary>
public class SaveSlotDirectory {
	/// <summary>How many slots the file carries, a literal in the writer rather than a stored field.</summary>
	public const int SlotCount = 12;

	/// <summary>How many of those the player can name and write to from the save screen.</summary>
	public const int PlayerSlotCount = 10;

	/// <summary>The campaign autosave — <c>GAME_R.SAV</c>, written on returning to the main menu.</summary>
	public const int ResumeSlot = 10;

	/// <summary>The training autosave — <c>GAME_T.SAV</c>, the same slot seen in training mode.</summary>
	public const int TrainingSlot = 11;

	/// <summary>
	/// The leading <c>int32</c>, which is the physical file length less four: the writer takes it by
	/// seeking to the end <i>after</i> writing, so on a file that has been rewritten shorter it counts
	/// the stale tail too. The reader ignores it, and so should anything else — it is not a payload
	/// length. Kept so a round-trip can reproduce the file byte for byte.
	/// </summary>
	public int StoredLength { get; set; }

	/// <summary>The twelve slots, in index order.</summary>
	public List<SaveSlotEntry> Slots { get; set; } = new();
}
