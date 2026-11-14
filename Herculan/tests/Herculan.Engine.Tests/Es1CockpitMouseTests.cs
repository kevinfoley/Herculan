using System.Buffers.Binary;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Render;
using Herculan.Es1.Host;
using Herculan.Es1.Input;
using Herculan.Es1.View;
using Silk.NET.Maths;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The cockpit's mouse, its root widget and the screen-edge strips (docs/es1/simulation/cockpit-mouse.md,
/// docs/es1/simulation/cockpit-panel.md#the-screen-edges-and-the-cursors), on constructed widgets and on views whose
/// canvas origins are the retail <c>.VUE</c> files', and the pointer's mapping into the 320 × 200 screen.
/// </summary>
public class Es1CockpitMouseTests {
	/// <summary>
	/// Two overlapping widgets: the one registered first takes the point, and a hidden one is passed over. Catches the
	/// list walked from its end, and the state-2 test dropped.
	/// </summary>
	[Fact]
	public void TheFirstRegisteredWidgetTakesThePointAndAHiddenOneIsPassedOver() {
		var root = new Es1WidgetRoot(new Es1Rect(0, 0, 319, 199));
		var first = new CountingButton { Rect = new Es1Rect(10, 10, 50, 50) };
		var second = new CountingButton { Rect = new Es1Rect(0, 0, 100, 100) };
		root.RegisterClickable(first);
		root.RegisterClickable(second);

		Assert.Equal(0, root.HitTestChildren(20, 20));
		Assert.Equal(1, root.HitTestChildren(80, 80));

		first.State = Es1Widget.Hidden;
		Assert.Equal(1, root.HitTestChildren(20, 20));
	}

	/// <summary>
	/// A right press over a widget presses nothing, and its release clicks the widget with the right bit, which
	/// <c>Widget_OnMouseUp</c> lets through though nothing was pressed; a left press presses it and its release clicks it
	/// with the left bit. Catches the right button taken as a press, and the click handed the record's buttons without
	/// the released one.
	/// </summary>
	[Fact]
	public void ARightPressPressesNothing() {
		var mouse = new Es1CockpitMouse(new Es1Cursor());
		mouse.Init(null);
		var root = new Es1WidgetRoot(new Es1Rect(0, 0, 319, 199));
		var button = new CountingButton { Rect = new Es1Rect(10, 10, 50, 50) };
		root.RegisterClickable(button);
		mouse.Widgets.ClearPressed(root);

		mouse.Push(1, new Es1MouseRecord(20, 20, Es1HostMouse.RightBit));
		mouse.ProcessQueue();
		Assert.Equal(Es1Widget.Up, button.State);
		Assert.Equal(-1, mouse.Widgets.PressedIndex);
		Assert.Empty(button.Clicks);

		mouse.Push(2, new Es1MouseRecord(20, 20, 0));
		mouse.ProcessQueue();
		Assert.Equal(new short[] { (short)Es1HostMouse.RightBit }, button.Clicks);

		mouse.Push(3, new Es1MouseRecord(20, 20, Es1HostMouse.LeftBit));
		mouse.ProcessQueue();
		Assert.Equal(Es1Widget.Pressed, button.State);

		mouse.Push(4, new Es1MouseRecord(20, 20, 0));
		mouse.ProcessQueue();
		Assert.Equal(Es1Widget.Up, button.State);
		Assert.Equal(new short[] { (short)Es1HostMouse.RightBit, (short)Es1HostMouse.LeftBit }, button.Clicks);
	}

	/// <summary>
	/// In the forward view a left click on the bottom rows queues command 0, the pan down, one on the left edge command
	/// 5 and one on the right edge command 4. Catches a strip's rect or its command by view wrong, and strips that are
	/// never built or registered.
	/// </summary>
	[Theory]
	[InlineData(160, 199, 0)]
	[InlineData(2, 100, 5)]
	[InlineData(317, 100, 4)]
	public void AnEdgeClickFromTheForwardViewQueuesItsCommand(int x, int y, int command) {
		var (views, _) = Start();

		Click(views.Mouse, x, y);

		Assert.Equal(command, views.PendingCommand);
	}

	/// <summary>
	/// After the pan down the root's origin has moved 197 rows up and the bottom strip lies on the screen's top rows, so a
	/// click there queues command 1, the pan back up, and one on the bottom rows queues nothing. Catches the root not
	/// moved with the view, the delta's sign, and the strips' reflection moving the bottom strip off the band.
	/// </summary>
	[Fact]
	public void AfterThePanDownAClickOnTheTopRowsPansBackUp() {
		var (views, frames) = Start();
		Click(views.Mouse, 160, 199);
		for (int frame = 0; views.CurrentView != 1; frame++) {
			Assert.True(frame < 10, "The pan down never ended.");
			RunFrame(frames);
		}

		Click(views.Mouse, 160, 198);
		Assert.Equal(-1, views.PendingCommand);

		Click(views.Mouse, 160, 1);
		Assert.Equal(1, views.PendingCommand);
	}

	/// <summary>
	/// The host's pointer maps into the 320 × 200 screen through the screen's 4:3 placement: in a window wider than 4:3
	/// the screen's left edge is the placement's, not the window's, its far corner is (319, 199), a pointer over the bar
	/// or past the window is held to the edge, and on a scaled display the client's coordinates are scaled to the
	/// framebuffer's first. Catches the window's width taken for the placement's, a missing clamp, and the client and
	/// framebuffer sizes mixed up.
	/// </summary>
	[Fact]
	public void ThePointerMapsThroughTheFourByThreePlacementAndIsHeldToTheScreen() {
		// A 1000 × 600 framebuffer shows the screen 800 × 600 from column 100; the client is half that, a 2x display.
		var pointer = Es1ScreenPresenter.Pointer(new Vector2D<int>(1000, 600), new Vector2D<int>(500, 300));

		Assert.Equal((0, 0), pointer.ToScreen(50, 0));
		Assert.Equal((319, 199), pointer.ToScreen(449.9f, 299.9f));
		Assert.Equal((160, 100), pointer.ToScreen(250, 150));
		Assert.Equal((0, 100), pointer.ToScreen(10, 150));
		Assert.Equal((319, 199), pointer.ToScreen(600, 400));
		Assert.Equal((0, 0), pointer.ToScreen(-20, -20));

		var (clientX, clientY) = pointer.ToClient(319, 0);
		Assert.Equal((319, 0), pointer.ToScreen(clientX, clientY));
	}

	/// <summary>
	/// In a 1400 × 600 window showing the canvas beside the screen's 800 × 600, a pointer over a side panel rests on the
	/// screen's edge column toward it; at a glance right's end, the screen moved 300 pixels right of centre, the pointer
	/// maps onto the screen where it now lies, and over view 0's canvas left of it holds to column 0. Catches the pointer
	/// mapped across the whole window, and mapped through the centred placement after the screen has moved.
	/// </summary>
	[Fact]
	public void InAWideWindowThePointerIsHeldToTheScreensColumnsWhereverTheyLie() {
		var window = new Vector2D<int>(1400, 600);

		var rest = Es1CanvasLayout.Create(window, 0, 0).Pointer(window);
		var glanced = Es1CanvasLayout.Create(window, 320, 0).Pointer(window);

		Assert.Equal((319, 100), rest.ToScreen(1350, 300));
		Assert.Equal((0, 100), rest.ToScreen(50, 300));
		Assert.Equal((160, 100), glanced.ToScreen(1000, 300));
		Assert.Equal((0, 100), glanced.ToScreen(500, 300));
	}

	/// <summary>A left press and release at (<paramref name="x"/>, <paramref name="y"/>), handled as the input read's step 4 handles them.</summary>
	private static void Click(Es1CockpitMouse mouse, int x, int y) {
		mouse.Push(1, new Es1MouseRecord(x, y, Es1HostMouse.LeftBit));
		mouse.Push(2, new Es1MouseRecord(x, y, 0));
		mouse.ProcessQueue();
	}

	/// <summary>The views as the mission starts them on a layout whose root is the 320 × 200 screen, and the frame loop on them.</summary>
	private static (Es1ViewManager Views, Es1FrameLoop Frames) Start() {
		var records = new (int X, int Y)[] { (0, 0), (0, 197), (320, 0), (-320, 0) }
			.Select(origin => new Vue.Entry {
				ViewportX0 = 0, ViewportY0 = 0, ViewportX1 = 319, ViewportY1 = 140, CenterX = -160, CenterY = -70,
				CanvasOriginX = origin.X, CanvasOriginY = origin.Y,
			})
			.ToArray();
		var clips = records.Select(_ => Es1ClipBlock.FromRect(0, 0, 319, 140)).ToArray();
		var layout = new Es1GauFile { Origin = new PixelPoint(0, 0) };
		BinaryPrimitives.WriteInt32LittleEndian(layout.Bytes8.AsSpan(0), 319);
		BinaryPrimitives.WriteInt32LittleEndian(layout.Bytes8.AsSpan(4), 199);
		var files = new Es1CockpitViews("TEST", records, clips, new HercWorks.Core.Data.File.Dyn.DynamixBitmap?[4], null, layout);
		var views = new Es1ViewManager(files, new Es1VideoMemory());
		var frames = new Es1FrameLoop(views, () => { });
		frames.StartMission();
		return (views, frames);
	}

	/// <summary>One frame after a tick, every retrace it waits taken at once.</summary>
	private static void RunFrame(Es1FrameLoop frames) {
		int retraces = 0;
		foreach (var _ in frames.RunFrame(frozen: false)) {
			Assert.True(++retraces < 1000, "The frame's end never finished.");
		}
	}

	private sealed class CountingButton : Es1ButtonWidget {
		public List<short> Clicks { get; } = [];

		public override void OnClick(short value) => Clicks.Add(value);
	}
}
