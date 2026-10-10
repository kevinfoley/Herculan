using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Host.Debugging;

/// <summary>
/// What the debug panel reports that has to be measured as it happens rather than read when the panel is drawn:
/// the walk, sampled every frame, and the shots, sampled every tick. Reading the simulation is all it does.
/// </summary>
sealed class DebugProbes {
	// What the panel reports about the walk. The eye's rise above the machine's own origin is the
	// whole of the cockpit bob (see MechObject.EyePosition), so tracking its swing turns "it feels
	// wrong" into a number that can be checked against the 0.24-0.42 m a retail stride is supposed
	// to cover.
	private Vec3i _lastMechPosition;
	private bool _haveLastPosition;

	public float EyeRiseMeters { get; private set; }
	public float EyeRiseMin { get; private set; } = float.MaxValue;
	public float EyeRiseMax { get; private set; } = float.MinValue;
	public float LastStepMeters { get; private set; }

	public int ProjectilesLive { get; private set; }
	public int EffectsLive { get; private set; }
	public int ProjectileImpacts { get; private set; }
	public string? LastImpactTarget { get; private set; }

	public int BeamsFired { get; private set; }
	public int BeamsHit { get; private set; }
	public int BeamsGrounded { get; private set; }
	public int LastBeamRange { get; private set; }
	public int LastBeamDistance { get; private set; }
	public string? LastBeamTarget { get; private set; }

	/// <summary>
	/// Takes this frame's walk measurements. Called every frame whether or not the panel is open, so
	/// opening it mid-stride shows the stride rather than starting from nothing — and so the min/max
	/// swing is a record of the walk, not of how long the panel has been up.
	/// </summary>
	public void Sample(MechObject? mech) {
		if (mech == null) {
			return;
		}

		EyeRiseMeters = (mech.EyePosition.Z - mech.Position.Z) / WorldScale.WorldUnitsPerMeter;
		EyeRiseMin = Math.Min(EyeRiseMin, EyeRiseMeters);
		EyeRiseMax = Math.Max(EyeRiseMax, EyeRiseMeters);

		if (_haveLastPosition) {
			var step = mech.Position;
			LastStepMeters = new Vector2(
				(step.X - _lastMechPosition.X) / WorldScale.WorldUnitsPerMeter,
				(step.Y - _lastMechPosition.Y) / WorldScale.WorldUnitsPerMeter).Length();
		}

		_lastMechPosition = mech.Position;
		_haveLastPosition = true;
	}

	/// <summary>Forgets the eye's swing so far, for the panel's [Reset swing].</summary>
	public void ResetSwing() {
		EyeRiseMin = float.MaxValue;
		EyeRiseMax = float.MinValue;
	}

	/// <summary>
	/// Records the beams the tick just finished resolved. Called once per <see cref="SimWorld.Tick()"/>
	/// rather than once per frame, because that list is cleared at the top of each tick and a frame
	/// can cover several.
	///
	/// <para>This is the only thing that can currently see a shot happen: beams have no visual yet, so
	/// the tally below is what tells you whether the trigger reached the mounts, whether the ray found
	/// anything, and how far it got.</para>
	/// </summary>
	public void SampleBeams(SimWorld world) {
		foreach (var beam in world.Beams) {
			BeamsFired++;
			LastBeamRange = beam.Range;
			LastBeamDistance = beam.Distance;
			LastBeamTarget = beam.HitObject switch {
				MechObject mech => mech.Name,
				null => beam.GroundHit != null ? "ground" : null,
				var other => other.GetType().Name,
			};

			if (beam.HitObject != null) {
				BeamsHit++;
			}

			if (beam.GroundHit != null) {
				BeamsGrounded++;
			}
		}

		ProjectilesLive = world.Projectiles.Count;
		EffectsLive = world.Effects.ImpactEffects.Count;

		foreach (var impact in world.Impacts) {
			ProjectileImpacts++;
			LastImpactTarget = impact.HitObject switch {
				MechObject mech => mech.Name,
				null => impact.GroundHit != null ? "ground" : null,
				var other => other.GetType().Name,
			};
		}
	}
}
