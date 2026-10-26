using Herculan.Engine.Numerics;
using Herculan.Engine.Render;

namespace Herculan.Es1.Effects;

/// <summary>
/// The effect light manager (<c>ES1DBSIM 000bc19c</c>) and its handle pool (<c>000aee18</c>), which
/// <c>LightManager_InitSubsystem</c> (<c>000055b8</c>) builds at phase 2 (docs/es1/rendering/polygon-shading.md#effect-lights):
/// <see cref="SlotTotal"/> slots, <see cref="HandleTotal"/> handles, and the falloff constants A = 10 &gt;&gt; 5 = 0 and
/// B = 2000 &gt;&gt; 5 = 62, ES2's, which the renderer's per-object selection reads them with
/// (<see cref="EffectLightSelection"/>). <c>EffectLight_Destruct</c> (<c>00005534</c>) releases its slot at once, so the
/// light is out from the next draw, and only its handle waits on the delete queue (<c>000aee1c</c>) for
/// <c>EffectLightPool_FlushDeletes</c> (<c>000055f4</c>) at phase 5.
/// </summary>
public sealed class Es1EffectLights : IEffectLightSlots {
	/// <summary>The slots <c>LightManager_Construct</c> (<c>00004bb8</c>) builds at <c>+0x6c</c>.</summary>
	public const int SlotTotal = 20;

	/// <summary>The handles of the pool, <c>Pool_Init</c>(0, 8, 3).</summary>
	public const int HandleTotal = 3;

	/// <summary>The manager's A and B, <c>LightManager_SetFalloffConstants</c>(2000, 10)'s.</summary>
	public const int FalloffA = 10 >> 5, FalloffB = 2000 >> 5;

	private readonly (bool Claimed, Vec3i Position, short Intensity)[] _slots = new (bool, Vec3i, short)[SlotTotal];
	private int _handlesOut;

	/// <inheritdoc/>
	public int SlotCount => SlotTotal;

	/// <summary>
	/// <c>EffectLight_Construct</c> (<c>000054b0</c>)(0, <paramref name="intensity"/>, <paramref name="point"/>): a handle
	/// from the pool, then <c>LightManager_ClaimSlot</c> (<c>00004dfc</c>), the first free slot at the point, and
	/// <c>LightManager_SetSlotIntensity</c> (<c>00004f34</c>). Answers the slot, or −1 with every handle out, the ones
	/// queued since the last flush included. Three handles never fill twenty slots.
	/// </summary>
	public int Construct(short intensity, Vec3i point) {
		if (_handlesOut >= HandleTotal) {
			return -1;
		}

		int slot = Array.FindIndex(_slots, entry => !entry.Claimed);
		_handlesOut++;
		_slots[slot] = (true, point, intensity);
		return slot;
	}

	/// <summary><c>EffectLight_SetIntensity</c> (<c>00005578</c>): the slot's intensity; nothing for −1.</summary>
	public void SetIntensity(int handle, byte intensity) {
		if (handle >= 0 && _slots[handle].Claimed) {
			_slots[handle].Intensity = intensity;
		}
	}

	/// <summary>
	/// <c>EffectLight_Destruct</c>(<paramref name="handle"/>, 3): <c>LightManager_ReleaseSlot</c> (<c>00004e84</c>) now,
	/// the handle queued; nothing for −1.
	/// </summary>
	public void Destruct(int handle) {
		if (handle >= 0) {
			_slots[handle] = default;
		}
	}

	/// <summary><c>EffectLightPool_FlushDeletes</c>: the queued handles given back to the pool.</summary>
	public void FlushDeletes() => _handlesOut = _slots.Count(entry => entry.Claimed);

	/// <summary>
	/// A claimed slot whose intensity (<c>+0x1b</c>) is not 0, with its cull radius as
	/// <c>LightManager_RecomputeCullRadius</c> (<c>00005274</c>) works it: (B × <c>0x20</c> × intensity) / 10 + A × <c>0x20</c>.
	/// </summary>
	public bool TryGetLive(int slot, out Vec3i position, out int intensity, out int cullRadius) {
		var entry = _slots[slot];
		position = entry.Position;
		intensity = entry.Intensity;
		cullRadius = FalloffB * 0x20 * entry.Intensity / 10 + FalloffA * 0x20;
		return entry.Claimed && entry.Intensity != 0;
	}
}
