using System.Numerics;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using ImGuiNET;

namespace Herculan.Engine.Host.Debugging;

/// <summary>
/// The debug settings panel, opened from the menu bar's "Debug" item. Closes on [Esc] or a click
/// outside the window; see <see cref="Simulator.WindowKeys.ReadMenuBarEscapeKey"/> for the shared [Esc]/menu-bar
/// state machine that also covers TweaksMenu. It is drawn over whatever view is up, cockpit or external.
///
/// <para>Built with ImGui, the toolkit the editor host already uses, rather than the game's own HUD
/// font: that font and its sprite banks are the original's art placed from the original's own layout
/// files, and bending them into a live settings panel would cost far more than adding a toolkit
/// already in the tree. Nothing the <i>game</i> draws goes through ImGui.</para>
///
/// <para>Everything it shows is read from live simulation state and everything it sets is a
/// host-side view option — nothing here feeds back into the sim, so leaving it open cannot change
/// what it is reporting. The two [Drain] buttons are the exception, and are test seams rather than
/// mechanics. What it sets is <see cref="DebugOptions"/> and what it measures is <see cref="DebugProbes"/>, both of
/// which the renderer and the frame stepper read whether or not the panel is reachable.</para>
/// </summary>
sealed class DebugPanel(DebugOptions options, DebugProbes probes) {
	/// <summary>Whether the panel is up. Set by the host's [Esc]/menu-bar logic (<see cref="Simulator.WindowKeys"/>), and
	/// cleared here on a click outside the window.</summary>
	public bool IsOpen { get; set; }

	/// <summary>Whether the panel was already open on the previous <see cref="Draw"/> call. Suppresses
	/// the click-outside-closes check for the one frame it opens on — that frame's own click (the menu
	/// bar's "Debug" item) is itself outside this window and would otherwise close it on arrival.</summary>
	private bool _wasOpenLastDraw;

	/// <summary>
	/// One line naming a simulation object, for the readouts. A HERC and a flyer have a type name; a
	/// structure is only ever a table index, which is how the game names them too.
	/// </summary>
	private static string Describe(SimObject? simObject) => simObject switch {
		null => "none",
		MechObject mech => mech.Name,
		FlyerObject flyer => flyer.Name,
		BaseObject structure => $"base type {structure.Type.Index}",
		var other => other.GetType().Name,
	};

	/// <summary>
	/// Builds this frame's ImGui draw list for the panel. Does nothing while <see cref="IsOpen"/> is
	/// false, so a host can call it unconditionally.
	/// </summary>
	public void Draw(in DebugPanelContext context, int windowHeight) {
		if (!IsOpen) {
			_wasOpenLastDraw = false;
			return;
		}

		bool justOpened = !_wasOpenLastDraw;
		_wasOpenLastDraw = true;

		float inset = ScaledImGui.Scaled(16f);
		ImGui.SetNextWindowPos(new Vector2(inset, inset), ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(340f), MathF.Min(windowHeight - 2f * inset, ScaledImGui.Scaled(560f))),
			ImGuiCond.FirstUseEver);
		ImGui.Begin("Debug");

		bool skeleton = options.DrawSkeleton;
		if (ImGui.Checkbox("Draw skeleton", ref skeleton)) {
			options.DrawSkeleton = skeleton;
		}

		bool steady = options.SteadyEye.Enabled;
		if (ImGui.Checkbox("Steady eye (pin cockpit height)", ref steady)) {
			options.SteadyEye.Enabled = steady;
		}

		ImGui.Separator();
		ImGui.Text("View: " + (!context.Piloting ? "free camera"
			: context.ExternalView ? "external" : "cockpit"));

		if (context.PilotMech is not { } pilotMech) {
			ImGui.TextWrapped("No player machine in this mission, so there is nothing to report.");
			CloseIfClickedOutside(justOpened);
			ImGui.End();
			return;
		}

		ImGui.Text($"Machine: {pilotMech.Name}");
		ImGui.Text($"Skeleton joints: {options.SkeletonJointCount}");
		ImGui.Text($"Posed geometry nodes: {context.PosedNodeCount}"
			+ " (visible in the external view, [V])");

		ImGui.Separator();
		if (pilotMech.Thread is { } thread) {
			var sequence = pilotMech.Animation?.Sequences[thread.Sequence];
			ImGui.Text($"Sequence: {thread.Sequence}  frame {thread.Frame}"
				+ (sequence != null ? $" / {sequence.FrameCount}" : ""));
			ImGui.Text($"Target: {thread.TargetSequence}  {(thread.AtTarget ? "reached" : "seeking")}"
				+ (thread.InTransition ? ", in transition" : ""));
			ImGui.Text($"Rate: {thread.Rate}  (mech AnimRate {pilotMech.AnimRate})");
			ImGui.Text($"Root motion: {(sequence?.GroundMovement == true ? "yes" : "no")}");
		} else {
			ImGui.TextWrapped("No animation data — this machine cannot walk.");
		}

		// The turret's own state. "Drawn" is where the animation actually put the eye, which is not
		// the same as the angle the sim holds — the twist sequence's keyframes are not evenly spaced,
		// so the two drift apart by up to about 7% across the travel. See
		// docs/retail/simulation/mech-locomotion.md.
		ImGui.Separator();
		var turret = pilotMech.EyeTransform.ToEuler();
		ImGui.Text($"Turret twist: {Degrees(pilotMech.TorsoTwistAngle):F1} deg"
			+ $" (limit {Degrees(pilotMech.Type.TorsoTwistLimit):F1}), rate {pilotMech.TorsoTwistRate}");
		ImGui.Text($"Turret pitch: {Degrees(pilotMech.TorsoPitchAngle):F1} deg"
			+ $" ({Degrees(pilotMech.Type.TorsoPitchMin):F1} to {Degrees(pilotMech.Type.TorsoPitchMax):F1})"
			+ $", rate {pilotMech.TorsoPitchRate}");
		ImGui.Text($"Drawn: twist {Degrees((short)(turret.Z - pilotMech.Heading)):F1} deg,"
			+ $" pitch {Degrees(turret.X):F1} deg");
		ImGui.Text(pilotMech.CenteringBody
			? $"Centring: body, onto {Degrees(pilotMech.CenterBodyReference):F1} deg"
			: pilotMech.CenteringTorso ? "Centring: turret" : "Centring: none");
		ImGui.Text("ATT: " + (!pilotMech.Weapons.AutoTrack ? "off"
			: pilotMech.Target != null ? "tracking"
			: $"idle, centring in {pilotMech.AutoTrackIdleTimer}"));

		ImGui.Separator();
		ImGui.Text($"Throttle: {pilotMech.Throttle} / {ThrottleTrack.Full}");
		ImGui.Text($"Speed: {pilotMech.Speed} raw, {pilotMech.DisplaySpeedKph} km/h"
			+ $" (ground {pilotMech.GroundSpeedKph} km/h)");
		ImGui.Text($"Gait: {(Math.Abs(pilotMech.Speed) >= pilotMech.Type.GaitThreshold ? "run" : "walk")}");
		ImGui.Text($"Step this frame: {probes.LastStepMeters:F3} m");

		ImGui.Separator();
		var position = pilotMech.Position;
		ImGui.Text($"Position: {position.X}, {position.Y}, {position.Z}");
		ImGui.Text($"Heading: {BinaryAngle.ToRadians(pilotMech.Heading) * (180f / MathF.PI):F1} deg");
		ImGui.Text($"Lean: pitch {BinaryAngle.ToRadians(pilotMech.Pitch) * (180f / MathF.PI):F1}, "
			+ $"roll {BinaryAngle.ToRadians(pilotMech.Roll) * (180f / MathF.PI):F1} deg");
		ImGui.Text($"Ground clearance: "
			+ $"{(position.Z - context.Terrain.HeightAtWorld(position.X, position.Y)) / WorldScale.WorldUnitsPerMeter:F2} m");

		// Reactor and Master Energy Pool. Nothing spends the pool yet except the shields — weapons
		// are a later milestone — so a machine standing still sits at a full 10000 and the cockpit's
		// energy bar stays hard right, which is correct rather than unwired. [Drain shields] is the
		// test seam that makes the trickle visible: watch the pool dip below full while the array
		// rebuilds at its 5-a-tick cap, then climb back once the deficit closes.
		var pods = pilotMech.Pods;
		var shields = pilotMech.Shields;
		ImGui.Separator();
		ImGui.Text($"Energy pool: {pilotMech.EnergyPool} / {MechObject.EnergyPoolMax}"
			+ $"  (reserve {MechObject.EnergyPoolReserve})");
		ImGui.Text($"Reactor output: {pilotMech.ReactorOutputRate}"
			+ $"  (base {MechObject.BaseReactorOutputRate}{(pods.EnergyPod ? ", Energy Pod fitted" : "")})");
		ImGui.Text($"Shields: {shields.Front} front, {shields.Rear} rear"
			+ $" of {shields.Max}{(pods.ShieldPod ? " (Shield Pod fitted)" : "")}");
		ImGui.Text($"  rings: {CockpitPalette.ShieldFacingCharge(shields.Front, shields.BaseMax)}"
			+ $" / {CockpitPalette.ShieldFacingCharge(shields.Rear, shields.BaseMax)} of 0x400");
		// The printed numbers are the balance and nothing else — they sum to 200 even on an empty array.
		var (retailFront, retailRear) = shields.Readout(rounded: false);
		var (roundedFront, roundedRear) = shields.Readout(rounded: true);
		ImGui.Text($"Balance: {shields.Balance} / {ShieldCharge.BalanceMax}"
			+ $"  — prints {retailFront} / {retailRear} (tweak {roundedFront} / {roundedRear})   [ and ] move it");

		// Both are test seams, not mechanics — see their own summaries. They are the only way to
		// watch either system refill until weapons and incoming fire exist to empty them for real.
		if (ImGui.Button("Drain shields")) {
			shields.Empty();
		}

		ImGui.SameLine();
		if (ImGui.Button("Drain energy pool")) {
			pilotMech.DrainEnergyPoolForTest();
		}

		// Target selection. Nothing is drawn for it yet — the HUD target box hangs off a gunsight
		// child that has not been traced — so this is the only place the selection is visible, and
		// the only way to see why something is not selectable: a candidate has to be known through
		// one of the two sensor routes before it can be picked at all (see Detection).
		ImGui.Separator();
		if (context.Targeting is { } targeting) {
			ImGui.Text($"Target: {Describe(targeting.Selected)}"
				+ "   Enter cycles, ' nearest, ; clears");

			int shortlisted = targeting.Shortlist.Count(entry => entry != null);
			ImGui.Text($"  shortlist ({shortlisted}): "
				+ string.Join(", ", targeting.Shortlist.Where(e => e != null).Select(Describe)));

			int known = 0;
			int painted = 0;
			SimObject? nearest = null;
			int nearestDistance = int.MaxValue;

			foreach (var candidate in context.World.Objects) {
				if (candidate.Side == pilotMech.Side || candidate.Removed
						|| candidate.AwaitingDeployment || candidate.TargetClass == TargetClass.None) {
					continue;
				}

				if (pilotMech.Detects(candidate)) {
					known++;
				}

				if (candidate.RadarVisible) {
					painted++;
				}

				int distance = pilotMech.Position.ApproxDistanceTo(candidate.Position);
				if (distance < nearestDistance) {
					nearest = candidate;
					nearestDistance = distance;
				}
			}

			ImGui.Text($"Radar: {(pilotMech.Scanner ? "ACTIVE" : "PASSIVE")} — [R] toggles"
				+ $"   contacts {known}, painted {painted}");

			// Why nothing can be selected is the question this panel gets asked, so it answers it
			// rather than leaving a row of zeroes: a hostile has to be a held contact or painted by
			// somebody's radar before Enter can do anything with it.
			if (nearest != null) {
				string reason = pilotMech.Detects(nearest)
						|| (nearest.RadarVisible && nearestDistance < Detection.RadarTargetingRange)
					? "selectable"
					: nearestDistance >= Detection.RadarTargetingRange ? "out of every sensor range"
					: pilotMech.Scanner ? "no line of sight" : "unknown — radar is PASSIVE";

				ImGui.Text($"  nearest hostile {Describe(nearest)} at "
					+ $"{nearestDistance / WorldScale.WorldUnitsPerMeter:F0} m — {reason}");
			} else {
				ImGui.Text("  no hostiles in the mission");
			}

			if (pilotMech.Weapons.MissileLock.Any(l => l)) {
				ImGui.Text("  missile lock: " + string.Join(", ",
					pilotMech.Weapons.MissileLock
						.Select((locked, type) => (locked, type))
						.Where(e => e.locked)
						.Select(e => $"type {e.type}")));
			}
		} else {
			ImGui.Text("Target: no selection (mission fields no player machine)");
		}

		// The armed weapon and what it does when [Space] is held. A beam is resolved and gone inside
		// the tick that fired it, so the tally is the only evidence one happened until the tracer
		// exists; the mount's own three numbers are what gate the next shot.
		ImGui.Separator();
		var mounts = pilotMech.Weapons;
		if (mounts.Slots.ElementAtOrDefault(mounts.Selected) is { } armed) {
			ImGui.Text($"Armed: {armed.Name} (row {armed.GaugeSlot + 1}"
				+ $"{(armed.Linked ? ", linked" : "")}{(mounts.SingleFire ? ", single fire" : "")})");
			ImGui.Text($"  charge {armed.Charge} of target {armed.ChargeTarget}"
				+ $"  — costs {armed.ShotCost} a shot   - and = move the target");
			ImGui.Text($"  refire {armed.RefireTimer} of {armed.RefireDelay}"
				+ $"  ({SimWorld.TickDelta} a tick)   range {armed.Range}");
			ImGui.Text($"  ready: {(armed.CanFire ? "yes" : "no")}");
		} else {
			ImGui.Text("Armed: nothing");
		}

		ImGui.Text($"Beams fired: {probes.BeamsFired}   hit: {probes.BeamsHit}   clipped at ground: {probes.BeamsGrounded}");
		if (probes.BeamsFired > 0) {
			ImGui.Text($"  last: reached {probes.LastBeamDistance} of {probes.LastBeamRange}"
				+ $"  — {probes.LastBeamTarget ?? "no hit"}");
		}

		ImGui.Text($"Shots in flight: {probes.ProjectilesLive}   impacts: {probes.ProjectileImpacts}");
		ImGui.Text($"Impact effects playing: {probes.EffectsLive}");
		if (probes.ProjectileImpacts > 0) {
			ImGui.Text($"  last struck: {probes.LastImpactTarget ?? "nothing"}");
		}

		ImGui.Text($"Damage taken: {pilotMech.DamageTaken}"
			+ $"   ({pilotMech.PenetratingHits} hits past shields)");

		if (pilotMech.Damage is { } components) {
			int standing = 0;
			int worst = 0;
			int worstAt = -1;
			for (int i = 0; i < components.Count; i++) {
				if (components.IsActive(i)) {
					standing++;
				}

				int taken = components.DamagePercent(i);
				if (taken > worst) {
					worst = taken;
					worstAt = i;
				}
			}

			ImGui.Text($"Components standing: {standing} of {components.Count}"
				+ (worstAt < 0 ? "" : $"   worst: #{worstAt} at {worst * 100 / 0x100}%"));
			ImGui.Text($"  legs: {(pilotMech.Immobilised ? "destroyed" : pilotMech.LegsCrippled ? "crippled" : "ok")}"
				+ $"   reactor: {pilotMech.Reactor}"
				+ $"   {(pilotMech.Destroyed ? "DEAD" : "alive")}");
		}

		// The bob itself. A retail stride is supposed to swing the eye 0.24-0.42 m (see
		// MechObject.EyePosition), so a swing far outside that band is the measurement that turns the
		// complaint into a lead.
		ImGui.Separator();
		ImGui.Text($"Eye rise: {probes.EyeRiseMeters:F3} m");
		if (probes.EyeRiseMin <= probes.EyeRiseMax) {
			ImGui.Text($"  seen {probes.EyeRiseMin:F3} .. {probes.EyeRiseMax:F3} m");
			ImGui.Text($"  swing {probes.EyeRiseMax - probes.EyeRiseMin:F3} m (retail stride: 0.24-0.42)");
		}

		if (ImGui.Button("Reset swing")) {
			probes.ResetSwing();
		}

		CloseIfClickedOutside(justOpened);
		ImGui.End();
	}

	/// <summary>Closes the panel on a left-click that lands outside it — everything it holds is a
	/// view option or a live readout (see the class summary), so there is nothing to lose by
	/// dismissing it this way, unlike TweaksMenu's Save/Cancel. Call once per <see cref="Draw"/>,
	/// while the window is still current (before <c>ImGui.End()</c>).</summary>
	private void CloseIfClickedOutside(bool justOpened) {
		if (!justOpened && !ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)
				&& ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
			IsOpen = false;
		}
	}

	/// <summary>A binary angle in degrees, for the panel's readouts.</summary>
	private static float Degrees(int binaryAngle) => BinaryAngle.ToRadians(binaryAngle) * (180f / MathF.PI);
}

/// <summary>
/// The live state <see cref="DebugPanel.Draw"/> reports on, handed in each frame rather than held by
/// the panel: all of it belongs to the host's loop, and the panel only reads it.
/// </summary>
/// <param name="Piloting">Whether the player is flying the machine rather than the free camera.</param>
/// <param name="ExternalView">Whether the chase camera is up rather than the cockpit.</param>
/// <param name="PilotMech">The player's machine, or null if the mission fields none.</param>
/// <param name="Targeting">The player's target selection, or null with no machine to select from.</param>
/// <param name="World">The running simulation, for the sensor readouts.</param>
/// <param name="PosedNodeCount">Geometry segments the player's model is drawn as.</param>
/// <param name="Terrain">The zone's heightmap, for the ground-clearance readout.</param>
readonly record struct DebugPanelContext(
	bool Piloting,
	bool ExternalView,
	MechObject? PilotMech,
	TargetSelection? Targeting,
	SimWorld World,
	int PosedNodeCount,
	HeightGrid Terrain);
