using HercWorks.Core.Data.File.Dbsim;
using Herculan.Es1.Host;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The frame loop's pacing (docs/es1/simulation/fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40,
/// docs/es1/simulation/cockpit-views.md#cockpitview_processviewcommand-000226f0): a tick every three PC timer
/// interrupts (the engine's departure, plan decision 2), and the frame's end held to the retraces of a 70 Hz monitor,
/// on the clock and on constructed views whose canvas origins are the retail <c>.VUE</c> files'.
/// </summary>
public class Es1FramePacingTests {
	private const long Second = TimeSpan.TicksPerSecond;

	/// <summary>
	/// A host that advances 0.1 ms at a time runs a tick every three interrupts, 25 ms, each counted as <c>0x40</c>,
	/// and each frame's end waits for the next retrace start, which comes before the next tick. Its steps lie half a step
	/// off the whole tenths of a millisecond, where every seventh retrace starts, so no tick falls on a retrace start.
	/// Catches retail's wait of two interrupts coming back, which plays at 1.5 times real time, and the frame's retrace
	/// wait dropped.
	/// </summary>
	[Fact]
	public void TicksComeThreeInterruptsApartWithTheirRetraceWaitBetween() {
		var (_, frames) = Start();
		var pacer = new ClockPacer();
		frames.Advance(TimeSpan.FromTicks(Second / 20000), pacer);

		var run = Run(frames, pacer, Second * 3 / 10, Second / 10000);

		Assert.True(run.Ticks.Count >= 10);
		Assert.All(pacer.Deltas, delta => Assert.Equal(0x40, delta));
		for (int i = 1; i < run.Ticks.Count; i++) {
			Assert.InRange(run.Ticks[i] - run.Ticks[i - 1], 3 * Second / 120 - Second / 10000, 3 * Second / 120 + Second / 10000);
			var wait = Assert.Single(run.Waits, end => end > run.Ticks[i - 1] && end < run.Ticks[i]);
			Assert.Equal(FirstRetraceStartAfter(run.Ticks[i - 1]), wait);
		}
	}

	/// <summary>
	/// A tick at 28.6 ms, just after the retrace at 2/70 s, waits until the next retrace, 3/70 s, 14.3 ms on: still
	/// waiting a <see cref="TimeSpan"/> tick before it, and done at it. Catches the wait dropped, and a wait counted from
	/// the frame's end (which would end 14.29 ms after it, past the retrace) rather than from the monitor's retraces.
	/// </summary>
	[Fact]
	public void AFrameEndingJustAfterARetraceWaitsUntilTheNextOne() {
		var (_, frames) = Start();
		var pacer = new ClockPacer();
		long retrace3 = 428572;

		frames.Advance(TimeSpan.FromTicks(286000), pacer);
		Assert.Single(pacer.Deltas);
		Assert.Equal(retrace3, frames.WaitEnds);

		frames.Advance(TimeSpan.FromTicks(retrace3 - 1 - 286000), pacer);
		Assert.NotNull(frames.WaitEnds);

		frames.Advance(TimeSpan.FromTicks(1), pacer);
		Assert.Null(frames.WaitEnds);
		Assert.Single(pacer.Deltas);
	}

	/// <summary>
	/// A pan down's 41 steps and the flip after them wait for 42 retraces in a row, the first the next to start after
	/// the tick, while no tick runs. Catches a slide's waits counted in host frames or from the tick rather than on the
	/// monitor's retraces, and a step's wait ended by the retrace the step before waited for.
	/// </summary>
	[Fact]
	public void ASlidesStepsFallOnConsecutiveRetraceStarts() {
		var (views, frames) = Start();
		var pacer = new ClockPacer();
		views.QueueViewCommand(0);

		var run = Run(frames, pacer, Second, Second / 2000);

		Assert.Equal(1, views.CurrentView);
		// The first frame's end waits once and stages the pan; the second's runs the slide.
		long slideTick = run.Ticks[1];
		var slide = run.Waits.Where(end => end > slideTick).Take(42).ToList();
		long first = FirstRetraceStartAfter(slideTick);
		long index = (long)Math.Round(first * 70.0 / Second);
		Assert.Equal(Enumerable.Range(0, 42).Select(step => RetraceStart(index + step)), slide);
		Assert.DoesNotContain(run.Ticks, tick => tick > slideTick && tick < slide[^1]);
	}

	/// <summary>The host time retrace <paramref name="index"/> starts at, 1/70 s apart from 0, rounded up to a tick.</summary>
	private static long RetraceStart(long index) => (index * Second + 69) / 70;

	/// <summary>The first retrace start after <paramref name="time"/>.</summary>
	private static long FirstRetraceStartAfter(long time) {
		long index = (long)Math.Floor(time * 70.0 / Second);
		while (RetraceStart(index) <= time) {
			index++;
		}

		return RetraceStart(index);
	}

	/// <summary>
	/// The loop advanced <paramref name="step"/> at a time for <paramref name="length"/> of host time; returns the host
	/// time of each tick and the end of each wait the frame's ends made.
	/// </summary>
	private static (List<long> Ticks, List<long> Waits) Run(Es1FrameLoop frames, ClockPacer pacer, long length, long step) {
		var ticks = new List<long>();
		var waits = new List<long>();
		long? waiting = null;
		while (frames.HostTime < length) {
			int before = pacer.Deltas.Count;
			frames.Advance(TimeSpan.FromTicks(step), pacer);
			if (pacer.Deltas.Count != before) {
				ticks.Add(frames.HostTime);
			}

			if (frames.WaitEnds != waiting && waiting is { } ended) {
				waits.Add(ended);
			}

			waiting = frames.WaitEnds;
		}

		return (ticks, waits);
	}

	/// <summary>The views as the mission starts them, and the frame loop on them with no camera.</summary>
	private static (Es1ViewManager Views, Es1FrameLoop Frames) Start() {
		var records = new (int X, int Y)[] { (0, 0), (0, 197), (320, 0), (-320, 0) }
			.Select(origin => new Vue.Entry {
				ViewportX0 = 0, ViewportY0 = 0, ViewportX1 = 319, ViewportY1 = 140, CenterX = -160, CenterY = -70,
				CanvasOriginX = origin.X, CanvasOriginY = origin.Y,
			})
			.ToArray();
		var clips = records.Select(_ => Es1ClipBlock.FromRect(0, 0, 319, 140)).ToArray();
		var files = new Es1CockpitViews("TEST", records, clips, new HercWorks.Core.Data.File.Dyn.DynamixBitmap?[4], null);
		var views = new Es1ViewManager(files, new Es1VideoMemory());
		var frames = new Es1FrameLoop(views, () => { });
		frames.StartMission();
		return (views, frames);
	}

	/// <summary>A simulation clock as the frame loop's pacer, keeping each tick's <c>SimTickDelta</c>.</summary>
	private sealed class ClockPacer : IEs1FramePacer {
		private readonly Es1SimClock _clock = new();

		public List<short> Deltas { get; } = [];

		public bool Frozen => false;

		public Es1SimulationRunner.Frame AdvanceHostFrame(TimeSpan elapsed) {
			if (!_clock.AdvanceHostFrame(elapsed)) {
				return Es1SimulationRunner.Frame.NoTick;
			}

			Deltas.Add(_clock.TickDelta);
			return Es1SimulationRunner.Frame.Ticked;
		}

		public void Hold(TimeSpan elapsed) => _clock.Hold(elapsed);
	}
}
