using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// Giving fire: the trigger path (<c>Mech_PlayerFireTick</c> (<c>00415608</c>) → <c>WeaponMounts_FireTrigger</c> (<c>00410dbc</c>)),
/// the selected target and what a change of it restarts, and the component queries a target
/// answers for the Targeting Pod and a missile's lock. Taking fire is <c>MechObject.Damage.cs</c>.
/// </summary>
public sealed partial class MechObject {
	/// <summary>
	/// <c>Mech_PlayerFireTick</c> (<c>00415608</c>), the player's own fire path, called once a frame from
	/// <c>Sim_PollPlayerInput</c> with the input device struct.
	///
	/// <para><b>The trigger is a held state, not a keypress.</b> The mount's own vtable <c>+0x30</c>
	/// (<c>WeaponMount_TriggerHeld</c>, <c>0040f8ad</c>) does nothing but read the device struct's byte at <c>+0x0d</c> — the
	/// fire button — so holding it fires again the moment the refire timer runs out and the capacitor
	/// is back over the threshold. Nothing edge-detects it anywhere along the path.</para>
	///
	/// <para>Only a machine with a pilot ever reaches this: the original calls it from the input poll
	/// for <c>LocalPlayerMech</c> alone, and AI machines fire through their own think function, which
	/// is unported. Here that falls out of <see cref="Controls"/>, which is
	/// <see cref="MechControls.Neutral"/> for everything the player is not flying.</para>
	///
	/// <para>The rest of <c>Mech_PlayerFireTick</c> is the player's <b>line of fire</b>: on a shot it stamps
	/// up to 40 points along the turret bearing at <see cref="FiringLineSpacing"/> spacing, cut to the
	/// range of the selected target. Nothing draws them — their only reader is
	/// <see cref="ObstacleAvoidance"/>, which steers the player's own squadmates out of the way. See
	/// docs/retail/simulation/ai-navigation.md.</para>
	/// </summary>
	private void FireTick(SimWorld world) {
		// A round the player is flying clears the device's trigger byte before this reads it.
		bool trigger = Controls.Fire && !(LocallyPiloted && world.PlayerTriggerCleared);
		bool fired = Weapons.FireTick(this, world, trigger);

		if (!IsPlayer) {
			return;
		}

		if (!fired) {
			world.ClearPlayerFiringLine();
			return;
		}

		int count = FiringLineDefaultPoints;

		if (Target is { } target) {
			count = Position.ApproxDistanceTo(target.Position) >> FiringLineRangeShift;
			count = count > FiringLineMaxPoints ? FiringLineMaxPoints : count < 1 ? 1 : count;
		}

		short cos = BinaryAngle.Cos((short)(Heading - TorsoTwistAngle));
		short sin = BinaryAngle.Sin((short)(Heading - TorsoTwistAngle));
		int stepX = (int)((-(long)FiringLineSpacing * sin + 0x2000) >> 14);
		int stepY = (int)(((long)FiringLineSpacing * cos + 0x2000) >> 14);

		world.SetPlayerFiringLine(Position, stepX, stepY, count);
	}

	/// <summary>How far apart the player's line-of-fire points are laid.</summary>
	private const int FiringLineSpacing = 0x1000;

	/// <summary>How many points the line runs to when the player has nothing selected.</summary>
	private const int FiringLineDefaultPoints = 20;

	/// <summary>The ceiling on the point count, and the size of the original's own vector.</summary>
	private const int FiringLineMaxPoints = 40;

	/// <summary>Target range is shifted by this to give the point count.</summary>
	private const int FiringLineRangeShift = 12;

	/// <summary>
	/// <inheritdoc cref="SimObject.Target"/>
	/// <summary>
	/// <c>mech+0x1a4</c> — the machine's selected target, and the field the whole of homing hangs
	/// off: <c>Bullet_FirePowered</c> reads it to give a plasma round something to chase and
	/// <c>Rocket_Fire</c> reads it to give a missile a lock, so before anything wrote it every guided
	/// weapon in the game flew straight.
	///
	/// <para><b>Nothing in the simulation writes it for the player's machine.</b> The selection is
	/// made in the cockpit and copied here once a frame — see <see cref="TargetSelection"/>, which is
	/// where the RE for that lives. An AI machine writes it from its own think and from the combat
	/// reassess.</para>
	///
	/// <para>The field and its refcount bookkeeping are <see cref="SimObject.Target"/>'s, because the
	/// original keeps them on the shared base; what a HERC adds on top of them is here.</para>
	/// </summary>
	private protected override void OnTargetChanged(SimObject? previous) {
		if (Target == null && Weapons.AutoTrack) {
			// Player_PerFrameCockpitUpdate arms mech+0x31c here, on the change that leaves ATT
			// with nothing to track. See MechObject.TorsoTick, which runs it down.
			_autoTrackIdle = AutoTrackIdleDelay;
		}

		// And the Targeting Pod's lock restarts on the same change. Both gates are
		// Player_PerFrameCockpitUpdate's own: it runs for the piloted machine alone, and it calls the
		// reset only when the new selection is something — clearing the target leaves the lock where
		// it was. See TargetingPodLock.ResetComponentLock.
		if (LocallyPiloted && Target != null) {
			Pods.TargetingMount?.ComponentLock?.ResetComponentLock(Target);
		}

		TargetChanged = true;
	}

	/// <summary>
	/// <c>[Tab]</c> — <c>CockpitWidgets_HandleCommand</c>'s <c>0x0f</c> case, which steps the
	/// Targeting Pod's component lock on the selected machine. A machine with no pod, or with nothing
	/// selected, does nothing.
	///
	/// <para>The command only reaches the pod while the heads-down display is <i>not</i> down: in view
	/// mode 1 the same scancode goes to the display's own command slot instead, which is the manual's
	/// <c>Zoom Map In/Out</c>. That split is the host's to make — see
	/// docs/retail/formats/cockpit-input.md.</para>
	/// </summary>
	public void CycleTargetComponent() => Pods.TargetingMount?.ComponentLock?.CycleComponent(Target);

	/// <summary>
	/// <c>Player_ResolveTargetAimPoint</c> (<c>0041b728</c>) — the point the HUD aims at on the
	/// selected target, and which component of it that is. With a Targeting Pod fitted and the target
	/// inside <see cref="ComponentAimRange"/> the pod answers both; otherwise it is the target's own
	/// <see cref="SimObject.AimPoint"/> and no component.
	///
	/// <para>Both halves reach the cockpit through <c>CockpitView_SetTargetBlock</c>: the flag lands
	/// at <c>+0x27c</c> and drops the target box to its bare pip, and the component id lands at
	/// <c>+0x27e</c> and highlights that region of the MFD's paper doll. See
	/// docs/retail/formats/hud-target-indicator.md and docs/retail/formats/mfd.md.</para>
	/// </summary>
	/// <returns>Where to aim, whether a component was singled out, and which.</returns>
	public (Vec3i Point, bool ComponentTargeted, short Component) ResolveTargetAimPoint() {
		if (Target is not { } target) {
			return (Position, false, 0);
		}

		if (Pods.TargetingMount?.ComponentLock is { } pod
			&& Position.ApproxDistanceTo(target.Position) < ComponentAimRange) {
			bool targeted = pod.ResolveAimPoint(target, out var point, out short component);
			return (point, targeted, component);
		}

		return (target.AimPoint, false, 0);
	}

	/// <summary>
	/// How close the selected machine has to be before the Targeting Pod is asked for a component aim
	/// point at all — the original's literal 30000, 180 m, the manual's "close range". Outside it the
	/// pod is skipped entirely and the box goes back to whole.
	/// </summary>
	public const int ComponentAimRange = 30000;

	/// <summary>
	/// <c>mech+0x9d</c> — raised whenever <see cref="Target"/> changes and never cleared by the write
	/// itself. In the original it gates the AI's per-tick weapon arbitration (a machine that has just
	/// switched target does not shoot on that tick) and it is what tells the cockpit to reset the
	/// gunsight's lock state. Nothing consumes it yet; it is set because the setter is the only place
	/// that can, and leaving it out would mean revisiting the setter later.
	/// </summary>
	public bool TargetChanged { get; set; }

	/// <summary>
	/// <c>mech+0x31c</c> — how long Automatic Turret Tracking waits, with the latch on and nothing
	/// selected, before it gives up and brings the turret home. <c>Player_PerFrameCockpitUpdate</c>
	/// (<c>0041b130</c>) arms it from the selection change that cleared the target and runs it down
	/// every frame the pair still holds; the engine runs it down in the turret block instead, which
	/// is the only thing that reads the result. See <see cref="AutoTrackIdleDelay"/>.
	/// </summary>
	public short AutoTrackIdleTimer => _autoTrackIdle;

	/// <summary>
	/// What that timer is armed with — the original's own <c>0x1194</c>, about 55 ticks.
	/// </summary>
	public const short AutoTrackIdleDelay = 0x1194;

	private short _autoTrackIdle;

	/// <summary>
	/// <c>Mech_ComponentNearestAim</c> (<c>0041b534</c>), vtable <c>+0x54</c> — over all
	/// <see cref="ComponentDamage.MechComponentCount"/> slots, the live one whose
	/// <see cref="ComponentWorldPosition"/> has the least <see cref="SimObject.AimOffset"/>, the
	/// first of equals; −1 when none is live. See docs/retail/simulation/rockets.md, "Spawning".
	/// </summary>
	public override short ComponentNearestAim(Vec3i from, (short X, short Y, short Z) attitude) {
		int best = NoAimOffset;
		short chosen = -1;

		for (short i = 0; i < ComponentDamage.MechComponentCount; i++) {
			if (_damage?.IsActive(i) != true) {
				continue;
			}

			int offset = AimOffset(ComponentPosition(i), from, attitude);
			if (offset < best) {
				best = offset;
				chosen = i;
			}
		}

		return chosen;
	}

	/// <summary>
	/// <c>Mech_NextTargetableComponent</c> (<c>00415558</c>), vtable <c>+0x80</c> — the Targeting
	/// Pod's rotation over <see cref="TargetingPodLock.ComponentRotation"/>, skipping any slot the
	/// machine has lost and wrapping at seven.
	///
	/// <para><b>The slot the cursor starts on is never tested.</b> The walk steps before it looks, and
	/// stops when it comes back to where it began — so a machine with only its current slot left
	/// answers "nothing", and a cursor of <see cref="TargetingPodLock.NoComponent"/> starts from
	/// position 0 and so gives rotation entry 1, component 4, rather than component 0. Both are the
	/// original's.</para>
	/// </summary>
	public override int NextTargetableComponent(int cursor, out int componentId) {
		int start = cursor < 0 ? 0 : cursor;
		int at = start;

		do {
			at++;
			if (at == TargetingPodLock.ComponentRotation.Length) {
				at = 0;
			}
		} while (at != start && !ComponentPresent(TargetingPodLock.ComponentRotation[at]));

		if (at == start) {
			componentId = TargetingPodLock.NoComponent;
			return TargetingPodLock.NoComponent;
		}

		componentId = TargetingPodLock.ComponentRotation[at];
		return at;
	}

	/// <summary>
	/// <c>Mech_ComponentPresent</c> (<c>00415540</c>), vtable <c>+0x84</c> — one entry of the
	/// occupancy array at <c>mech+0x20e</c>, which is <see cref="ComponentDamage.IsActive"/>.
	/// </summary>
	public override bool ComponentPresent(int componentId) => _damage?.IsActive(componentId) ?? false;
}
