using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Editor;

/// <summary>One retail mission as the install's archives ship it: its name without the extension (<c>C1_03</c>) and its <c>.MSN</c>.</summary>
internal sealed record ArchivedMission(string Name, MissionFile Mission);

/// <summary>
/// Every <c>MSN\*.MSN</c> the shell's archives hold, read as it shipped, not from a mission saved over one
/// (<see cref="GameContent.ReadArchived"/>), once per editor run, the first time something asks: what a Cybrid's hardpoints
/// offer (<see cref="CybridWeapons"/>) and which missions use each zone (<see cref="ZoneCatalog"/>). With none there, the list
/// is empty.
/// </summary>
internal sealed class ArchivedMissions {
	private readonly Func<GameContent> _shell;
	private IReadOnlyList<ArchivedMission>? _all;

	/// <param name="shell">VSHELL's mount of the install, which holds the missions.</param>
	public ArchivedMissions(Func<GameContent> shell) => _shell = shell;

	/// <summary>Every mission the archives hold, in the order the shell lists them.</summary>
	public IReadOnlyList<ArchivedMission> All => _all ??= Read();

	private List<ArchivedMission> Read() {
		var shell = _shell();
		var transformer = new MissionFileTransformer();
		return MissionFileLaunch.List(shell)
			.Select(name => shell.ReadArchived(MissionFileLaunch.MissionFolder, name) is { } bytes && transformer.Parse(bytes) is { } file
				? new ArchivedMission(Path.GetFileNameWithoutExtension(name).ToUpperInvariant(), file)
				: null)
			.OfType<ArchivedMission>()
			.ToList();
	}
}
