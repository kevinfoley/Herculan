using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Vol;
using Herculan.Engine.Content;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Shell;

/// <summary>
/// A named mission started as the shell's SINGLE MISSION starts one: <see cref="Es1MissionLoad"/>, the mode-0 load, on
/// the mission, the MISSION PARAMS options of the install's <c>data\prefs.cfg</c>, <c>car\sm_init.dat</c> and
/// <c>bst\tech_sng.dat</c>, writing <c>data\script.dat</c>, <c>data\mission.var</c> and <c>data\player.mec</c> into the
/// install for the simulator to read (docs/es1/formats/msn-mission-file.md#loading-a-mission), and
/// <c>car\sm_play.mec</c> and <c>car\sm_play.gam</c> for the shell to read back after the mission
/// (docs/es1/formats/msn-mission-file.md#the-squad). The shell's own loads,
/// <see cref="Es1ShellMission.StartSingleMission"/>'s, go through it with the shell's MISSION PARAMS words.
///
/// <para>The files are written as the shell writes them: <c>data\script.dat</c> and <c>car\sm_play.mec</c> through a
/// stream that does not truncate, so a shorter file leaves the last one's tail (docs/es1/formats/script-dat.md#writing,
/// <c>Hangar_SaveFile</c>'s <c>FileRWStream</c>); <c>data\mission.var</c> and <c>car\sm_play.gam</c> truncated;
/// <c>data\player.mec</c> deleted first (docs/es1/formats/career-log-and-player-mec.md#the-player-file).</para>
/// </summary>
public static class Es1SingleMission {
	/// <summary>The folder of the shell's career files.</summary>
	public const string CarFolder = "car";

	/// <summary><c>car\sm_play.mec</c>, entry 10 of <c>cfg\filename.bin</c>: the hangar's slots 0 to 7.</summary>
	public const string SavedHangarName = "sm_play.mec";

	/// <summary><c>car\sm_play.gam</c>, entry 9: <c>SingleMission_Record</c>.</summary>
	public const string SavedRecordName = "sm_play.gam";

	/// <summary><paramref name="name"/> in the install's <c>car\</c>.</summary>
	public static string CarPath(string installRoot, string name) => CaseInsensitivePath.Combine(installRoot, CarFolder, name);

	/// <summary>The shell's resources, from the archives its program mask mounts.</summary>
	public static GameContent MountShell(string installRoot) => GameContent.MountInstall(installRoot, disc: null, Voln.VshellProgram);

	/// <summary>
	/// Loads <paramref name="missionName"/> (with or without <c>.MSN</c>) from <c>msn\</c> and writes the five files.
	/// Returns null with the reason in <paramref name="error"/> when the mission or one of the shell's files is missing or
	/// unreadable.
	/// </summary>
	public static Es1MissionLoadResult? Start(string installRoot, GameContent shell, string missionName,
			Es1ShellRandom random, out string? error) {
		string prefsPath = Es1MissionFiles.DataPath(installRoot, "prefs.cfg");
		var missionParams = Es1MissionParams.FromPrefs(File.Exists(prefsPath) ? File.ReadAllBytes(prefsPath) : null);
		return Start(installRoot, shell, missionName, missionParams, random, null, out error);
	}

	/// <summary>
	/// <see cref="Start(string, GameContent, string, Es1ShellRandom, out string?)"/> with the MISSION PARAMS words
	/// <paramref name="missionParams"/>, as the shell holds them from its start-up (<see cref="Es1ShellData.MissionParams"/>),
	/// and <paramref name="record"/>, <c>SingleMission_Record</c> as the shell holds it, which the load sets up in place
	/// (a new one when null).
	/// </summary>
	public static Es1MissionLoadResult? Start(string installRoot, GameContent shell, string missionName,
			Es1MissionParams missionParams, Es1ShellRandom random, Es1CareerRecord? record, out string? error) {
		string fileName = MissionFileName(missionName);
		if (ReadMission(installRoot, shell, missionName, out error) is not { } mission) {
			return null;
		}

		if (shell.Read(CarFolder, "SM_INIT.DAT") is not { } hangarBytes
				|| new Es1HercListTransformer().Parse(hangarBytes) is not Es1HercList hangar) {
			error = "car\\sm_init.dat is missing or unreadable.";
			return null;
		}

		if (shell.Read("bst", "TECH_SNG.DAT") is not { } techBytes
				|| new Es1TechFileTransformer().Parse(techBytes) is not Es1TechFile tech) {
			error = "bst\\tech_sng.dat is missing or unreadable.";
			return null;
		}

		Es1MissionLoadResult result;
		try {
			result = Es1MissionLoad.Load(mission, missionParams, hangar, tech, random.Below, record);
		} catch (Exception ex) when (ex is NotSupportedException or InvalidDataException) {
			error = $"Cannot load {fileName}: {ex.Message}";
			return null;
		}

		Write(installRoot, result);
		error = null;
		return result;
	}

	/// <summary><paramref name="missionName"/> with <c>.MSN</c> after it when it has no extension.</summary>
	private static string MissionFileName(string missionName) => Path.HasExtension(missionName) ? missionName : missionName + ".MSN";

	/// <summary>
	/// <c>MsnGen_LoadMission</c>'s step 1, which both modes share: <paramref name="missionName"/> (with or without
	/// <c>.MSN</c>) read from <c>msn\</c> and parsed; null with the reason in <paramref name="error"/> when it is missing or
	/// does not parse.
	/// </summary>
	public static Es1MissionFile? ReadMission(string installRoot, GameContent shell, string missionName, out string? error) {
		string fileName = MissionFileName(missionName);
		if (shell.Read(Es1MissionFiles.MissionFolder, fileName) is not { } missionBytes
				|| new Es1MissionFileTransformer().Parse(missionBytes) is not Es1MissionFile mission) {
			error = $"No ES1 mission {Es1MissionFiles.MissionFolder}\\{fileName} in {installRoot}.";
			return null;
		}

		error = null;
		return mission;
	}

	/// <summary>Writes the load's files into the install's <c>data\</c> and <c>car\</c>, in the load's order.</summary>
	public static void Write(string installRoot, Es1MissionLoadResult result) {
		WriteDataFiles(installRoot, result);

		// Step 9's step 5: Hangar_SaveFile (0002909c) through a FileRWStream. Retail asserts at severity 5 (mechman.cpp line
		// 0x132) when the file does not open; here the exception reaches the caller as the other files' do.
		Directory.CreateDirectory(CaseInsensitivePath.Combine(installRoot, CarFolder));
		if (result.SavedHangar is { } saved) {
			WriteOver(CarPath(installRoot, SavedHangarName), new Es1HercListTransformer().Write(saved)!);
		}

		// Step 10.
		WritePlayerMec(installRoot, result.Player, result.Record);
	}

	/// <summary>
	/// The load's step 7 in either mode: <c>data\mission.var</c> through a <c>FileWStream</c>, which truncates, and
	/// <c>data\script.dat</c> through the <c>FileRWStream</c> the load opened before reading the rows, which does not.
	/// </summary>
	public static void WriteDataFiles(string installRoot, Es1MissionLoadResult result) {
		Directory.CreateDirectory(CaseInsensitivePath.Combine(installRoot, Es1MissionFiles.DataFolder));
		File.WriteAllBytes(Es1MissionFiles.DataPath(installRoot, Es1MissionFiles.MissionVarName), result.MissionVarBytes());
		WriteOver(Es1MissionFiles.DataPath(installRoot, Es1MissionFiles.ScriptName), new Es1ScriptDatTransformer().Write(result.Script)!);
	}

	/// <summary>
	/// <c>PlayerMec_Write</c> (<c>0002299e</c>)'s files in <c>Shell_Mode</c> 0: <c>data\player.mec</c>
	/// (<see cref="WritePlayerFile"/>), then <c>CareerRecord_SaveCurrent</c> (<c>00014f28</c>)'s
	/// <see cref="SaveRecord"/>.
	/// </summary>
	public static void WritePlayerMec(string installRoot, Es1PlayerMec player, Es1CareerRecord record) {
		WritePlayerFile(installRoot, player);
		SaveRecord(installRoot, record);
	}

	/// <summary>
	/// <c>SingleMission_SaveRecord</c> (<c>00014f54</c>): <paramref name="record"/> written to <c>car\sm_play.gam</c> through
	/// a <c>FileWStream</c>. Retail asserts at <c>career.cpp</c> line <c>0x58</c> when the record's file does not open; here
	/// the exception reaches the caller.
	/// </summary>
	public static void SaveRecord(string installRoot, Es1CareerRecord record) {
		Directory.CreateDirectory(CaseInsensitivePath.Combine(installRoot, CarFolder));
		File.WriteAllBytes(CarPath(installRoot, SavedRecordName), new Es1CareerRecordTransformer().Write(record)!);
	}

	/// <summary>
	/// <c>PlayerMec_Write</c>'s own file in either mode: <c>data\player.mec</c> deleted and written again with
	/// <paramref name="player"/> (docs/es1/formats/career-log-and-player-mec.md#the-player-file).
	/// </summary>
	public static void WritePlayerFile(string installRoot, Es1PlayerMec player) {
		string playerPath = Es1MissionFiles.DataPath(installRoot, Es1MissionFiles.PlayerName);
		File.Delete(playerPath);
		File.WriteAllBytes(playerPath, new Es1PlayerMecTransformer().Write(player)!);
	}

	/// <summary>The hangar file ARMING's DONE writes in a single mission, <c>car\sm_init.dat</c> (<c>00080da8</c>).</summary>
	public const string InitHangarName = "sm_init.dat";

	/// <summary>
	/// <c>Hangar_SaveFile</c> (<c>0002909c</c>)(<c>car\</c><paramref name="name"/>) in <c>Shell_Mode</c> 0: <paramref name="hangar"/>
	/// as <c>Hangar_Write</c> writes it (<see cref="Es1Hangar.SingleMissionSave"/>) through a <c>FileRWStream</c>. Retail
	/// asserts at severity 5 (<c>mechman.cpp</c> line <c>0x132</c>) when the file does not open; here the exception reaches
	/// the caller.
	/// </summary>
	public static void SaveHangar(string installRoot, string name, Es1Hangar hangar) {
		Directory.CreateDirectory(CaseInsensitivePath.Combine(installRoot, CarFolder));
		WriteOver(CarPath(installRoot, name), new Es1HercListTransformer().Write(hangar.SingleMissionSave())!);
	}

	/// <summary><paramref name="bytes"/> written over <paramref name="path"/> from its start, its tail past them kept, as a <c>FileRWStream</c> writes.</summary>
	private static void WriteOver(string path, byte[] bytes) {
		using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
		stream.Write(bytes);
	}
}
