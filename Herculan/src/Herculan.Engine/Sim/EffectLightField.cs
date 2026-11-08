using Herculan.Engine.Numerics;
using Herculan.Engine.Render;

namespace Herculan.Engine.Sim;

/// <summary>
/// The effect light manager, <c>DAT_004a968c</c> — twenty slots an impact effect can claim a
/// dynamic light in. The constants that decide how far one reaches are the renderer's
/// (<see cref="EffectLightSelection.FalloffRange"/> and its siblings), which both games share.
///
/// <para>This is the simulation half only: it holds where the lights are and how bright, and the
/// renderer reads <see cref="Slots"/> to decide what each drawn object is lit by. The per-object
/// directional/point selection (<c>LightManager_SelectLightsForObject</c>, <c>00407098</c>) belongs to the renderer, not here, because it
/// depends on the object being drawn rather than on the light.</para>
///
/// <para>The whole derivation — slot layout, the constants, the selection test and the shade terms —
/// is docs/retail/rendering/effect-lights.md.</para>
/// </summary>
public sealed class EffectLightField : Render.IEffectLightSlots {
	/// <summary>
	/// Slots the manager has, the <c>Rtl_VectorNew</c> count at <c>mgr+0x6c</c>. The original's
	/// allocator has no full-table guard; <see cref="HandleCount"/> keeps it from being reached.
	/// </summary>
	public const int SlotCount = 20;

	/// <summary>
	/// <c>EffectLightPool</c> (<c>004a9682</c>), the handle pool an effect allocates from before it
	/// claims a slot: three, so at most three effect lights exist at once and a fourth light-bearing
	/// effect runs dark. See docs/retail/rendering/effect-lights.md, "Claiming a slot".
	/// </summary>
	public const int HandleCount = 3;

	private readonly EffectLight[] _slots = new EffectLight[SlotCount];
	private readonly List<int> _pendingReleases = new(HandleCount);
	private int _handlesInUse;

	/// <summary>
	/// Every slot, claimed and free alike — index is the handle <see cref="Claim"/> returns. Read
	/// <see cref="EffectLight.IsLive"/> before lighting with one.
	/// </summary>
	public IReadOnlyList<EffectLight> Slots => _slots;

	/// <inheritdoc/>
	int Render.IEffectLightSlots.SlotCount => SlotCount;

	/// <inheritdoc/>
	public bool TryGetLive(int slot, out Vec3i position, out int intensity, out int cullRadius) {
		var light = _slots[slot];
		(position, intensity, cullRadius) = (light.Position, light.Intensity, light.CullRadius);
		return light.IsLive;
	}

	/// <summary>
	/// <c>EffectLight_Construct</c> (<c>00407604</c>) and <c>LightManager_ClaimSlot</c> (<c>00406f38</c>) — takes a handle
	/// and claims the first free slot for a light at <paramref name="position"/> with
	/// <paramref name="intensity"/>, returning its index, or -1 when all <see cref="HandleCount"/>
	/// handles are out and the effect runs without a light.
	///
	/// <para>The original seeds the intensity to 255 and lets <c>LightManager_SetSlotIntensity</c> (<c>00407048</c>) overwrite it a
	/// call later; the two are folded together here because no caller can observe the gap.</para>
	/// </summary>
	public int Claim(Vec3i position, int intensity) {
		if (_handlesInUse >= HandleCount) {
			return -1;
		}

		for (int i = 0; i < _slots.Length; i++) {
			if (_slots[i].Claimed) {
				continue;
			}

			_handlesInUse++;
			_slots[i] = new EffectLight(position, ClampIntensity(intensity), Claimed: true);
			return i;
		}

		return -1;
	}

	/// <summary>
	/// <c>LightManager_SetSlotIntensity</c> (<c>00407048</c>) — the intensity setter, which is also what recomputes the cull radius.
	/// A handle of -1 does nothing, so a caller that failed to claim needs no branch of its own.
	/// </summary>
	public void SetIntensity(int handle, int intensity) {
		if (handle < 0 || handle >= _slots.Length || !_slots[handle].Claimed) {
			return;
		}

		_slots[handle] = _slots[handle] with { Intensity = ClampIntensity(intensity) };
	}

	/// <summary>
	/// An ended effect's light, queued for <see cref="FlushReleases"/> the way <c>Explosion_Destruct</c>
	/// (<c>00407e48</c>) queues its handle with <c>ObjectPool_QueueForDelete</c>. The slot stays claimed,
	/// and the handle out of the pool, until then. A handle of -1 does nothing.
	/// </summary>
	public void Release(int handle) {
		if (handle >= 0 && handle < _slots.Length && _slots[handle].Claimed && !_pendingReleases.Contains(handle)) {
			_pendingReleases.Add(handle);
		}
	}

	/// <summary>
	/// <c>EffectLightPool_FlushDeletes</c> (<c>004077e8</c>) — runs <c>EffectLight_Destruct</c> (<c>0040765c</c>) on every
	/// queued handle, which frees its slot (<c>LightManager_ReleaseSlot</c>, <c>00406fbc</c>), and returns the
	/// handles to the pool. The original runs it from the top of <c>Sim_RenderFrame</c>, between one
	/// <c>Sim_MainTick</c> and the next.
	/// </summary>
	public void FlushReleases() {
		foreach (int handle in _pendingReleases) {
			_slots[handle] = default;
			_handlesInUse--;
		}

		_pendingReleases.Clear();
	}

	/// <summary>Frees every slot and handle, for a mission teardown.</summary>
	public void Clear() {
		Array.Clear(_slots);
		_pendingReleases.Clear();
		_handlesInUse = 0;
	}

	private static int ClampIntensity(int intensity) => Math.Clamp(intensity, 0, EffectLightSelection.MaxIntensity);
}

/// <summary>
/// One slot of <see cref="EffectLightField"/> — a dynamic light's position, brightness and the
/// range past which an object stops being lit by it.
/// </summary>
/// <param name="Position">Where it sits, in world units. Nothing moves an effect light.</param>
/// <param name="Intensity">
/// Brightness, 0-255 — <c>slot+0x1b</c>, the field the per-object selection reads. An
/// <see cref="ImpactEffect"/> drives it from its type row's per-frame ramp.
/// </param>
/// <param name="Claimed">
/// Whether an effect holds the slot — the inverse of the slot's free flag at <c>+0x00</c>, which only
/// <c>LightManager_ClaimSlot</c> (<c>00406f38</c>) and <c>LightManager_ReleaseSlot</c> (<c>00406fbc</c>) write. A claimed
/// slot at intensity 0 is dark, not free.
/// </param>
public readonly record struct EffectLight(Vec3i Position, int Intensity, bool Claimed) {
	/// <summary>
	/// Whether the slot lights anything this frame: claimed, with a nonzero intensity.
	/// <c>LightManager_SelectLightsForObject</c> (<c>00407098</c>) skips a slot whose <c>+0x1b</c> is 0.
	/// </summary>
	public bool IsLive => Claimed && Intensity > 0;

	/// <summary>
	/// <c>LightManager_RecomputeCullRadius</c> (<c>0040735c</c>) — how far this light reaches, past which
	/// <c>LightManager_SelectLightsForObject</c> (<c>00407098</c>) detaches it from an object rather than lighting with it:
	/// <code>
	/// cullRadius = (intensity * B * 0x20) / 10 + A * 0x20
	/// </code>
	/// which with the live constants is <c>intensity * 198.4</c>, about 300 m at full brightness.
	/// </summary>
	public int CullRadius =>
		Intensity * EffectLightSelection.FalloffRange * EffectLightSelection.FalloffScale / 10
			+ EffectLightSelection.FalloffOffset * EffectLightSelection.FalloffScale;
}
