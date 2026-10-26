using Herculan.Engine.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// An effect light manager's slots as the renderer's per-object selection reads them (<see cref="EffectLightSelection"/>):
/// the seam each game's own manager stands behind, so the renderer reads either game's without reaching into its
/// simulation.
/// </summary>
public interface IEffectLightSlots {
	/// <summary>How many slots the manager has, live or not.</summary>
	int SlotCount { get; }

	/// <summary>
	/// Whether slot <paramref name="slot"/> lights anything: claimed, with an intensity that is not 0. When it does, its
	/// position in world units, its intensity, 0 to 255, and the distance past which it lights nothing.
	/// </summary>
	bool TryGetLive(int slot, out Vec3i position, out int intensity, out int cullRadius);
}
