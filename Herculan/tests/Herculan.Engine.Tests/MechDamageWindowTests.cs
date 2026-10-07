using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="MechObject.DamageTaken"/> is a window, not a running total: the systems pass zeroes it
/// each time <see cref="MechObject.DamageWindowTimer"/> runs out. See
/// docs/retail/simulation/ai-combat-states.md ("The circling step").
///
/// <para>Every test skips silently when no Earthsiege 2 install can be found.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public class MechDamageWindowTests {
	/// <summary>
	/// Damage from a direct-fire hit survives every tick of the window it landed in and is gone on the
	/// tick the window expires.
	/// </summary>
	[Fact]
	public void DamageTakenIsZeroedWhenTheWindowExpires() {
		if (ComputerWarningTests.Content() is not { } content
				|| ComputerWarningTests.Spawn(content, "OUTLAW") is not { } shooter
				|| ComputerWarningTests.Spawn(content, "OUTLAW") is not { } victim) {
			return;
		}

		var world = ComputerWarningTests.FlatWorld(new ComputerWarningTests.MessageLog(), shooter, victim);
		victim.Position = shooter.Position + new Vec3i(0, 6000, 0);

		// The first systems pass finds the window at zero and arms it.
		world.Tick();
		Assert.Equal(MechObject.DamageWindowReload, victim.DamageWindowTimer);

		var shot = ComputerWarningTests.Shot(shooter);
		Assert.NotEqual(0, world.Raycast(shot));
		Assert.Same(victim, shot.HitObject);

		int taken = victim.DamageTaken;
		Assert.True(taken > 0, "the hit should have been counted");

		int ticksToExpiry = (MechObject.DamageWindowReload + SimWorld.TickDelta - 1) / SimWorld.TickDelta;
		for (int i = 1; i < ticksToExpiry; i++) {
			world.Tick();
			Assert.Equal(taken, victim.DamageTaken);
		}

		world.Tick();
		Assert.Equal(0, victim.DamageTaken);
		Assert.Equal(MechObject.DamageWindowReload, victim.DamageWindowTimer);
	}
}
