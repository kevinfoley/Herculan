using Herculan.Es1.View;

namespace Herculan.Es1.Sim;

/// <summary>
/// Where a piece of the frame stands waiting before it goes on, which the frame loop resumes it from
/// (<see cref="Host.Es1FrameLoop"/>): the tick inside a panel's loop, or the frame's end inside a routine that waits for
/// the monitor (docs/herculan/plan-es1-first-mission.md#decisions, decisions 2 and 11).
///
/// <para>Retail's panel loops run inside <c>Sim_MainTick</c> until the panel closes
/// (docs/es1/simulation/alert-panels.md#the-loop). The port suspends the tick at each pass of a loop and goes on from
/// there: a pass of the status panel's loop, which in retail goes round again at once, ends at
/// <see cref="NextHostFrame"/>, so the host shows the monitor between passes; a pass that waits for the monitor ends at
/// its <see cref="Retrace"/>.</para>
/// </summary>
public readonly record struct Es1Wait {
	/// <summary>A wait that ends with the next host frame.</summary>
	public static Es1Wait NextHostFrame => default;

	/// <summary>The monitor's wait <paramref name="retrace"/>, which ends on the monitor's retraces.</summary>
	public static Es1Wait For(Es1Retrace retrace) => new() { Retrace = retrace };

	/// <summary>The monitor's wait; null for <see cref="NextHostFrame"/>.</summary>
	public Es1Retrace? Retrace { get; init; }
}
