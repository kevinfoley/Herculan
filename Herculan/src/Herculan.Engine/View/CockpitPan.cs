namespace Herculan.Engine.View;

/// <summary>
/// The cockpit's vertical pan between the forward view and the heads-down display — how far down the
/// cockpit canvas the display window currently sits, and how it travels there over time.
///
/// <para>In the original the slide is <c>CockpitView_StepViewTransition</c> (<c>0042a9c0</c>), run
/// the frame after the view command is processed and followed by a two-frame cooldown — see
/// docs/retail/simulation/cockpit-views.md#heads-down-pan--cockpitview_stepviewtransition-0042a9c0.</para>
///
/// <para>The speed is this engine's. The original's slide is an untimed loop, so only its step
/// count is recoverable: 10 canvas rows a step over the 237-row travel of its 320-wide mode, 24
/// steps. <see cref="DurationSeconds"/> is that count at one step per 60 Hz refresh, so the pan
/// takes the same real time on every machine. It is a duration rather than a rate because the
/// original's step is in device rows, so its 640x480 mode covers the same visual distance in twice
/// as many steps. The motion is continuous rather than in 10-row jumps.</para>
/// </summary>
public sealed class CockpitPan {
	/// <summary>Canvas rows the original advances per iteration of its slide loop (<c>0042a9c0</c>).</summary>
	public const int OriginalStepRows = 10;

	/// <summary>
	/// Iterations that takes over the 237-row travel of the original's 320-wide video mode —
	/// <c>for (i = 0; i &lt; 237; i += 10)</c>, i.e. 24, before the loop's final remainder step.
	/// </summary>
	public const int OriginalStepCount = 24;

	/// <summary>Refresh the step count is paced against to turn it into a real-time duration.</summary>
	public const float ReferenceRefreshHz = 60f;

	/// <summary>Fixed real time for the full travel in either direction, in seconds (0.4).</summary>
	public const float DurationSeconds = OriginalStepCount / ReferenceRefreshHz;

	/// <summary>
	/// Creates a pan over <paramref name="travelRows"/> device rows of cockpit canvas — normally
	/// <see cref="Content.CockpitViewGeometry.HeadsDownTravelY"/>. Starts parked at the forward view.
	/// </summary>
	public CockpitPan(int travelRows) => TravelRows = Math.Max(travelRows, 0);

	/// <summary>Distance between the forward and heads-down canvas origins, in device rows.</summary>
	public int TravelRows { get; }

	/// <summary>
	/// How far down the canvas the display window currently sits, in device rows: 0 at the forward
	/// view, <see cref="TravelRows"/> at the heads-down display, anywhere between during the pan.
	/// </summary>
	public float OffsetRows { get; private set; }

	/// <summary>Which view the pan is heading for — true for the heads-down display.</summary>
	public bool HeadsDownRequested { get; private set; }

	/// <summary>True while the window is neither fully forward nor fully heads-down.</summary>
	public bool IsPanning => OffsetRows > 0f && OffsetRows < TravelRows;

	/// <summary>
	/// True once the window has fully arrived at the heads-down display — the point at which the
	/// original advances its current-view index and would start accepting an "up" command again.
	/// </summary>
	public bool AtHeadsDown => TravelRows > 0 && OffsetRows >= TravelRows;

	/// <summary>True while the window is fully at the forward view.</summary>
	public bool AtForward => OffsetRows <= 0f;

	/// <summary>
	/// The original's current view index (<c>+0x14</c>) reading the heads-down display: set when the pan down
	/// arrives and cleared when the pan up arrives, so it holds through the pan back up. A pan reversed before
	/// it arrives leaves it as it was.
	/// </summary>
	public bool HeadsDownViewIndex { get; private set; }

	/// <summary>
	/// Asks for a view. Unlike <c>CockpitView_QueueViewCommand</c>'s gate on having fully arrived in
	/// the other view, a request mid-pan simply reverses the travel from where it is: the original's
	/// gate exists because its slide is an uninterruptible in-frame loop, and there is nothing to
	/// protect once the pan is a per-frame interpolation.
	/// </summary>
	public void Request(bool headsDown) => HeadsDownRequested = headsDown;

	/// <summary>Advances the pan by one frame's worth of real time.</summary>
	public void Advance(double deltaSeconds) {
		if (TravelRows <= 0) {
			return;
		}

		float step = (float)(deltaSeconds / DurationSeconds) * TravelRows;
		OffsetRows = Math.Clamp(HeadsDownRequested ? OffsetRows + step : OffsetRows - step, 0f, TravelRows);
		if (AtHeadsDown) {
			HeadsDownViewIndex = true;
		} else if (AtForward) {
			HeadsDownViewIndex = false;
		}
	}
}
