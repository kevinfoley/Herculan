using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;
using Herculan.Engine.World;

namespace Herculan.Engine.Host;

/// <summary>
/// A <c>.MSN</c> named as the mission argument, loaded the way VSHELL loads the career position that holds
/// it and written as a handoff for this host to fly. The original has no such path — a mission reaches the
/// simulator only through a career — so what stands in for the career here is this engine's:
///
/// <list type="bullet">
/// <item>A stage-0 mission (the practice missions and the demos) takes the training half whole,
/// <see cref="ShellTrainingLaunch"/>, as <c>Begin Mission</c> on its row would, or <c>INSTANT ACTION</c> for a
/// row past the practice list, with the practice options <c>data\prefs.cfg</c> holds.</item>
/// <item>Any other takes the campaign half, <see cref="ShellCampaignLaunch"/>, with a career that has no
/// history: an empty campaign flag array, which the load then seeds with the position, and the player's
/// lance and skill taken from the install's <c>DATA\player.mec</c> — the last handoff written — in
/// place of the export a career's hangar would make, and left there as the handoff's own.</item>
/// </list>
///
/// <para>The handoff is written into the install's <c>DATA\</c>, as the shell's is.</para>
/// </summary>
static class MissionFileLaunch {
	/// <summary>
	/// Whether the mission argument names a mission file rather than a <c>script.dat</c>: not a file that
	/// exists, and either a <c>.MSN</c> or a bare name with no extension.
	/// </summary>
	public static bool Names(string argument) =>
		!File.Exists(argument)
		&& (Path.GetExtension(argument).Equals(".msn", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(argument).Length == 0);

	/// <summary>Writes the handoff for <paramref name="name"/> and returns its <c>script.dat</c>, or null with the reason.</summary>
	public static string? Write(string installRoot, string name, out string? failure) {
		// The career table, the name tables and the catalogs are the shell's, and the missions are in ZONES.VOL,
		// which the shell mounts with them.
		var content = GameContent.MountShell(installRoot);
		if (ShellCampaignLaunch.Find(content, name) is not var (stage, mission)) {
			failure = $"{name} is not a mission gam\\career.dat lists.";
			return null;
		}

		string dataDirectory = ShellWorkingFiles.DataDirectory(installRoot);
		var random = ShellTrainingLaunch.StartupRandom();
		var clearList = new short[MissionGenerator.ClearListLength];

		if (stage == 0) {
			var options = SimulatorPreferences.Load(dataDirectory) ?? SimulatorPreferences.Defaults();
			options.SaveEnabled = false;
			var training = ShellTrainingLaunch.Write(dataDirectory, content, options, mission,
				instantAction: mission >= ShellPracticeScreen.RowCount, random, clearList, held: null, out failure);
			if (training != null) {
				Console.WriteLine($"{training.MissionPath}: stage 0 row {mission}, loaded as a training mission — "
					+ $"{training.SquadPositions} squad position(s), handoff written to {dataDirectory}.");
			}

			return training?.ScriptPath;
		}

		string playerPath = CaseInsensitivePath.Combine(dataDirectory, MissionLoader.PlayerFileName);
		if (!File.Exists(playerPath) || new MecFileTransformer().Parse(File.ReadAllBytes(playerPath)) is not MecFile { Entries: [var player, ..] }) {
			failure = $"A campaign mission flies the player's lance from {playerPath}, and there is no readable one.";
			return null;
		}

		var flags = new short[MissionGenerator.CampaignFlagCount];
		var campaign = ShellCampaignLaunch.Write(dataDirectory, content, stage, mission, player.Skill, flags, clearList,
			bound => random.NextBelow(bound), out failure);
		if (campaign == null) {
			return null;
		}

		var flagBytes = new byte[flags.Length * 2];
		Buffer.BlockCopy(flags, 0, flagBytes, 0, flagBytes.Length);
		File.WriteAllBytes(CaseInsensitivePath.Combine(dataDirectory, ShellMissionLaunch.MissionVarFileName), flagBytes);

		Console.WriteLine($"{campaign.MissionPath}: stage {stage} mission {mission}, loaded as a campaign mission at "
			+ $"skill {player.Skill} — {campaign.SquadPositions} squad position(s), the lance from {playerPath}, "
			+ $"handoff written to {dataDirectory}.");
		return campaign.ScriptPath;
	}
}
