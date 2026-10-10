using Herculan.Engine.Numerics;
using Herculan.Engine.Render;

namespace Herculan.Engine.View;

/// <summary>
/// The cockpit's damage shake — what a hit on the player's own cockpit, or the landing at the bottom
/// of a long slide, does to the view for <c>0x3c</c> coarse ticks (0.96 s).
///
/// <para><b>It is the projection centre again</b>, the same mechanism as
/// <see cref="CockpitViewKick"/> and not a camera move: <c>CockpitView_SetShakeBand</c>
/// (<c>0042d2f8</c>) seeds a vertical band at the resting view offset and
/// <c>CockpitView_StepShake</c> (<c>0042d4a8</c>) walks the offset inside it once a frame. The
/// derivation is in docs/retail/rendering/cockpit-canopy-palette.md, "The damage shake".</para>
///
/// <para><b>The walk never settles, and it never reaches the band's limits.</b> Each step moves
/// toward whichever of the two limits is <i>farther</i> away, and which one that is flips at the
/// middle — so the walk reverses on every crossing, and moving outward requires already being on the
/// near side. With <see cref="BandPixels"/> and steps of 0-4 the offset starts at the low limit and
/// thereafter ranges over 1 to 8: the band is wider than the excursion it produces, which is why
/// reading the amplitude as the travel overstates the effect.</para>
///
/// <para><b>The palette flash runs beside it.</b> The original swaps the whole live palette to the
/// theater's <c>IMPACT&lt;n&gt;.DPL</c> — a heavy red shift of the theater's own — and alternates the
/// two every 0-9 coarse ticks. <see cref="FlashActive"/> is that switch; the host follows it into
/// <see cref="SceneRenderer.ImpactPaletteActive"/> and the canopy's own flash buffer. See
/// <see cref="Scene.ImpactFlash"/> for what does and does not follow a swap in this renderer.</para>
///
/// <para><b>Retriggering does not compound — it stops the shake.</b> A second trigger inside the
/// window clears the band and restores the palette, then re-arms only if no shake was running, which
/// it was. So the timer is extended while the view goes still for the rest of it. That is the
/// original's own behaviour, not a simplification; see KNOWN_ISSUES.md.</para>
///
/// <para><b>What the flash does after a restart depends on its phase</b>, as in the original: the
/// restore leaves the toggle's swap partner alone, so a restart on a theater phase carries on
/// flashing and one on an impact phase leaves the theater palette in both slots, and the flash stops.
/// <see cref="FlashSurvivesRestart"/> is the
/// <see cref="Settings.TweakSettingDefinitions.FlashThroughSecondHit"/> tweak, under which the flash
/// carries on from either phase.</para>
///
/// <para>The original's view slides can also re-seed a disarmed band, which this class has no
/// counterpart for. See docs/retail/rendering/cockpit-canopy-palette.md, "The damage shake".</para>
/// </summary>
public sealed class CockpitHitShake {
	/// <summary>
	/// How long one shake lasts, in seconds — the original's <c>0x3c</c> coarse ticks at
	/// <see cref="CoarseTickSeconds"/> each, the same window the step kick runs over.
	/// </summary>
	public const double DurationSeconds = 0x3c * CoarseTickSeconds;

	/// <summary><c>Time_GetCoarseTicks</c>' unit: <c>GetTickCount() >> 4</c>, so 16 ms.</summary>
	private const double CoarseTickSeconds = 0.016;

	/// <summary>
	/// The band's height in the art's pixels — <c>Cockpit_StartHitShake</c>'s
	/// <c>5 &lt;&lt; VideoMode_YCoordShift</c>, which is 10 in the 640x480 modes this engine draws at.
	/// </summary>
	public const int BandPixels = 10;

	/// <summary>The step argument's bound, <c>Cockpit_HitShakeTick</c>'s <c>rand() % 5</c>.</summary>
	private const int StepBound = 5;

	/// <summary>The toggle interval's bound in coarse ticks, the same function's <c>rand() % 10</c>.</summary>
	private const short ToggleTickBound = 10;

	private readonly SimRandom _random;

	/// <param name="random">
	/// The generator the step and the flash interval draw on — pass the world's
	/// <see cref="Sim.SimWorld.PresentationRandom"/>, the block both of the original's functions take.
	/// Omitted, it is a private one at the vanilla state.
	/// </param>
	public CockpitHitShake(SimRandom? random = null) {
		_random = random ?? new SimRandom();
	}

	private bool _running;
	private double _remainingSeconds;

	private bool _toggleArmed;
	private double _toggleRemainingSeconds;

	/// <summary>
	/// Whether the palette <c>Palette_ToggleImpact</c> would swap in is the impact one — the saved slot
	/// at <c>004cfd8c</c>, which the arm fills with the palette it replaces and the restore never
	/// touches.
	/// </summary>
	private bool _swapPartnerIsImpact;

	private bool _banded;
	private int _offset;

	private int _lastHits;

	/// <summary>
	/// This shake's current offset, in the art's device pixels. <b>It is added to the projection
	/// centre</b>, where <see cref="CockpitViewKick.OffsetPixels"/> is subtracted from it: the shake
	/// drives the view offset the original installs directly, and the kick drives the render target's
	/// own centre offset, which the projection install subtracts. Both sign conventions are the
	/// original's, and the two effects are live at the same time.
	/// </summary>
	public int OffsetPixels => _banded ? _offset : 0;

	/// <summary>
	/// Whether the scene and the canopy draw through the theater's impact palette this frame. It
	/// alternates on its own timer rather than with the view's walk, so the flash and the shake are
	/// not in step — which is the original's arrangement, and why the arm gate below tests this
	/// timer and not the shake's.
	/// </summary>
	public bool FlashActive { get; private set; }

	/// <summary>
	/// The <see cref="Settings.TweakSettingDefinitions.FlashThroughSecondHit"/> tweak, which the host
	/// sets: a restart over a running shake hands the toggle the impact palette whatever phase it
	/// landed on, so the flash carries on for the rest of the window. Off, the restart leaves the swap
	/// partner where the toggles left it, as the original does.
	/// </summary>
	public bool FlashSurvivesRestart { get; set; }

	/// <summary>
	/// Advances the shake and starts one on each new cockpit hit. <paramref name="cockpitHits"/> is
	/// <see cref="Sim.MechObject.CockpitHits"/>; as with the step kick it is a running count rather
	/// than a flag, so a hit the simulation took between two frames is not missed.
	/// </summary>
	public void Update(double deltaSeconds, int cockpitHits) {
		if (cockpitHits != _lastHits) {
			_lastHits = cockpitHits;
			Start();
		}

		Tick(deltaSeconds);
	}

	/// <summary>
	/// <c>Cockpit_StartHitShake</c> (<c>00434010</c>). Public for a caller that already knows a hit
	/// landed; <see cref="Update"/> is the ordinary route in.
	/// </summary>
	public void Start() {
		// Restarting over a running shake undoes the running one first — and then finds the toggle
		// timer still armed, so it does not seed a new band. The offset stays where the clear put it.
		if (_running) {
			FlashActive = false;
			if (FlashSurvivesRestart) {
				_swapPartnerIsImpact = true;
			}

			ClearBand();
		}

		_running = true;
		_remainingSeconds = DurationSeconds;

		if (!_toggleArmed) {
			_toggleArmed = true;
			_toggleRemainingSeconds = NextToggleSeconds();

			// Palette_ActivateImpact (0042ea44): the impact palette in, and the one it replaced saved
			// as the toggle's partner.
			_swapPartnerIsImpact = FlashActive;
			FlashActive = true;
			SetBand();
		}
	}

	/// <summary>
	/// <c>Cockpit_HitShakeTick</c> (<c>0043408c</c>) — the per-frame half, which the original's
	/// cockpit pass runs immediately before the step kick's own tick.
	/// </summary>
	private void Tick(double deltaSeconds) {
		if (_running) {
			_remainingSeconds -= deltaSeconds;

			if (_remainingSeconds <= 0) {
				_running = false;
				return;
			}

			_toggleRemainingSeconds -= deltaSeconds;
			if (_toggleRemainingSeconds <= 0) {
				// Palette_ToggleImpact (0042ea70) swaps the active palette with its partner, which after
				// a restart on an impact phase is the theater palette too.
				(FlashActive, _swapPartnerIsImpact) = (_swapPartnerIsImpact, FlashActive);
				_toggleRemainingSeconds = NextToggleSeconds();
			}

			StepBand(_random.NextMasked(0xffff) % StepBound);
			return;
		}

		// The frame after the window closes puts both halves back.
		if (_toggleArmed) {
			FlashActive = false;
			_toggleArmed = false;
			ClearBand();
		}
	}

	/// <summary>
	/// Drops a shake in progress and everything it put in place — the original's view mode 4, which
	/// clears the end tick outright so leaving the cockpit ends a shake rather than pausing it. The
	/// host calls it every frame an outside view is up, in place of <see cref="Update"/>.
	/// <paramref name="cockpitHits"/> is the same count <see cref="Update"/> takes: it is taken as
	/// seen, so a hit that lands while outside never shakes the cockpit on the way back in, as
	/// <c>Cockpit_StartHitShake</c> returns at once in view mode 4.
	/// </summary>
	public void Reset(int cockpitHits) {
		_lastHits = cockpitHits;
		_running = false;
		_toggleArmed = false;
		FlashActive = false;
		ClearBand();
	}

	/// <summary>
	/// <c>CockpitView_SetShakeBand</c>'s tail, for the forward cockpit view: the band runs from the
	/// resting offset to <see cref="BandPixels"/> past it, and the walk starts at the resting end.
	/// </summary>
	private void SetBand() {
		_banded = true;
		_offset = 0;
	}

	/// <summary>
	/// <c>CockpitView_ClearShake</c> (<c>0042d624</c>) — the offset back to rest and the band
	/// disarmed, which is what makes <see cref="StepBand"/> a no-op until one is seeded again.
	/// </summary>
	private void ClearBand() {
		_banded = false;
		_offset = 0;
	}

	/// <summary>
	/// <c>CockpitView_StepShake</c>'s arithmetic: move toward the farther limit by up to
	/// <paramref name="step"/>, and no farther than that limit. Landing exactly on a limit is what
	/// turns the walk round, since the other one is then the farther of the two.
	/// </summary>
	private void StepBand(int step) {
		if (!_banded) {
			return;
		}

		int belowTop = BandPixels - _offset;
		int aboveBottom = _offset;

		int delta = aboveBottom < belowTop
			? Math.Min(step, belowTop)
			: -Math.Min(step, aboveBottom);

		_offset += delta;
	}

	/// <summary>The next flash interval — <c>rand() % 10</c> coarse ticks, so 0 to 144 ms.</summary>
	private double NextToggleSeconds() =>
		_random.NextMasked(0xffff) % ToggleTickBound * CoarseTickSeconds;
}
