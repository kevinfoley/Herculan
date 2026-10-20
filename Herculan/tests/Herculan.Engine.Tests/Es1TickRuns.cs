using System.Runtime.CompilerServices;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The tick's suspendable calls run to their end for a test that raises no panel, failing at the first wait, which only
/// a panel's loop makes.
/// </summary>
internal static class Es1TickRuns {
	/// <summary><see cref="Es1Simulation.MainTick"/> run through; whether it ended the mission.</summary>
	public static bool Tick(this Es1Simulation simulation) {
		var ended = new StrongBox<bool>();
		RunThrough(simulation.MainTick(ended));
		return ended.Value;
	}

	/// <summary><c>Sim_DispatchCommand</c> run through; its answer.</summary>
	public static short Dispatch(this Es1Simulation simulation, Es1PlayerInputBlock block) {
		var answer = new StrongBox<short>();
		RunThrough(simulation.DispatchCommand(block, answer));
		return answer.Value;
	}

	/// <summary><c>Sim_PollPlayerInput</c> run through; whether the dispatch answered 2.</summary>
	public static bool Poll(this Es1Simulation simulation, Es1PlayerInputBlock block) {
		var ended = new StrongBox<bool>();
		RunThrough(simulation.PollPlayerInput(block, ended));
		return ended.Value;
	}

	/// <summary><c>CockpitWidgets_HandleCommand</c> run through with no panels; its answer.</summary>
	public static short Command(this Es1Cockpit cockpit, ushort code) {
		var answer = new StrongBox<short>();
		RunThrough(cockpit.HandleCommand(code, null, answer));
		return answer.Value;
	}

	private static void RunThrough(IEnumerable<Es1Wait> waits) {
		foreach (var _ in waits) {
			Assert.Fail("The tick waited, which only a panel's loop does.");
		}
	}
}
