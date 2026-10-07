using Herculan.Engine.Shell;
using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="ShellSaveSlots.SaveGame"/> — <c>Game_SaveSlot</c>'s files, written into a scratch
/// install: the directory, the save, and the three working files beside it.
/// </summary>
public class ShellSaveGameTests : IDisposable {
	private readonly string _root = Path.Combine(Path.GetTempPath(), "herculan-save-test-" + Guid.NewGuid().ToString("N"));

	public ShellSaveGameTests() => Directory.CreateDirectory(_root);

	public void Dispose() => Directory.Delete(_root, recursive: true);

	private string Sav => ShellSaveSlots.Directory(_root);

	private static readonly ShellWorkingFiles NoWorkingFiles = new(null, null, null);

	/// <summary>
	/// A player slot takes the typed label and is marked in use; every other slot's entry is written
	/// back as it was; and the save file is the game as the transformer writes it.
	/// </summary>
	[Fact]
	public void WritesAPlayerSlotUnderItsLabel() {
		var slots = Slots();
		var game = Game();

		var entry = ShellSaveSlots.SaveGame(_root, slots, 3, " 4. ALPHA", training: false, game, NoWorkingFiles,
			out string? failure);

		Assert.Null(failure);
		Assert.Equal(new ShellSaveSlot("GAME_3.SAV", " 4. ALPHA", true, null), entry);

		var directory = ReadDirectory();
		Assert.Equal(" 4. ALPHA", directory.Slots[3].Label);
		Assert.True(directory.Slots[3].InUse);
		for (int i = 0; i < slots.Count; i++) {
			Assert.Equal(slots[i].FileName, directory.Slots[i].FileName);
			if (i != 3) {
				Assert.Equal(slots[i].Label, directory.Slots[i].Label);
				Assert.Equal(slots[i].InUse, directory.Slots[i].InUse);
			}
		}

		Assert.Equal(new PlayerSaveTransform().Write(Game()), File.ReadAllBytes(Path.Combine(Sav, "GAME_3.SAV")));
	}

	/// <summary>The directory's leading length is the physical file less four, as <c>GameFileStr_Write</c> patches it.</summary>
	[Fact]
	public void PatchesTheDirectoryLengthToTheFile() {
		ShellSaveSlots.SaveGame(_root, Slots(), 0, " 1. A", false, Game(), NoWorkingFiles, out _);

		byte[] bytes = File.ReadAllBytes(Path.Combine(Sav, ShellSaveSlots.DirectoryFileName));
		Assert.Equal(bytes.Length - 4, BitConverter.ToInt32(bytes, 0));
	}

	/// <summary>The autosave keeps its own label whatever is passed, and is slot 11 in training.</summary>
	[Fact]
	public void WritesTheAutosaveToTheTrainingSlotInTraining() {
		var slots = Slots();

		var campaign = ShellSaveSlots.SaveGame(_root, slots, SaveSlotDirectory.ResumeSlot, "IGNORED", false, Game(),
			NoWorkingFiles, out _);
		Assert.Equal("GAME_R.SAV", campaign!.FileName);
		Assert.Equal("RESUME", campaign.Label);

		var training = ShellSaveSlots.SaveGame(_root, slots, SaveSlotDirectory.ResumeSlot, null, true, Game(),
			NoWorkingFiles, out _);
		Assert.Equal("GAME_T.SAV", training!.FileName);
		Assert.Equal("TRAINING", training.Label);
		Assert.True(File.Exists(Path.Combine(Sav, "GAME_T.SAV")));
		Assert.True(ReadDirectory().Slots[SaveSlotDirectory.TrainingSlot].InUse);
	}

	/// <summary>
	/// A stale tail the reader carried in from a file is cut back to the save's own 2022 bytes before
	/// the game is written.
	/// </summary>
	[Fact]
	public void DropsAStaleTailFromTheGame() {
		const int TailLength = PlayerSave.CampaignFlagCount * 2 + 2 + 20;
		var game = Game();
		game.CampaignStateTail = Enumerable.Range(0, TailLength + 100).Select(i => (byte)i).ToArray();

		ShellSaveSlots.SaveGame(_root, Slots(), 2, " 3. B", false, game, NoWorkingFiles, out _);

		Assert.Equal(TailLength, game.CampaignStateTail!.Length);
		var written = new PlayerSaveTransform().Parse(File.ReadAllBytes(Path.Combine(Sav, "GAME_2.SAV")));
		Assert.Equal(TailLength, written!.CampaignStateTail!.Length);
	}

	/// <summary>
	/// The save is written in place without truncating, as <c>FileRWStream_Open</c> opens it, so a longer
	/// file already there keeps its tail (docs/retail/formats/save-games.md#streams-never-truncate).
	/// </summary>
	[Fact]
	public void WritesTheSaveInPlaceWithoutTruncating() {
		Directory.CreateDirectory(Sav);
		string path = Path.Combine(Sav, "GAME_4.SAV");
		byte[] payload = new PlayerSaveTransform().Write(Game())!;
		byte[] old = Enumerable.Repeat((byte)0xee, payload.Length + 50).ToArray();
		File.WriteAllBytes(path, old);

		ShellSaveSlots.SaveGame(_root, Slots(), 4, " 5. C", false, Game(), NoWorkingFiles, out _);

		byte[] bytes = File.ReadAllBytes(path);
		Assert.Equal(old.Length, bytes.Length);
		Assert.Equal(payload, bytes[..payload.Length]);
		Assert.All(bytes[payload.Length..], b => Assert.Equal(0xee, b));
	}

	/// <summary>
	/// The three working files are copied beside the save under the slot's own names; one that is
	/// missing is skipped, and one that already is the slot's file is left alone.
	/// </summary>
	[Fact]
	public void CopiesTheWorkingFilesBesideTheSave() {
		Directory.CreateDirectory(Sav);
		string script = Path.Combine(_root, "script.dat");
		File.WriteAllBytes(script, new byte[] { 1, 2, 3 });
		string player = Path.Combine(Sav, ShellSaveSlots.PlayerFile(6));
		File.WriteAllBytes(player, new byte[] { 4, 5 });
		var working = new ShellWorkingFiles(script, Path.Combine(_root, "missing.str"), player);

		var entry = ShellSaveSlots.SaveGame(_root, Slots(), 6, " 7. D", false, Game(), working, out string? failure);

		Assert.NotNull(entry);
		Assert.Null(failure);
		Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Sav, "script6.dat")));
		Assert.False(File.Exists(Path.Combine(Sav, "missn6.str")));
		Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(player));
	}

	/// <summary>
	/// A slot's load copies its three files over the install's <c>data\</c> ones, as <c>Career_LoadSlot</c> does,
	/// leaving the one the slot lacks as it was, and names the <c>data\</c> files as the working files.
	/// </summary>
	[Fact]
	public void LoadCopiesTheSlotsWorkingFilesIntoData() {
		Directory.CreateDirectory(Sav);
		File.WriteAllBytes(Path.Combine(Sav, ShellSaveSlots.ScriptFile(3)), new byte[] { 1, 2 });
		File.WriteAllBytes(Path.Combine(Sav, ShellSaveSlots.PlayerFile(3)), new byte[] { 3 });
		var data = ShellWorkingFiles.InData(_root);
		Directory.CreateDirectory(ShellWorkingFiles.DataDirectory(_root));
		File.WriteAllBytes(data.Script!, new byte[] { 9, 9, 9, 9 });
		File.WriteAllBytes(data.Text!, new byte[] { 8 });

		var working = ShellSaveSlots.CopyWorkingFilesIn(_root, 3);

		Assert.Equal(data, working);
		Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(data.Script!));
		Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(data.Text!));
		Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(data.Player!));
	}

	/// <summary>
	/// <c>data\script.dat</c> and <c>data\mission.str</c> are written in place, so a shorter mission keeps the
	/// longer one's tail.
	/// </summary>
	[Fact]
	public void WritesAMissionFileWithoutTruncating() {
		string path = Path.Combine(_root, "script.dat");
		File.WriteAllBytes(path, new byte[] { 7, 7, 7, 7, 7 });

		ShellWorkingFiles.WriteMissionFile(path, new byte[] { 1, 2 });

		Assert.Equal(new byte[] { 1, 2, 7, 7, 7 }, File.ReadAllBytes(path));
	}

	/// <summary>A slot the directory does not list, or a file that cannot be opened, writes nothing and says why.</summary>
	[Fact]
	public void ReportsASlotItCannotWrite() {
		Assert.Null(ShellSaveSlots.SaveGame(_root, Slots(), 12, null, false, Game(), NoWorkingFiles, out string? missing));
		Assert.NotNull(missing);
		Assert.False(Directory.Exists(Sav));

		// A folder where the save should be makes the open fail.
		Directory.CreateDirectory(Path.Combine(Sav, "GAME_1.SAV"));
		Assert.Null(ShellSaveSlots.SaveGame(_root, Slots(), 1, " 2. E", false, Game(), NoWorkingFiles, out string? blocked));
		Assert.NotNull(blocked);
	}

	/// <summary>The retail directory's twelve slots, with only slot 0 in use.</summary>
	private static IReadOnlyList<ShellSaveSlot> Slots() {
		var slots = new List<ShellSaveSlot>();
		for (int i = 0; i < SaveSlotDirectory.PlayerSlotCount; i++) {
			slots.Add(new ShellSaveSlot($"GAME_{i}.SAV", i == 0 ? " 1. KEVIN" : $"{i + 1,2}. EMPTY", i == 0, null));
		}

		slots.Add(new ShellSaveSlot("GAME_R.SAV", "RESUME", false, null));
		slots.Add(new ShellSaveSlot("GAME_T.SAV", "TRAINING", false, null));
		return slots;
	}

	private static PlayerSave Game() =>
		Assert.IsType<PlayerSave>(new PlayerSaveTransform().Parse(PlayerSaveRoundTripTests.BuildSave()));

	private SaveSlotDirectory ReadDirectory() =>
		Assert.IsType<SaveSlotDirectory>(new SaveSlotDirectoryTransform().Parse(
			File.ReadAllBytes(Path.Combine(Sav, ShellSaveSlots.DirectoryFileName))));
}
