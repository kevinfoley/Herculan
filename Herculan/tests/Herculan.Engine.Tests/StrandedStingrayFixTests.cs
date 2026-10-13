using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Settings;
using Herculan.Engine.Shell;
using Herculan.Engine.Sim;
using Herculan.Engine.World;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="StrandedStingrayFix"/> over the retail missions themselves, each loaded as the practice screen's DEBUG
/// dialog loads a typed mission and handed to the simulator: it finds <c>C2_05</c>'s Stingray in both releases, finds
/// nothing in another mission, and once bound walks the Stingray off its loop.
///
/// <para>Skips silently when an install is missing: v1.0 is the located install, v1.10 the <c>ES2v110\Install-ENG</c>
/// test install.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public sealed class StrandedStingrayFixTests : IDisposable {
	private readonly string _root = Path.Combine(Path.GetTempPath(), "herculan-tests", "stingray-" + Guid.NewGuid().ToString("N"));

	public void Dispose() {
		try {
			Directory.Delete(_root, recursive: true);
		} catch (IOException) {
		}
	}

	[Fact]
	public void FindsTheStingrayInV10() => AssertFound(GameInstall.Locate(null));

	[Fact]
	public void FindsTheStingrayInV110() => AssertFound(GameContentMountTests.FindTestInstall("Install-ENG"));

	/// <summary><c>C2_08</c> carries hunters of a named mech and groups whose later orders change route, and none is bound.</summary>
	[Fact]
	public void LeavesAnotherMissionAlone() {
		if (Load(GameInstall.Locate(null), "C2_08") is not { } scene) {
			return;
		}

		Assert.DoesNotContain(scene.World.Groups, group => group.ClosesInOn != null);
	}

	/// <summary>
	/// With the tweak on and action 140 fired, the Stingray walks at the prototype, which here waits at the rendezvous:
	/// a minute later it is well inside the 296,478 units that separate the rendezvous from the nearest point of its loop.
	/// </summary>
	[Fact]
	public void TheStingrayLeavesItsLoopOnceThePrototypeIsHome() {
		if (Load(GameInstall.Locate(null), "C2_05") is not { } scene) {
			return;
		}

		var world = scene.World;
		world.Tweaks = new TweakSettings();
		world.Tweaks.SetSettingValue(TweakSettingDefinitions.StrandedStingrayClosesIn, true);

		var hunter = Assert.Single(world.Groups, group => group.ClosesInOn != null);
		var stingray = Assert.IsType<MechObject>(hunter.Leader);
		var prototype = Assert.IsType<MechObject>(hunter.OrderTarget);

		prototype.Group!.DeploymentAction!.Activate(world);
		hunter.ClosesInOn!.Activate(world);

		for (int tick = 0; tick < 1500; tick++) {
			world.Tick();
		}

		Assert.False(prototype.AwaitingDeployment);
		int range = SimMath.FastMagnitude2D(prototype.Position.X - stingray.Position.X,
			prototype.Position.Y - stingray.Position.Y);
		Assert.True(range < 260000, $"the Stingray is still {range} units from the prototype");
	}

	private void AssertFound(string? install) {
		if (Load(install, "C2_05") is not { } scene) {
			return;
		}

		var hunter = Assert.Single(scene.World.Groups, group => group.ClosesInOn != null);
		Assert.Equal(MissionSide.Cybrid, hunter.Side);
		Assert.Same(hunter.OrderTarget, hunter.ClosesInOn!.TargetObject);
		Assert.Equal(MissionSide.Human, hunter.OrderTarget!.Side);
	}

	/// <summary>The mission as a training load of the typed path writes it, flown from the first practice row.</summary>
	private MissionScene? Load(string? install, string mission) {
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

		var handoff = ShellTrainingLaunch.Load(_root, shell, options, 0, $@"MSN\{mission}.MSN", false, Roll,
			new short[MissionGenerator.ClearListLength], game, hangar, out string? failure);
		Assert.True(handoff != null, failure);
		return MissionScene.Load(GameContent.MountSimulator(install), handoff.ScriptPath);
	}
}
