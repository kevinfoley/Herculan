using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Host;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The display's pages and the view changes on them (docs/es1/simulation/cockpit-views.md#switching-views,
/// docs/es1/rendering/screen-pages.md#the-video-memory), on constructed views whose canvas origins are the retail
/// <c>.VUE</c> files' {0, 0}, {0, 197}, {320, 0} and {−320, 0}.
/// </summary>
public class Es1ViewChangeTests {
	/// <summary>Each view's canopy is filled with this index plus the view.</summary>
	private const byte CanopyIndex = 10;

	/// <summary>
	/// A pan down slides the page on the monitor down 5 rows a retrace by the heads-down view's 197: to row 200, the
	/// steps' overshoot, then back to 197, after which the frame's end shows the other page at its own 197. Hand-worked
	/// from the loop of <c>CockpitView_StepViewTransition</c>. Catches the step changed, the last step dropped or taken
	/// inside the loop, the slide put on the drawn page, and a step that does not wait its retrace.
	/// </summary>
	[Fact]
	public void PanDownSlidesFiveRowsARetraceAndEndsOnBothPagesAt197() {
		var (views, frames) = Start();
		RunFrame(frames);
		views.QueueViewCommand(0);
		RunFrame(frames);
		Assert.True(views.Staged);

		var shown = RunFrame(frames);

		var expected = Enumerable.Range(1, 40).Select(step => (0, 5 * step)).Append((0, 197)).Append((320, 197));
		Assert.Equal(expected, shown);
		Assert.Equal(1, views.CurrentView);
	}

	/// <summary>
	/// A glance right whose frame's end flips page 2 onto the monitor is held there, until the next flip puts page 0 on it;
	/// it then slides page 0 right 10 columns a retrace to 320, the far half, shows page 2 and swaps the pair, and copies
	/// page 2 back onto page 0. Catches the gate read from the drawn page or inverted, the slide's count or direction,
	/// and the pair left unswapped or the far half not copied back.
	/// </summary>
	[Fact]
	public void GlanceWaitsForPageZeroThenSlidesToTheFarHalfAndCopiesItBack() {
		var (views, frames) = Start();
		var display = views.Display;
		RunFrame(frames);
		RunFrame(frames);
		Assert.Equal(0, display.PairVisible);

		views.QueueViewCommand(4);
		RunFrame(frames);
		Assert.Equal(2, display.PairVisible);
		Assert.False(views.Staged);
		Assert.Equal(4, views.PendingCommand);
		RunFrame(frames);
		Assert.True(views.Staged);

		var shown = RunFrame(frames);

		var slide = Enumerable.Range(1, 32).Select(step => (10 * step, 0)).Append((320, 0));
		Assert.Equal(slide.Append((320, 0)).Append((0, 0)), shown);
		Assert.Equal(2, views.CurrentView);
		Assert.Equal(0, display.PairVisible);
		Assert.Equal((0, 0), display.PageOrigin(0));
		// Below the view's window, the side canopy that the glance blitted into page 2 is on page 0 too.
		Assert.Equal(CanopyIndex + 2, display.Cell(0, 5, 150));
		Assert.Equal(CanopyIndex + 2, display.Cell(2, 5, 150));
	}

	/// <summary>
	/// A copy between pages moves the source page's rect by the offset onto the draw page, both ends included, as the
	/// pan down's {0, 3, 319, 199} at (0, 197) lands on rows 200 to 396. Catches the offset added to the source rather than
	/// the target, and the rect's far row or column dropped.
	/// </summary>
	[Fact]
	public void CopyMovesTheSourceRectByTheOffsetOntoTheDrawPage() {
		var display = Pages();
		display.DrawPage = 2;
		display.FillRect(0, 3, 319, 3, 7);
		display.FillRect(319, 199, 319, 199, 8);
		display.SourcePage = 2;
		display.DrawPage = 0;

		display.CopyRect(0, 3, 319, 199, 0, 197);

		Assert.Equal(7, display.Cell(0, 0, 200));
		Assert.Equal(7, display.Cell(0, 319, 200));
		Assert.Equal(8, display.Cell(0, 319, 396));
		Assert.Equal(0, display.Cell(0, 0, 199));
		Assert.Equal(0, display.Cell(0, 0, 397));
	}

	/// <summary>
	/// The world's cells and the 2D drawing share one store: a fill over part of the world replaces it, a copy carries
	/// both the indices and the world cells, and the world layer is copied under them a row at a time. Catches a world
	/// pass that marks outside its clip block, a fill that leaves world cells, and a copy that moves the cells without
	/// the world pixels.
	/// </summary>
	[Fact]
	public void LastWriterWinsAndACopyCarriesTheWorldAndItsPixels() {
		var display = Pages();
		var layer = new RecordingLayer();
		display.Layer = layer;
		display.DrawPage = 2;
		display.DrawWorld(new Es1ViewWindow(0, 0, 9, 1, 0, 0), Es1ClipBlock.FromRect(0, 0, 4, 1));
		display.FillRect(3, 0, 3, 0, 9);

		display.SourcePage = 2;
		display.DrawPage = 0;
		display.CopyRect(0, 0, 9, 1, 0, 0);

		Assert.Equal(new[] { (320, 0, 10, 2) }, layer.Drawn);
		Assert.Equal(Es1VideoMemory.WorldCell, display.Cell(0, 2, 0));
		Assert.Equal(9, display.Cell(0, 3, 0));
		Assert.Equal(Es1VideoMemory.WorldCell, display.Cell(0, 3, 1));
		Assert.Equal(0, display.Cell(0, 5, 0));
		Assert.Equal(new[] { (320, 0, 0, 0, 10, 1), (320, 1, 0, 1, 10, 1) }, layer.Copied);
	}

	/// <summary>
	/// The PC timer runs on while a frame's end holds the host: a slide of 42 retraces, 0.6 s, is 72 interrupts, so the
	/// tick after it is the longest, <c>0x1c2</c>. Catches a hold that does not count the time (no tick after it) or that
	/// answers a tick itself.
	/// </summary>
	[Fact]
	public void TheTickAfterAHeldFrameMeasuresTheHold() {
		var clock = new Es1SimClock();
		Assert.True(clock.AdvanceHostFrame(TimeSpan.FromMilliseconds(25)));

		clock.Hold(TimeSpan.FromSeconds(42.0 / Es1FrameLoop.RetracesPerSecond));

		Assert.True(clock.AdvanceHostFrame(TimeSpan.Zero));
		Assert.Equal(72, clock.ElapsedInterrupts);
		Assert.Equal(0x1c2, clock.TickDelta);
	}

	/// <summary>A display with the four pages at the mission's origins.</summary>
	private static Es1VideoMemory Pages() {
		var display = new Es1VideoMemory();
		display.SetPageBase(0, 0, 0);
		display.SetPageBase(1, 0, 200);
		display.SetPageBase(2, 320, 0);
		display.SetPageBase(3, 320, 200);
		return display;
	}

	/// <summary>The views as the mission starts them, and the frame loop on them with no camera.</summary>
	private static (Es1ViewManager Views, Es1FrameLoop Frames) Start() {
		var records = new (int X, int Y)[] { (0, 0), (0, 197), (320, 0), (-320, 0) }
			.Select(origin => new Vue.Entry {
				ViewportX0 = 0, ViewportY0 = 0, ViewportX1 = 319, ViewportY1 = 140, CenterX = -160, CenterY = -70,
				CanvasOriginX = origin.X, CanvasOriginY = origin.Y,
			})
			.ToArray();
		records[1].ViewportX1 = records[1].ViewportY1 = 0;
		var clips = records.Select(_ => Es1ClipBlock.FromRect(0, 0, 319, 140)).ToArray();
		var canopies = Enumerable.Range(0, 4).Select(view => (DynamixBitmap?)Canopy((byte)(CanopyIndex + (view == 3 ? 2 : view)))).ToArray();
		var files = new Es1CockpitViews("TEST", records, clips, canopies, null);
		var views = new Es1ViewManager(files, new Es1VideoMemory());
		var frames = new Es1FrameLoop(views, () => { });
		frames.StartMission();
		return (views, frames);
	}

	private static DynamixBitmap Canopy(byte index) => new() {
		Rows = 200, Cols = 320, ImageData = Enumerable.Repeat(index, 320 * 200).ToArray(),
	};

	/// <summary>One frame after a tick, every retrace it waits taken at once; returns the monitor's origin at each.</summary>
	private static List<(int X, int Y)> RunFrame(Es1FrameLoop frames) {
		var shown = new List<(int X, int Y)>();
		foreach (var _ in frames.RunFrame(frozen: false)) {
			shown.Add(frames.Views.Display.MonitorOrigin);
			Assert.True(shown.Count < 1000, "The frame's end never finished.");
		}

		return shown;
	}

	private sealed class RecordingLayer : IEs1WorldLayer {
		public List<(int, int, int, int)> Drawn { get; } = [];

		public List<(int, int, int, int, int, int)> Copied { get; } = [];

		public void DrawWorld(int x, int y, int width, int height) => Drawn.Add((x, y, width, height));

		public void Copy(int sourceX, int sourceY, int targetX, int targetY, int width, int height) =>
			Copied.Add((sourceX, sourceY, targetX, targetY, width, height));
	}
}
