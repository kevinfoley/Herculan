using Herculan.Engine.View;

namespace Herculan.Engine.Host.Debugging;

/// <summary>
/// The debug panel's view options, which the renderer and the cockpit eye read: this engine's own diagnostics,
/// none of which feeds back into the simulation.
/// </summary>
/// <param name="drawSkeleton">Whether the skeleton starts drawn: on under <c>--developer</c>, off otherwise, since only then can the panel turn it off.</param>
/// <param name="steadyEye">The cockpit eye's height pin, which the cockpit view applies.</param>
sealed class DebugOptions(bool drawSkeleton, SteadyEye steadyEye) {
	/// <summary>Whether the host should draw the animating skeleton over the world.</summary>
	public bool DrawSkeleton { get; set; } = drawSkeleton;

	/// <summary>
	/// Joints in the last skeleton the host built, for the readout. Set by whatever draws the
	/// skeleton, since that is the only thing that knows.
	/// </summary>
	public int SkeletonJointCount { get; set; }

	/// <inheritdoc cref="View.SteadyEye"/>
	public SteadyEye SteadyEye { get; } = steadyEye;
}
