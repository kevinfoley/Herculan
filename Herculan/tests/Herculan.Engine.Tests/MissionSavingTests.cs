using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Content;
using Herculan.Engine.Editor;
using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The mission editor's saving: the loose files a save writes into an install (<see cref="SavedMissions"/>), the export
/// of the handoff (<see cref="HandoffExport"/>), the browser's merged list and the unsaved-changes state
/// (<see cref="EditHistory"/>). Every install here is a temp folder.
/// </summary>
public sealed class MissionSavingTests : IDisposable {
	private readonly string _root = Path.Combine(Path.GetTempPath(), "herculan-tests", nameof(MissionSavingTests),
		Guid.NewGuid().ToString("N"));

	public MissionSavingTests() => Directory.CreateDirectory(_root);

	public void Dispose() => Directory.Delete(_root, recursive: true);

	/// <summary>
	/// Catches a save that writes outside the install's mission folder, that keeps no backup, or that copies the backup
	/// after writing, so the <c>.bak</c> holds the new file rather than the one it replaced.
	/// </summary>
	[Fact]
	public void SaveWritesIntoTheMissionFolderAndBacksUpWhatItReplaces() {
		SavedMissions.Write(_root, "C1_01", GameLanguage.English, [1, 1], [10], MissionAbout.Blank, null);
		var written = SavedMissions.Write(_root, "C1_01", GameLanguage.English, [2, 2], [20], MissionAbout.Blank, null);

		string folder = Path.Combine(_root, MissionFileLaunch.MissionFolder);
		Assert.Equal([Path.Combine(folder, "C1_01.MSN"), Path.Combine(folder, "C1_01.ENG")], written);
		Assert.Equal([2, 2], File.ReadAllBytes(Path.Combine(folder, "C1_01.MSN")));
		Assert.Equal([1, 1], File.ReadAllBytes(Path.Combine(folder, "C1_01.MSN.bak")));
		Assert.Equal([10], File.ReadAllBytes(Path.Combine(folder, "C1_01.ENG.bak")));
		Assert.True(SavedMissions.Exists(_root, "c1_01"));
	}

	/// <summary>Catches a text file always written as <c>.ENG</c>, which a French or German shell would not read.</summary>
	[Fact]
	public void TextFileTakesTheShellLanguagesExtension() {
		SavedMissions.Write(_root, "MINE", GameLanguage.French, [1], [2], MissionAbout.Blank, null);

		string folder = Path.Combine(_root, MissionFileLaunch.MissionFolder);
		Assert.True(File.Exists(Path.Combine(folder, "MINE.FRE")));
		Assert.False(File.Exists(Path.Combine(folder, "MINE.ENG")));
	}

	/// <summary>
	/// Catches a save that leaves out the mission's own zone or writes it under names the read does not look for, and one
	/// without a zone that leaves a replaced zone's files behind, or removes them with no backup.
	/// </summary>
	[Fact]
	public void SaveWritesTheOwnZoneAndASaveWithoutOneRemovesIt() {
		var zone = ZoneFiles.Create(Heights(128), 14, 149);
		SavedMissions.Write(_root, "MINE", GameLanguage.English, [1], null, MissionAbout.Blank, zone);

		var read = SavedMissions.ReadZone(_root, "mine", out string? failure);
		Assert.Null(failure);
		Assert.Equal(zone.Heights.ToArray(), read!.Heights.ToArray());
		Assert.Equal(zone.CellSizeShift, read.CellSizeShift);
		Assert.Equal(zone.HeightScale, read.HeightScale);

		SavedMissions.Write(_root, "MINE", GameLanguage.English, [1], null, MissionAbout.Blank, null);
		Assert.Null(SavedMissions.ReadZone(_root, "MINE", out failure));
		Assert.Null(failure);
		Assert.True(File.Exists(SavedMissions.ZoneHeaderPath(_root, "MINE") + SavedMissions.BackupExtension));
		Assert.True(File.Exists(SavedMissions.ZoneHeightmapPath(_root, "MINE") + SavedMissions.BackupExtension));
	}

	/// <summary>
	/// Catches half a zone read as no zone, which would open the mission without one and let its next save remove the
	/// half that is there, and a failure that does not name the missing file.
	/// </summary>
	[Fact]
	public void AZoneHeaderWithoutItsHeightmapIsAFailureNamingIt() {
		var zone = ZoneFiles.Create(Heights(128), 14, 149);
		SavedMissions.Write(_root, "MINE", GameLanguage.English, [1], null, MissionAbout.Blank, zone);
		File.Delete(SavedMissions.ZoneHeightmapPath(_root, "MINE"));

		Assert.Null(SavedMissions.ReadZone(_root, "MINE", out string? failure));
		Assert.StartsWith(Path.GetFileName(SavedMissions.ZoneHeightmapPath(_root, "MINE")), failure);
	}

	/// <summary>Catches deleting a loose mission that leaves its own zone's files, or their backups, in the mission folder.</summary>
	[Fact]
	public void DeletingALooseMissionDeletesItsOwnZone() {
		var zone = ZoneFiles.Create(Heights(128), 14, 149);
		SavedMissions.Write(_root, "MINE", GameLanguage.English, [1], null, MissionAbout.Blank, zone);
		SavedMissions.Write(_root, "MINE", GameLanguage.English, [1], null, MissionAbout.Blank, zone);
		var mission = new CustomMission("MINE", SavedMissions.MissionPath(_root, "MINE"), CustomMissionForm.Loose, null, null);

		var files = CustomMissions.Files(_root, mission);

		foreach (string path in new[] { SavedMissions.ZoneHeaderPath(_root, "MINE"), SavedMissions.ZoneHeightmapPath(_root, "MINE") }) {
			Assert.Contains(path, files);
			Assert.Contains(path + SavedMissions.BackupExtension, files);
		}
	}

	/// <summary>
	/// Catches a Save As name check loosened past a DOS 8.3 stem: a dot, which would write <c>A.MSN.MSN</c>, a space, a
	/// path separator, or a ninth character.
	/// </summary>
	[Fact]
	public void SaveAsNamesAreEightLettersDigitsOrUnderscores() {
		Assert.Null(SavedMissions.WhyNotName("C1_01"));
		Assert.Null(SavedMissions.WhyNotName("ABCDEFG8"));
		foreach (string name in new[] { "", "ABCDEFGHI", "A.MSN", "MY MSN", "..\\X", "É" }) {
			Assert.NotNull(SavedMissions.WhyNotName(name));
		}
	}

	/// <summary>
	/// Catches the browser listing a saved mission twice (beside the archive's copy it overrides, or once per case of its
	/// name), or not marking which lines are loose files.
	/// </summary>
	[Fact]
	public void BrowserListsEachMissionOnceMarkingSavedOnes() {
		var listed = MissionBrowser.Merge(["C1_01.MSN", "TRAIN1.MSN"], ["c1_01.msn", "MINE.MSN"]);

		Assert.Equal(["C1_01.MSN (saved)", "MINE.MSN", "TRAIN1.MSN"], listed.Select(mission => mission.Label));
	}

	/// <summary>
	/// Catches the editor host's mission argument matched only as the list spells it, so <c>c1_01</c> or a saved mission
	/// named without its <c>.MSN</c> would not open, or matched by prefix.
	/// </summary>
	[Fact]
	public void MissionArgumentFindsTheListedMissionByName() {
		var listed = MissionBrowser.Merge(["C1_01.MSN", "C1_010.MSN"], ["MINE.MSN"]);

		Assert.Equal("C1_01.MSN", MissionBrowser.Find(listed, "c1_01")?.File);
		Assert.Equal("MINE.MSN", MissionBrowser.Find(listed, "mine.msn")?.File);
		Assert.Null(MissionBrowser.Find(listed, "C1_0"));
	}

	/// <summary>
	/// Catches the unsaved mark tracking edits made rather than the state reached: an undo back to the save is clean, and
	/// one past it is not.
	/// </summary>
	[Fact]
	public void UndoingBackToTheSaveIsClean() {
		var history = new EditHistory();
		history.Add(new Step());
		history.MarkSaved();
		history.Add(new Step());
		Assert.True(history.Dirty);

		history.TakeUndo();
		Assert.False(history.Dirty);
		history.TakeUndo();
		Assert.True(history.Dirty);
		history.TakeRedo();
		Assert.False(history.Dirty);
	}

	/// <summary>
	/// Catches the saved state held as the undo depth alone: undoing past the save and making a new edit reaches the
	/// saved depth with a different mission.
	/// </summary>
	[Fact]
	public void AnEditInPlaceOfAnUndoneOneIsUnsaved() {
		var history = new EditHistory();
		history.Add(new Step());
		history.Add(new Step());
		history.MarkSaved();

		history.TakeUndo();
		history.Add(new Step());
		Assert.True(history.Dirty);
	}

	/// <summary>
	/// Catches an export that writes into the install's own <c>DATA\</c> when it is named in another case or with a
	/// trailing separator, and one that leaves out the <c>player.mec</c> the open wrote.
	/// </summary>
	[Fact]
	public void ExportWritesTheHandoffAnywhereButTheInstallsData() {
		string data = Path.Combine(_root, "DATA");
		string scratch = Path.Combine(_root, "scratch");
		Directory.CreateDirectory(data);
		Directory.CreateDirectory(scratch);
		File.WriteAllBytes(Path.Combine(scratch, "player.mec"), [7]);

		string dataAgain = Path.Combine(_root, "data") + Path.DirectorySeparatorChar;
		Assert.NotNull(HandoffExport.WhyNotFolder(dataAgain, _root, scratch));
		Assert.Throws<ArgumentException>(() => HandoffExport.Write(dataAgain, _root, scratch, [1], [2], null));
		Assert.Empty(Directory.GetFiles(data));

		string target = Path.Combine(_root, "export");
		HandoffExport.Write(target, _root, scratch, [1], [2], null);
		Assert.Equal([1], File.ReadAllBytes(Path.Combine(target, "script.dat")));
		Assert.Equal([2], File.ReadAllBytes(Path.Combine(target, "mission.str")));
		Assert.Equal([7], File.ReadAllBytes(Path.Combine(target, "player.mec")));
		Assert.False(File.Exists(Path.Combine(target, "mission.var")));
	}

	private static byte[] Heights(int side) {
		var heights = new byte[side * side];
		new Random(side).NextBytes(heights);
		return heights;
	}

	private sealed class Step : IEditorEdit {
		public void Undo() {
		}

		public void Redo() {
		}
	}
}
