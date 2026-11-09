using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;
using Herculan.Es2.Content;

namespace Herculan.Es2.Shell;

/// <summary>
/// The career's missions as campaign and training both find and start loading them: the stage table in
/// <c>gam\career.dat</c>, a career position's mission through <c>missions.bin</c>, the <c>.MSN</c> and its text
/// out of the archives, and the campaign flags every mission load draws.
/// </summary>
internal static class ShellCareerMissions {
	/// <summary>
	/// Flag 3's source, <c>00482606</c>, which the startup memset from <c>MissionScreenView</c> clears.
	/// Whether anything writes it is open — see docs/retail/shell/campaign-loop.md#open.
	/// </summary>
	private const short UnknownFlag3 = 0;

	/// <summary>
	/// The half of <c>MsnGen_SeedCampaignFlags</c> (<c>0040e94e</c>) both modes run: flag 3 from
	/// <c>00482606</c>, then flags 4, 5 and 6 a draw below 12 each.
	/// </summary>
	internal static void SeedDrawnFlags(short[] flags, Func<short, int> roll) {
		flags[3] = UnknownFlag3;
		for (int flag = 4; flag <= 6; flag++) {
			flags[flag] = (short)roll(12);
		}
	}

	/// <summary>
	/// The career position's mission — <c>Career_LoadCurrentMission</c> (<c>0044d4cc</c>) reads the name
	/// <c>CareerDat_ReadStage</c> resolved through <c>missions.bin</c>, a path such as <c>MSN\TRAIN1.MSN</c>.
	/// Null for a position <c>gam\career.dat</c> does not have.
	/// </summary>
	internal static string? MissionPath(GameContent content, int stage, int mission) =>
		CareerStages(content) is { } stages && stage >= 0 && stage < stages.Count
			&& mission >= 0 && mission < stages[stage].Missions.Length
			? ShellText.Load(content, "MISSIONS.BIN")?.Text(stages[stage].Missions[mission])
			: null;

	/// <summary><c>gam\career.dat</c>'s stages, as <c>LoadCareerDat</c> (<c>00412906</c>) reads them, or null without one.</summary>
	internal static List<(short CampaignIndex, int[] Missions)>? CareerStages(GameContent content) =>
		content.Read(ShellRepairCosts.CatalogFolder, "CAREER.DAT") is { } bytes
			? new CareerDataTransformer().Parse(bytes)?.Stages
			: null;

	/// <summary>
	/// A mission and its text as <c>MsnGen_ParseMsnFile</c> opens them: the <c>.MSN</c> at
	/// <paramref name="missionPath"/>, and the text <c>Msn_LoadEngText</c> (<c>0041768c</c>) finds by
	/// swapping everything from the name's first <c>.</c> for the shell language's extension, or null text when
	/// there is none. Null when the mission itself is not in the archives.
	/// </summary>
	internal static (byte[] Msn, byte[]? Text)? ReadMission(GameContent content, string missionPath) {
		int split = missionPath.LastIndexOf('\\');
		string folder = split < 0 ? string.Empty : missionPath[..split];
		string name = missionPath[(split + 1)..];
		if (content.Read(folder, name) is not { } msn) {
			return null;
		}

		int dot = name.IndexOf('.');
		string stem = dot < 0 ? name : name[..dot];
		return (msn, content.Read(folder, stem + MissionTextExtension(content.Language)));
	}

	/// <summary><c>Msn_LoadEngText</c>'s extension for <paramref name="language"/>'s mission text.</summary>
	internal static string MissionTextExtension(GameLanguage language) => language switch {
		GameLanguage.French => ".FRE",
		GameLanguage.German => ".GER",
		_ => ".ENG",
	};
}
