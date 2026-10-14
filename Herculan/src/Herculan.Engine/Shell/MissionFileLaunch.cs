using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Shell;

/// <summary>
/// A <c>.MSN</c> named by file, loaded the way VSHELL loads the career position that holds it and written as a
/// handoff to fly or open. The original has no such path — a mission reaches the simulator only through a career —
/// so what stands in for the career here is this engine's:
///
/// <list type="bullet">
/// <item>A stage-0 mission (the practice missions and the demos) takes the training half whole,
/// <see cref="ShellTrainingLaunch"/>, as <c>Begin Mission</c> on its row would, or <c>INSTANT ACTION</c> for a
/// row past the practice list, with the practice options <c>data\prefs.cfg</c> holds.</item>
/// <item>A mission <c>gam\career.dat</c> does not list (<c>DEMO2.MSN</c>) takes the same half as
/// <c>INSTANT ACTION</c>, through the typed path the DEBUG dialog loads, so its player flies the mission's own
/// group-0 machine.</item>
/// <item>Any other takes the campaign half, <see cref="ShellCampaignLaunch"/>, with a career that has no
/// history: an empty campaign flag array, which the load then seeds with the position, and the player's
/// lance and skill taken from the install's <c>DATA\player.mec</c> — the last handoff written — in
/// place of the export a career's hangar would make.</item>
/// </list>
///
/// <para>The handoff is written into the directory the caller names: the install's <c>DATA\</c>, as the shell's
/// is, for the simulator host; a scratch directory for the mission editor, which opens a mission without
/// replacing the install's own handoff. <c>prefs.cfg</c> and <c>player.mec</c> are read from <c>DATA\</c>
/// either way, and a campaign mission's <c>player.mec</c> is copied beside a handoff written anywhere else.</para>
/// </summary>
public static class MissionFileLaunch {
	/// <summary>The archive folder the missions are in, as <c>missions.bin</c>'s paths name it.</summary>
	public const string MissionFolder = "MSN";

	/// <summary>The mission file extension.</summary>
	public const string MissionExtension = ".MSN";

	/// <summary>
	/// Whether the mission argument names a mission file rather than a <c>script.dat</c>: not a file that
	/// exists, and either a <c>.MSN</c> or a bare name with no extension.
	/// </summary>
	public static bool Names(string argument) =>
		!File.Exists(argument)
		&& (Path.GetExtension(argument).Equals(MissionExtension, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(argument).Length == 0);

	/// <summary>Every <c>.MSN</c> the shell's archives hold, by file name.</summary>
	public static IReadOnlyList<string> List(GameContent shellContent) =>
		shellContent.ListFolder(MissionFolder)
			.Where(name => Path.GetExtension(name).Equals(MissionExtension, StringComparison.OrdinalIgnoreCase))
			.ToList();

	/// <summary>
	/// Writes the handoff for <paramref name="name"/> into the install's <c>DATA\</c> and returns its
	/// <c>script.dat</c>, or null with the reason.
	/// </summary>
	public static string? Write(string installRoot, string name, out string? failure) =>
		// The career table, the name tables and the catalogs are the shell's, and the missions are in ZONES.VOL,
		// which the shell mounts with them.
		Write(GameContent.MountShell(installRoot), installRoot, name, ShellWorkingFiles.DataDirectory(installRoot), out failure);

	/// <summary>
	/// Writes the handoff for <paramref name="name"/> into <paramref name="directory"/> and returns its
	/// <c>script.dat</c>, or null with the reason. <paramref name="shellContent"/> is VSHELL's mount of the install.
	/// </summary>
	public static string? Write(GameContent shellContent, string installRoot, string name, string directory, out string? failure) {
		string installData = ShellWorkingFiles.DataDirectory(installRoot);
		var random = ShellTrainingLaunch.StartupRandom();
		var clearList = new short[MissionGenerator.ClearListLength];

		if (ShellCampaignLaunch.Find(shellContent, name) is not var (stage, mission)) {
			return WriteUnlisted(shellContent, installData, name, directory, random, clearList, out failure);
		}

		if (stage == 0) {
			var options = PracticeOptions(installData);
			var training = ShellTrainingLaunch.Write(directory, shellContent, options, mission,
				instantAction: mission >= ShellPracticeScreen.RowCount, random, clearList, held: null, out failure);
			if (training != null) {
				Console.WriteLine($"{training.MissionPath}: stage 0 row {mission}, loaded as a training mission — "
					+ $"{training.SquadPositions} squad position(s), handoff written to {directory}.");
			}

			return training?.ScriptPath;
		}

		string playerPath = CaseInsensitivePath.Combine(installData, MissionLoader.PlayerFileName);
		byte[]? playerBytes = File.Exists(playerPath) ? File.ReadAllBytes(playerPath) : null;
		if (playerBytes == null || new MecFileTransformer().Parse(playerBytes) is not MecFile { Entries: [var player, ..] }) {
			failure = $"A campaign mission flies the player's lance from {playerPath}, and there is no readable one.";
			return null;
		}

		var flags = new short[MissionGenerator.CampaignFlagCount];
		var campaign = ShellCampaignLaunch.Write(directory, shellContent, stage, mission, player.Skill, flags, clearList,
			bound => random.NextBelow(bound), out failure);
		if (campaign == null) {
			return null;
		}

		var flagBytes = new byte[flags.Length * 2];
		Buffer.BlockCopy(flags, 0, flagBytes, 0, flagBytes.Length);
		File.WriteAllBytes(CaseInsensitivePath.Combine(directory, ShellMissionLaunch.MissionVarFileName), flagBytes);

		string handoffPlayer = CaseInsensitivePath.Combine(directory, MissionLoader.PlayerFileName);
		if (!string.Equals(Path.GetFullPath(handoffPlayer), Path.GetFullPath(playerPath), StringComparison.OrdinalIgnoreCase)) {
			File.WriteAllBytes(handoffPlayer, playerBytes);
		}

		Console.WriteLine($"{campaign.MissionPath}: stage {stage} mission {mission}, loaded as a campaign mission at "
			+ $"skill {player.Skill} — {campaign.SquadPositions} squad position(s), the lance from {playerPath}, "
			+ $"handoff written to {directory}.");
		return campaign.ScriptPath;
	}

	/// <summary>
	/// A mission <c>gam\career.dat</c> does not list, loaded as <c>INSTANT ACTION</c> would load it on the first row
	/// past the practice list, with its typed path in place of the row's.
	/// </summary>
	private static string? WriteUnlisted(GameContent shellContent, string installData, string name, string directory,
			SimRandom random, short[] clearList, out string? failure) {
		string file = Path.GetFileName(name);
		if (Path.GetExtension(file).Length == 0) {
			file += MissionExtension;
		}

		string missionPath = $"{MissionFolder}\\{file}";
		if (!shellContent.Contains(MissionFolder, file)) {
			failure = $"{name} is not a mission in any mounted archive.";
			return null;
		}

		int Roll(short bound) => random.NextBelow(bound);
		var options = PracticeOptions(installData);
		const int row = ShellPracticeScreen.RowCount;
		if (ShellTrainingLaunch.NewCareer(shellContent, options, row, Roll, held: null, out failure) is not var (game, hangar)) {
			return null;
		}

		var training = ShellTrainingLaunch.Load(directory, shellContent, options, row, missionPath, instantAction: true, Roll,
			clearList, game, hangar, out failure);
		if (training != null) {
			Console.WriteLine($"{missionPath}: not in gam\\career.dat, loaded as an INSTANT ACTION mission — "
				+ $"{training.SquadPositions} squad position(s), handoff written to {directory}.");
		}

		return training?.ScriptPath;
	}

	/// <summary>The practice options <c>data\prefs.cfg</c> holds, with saving off.</summary>
	private static SimulatorPreferences PracticeOptions(string installData) {
		var options = SimulatorPreferences.Load(installData) ?? SimulatorPreferences.Defaults();
		options.SaveEnabled = false;
		return options;
	}
}
