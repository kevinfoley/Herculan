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
/// install for the simulator to read (docs/es1/formats/msn-mission-file.md#loading-a-mission). The shell's own loads,
/// <see cref="Es1ShellMission.StartSingleMission"/>'s, go through it with the shell's MISSION PARAMS words.
///
/// <para>The files are written as the shell writes them: <c>data\script.dat</c> through a stream that does not truncate,
/// so a shorter mission leaves the last one's tail (docs/es1/formats/script-dat.md#writing); <c>data\mission.var</c>
/// truncated; <c>data\player.mec</c> deleted first (docs/es1/formats/career-log-and-player-mec.md#the-player-file).</para>
/// </summary>
public static class Es1SingleMission {
	/// <summary>The shell's resources, from the archives its program mask mounts.</summary>
	public static GameContent MountShell(string installRoot) => GameContent.MountInstall(installRoot, disc: null, Voln.VshellProgram);

	/// <summary>
	/// Loads <paramref name="missionName"/> (with or without <c>.MSN</c>) from <c>msn\</c> and writes the three files.
	/// Returns null with the reason in <paramref name="error"/> when the mission or one of the shell's files is missing or
	/// unreadable.
	/// </summary>
	public static Es1MissionLoadResult? Start(string installRoot, GameContent shell, string missionName,
			Es1ShellRandom random, out string? error) {
		string prefsPath = Es1MissionFiles.DataPath(installRoot, "prefs.cfg");
		var missionParams = Es1MissionParams.FromPrefs(File.Exists(prefsPath) ? File.ReadAllBytes(prefsPath) : null);
		return Start(installRoot, shell, missionName, missionParams, random, out error);
	}

	/// <summary>
	/// <see cref="Start(string, GameContent, string, Es1ShellRandom, out string?)"/> with the MISSION PARAMS words
	/// <paramref name="missionParams"/>, as the shell holds them from its start-up (<see cref="Es1ShellData.MissionParams"/>).
	/// </summary>
	public static Es1MissionLoadResult? Start(string installRoot, GameContent shell, string missionName,
			Es1MissionParams missionParams, Es1ShellRandom random, out string? error) {
		string fileName = Path.HasExtension(missionName) ? missionName : missionName + ".MSN";
		if (shell.Read(Es1MissionFiles.MissionFolder, fileName) is not { } missionBytes
				|| new Es1MissionFileTransformer().Parse(missionBytes) is not Es1MissionFile mission) {
			error = $"No ES1 mission {Es1MissionFiles.MissionFolder}\\{fileName} in {installRoot}.";
			return null;
		}

		if (shell.Read("car", "SM_INIT.DAT") is not { } hangarBytes
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
			result = Es1MissionLoad.Load(mission, missionParams, hangar, tech, random.Below);
		} catch (Exception ex) when (ex is NotSupportedException or InvalidDataException) {
			error = $"Cannot load {fileName}: {ex.Message}";
			return null;
		}

		Write(installRoot, result);
		error = null;
		return result;
	}

	/// <summary>Writes the load's three files into the install's <c>data\</c>.</summary>
	public static void Write(string installRoot, Es1MissionLoadResult result) {
		Directory.CreateDirectory(CaseInsensitivePath.Combine(installRoot, Es1MissionFiles.DataFolder));

		// Step 7: mission.var through a FileWStream, which truncates; script.dat through the FileRWStream the load opened
		// before reading the rows, which does not.
		File.WriteAllBytes(Es1MissionFiles.DataPath(installRoot, Es1MissionFiles.MissionVarName), result.MissionVarBytes());
		using (var script = new FileStream(Es1MissionFiles.DataPath(installRoot, Es1MissionFiles.ScriptName), FileMode.OpenOrCreate,
				FileAccess.Write)) {
			script.Write(new Es1ScriptDatTransformer().Write(result.Script));
		}

		// Step 10: PlayerMec_Write deletes the file and opens it again.
		string playerPath = Es1MissionFiles.DataPath(installRoot, Es1MissionFiles.PlayerName);
		File.Delete(playerPath);
		File.WriteAllBytes(playerPath, new Es1PlayerMecTransformer().Write(result.Player)!);
	}
}
