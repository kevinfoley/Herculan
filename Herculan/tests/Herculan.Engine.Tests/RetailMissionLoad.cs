using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>A retail mission loaded as the practice screen's DEBUG dialog loads a typed mission and handed to the simulator.</summary>
internal static class RetailMissionLoad {
	/// <summary>
	/// The mission as a training load of the typed path writes it into <paramref name="root"/>, flown from the first
	/// practice row; null when <paramref name="install"/> is missing.
	/// </summary>
	public static MissionScene? Load(string root, string? install, string mission) {
		if (install == null) {
			return null;
		}

		var shell = GameContent.MountShell(install);
		var options = SimulatorPreferences.Defaults();
		var random = new SimRandom();
		int Roll(short bound) => random.NextBelow(bound);

		if (ShellTrainingLaunch.NewCareer(shell, options, 0, Roll, null, out _) is not var (game, hangar)) {
			return null;
		}

		var handoff = ShellTrainingLaunch.Load(root, shell, options, 0, $@"MSN\{mission}.MSN", false, Roll,
			new short[MissionGenerator.ClearListLength], game, hangar, out string? failure);
		Assert.True(handoff != null, failure);
		return MissionScene.Load(GameContent.MountSimulator(install), handoff.ScriptPath);
	}
}
