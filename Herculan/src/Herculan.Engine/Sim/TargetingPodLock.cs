using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// The Targeting Pod's own state — catalog id 29, <c>mech+0x30b</c>, the pod that lets the player
/// aim at one part of the selected machine instead of at its aim node. The manual: select with
/// <c>[Enter]</c>, then <c>[Tab]</c> cycles through that target's components, and Automatic Turret
/// Tracking follows the part rather than centre mass.
///
/// <para>In the original this is a weapon-mount subclass, built by <c>TargetingPod_Ctor</c>
/// (<c>0040e308</c>) over <c>0x8a</c> bytes. Its four fields sit past every other mount class'; they
/// are gathered here rather than added to <see cref="WeaponMount"/> so that the one mount that has
/// them is the one that carries them, which is what <see cref="WeaponMount.ComponentLock"/> is.</para>
///
/// <para><b>The constructor writes none of them.</b> It writes the gauge handle and the catalog id
/// and stops, so what the four hold at spawn is whatever the block held — and the block is zeroed:
/// <c>MechLoadout_ConstructWeaponMounts</c> (<c>0040fff8</c>) opens by pushing a 200000-byte arena
/// that <c>Arena_Push</c> has just <c>calloc</c>'d and bump-allocates every mount out of it, and the
/// fallback that bump allocator drops to when the arena is absent or full
/// (<c>Mem_NewArray</c>) zeroes what it hands back anyway. So a
/// pristine pod starts with cursor 0, component 0, no cached damage and an expired decay timer, and
/// <see cref="ComponentDamage"/> agrees with <see cref="MechPods.DamageOf"/>'s live reading until
/// the first hit — after which <c>Mech_ComponentDamageWrite</c> hands every mount its component's
/// new reading, so the two never part company. <b>They are still not interchangeable</b>: the live
/// reading is a function of the damage model at the instant it is asked, and the cache is only as
/// current as the last write. See docs/retail/simulation/target-selection.md.</para>
///
/// <para><b>Nothing here is reached for an AI machine.</b> All three drivers hang off the player's
/// per-frame cockpit update or the <c>[Tab]</c> command, so an AI machine carrying a pod uses it for
/// exactly one thing: <see cref="ComponentDamage"/>'s <see cref="AiSystemsBandLimit"/> threshold in
/// <c>Mech_AiSelectAimComponent</c>.</para>
/// </summary>
public sealed class TargetingPodLock {
	/// <summary>
	/// <c>+0x7d</c> — the cursor's position in <see cref="ComponentRotation"/>, or
	/// <see cref="NoComponent"/> for no component lock. It is <b>not</b> a component id; that is
	/// <see cref="Component"/>, and the two move together only because the slot that advances the
	/// cursor writes the id through an out-parameter at the same time.
	/// </summary>
	public short Cursor { get; private set; }

	/// <summary>
	/// <c>+0x86</c> — the component id the cursor last resolved to, and the only one of the pair the
	/// target is ever asked about. The original stores it as an <c>int</c> and reads it back as a
	/// <c>short</c> everywhere but the presence test.
	/// </summary>
	public short Component { get; private set; }

	/// <summary>
	/// <c>+0x7f</c> — the pod's own component damage, 0 pristine to 256 gone, cached by
	/// <c>TargetingPod_ConditionChanged</c> (<c>0040ef6c</c>). <b>The Targeting Pod is the only pod
	/// that caches this</b>: the Shield and Energy pods read theirs live through
	/// <see cref="MechPods.DamageOf"/> each time their bonus is recomputed. Every one of the pod's
	/// four degradation thresholds is a test on this, not on the live figure.
	/// </summary>
	public short ComponentDamage { get; internal set; }

	/// <summary><c>+0x81</c>, counter at <c>+0x82</c> — the decay a damaged pod runs; see <see cref="DecayReload"/>.</summary>
	private int _decayTimer;

	/// <summary>The value <see cref="Cursor"/> and <see cref="Component"/> take when there is no lock.</summary>
	public const short NoComponent = -1;

	/// <summary>
	/// <c>TargetingPodComponentRotation</c> (<c>0049a060</c>) — the seven component slots
	/// <c>[Tab]</c> cycles a HERC target through, straddling both the chassis band (0, 4, 5) and the
	/// systems band (7-10) of <c>Mech_AiSelectAimComponent</c>'s table. The manual's "target areas".
	/// </summary>
	public static readonly short[] ComponentRotation = [0, 4, 5, 7, 8, 9, 10];

	/// <summary>
	/// Past this the lock stops holding: <see cref="ResolveAimPoint"/> runs <see cref="_decayTimer"/>
	/// down on every frame it answers, and each expiry drops the cursor back to
	/// <see cref="NoComponent"/>. So past 40% damage the player keeps a component for one
	/// <see cref="DecayReload"/> at a time and has to press <c>[Tab]</c> again. The test is strictly
	/// greater, as the original's is.
	/// </summary>
	public const short DecayDamage = 0x68;

	/// <summary>
	/// At this and above, component targeting stops entirely: <see cref="ResolveAimPoint"/> takes the
	/// target's aim node and reports no component, exactly as a machine with no pod does.
	/// </summary>
	public const short DisabledDamage = 0x9b;

	/// <summary>
	/// Under this, the pod quarters the weight of the roll by which a target's ECM spoofs this
	/// machine's missile lock — see <see cref="MechObject.EcmRollTick"/>. It is the <i>observer's</i>
	/// pod, not the jammer's.
	/// </summary>
	public const short EcmAssistLimit = 0x33;

	/// <summary>
	/// At or under this, an AI machine carrying a pod always works at the target's systems band
	/// instead of rolling for a band — see <see cref="MechObject.SelectAimComponent"/>. The original
	/// spells the failing case <c>&gt; 0xa9</c>.
	/// </summary>
	public const short AiSystemsBandLimit = 0xa9;

	/// <summary>
	/// What <see cref="_decayTimer"/> reloads with on each expiry — the original's literal 5000,
	/// which in <see cref="SimMath.TimerCountDown"/>'s unit is about 2.4 seconds rather than five.
	/// </summary>
	public const int DecayReload = 5000;

	/// <summary>
	/// <c>TargetingPod_ResetComponentLock</c> (<c>0040e484</c>), from the selection change in
	/// <c>Player_PerFrameCockpitUpdate</c>. A HERC restarts the rotation; anything else switches
	/// component targeting off outright — the same <see cref="TargetClass"/> fence that keeps a
	/// structure's and a flyer's components out of the aim-point path.
	///
	/// <para><b>It moves the cursor and not the id.</b> Restarting means cursor 0, which is a
	/// <i>position</i> in the rotation and not a component, so the pod goes on aiming at whatever
	/// <see cref="Component"/> already held until the next <c>[Tab]</c> — the last target's component
	/// on a fresh selection, and component 0 on the first selection of the mission. That is the
	/// original's, and is why selecting a new machine does not take the player back to centre mass.</para>
	/// </summary>
	public void ResetComponentLock(SimObject? target) =>
		Cursor = target is { TargetClass: TargetClass.Herc } ? (short)0 : NoComponent;

	/// <summary>
	/// <c>TargetingPod_CycleComponent</c> (<c>0040e4ac</c>) — <c>[Tab]</c>, and the resolver's own
	/// recovery when the locked component has been shot off. It asks the target for the next slot it
	/// still has and takes both halves of the answer; a target with nothing left answers
	/// <see cref="NoComponent"/> to both. With nothing selected it does nothing at all.
	/// </summary>
	public void CycleComponent(SimObject? target) {
		if (target == null) {
			return;
		}

		Cursor = (short)target.NextTargetableComponent(Cursor, out int component);
		Component = (short)component;
	}

	/// <summary>
	/// <c>TargetingPod_ResolveAimPoint</c> (<c>0040e4dc</c>) — where the HUD aims on the selected
	/// target, and which component that is. <c>Player_ResolveTargetAimPoint</c> reaches it only with
	/// a pod fitted and the target inside <see cref="MechObject.ComponentAimRange"/>.
	///
	/// <para>With no lock, or a pod at <see cref="DisabledDamage"/> or worse, it answers the target's
	/// own aim node and false. Otherwise it asks whether the locked component is still there,
	/// cycling once if it is not — <b>without re-testing</b>, so a target that has run out of parts
	/// is asked for the position of component <see cref="NoComponent"/>, which every class answers
	/// with its own origin.</para>
	///
	/// <para>The original's fourth parameter is dead: <c>Player_ResolveTargetAimPoint</c> passes the
	/// target's occupancy array and the body never touches it. The pod reaches the same array through
	/// the target's own slots instead.</para>
	/// </summary>
	/// <param name="target">The selected object — always a HERC by the time the lock is non-negative.</param>
	/// <param name="point">Where to aim.</param>
	/// <param name="component">The component that is, or 0 when there is none.</param>
	/// <returns>
	/// Whether a component was singled out — the original's first out-parameter, which reaches the
	/// gunsight's state block at offset 24 and the MFD through <c>CockpitView+0x27c</c>. The target
	/// box drops its brackets and ticks on it, leaving the bare pip.
	/// </returns>
	public bool ResolveAimPoint(SimObject target, out Vec3i point, out short component) {
		if (Cursor < 0 || ComponentDamage >= DisabledDamage) {
			point = target.AimPoint;
			component = 0;
			return false;
		}

		if (!target.ComponentPresent(Component)) {
			CycleComponent(target);
		}

		point = target.ComponentWorldPosition(Component);
		component = Component;

		// Only a damaged pod runs the decay, and it runs it here rather than on a tick — so a pod
		// past the threshold holds a component for as long as the player keeps the target inside the
		// resolver's range, and no longer. The counter is never initialised, so the first frame that
		// reaches this expires immediately.
		if (ComponentDamage > DecayDamage && SimMath.TimerCountDown(ref _decayTimer) == 0) {
			_decayTimer = DecayReload;
			Cursor = NoComponent;
		}

		return true;
	}
}
