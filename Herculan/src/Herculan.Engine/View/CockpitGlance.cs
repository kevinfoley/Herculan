using Herculan.Engine.Render;

namespace Herculan.Engine.View;

/// <summary>Which way the cockpit is glancing.</summary>
public enum GlanceSide {
	/// <summary>DBSIM's view 3, the mirrored side window, canvas origin (-320,0) — <c>[F9]</c>.</summary>
	Left = -1,

	/// <summary>DBSIM's view 0.</summary>
	Forward = 0,

	/// <summary>DBSIM's view 2, the authored side window, canvas origin (+320,0) — <c>[F10]</c>.</summary>
	Right = 1,
}

/// <summary>
/// The cockpit's sideways glance — how far the three-panel strip has slid toward one of the side
/// windows, and how it travels there over time. The horizontal sibling of <see cref="CockpitPan"/>.
///
/// <para><b>What the original does.</b> A glance is view command 4 (to view 2) or 5 (to view 3),
/// queued by <c>CockpitView_QueueViewCommand</c> (<c>0042a3f4</c>) and run by the same stepper as the
/// heads-down pan, <c>CockpitView_StepViewTransition</c> (<c>0042a9c0</c>), scrolling on x in steps of
/// <c>0x14</c> rather than on y in steps of 10. Command 6 returns to the forward view. The gates are
/// the queue's: a glance starts only from the forward view, the opposite glance's command from a
/// glance is a return, and the same one is ignored. See docs/retail/formats/cockpit-views.md, "View
/// switching".</para>
///
/// <para><b>Where this engine diverges.</b> Retail's glance slides one whole panel, which in its 4:3
/// frame takes the forward view entirely off screen. This engine shows all three panels at once, so
/// a wider window already shows part of each side window and a full panel's slide would run the
/// strip off the window's edge. The travel is therefore <i>reach</i>-limited: it stops when the side
/// panel's outer edge meets the window's edge (<see cref="CockpitScreenLayout.GlanceReachPanels"/>).
/// In a 4:3 window the reach is exactly one panel and this is retail's glance; in wider ones it is
/// shorter. This limit is this engine's invention; KNOWN_ISSUES.md carries it.</para>
///
/// <para>The speed follows <see cref="CockpitPan"/>'s reasoning: the original slide is untimed, so
/// only its step count is recoverable — 320 columns at <c>0x14</c> a step in the 320-wide mode is
/// 16 steps — and <see cref="DurationSeconds"/> is that count at one step per 60 Hz refresh for a
/// whole panel. A shorter reach takes proportionally less time, so the strip moves at one speed.</para>
/// </summary>
public sealed class CockpitGlance {
	/// <summary>Canvas columns the original advances per iteration of its sideways slide.</summary>
	public const int OriginalStepColumns = 0x14;

	/// <summary>Iterations that takes over the 320-column travel of the original's 320-wide mode.</summary>
	public const int OriginalStepCount = 320 / OriginalStepColumns;

	/// <summary>Fixed real time for one whole panel of travel, in seconds (~0.27).</summary>
	public const float DurationSeconds = OriginalStepCount / CockpitPan.ReferenceRefreshHz;

	private float _reachPanels = 1f;

	/// <summary>Which window the glance is heading for.</summary>
	public GlanceSide Requested { get; private set; }

	/// <summary>
	/// How far the strip has slid, in side-panel widths — negative toward the left window, positive
	/// toward the right, 0 at the forward view. Never past the reach the last <see cref="Advance"/>
	/// was given.
	/// </summary>
	public float OffsetPanels { get; private set; }

	/// <summary>
	/// True while the forward view is both shown and asked for — the original's "current view is 0",
	/// which is what the heads-down pan requires before it may start.
	/// </summary>
	public bool AtForward => Requested == GlanceSide.Forward && OffsetPanels == 0f;

	/// <summary>Whether the strip is still on its way to the window <see cref="Requested"/> names.</summary>
	public bool Sliding => OffsetPanels != (int)Requested * _reachPanels;

	/// <summary>
	/// The original's glance command toward <paramref name="side"/>, with its queue gate: from the
	/// forward view it starts that glance; from the opposite glance it returns to the forward view;
	/// from the same glance it does nothing.
	///
	/// <para>A return takes effect mid-slide, from wherever the strip is, as
	/// <see cref="CockpitPan.Request"/> does. A new glance waits for the strip to be fully back at the
	/// forward view (<see cref="AtForward"/>), because that is the original's gate — its current view
	/// only becomes 0 once the return has run — and it is what keeps a held hat from flicking straight
	/// from one side window to the other. Nor does one start when the window already shows the whole
	/// side window (reach 0), since there would be nothing to slide.</para>
	/// </summary>
	public void Command(GlanceSide side) {
		if (side == GlanceSide.Forward || side == Requested) {
			return;
		}

		if (Requested != GlanceSide.Forward) {
			Requested = GlanceSide.Forward;
		} else if (AtForward && _reachPanels > 0f) {
			Requested = side;
		}
	}

	/// <summary>The original's command 6: back to the forward view from either glance.</summary>
	public void Return() => Requested = GlanceSide.Forward;

	/// <summary>
	/// Advances the glance by one frame's worth of real time.
	/// </summary>
	/// <param name="deltaSeconds">Real time since the previous frame.</param>
	/// <param name="reachPanels">
	/// This frame's <see cref="CockpitScreenLayout.GlanceReachPanels"/>. Taken every frame so a window
	/// resized mid-glance moves the stop with it.
	/// </param>
	public void Advance(double deltaSeconds, float reachPanels) {
		_reachPanels = Math.Clamp(reachPanels, 0f, 1f);

		float target = (int)Requested * _reachPanels;
		float step = (float)(Math.Max(deltaSeconds, 0d) / DurationSeconds);
		float offset = OffsetPanels < target
			? Math.Min(OffsetPanels + step, target)
			: Math.Max(OffsetPanels - step, target);
		OffsetPanels = Math.Clamp(offset, -_reachPanels, _reachPanels);
	}
}
