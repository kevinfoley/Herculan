using Herculan.Engine.Install;
using Herculan.Engine.Settings;
using Herculan.Engine.World;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="TweakSettingDefinitions.GuardHoldsUntilDestroyed"/> over <c>C5_10</c> itself, loaded as the practice
/// screen's DEBUG dialog loads a typed mission: a Cybrid ordered to guard the base and then sleep keeps its guard order
/// while the tweak is on, and moves on to the sleep as retail does once it is off.
///
/// <para>Skips silently when the v1.0 install is missing.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public sealed class GuardHoldsUntilDestroyedTests : IDisposable {
	private readonly string _root = Path.Combine(Path.GetTempPath(), "herculan-tests", "guard-" + Guid.NewGuid().ToString("N"));

	public void Dispose() {
		try {
			Directory.Delete(_root, recursive: true);
		} catch (IOException) {
		}
	}

	[Fact]
	public void TheBaseGuardsHoldTheirPostOnlyWithTheTweak() {
		if (RetailMissionLoad.Load(_root, GameInstall.Locate(null), "C5_10") is not { } scene) {
			return;
		}

		var world = scene.World;
		world.Tweaks = new TweakSettings();
		world.Tweaks.SetSettingValue(TweakSettingDefinitions.GuardHoldsUntilDestroyed, true);

		var guards = world.Groups.Where(group => group.Side == MissionSide.Cybrid
			&& group.Orders.Count > 1
			&& group.Orders[0] is { Verb: MissionOrder.VerbGuard, SubjectKind: MissionOrderSubject.Group }
			&& group.Orders[1] is { Verb: MissionOrder.VerbSleep }).ToList();
		Assert.Equal(6, guards.Count);

		var guard = guards[0];
		guard.DeploymentAction!.Activate(world);

		for (int tick = 0; tick < 10; tick++) {
			world.Tick();
		}

		Assert.False(guard.AwaitingDeployment);
		Assert.Equal(0, guard.OrderIndex);

		world.Tweaks.SetSettingValue(TweakSettingDefinitions.GuardHoldsUntilDestroyed, false);
		world.Tick();

		Assert.Equal(1, guard.OrderIndex);
	}
}
