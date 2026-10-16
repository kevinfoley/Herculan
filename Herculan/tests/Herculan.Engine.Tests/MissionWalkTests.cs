using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.Shell;
using Herculan.Engine.Sim;
using Herculan.Engine.World;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Walks the player's HERC around a real mission — the whole chain at once: <c>script.dat</c> and
/// <c>player.mec</c> to a scene, the zone's own heightmap under the machine's feet, and the control
/// law driving it over real terrain rather than the flat plate
/// <see cref="MechLocomotionTests"/> uses.
///
/// <para>Skips silently when no Earthsiege 2 install or mission can be found.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public sealed class MissionWalkTests : IDisposable {
	private readonly string _root = Path.Combine(Path.GetTempPath(), "herculan-tests", "walk-" + Guid.NewGuid().ToString("N"));

	public void Dispose() {
		try {
			Directory.Delete(_root, recursive: true);
		} catch (IOException) {
		}
	}

	/// <summary>
	/// <c>TRAIN1</c>, Basic Training 1, launched from the practice screen's first row into a temp folder, so the walk
	/// is the same every run whatever the install's own <c>script.dat</c> holds. Below row 4 the player flies the
	/// mission's own machine (docs/retail/shell/main-menu.md#selecting-a-mission).
	/// </summary>
	[Fact]
	public void PlayerMechWalksAcrossRealTerrain() {
		if (LoadTraining(row: 0) is not { } scene) {
			return;
		}

		var mech = scene.PlayerMech;
		Assert.True(mech is { Thread: not null }, "TRAIN1 has no player HERC with a thread");

		var start = mech.Position;
		var full = new MechControls(0, -MechControls.AxisFull);

		for (int i = 0; i < 500; i++) {
			mech.Controls = full;
			scene.World.Tick();
		}

		int travelled = SimMath.FastMagnitude2D(
			mech.Position.X - start.X, mech.Position.Y - start.Y);

		// 20 seconds of walking should cover hundreds of metres. The only ways it does not are a
		// collision it cannot back out of or ground too steep to leave, both of which are legitimate
		// but would make this a poor smoke test — so this asserts it moved a long way, not a
		// specific distance.
		Assert.True(travelled > 20000,
			$"{mech.Name} covered only {travelled} world units in 500 ticks");

		// And it must still be standing on the zone, not inside it or floating over it.
		Assert.Equal(scene.World.Terrain.HeightAtWorld(mech.Position.X, mech.Position.Y)
			+ mech.Type.RideHeight, mech.Position.Z);
	}

	/// <summary>
	/// A squadmate's formation post is the slot offset that spread it at spawn, applied to the
	/// leader — so before anything has moved, every follower in the player's group stands on its own
	/// post. A follower with no offset would take the player's own position as its post and walk into
	/// them.
	/// </summary>
	[Fact]
	public void SquadmatesStartOnTheirFormationPosts() {
		if (Load() is not { } scene) {
			return;
		}

		var squad = scene.Objects
			.Select(o => o.Object)
			.OfType<MechObject>()
			.Where(mech => mech.Group is { LedByPlayer: true })
			.ToList();

		foreach (var mech in squad) {
			if (mech.Group!.Leader is not MechObject leader || ReferenceEquals(leader, mech)) {
				continue;
			}

			Assert.NotNull(mech.FormationOffset);

			var post = mech.FormationPostAround(leader.Position, leader.Heading);
			Assert.Equal(post.X, mech.Position.X);
			Assert.Equal(post.Y, mech.Position.Y);
		}
	}

	/// <summary>The practice mission on <paramref name="row"/>, its handoff written under <see cref="_root"/>, never the install.</summary>
	private MissionScene? LoadTraining(int row) {
		if (GameInstall.Locate(null) is not { } install) {
			return null;
		}

		var handoff = ShellTrainingLaunch.Write(_root, GameContent.MountShell(install), SimulatorPreferences.Defaults(), row,
			instantAction: false, new SimRandom(), new short[MissionGenerator.ClearListLength], held: null, out string? failure);
		Assert.True(handoff != null, failure);
		return MissionScene.Load(GameContent.MountSimulator(install), handoff.ScriptPath);
	}

	private static MissionScene? Load() {
		string? root = GameInstall.Locate(null);
		if (root == null) {
			return null;
		}

		string script = MissionLoader.DefaultScriptPath(root);
		string data = Path.GetDirectoryName(script)!;
		if (!File.Exists(script) || !File.Exists(Path.Combine(data, MissionLoader.CountersFileName))
			|| !File.Exists(MissionLoader.PlayerPathFor(script))) {
			return null;
		}

		return MissionScene.Load(GameContent.MountSimulator(root), script);
	}
}
