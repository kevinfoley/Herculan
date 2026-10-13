using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct.Vshell.Sav;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.World;

namespace Herculan.Engine.Shell;

/// <summary>
/// What the save screen's detail panel prints about one slot — the 67-byte (<c>0x43</c>) staging
/// record VSHELL keeps eleven of, at <c>0048cddc</c>, and fills by opening each save in turn and
/// skipping to the fields it wants (<c>stats.cpp</c>, <c>00430378</c>).
///
/// <para><b>The record is a pilot record plus three fields.</b> Its first 59 bytes are laid out
/// exactly as <see cref="PilotEntry"/>, because the writer (<c>Stats_StageCurrentGame</c>, <c>0043084c</c>) copies them field for
/// field out of the player's own record; the salvage pool, the sector and the mission counter follow
/// at <c>+0x3b</c>, <c>+0x3f</c> and <c>+0x41</c>. That is why the panel can show a pilot's name, skill
/// and rank beside a career position without reading anything else.</para>
///
/// <para>Built here from a parsed <see cref="PlayerSave"/> rather than by reproducing the original's
/// skip-walk: the walk exists because VSHELL wants eleven summaries without eleven full loads, and
/// this engine already has a whole-file reader. The walk is still worth knowing — its skip counts are
/// an independent statement of every block length in <c>docs/retail/formats/save-games.md</c>.</para>
/// </summary>
public sealed record ShellSaveSummary(
	string PilotName,
	int Skill,
	int Rank,
	int HercKills,
	int FlyerKills,
	int BaseKills,
	int TotalHercKills,
	int TotalFlyerKills,
	int TotalBaseKills,
	int SalvageKilograms,
	int Sector,
	int Mission) {

	/// <summary>
	/// The summary of an already-parsed save. The sector and the mission come from the career block's
	/// first two shorts, which is where the original's scanner reads them too — it takes those two and
	/// then skips the remaining 148 bytes of the block.
	/// </summary>
	public static ShellSaveSummary? From(PlayerSave? save) {
		if (save?.PlayerPilot is not { } pilot) {
			return null;
		}

		short[] career = save.CareerBlock;
		return new ShellSaveSummary(
			pilot.Name ?? string.Empty,
			pilot.Skill?.Id ?? 0,
			pilot.Rank?.Id ?? 0,
			pilot.HercKills, pilot.FlyerKills, pilot.BaseKills,
			pilot.TotalHercKills, pilot.TotalFlyerKills, pilot.TotalBaseKills,
			save.SalvageTotal,
			career.Length > 0 ? career[0] : 0,
			career.Length > 1 ? career[1] : 0);
	}
}

/// <summary>
/// One row of the save screen's slot list: what <c>sav\GAMEFILE.STR</c> says about the slot, plus the
/// summary read out of the save itself when there is one.
/// </summary>
public sealed record ShellSaveSlot(string FileName, string Label, bool InUse, ShellSaveSummary? Summary);

/// <summary>
/// Where the game's three working files are — <c>data\script.dat</c>, <c>data\mission.str</c> and
/// <c>data\player.mec</c> in the original, which <c>Career_LoadSlot</c> fills from a slot's
/// <c>sav\script%d.dat</c>, <c>sav\missn%d.str</c> and <c>sav\player%d.mec</c>, <c>Rock &amp; Roll</c>'s
/// export rewrites <c>player.mec</c> in, and <c>Career_SaveSlot</c> copies out to the slot being saved.
/// Null when there is none.
/// </summary>
public sealed record ShellWorkingFiles(string? Script, string? Text, string? Player) {
	/// <summary>
	/// A slot's own three files in <c>sav\</c>, read where they are: the slot the shell opens at startup for the
	/// repair screen, which the original has no load for, so nothing is copied into <c>data\</c>.
	/// </summary>
	public static ShellWorkingFiles ForSlot(string installRoot, int slot) {
		string folder = ShellSaveSlots.Directory(installRoot);
		return new ShellWorkingFiles(CaseInsensitivePath.Combine(folder, ShellSaveSlots.ScriptFile(slot)),
			CaseInsensitivePath.Combine(folder, ShellSaveSlots.TextFile(slot)), CaseInsensitivePath.Combine(folder, ShellSaveSlots.PlayerFile(slot)));
	}

	/// <summary>The three files in the install's <c>data\</c>, where the original keeps them.</summary>
	public static ShellWorkingFiles InData(string installRoot) => In(DataDirectory(installRoot));

	/// <summary>The install's <c>data\</c>, which holds the working files and the mission handoff.</summary>
	public static string DataDirectory(string installRoot) => CaseInsensitivePath.Combine(installRoot, MissionLoader.DataFolderName);

	/// <summary>The three files under their <c>data\</c> names in <paramref name="directory"/>.</summary>
	public static ShellWorkingFiles In(string directory) =>
		new(CaseInsensitivePath.Combine(directory, MissionLoader.ScriptFileName), CaseInsensitivePath.Combine(directory, MissionLoader.TextFileName),
			CaseInsensitivePath.Combine(directory, MissionLoader.PlayerFileName));

	/// <summary>
	/// <c>data\script.dat</c> or <c>data\mission.str</c> written as <c>WriteScriptDatFile</c> and <c>MissionStr_Write</c>
	/// (<c>004179f0</c>) write them, through <c>FileRWStream_Open</c> (<c>0044e46c</c>), which does not truncate: a
	/// shorter mission leaves the longer one's tail in place (docs/retail/formats/save-games.md#streams-never-truncate).
	/// </summary>
	public static void WriteMissionFile(string path, byte[] bytes) => ShellSaveSlots.WriteInPlace(path, bytes);
}

/// <summary>
/// The twelve save slots, as the save screen sees them: the directory file, each slot's summary, and
/// the completion the directory's reader applies to a slot nobody has written yet.
///
/// <para>The screen shows ten of the twelve. Slots 10 and 11 are the campaign and training autosaves
/// and are one slot from the caller's side, reached by the resume path rather than by a row — which is
/// why the slot loop in every save-screen routine bounds at 10 while the summary scan bounds at
/// 11.</para>
/// </summary>
public static class ShellSaveSlots {
	/// <summary>The save subfolder inside an install root, beside <c>VOL</c>.</summary>
	public const string FolderName = "SAV";

	/// <summary>The slot directory's filename.</summary>
	public const string DirectoryFileName = "GAMEFILE.STR";

	/// <summary>
	/// <c>estext.bin</c> entry <c>0x21</c>, <c>EMPTY</c>: what the directory's reader appends to a label
	/// whose fifth character is NUL. A stored <c>" 8. "</c> becomes <c>" 8. EMPTY"</c>, which is why
	/// every retail label already reads that way — once such a slot is written back, the completed label
	/// is in the file. See <see cref="Complete"/>.
	/// </summary>
	public const int EmptyLabelText = 0x21;

	/// <summary>How many characters of a label are the stored <c>"N. "</c> prefix.</summary>
	private const int LabelPrefixLength = 4;

	/// <summary>The save folder inside an install root.</summary>
	public static string Directory(string installRoot) => CaseInsensitivePath.Combine(installRoot, FolderName);

	/// <summary>
	/// Reads the slot directory and every save it says is in use. Returns an empty list when there is no
	/// <c>GAMEFILE.STR</c> to read — the screen then draws its furniture and no rows, which is the same
	/// thing it does for a missing string table.
	/// </summary>
	public static IReadOnlyList<ShellSaveSlot> Load(string installRoot, ShellText? text = null) {
		string folder = Directory(installRoot);
		string path = CaseInsensitivePath.Combine(folder, DirectoryFileName);
		if (!File.Exists(path)
			|| new SaveSlotDirectoryTransform().Parse(ReadOrNull(path)) is not { } directory) {
			return Array.Empty<ShellSaveSlot>();
		}

		string? emptyWord = text?.Text(EmptyLabelText);
		var slots = new List<ShellSaveSlot>(directory.Slots.Count);
		foreach (var entry in directory.Slots) {
			slots.Add(new ShellSaveSlot(entry.FileName, Complete(entry.Label, emptyWord), entry.InUse,
				entry.InUse ? ReadSummary(folder, entry.FileName) : null));
		}

		return slots;
	}

	/// <summary>
	/// The reader's label completion: a label whose fifth character is missing is a slot that has never
	/// been written, and the word for that comes from the string table rather than from the file. Done
	/// here rather than in the transformer because the toolkit has no <c>estext.bin</c> to reach.
	/// </summary>
	public static string Complete(string label, string? emptyWord) =>
		label.Length > LabelPrefixLength ? label : label + (emptyWord ?? string.Empty);

	/// <summary>
	/// <c>Game_SaveSlot(slot, label)</c> (<c>0040e37b</c>): writes <paramref name="game"/> as slot
	/// <paramref name="slot"/> and returns the slot's new directory entry, or null when nothing was
	/// written. Slot 10 is slot 11 in training. A player slot (0-9) takes <paramref name="label"/> as its
	/// label; every slot is marked in use, and the whole directory is written back
	/// (<c>Game_SetSlotInUse</c> (<c>0040e115</c>)). The save follows, and <c>Career_SaveSlot</c> (<c>00412a71</c>) copies the
	/// three working files beside it. See docs/retail/formats/save-games.md. A missing working file is skipped,
	/// where the original's copy asserts.
	///
	/// <para>The summary is not restaged here: only <c>ACCEPT</c> does that, through
	/// <c>Stats_StageCurrentGame</c>.</para>
	/// </summary>
	public static ShellSaveSlot? SaveGame(string installRoot, IReadOnlyList<ShellSaveSlot> slots, int slot, string? label,
			bool training, PlayerSave game, ShellWorkingFiles working, out string? failure) {
		if (slot == SaveSlotDirectory.ResumeSlot && training) {
			slot = SaveSlotDirectory.TrainingSlot;
		}

		if (slot < 0 || slot >= slots.Count) {
			failure = $"no slot {slot} in {DirectoryFileName}";
			return null;
		}

		var entry = slots[slot] with {
			Label = slot < SaveSlotDirectory.PlayerSlotCount && label != null ? label : slots[slot].Label,
			InUse = true,
		};

		// The tail is the flag array, the game state and the 20-byte block, 2022 bytes; anything past that
		// is a stale tail the reader carried in from the file, which the original's memory never holds.
		if (game.CampaignStateTail is { Length: > SaveTailLength } tail) {
			game.CampaignStateTail = tail[..SaveTailLength];
		}

		string folder = Directory(installRoot);
		try {
			System.IO.Directory.CreateDirectory(folder);
			var directory = new SaveSlotDirectory {
				Slots = slots.Select((s, i) => new SaveSlotEntry {
					FileName = s.FileName,
					Label = i == slot ? entry.Label : s.Label,
					InUse = i == slot || s.InUse,
				}).ToList(),
			};

			WriteDirectory(CaseInsensitivePath.Combine(folder, DirectoryFileName), new SaveSlotDirectoryTransform().Write(directory)!);
			WriteInPlace(CaseInsensitivePath.Combine(folder, entry.FileName), new PlayerSaveTransform().Write(game)!);
			foreach (var (from, to) in new[] {
					(working.Script, ScriptFile(slot)), (working.Text, TextFile(slot)), (working.Player, PlayerFile(slot)) }) {
				string target = CaseInsensitivePath.Combine(folder, to);
				if (from != null && File.Exists(from) && !string.Equals(Path.GetFullPath(from), Path.GetFullPath(target),
						StringComparison.OrdinalIgnoreCase)) {
					File.Copy(from, target, overwrite: true);
				}
			}
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			failure = e.Message;
			return null;
		}

		failure = null;
		return entry;
	}

	/// <summary>The three working files <c>Career_SaveSlot</c> and <c>Career_LoadSlot</c> copy, for one slot.</summary>
	public static string ScriptFile(int slot) => $"script{slot}.dat";

	public static string TextFile(int slot) => $"missn{slot}.str";

	public static string PlayerFile(int slot) => $"player{slot}.mec";

	/// <summary>
	/// <c>Career_LoadSlot</c> (<c>00412bbf</c>)'s copies: the slot's <c>sav\script%d.dat</c>, <c>sav\missn%d.str</c>
	/// and <c>sav\player%d.mec</c> copied whole over <c>data\script.dat</c>, <c>data\mission.str</c> and
	/// <c>data\player.mec</c>. Returns the <c>data\</c> files. A missing one is skipped, where the original's copy
	/// asserts.
	/// </summary>
	public static ShellWorkingFiles CopyWorkingFilesIn(string installRoot, int slot) {
		var slotFiles = ShellWorkingFiles.ForSlot(installRoot, slot);
		var data = ShellWorkingFiles.InData(installRoot);
		System.IO.Directory.CreateDirectory(ShellWorkingFiles.DataDirectory(installRoot));
		foreach (var (from, to) in new[] { (slotFiles.Script, data.Script), (slotFiles.Text, data.Text), (slotFiles.Player, data.Player) }) {
			if (File.Exists(from)) {
				File.Copy(from, to!, overwrite: true);
			}
		}

		return data;
	}

	/// <summary>The save's last three blocks: 2000 bytes of flags, 2 of game state and 20 more.</summary>
	private const int SaveTailLength = PlayerSave.CampaignFlagCount * 2 + 2 + 20;

	/// <summary>
	/// Writes over whatever is at <paramref name="path"/> without truncating it, as
	/// <c>FileRWStream_Open</c> (<c>0044e46c</c>) opens every save: a shorter payload leaves the old tail
	/// in place (docs/retail/formats/save-games.md#streams-never-truncate).
	/// </summary>
	internal static void WriteInPlace(string path, byte[] bytes) {
		using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
		stream.Write(bytes);
	}

	/// <summary>
	/// <c>GameFileStr_Write</c> (<c>0040df4b</c>): the directory written in place, then its leading length
	/// patched to the physical file's length less four — the stale tail included.
	/// </summary>
	private static void WriteDirectory(string path, byte[] bytes) {
		using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
		stream.Write(bytes);
		int length = (int)stream.Length - 4;
		stream.Position = 0;
		stream.Write(BitConverter.GetBytes(length));
	}

	/// <summary>
	/// Parses one slot's save in full, or returns null when it is missing or will not read. The save
	/// screen only wants the staging record, but every other tab works over the whole thing — the
	/// hangar bays, the armory stock, the squad — so this is what a screen with more than a summary to
	/// draw asks for.
	/// </summary>
	public static PlayerSave? LoadSave(string installRoot, string fileName) =>
		ReadSave(Directory(installRoot), fileName);

	private static ShellSaveSummary? ReadSummary(string folder, string fileName) =>
		ShellSaveSummary.From(ReadSave(folder, fileName));

	private static PlayerSave? ReadSave(string folder, string fileName) {
		string path = CaseInsensitivePath.Combine(folder, fileName);
		if (!File.Exists(path)) {
			return null;
		}

		// A save that will not parse leaves the slot listed with no summary rather than dropping the row:
		// the row's label comes from the directory, which is a separate file and may well still be good.
		try {
			return new PlayerSaveTransform().Parse(ReadOrNull(path));
		} catch (Exception) {
			return null;
		}
	}

	private static byte[]? ReadOrNull(string path) {
		try {
			return File.ReadAllBytes(path);
		} catch (IOException) {
			return null;
		} catch (UnauthorizedAccessException) {
			return null;
		}
	}
}
