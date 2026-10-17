using Herculan.Engine.Cockpit;
using Herculan.Engine.Input;
using Herculan.Engine.Render;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The click state machine, exercised through <see cref="CockpitInput"/>'s hit-test seam so no
/// cockpit needs loading. Two widgets sit side by side: A occupies window x 0-99, B occupies 100-199,
/// and anything else is bare art.
/// </summary>
public class CockpitInputTests {
	private static readonly CockpitWidgetId A = CockpitWidgetId.Mfd(0);
	private static readonly CockpitWidgetId B = CockpitWidgetId.Mfd(1);

	/// <summary>Which widget covers a window x, in the two-button strip the tests click on.</summary>
	private static CockpitWidget? HitTest(float x, float y) => x switch {
		>= 0 and < 100 => new CockpitWidget(A, CockpitSurface.Forward, 0, 0, 99, 50, Lit: false),
		>= 100 and < 200 => new CockpitWidget(B, CockpitSurface.Forward, 100, 0, 199, 50, Lit: false),
		_ => null,
	};

	/// <summary>A frame's worth of drain, at a nominal 60Hz unless a longer step is asked for.</summary>
	private static IReadOnlyList<CockpitClick> Frame(CockpitInput input, double deltaSeconds = 1 / 60d) =>
		input.Drain(deltaSeconds, HitTest);

	private static void Press(CockpitInput input, float x) =>
		input.Enqueue(x, 10, CockpitMouseButtons.Left);

	private static void Release(CockpitInput input, float x) =>
		input.Enqueue(x, 10, CockpitMouseButtons.None);

	[Fact]
	public void PressAndReleaseOnTheSameWidgetFiresOneClick() {
		var input = new CockpitInput();
		Press(input, 50);
		Release(input, 50);

		var clicks = Frame(input);

		// The release position rides along on the click: no toArt converter is supplied here, so it
		// comes back in the window coordinates the events were enqueued in.
		Assert.Equal(new[] { new CockpitClick(A, CockpitMouseButtons.Left, 50f, 10f) }, clicks);
	}

	/// <summary>
	/// §7: the release has to land back on the widget that was pressed. Pressing A and releasing over
	/// B fires nothing — not a click on A, and not one on B either.
	/// </summary>
	[Fact]
	public void ReleasingOnADifferentWidgetFiresNothing() {
		var input = new CockpitInput();
		Press(input, 50);
		Release(input, 150);

		Assert.Empty(Frame(input));
	}

	/// <summary>Releasing over bare art fires nothing, and disarms the press.</summary>
	[Fact]
	public void ReleasingOffAnyWidgetFiresNothing() {
		var input = new CockpitInput();
		Press(input, 50);
		Release(input, 500);

		Assert.Empty(Frame(input));
		Assert.Null(input.Pressed);
	}

	/// <summary>A press that started on bare art arms nothing, so a release over a widget is not a click on it.</summary>
	[Fact]
	public void PressingOffAnyWidgetArmsNothing() {
		var input = new CockpitInput();
		Press(input, 500);
		Release(input, 50);

		Assert.Empty(Frame(input));
	}

	/// <summary>
	/// §4's double-click flag: a release inside <see cref="CockpitInput.DoubleClickSeconds"/> of the previous
	/// one marks its click, and one after the window does not.
	/// </summary>
	[Fact]
	public void ASecondReleaseInsideTheWindowIsADoubleClick() {
		var input = new CockpitInput();
		Press(input, 50);
		Release(input, 50);
		Assert.False(Assert.Single(Frame(input)).DoubleClick);

		Press(input, 50);
		Release(input, 50);
		Assert.True(Assert.Single(Frame(input, deltaSeconds: CockpitInput.DoubleClickSeconds * 0.9f)).DoubleClick);

		Press(input, 50);
		Release(input, 50);
		Assert.False(Assert.Single(Frame(input, deltaSeconds: CockpitInput.DoubleClickSeconds * 1.1f)).DoubleClick);
	}

	/// <summary>
	/// A press held across frames stays armed until it is released, however long that takes: §4 times
	/// releases, not holds, and <c>Widget_OnMouseUp</c> runs on every release, so a button held for a
	/// second still clicks.
	/// </summary>
	[Fact]
	public void PressSurvivesAcrossFrames() {
		var input = new CockpitInput();
		Press(input, 50);
		Assert.Empty(Frame(input));
		Assert.Equal(A, input.Pressed);

		Frame(input, deltaSeconds: 1.0);
		Release(input, 50);
		Assert.Single(Frame(input));
		Assert.Null(input.Pressed);
	}

	/// <summary>
	/// Moving the pointer with no button held changes nothing. DBSIM has no hover state — its cockpit
	/// listener does not even subscribe to plain movement (§3) — so neither does this. The pointer ends
	/// over a widget, which is where a hover state would show.
	/// </summary>
	[Fact]
	public void MovingWithNoButtonHeldChangesNothing() {
		var input = new CockpitInput();

		input.Enqueue(500, 10, CockpitMouseButtons.None);
		input.Enqueue(150, 10, CockpitMouseButtons.None);
		input.Enqueue(50, 10, CockpitMouseButtons.None);

		Assert.Empty(Frame(input));
		Assert.Null(input.Depressed);
		Assert.Null(input.Pressed);
	}

	/// <summary>
	/// <c>Widget_TrackPressedWidget</c> (<c>00452954</c>)'s whole behaviour: a held widget draws depressed, pops back up when the
	/// pointer slides off it, and depresses again when it comes back — all while the press stays armed,
	/// so letting go back on it still clicks. The original does not cancel on leave; it only checks where
	/// the release lands.
	/// </summary>
	[Fact]
	public void HeldWidgetDepressesAndPopsBackUpAsThePointerMoves() {
		var input = new CockpitInput();

		Press(input, 50);
		Frame(input);
		Assert.Equal(A, input.Depressed);
		Assert.Equal(A, input.Pressed);

		// Still held, but dragged off onto the neighbouring widget, which does not depress on the way
		// past: only the widget the press armed can.
		input.Enqueue(150, 10, CockpitMouseButtons.Left);
		Frame(input);
		Assert.Null(input.Depressed);
		Assert.Equal(A, input.Pressed);

		input.Enqueue(500, 10, CockpitMouseButtons.Left);
		Frame(input);
		Assert.Null(input.Depressed);

		input.Enqueue(50, 10, CockpitMouseButtons.Left);
		Frame(input);
		Assert.Equal(A, input.Depressed);

		Release(input, 50);
		Assert.Single(Frame(input));
	}

	/// <summary>
	/// A release that fires its click also pops the widget back up. A release anywhere else needs no
	/// test of its own: the pointer has already left the widget, which pops it up before the release
	/// is handled.
	/// </summary>
	[Fact]
	public void ReleaseClearsTheDepressedLook() {
		var input = new CockpitInput();

		Press(input, 50);
		Release(input, 50);
		Assert.Single(Frame(input));
		Assert.Null(input.Depressed);
	}

	/// <summary>
	/// The right button arms nothing on its press and fires on whatever its release is over, reporting
	/// itself as the button that did — pressed over A and let go over B, it clicks B. The left button
	/// would fire nothing there.
	/// </summary>
	[Fact]
	public void RightButtonClicksAreReportedSeparately() {
		var input = new CockpitInput();
		input.Enqueue(50, 10, CockpitMouseButtons.Right);
		Assert.Empty(Frame(input));
		Assert.Null(input.Pressed);

		input.Enqueue(150, 10, CockpitMouseButtons.None);
		Assert.Equal(new[] { new CockpitClick(B, CockpitMouseButtons.Right, 150f, 10f) }, Frame(input));
	}

	/// <summary>
	/// Events queue up between drains rather than being handled as they arrive (§3), so every click
	/// enqueued since the last drain completes in the next one, in order. The second release follows the
	/// first inside the double-click window, so it is a double-click on whichever widget it lands.
	/// </summary>
	[Fact]
	public void ADrainCompletesEveryClickQueuedSinceTheLast() {
		var input = new CockpitInput();
		Press(input, 50);
		Release(input, 50);
		Press(input, 150);
		Release(input, 150);

		Assert.Equal(
			new[] {
				new CockpitClick(A, CockpitMouseButtons.Left, 50f, 10f),
				new CockpitClick(B, CockpitMouseButtons.Left, 150f, 10f, DoubleClick: true),
			},
			Frame(input));
	}

	/// <summary>
	/// Reset forgets everything in flight: the armed press, the button it remembers as held — so the
	/// next press after it is seen as one — and any events queued but not yet drained, so a press
	/// queued before it cannot pair with a release after it.
	/// </summary>
	[Fact]
	public void ResetForgetsEverythingInFlight() {
		var input = new CockpitInput();
		Press(input, 50);
		Frame(input);

		input.Reset();
		Assert.Null(input.Pressed);
		Assert.Null(input.Depressed);

		Press(input, 150);
		Release(input, 150);
		Assert.Equal(B, Assert.Single(Frame(input)).Id);

		Press(input, 50);
		input.Reset();
		Release(input, 50);
		Assert.Empty(Frame(input));
	}

	/// <summary>
	/// A press on a draggable widget captures the pointer and reports a position straight away —
	/// <c>Widget_OnMouseDown</c> dispatches the drag handler itself, which is why clicking a slider
	/// track jumps its knob rather than waiting for the pointer to move.
	/// </summary>
	[Fact]
	public void PressingADraggableWidgetCapturesThePointerImmediately() {
		var input = new CockpitInput();
		input.Enqueue(50, 10, CockpitMouseButtons.Left);

		input.Drain(1 / 60d, DraggableHitTest);

		var drag = Assert.Single(input.Drags);
		Assert.Equal(A, drag.Id);
		Assert.Equal(50, drag.ArtX);
		Assert.Equal(10, drag.ArtY);
	}

	/// <summary>
	/// A capture follows the pointer off its own widget and off every widget — the original dispatches
	/// to the captured widget without hit-testing at all while the flag is set.
	/// </summary>
	[Fact]
	public void ACaptureFollowsThePointerOffTheWidget() {
		var input = new CockpitInput();
		input.Enqueue(50, 10, CockpitMouseButtons.Left);
		input.Enqueue(150, 20, CockpitMouseButtons.Left);
		input.Enqueue(900, 30, CockpitMouseButtons.Left);

		input.Drain(1 / 60d, DraggableHitTest);

		Assert.Equal(new[] { 10f, 20f, 30f }, input.Drags.Select(d => d.ArtY));
		Assert.All(input.Drags, d => Assert.Equal(A, d.Id));
	}

	/// <summary>
	/// The release that ends a capture fires no click, however quickly it followed the press: the
	/// original takes the capture branch and never reaches <c>Widget_OnMouseUp</c>.
	/// </summary>
	[Fact]
	public void ReleasingACaptureFiresNoClick() {
		var input = new CockpitInput();
		input.Enqueue(50, 10, CockpitMouseButtons.Left);
		input.Enqueue(50, 10, CockpitMouseButtons.None);

		Assert.Empty(input.Drain(1 / 60d, DraggableHitTest));
		Assert.Null(input.Pressed);
	}

	/// <summary>Widget A, but draggable — the flag the slider base sets and no button class does.</summary>
	private static CockpitWidget? DraggableHitTest(float x, float y) {
		var hit = HitTest(x, y);
		return hit is { } widget && widget.Id == A ? widget with { Draggable = true } : hit;
	}

	/// <summary>The queue is bounded; events past its capacity are dropped rather than growing it.</summary>
	[Fact]
	public void TheQueueIsBounded() {
		var input = new CockpitInput();
		for (int i = 0; i < CockpitInput.QueueCapacity; i++) {
			Press(input, 50);
			Release(input, 50);
		}

		// Only the first capacity's worth of events, half of the pairs, completes a click, and the
		// overflow was dropped at enqueue time rather than buffered for the next drain.
		Assert.Equal(CockpitInput.QueueCapacity / 2, Frame(input).Count);
		Assert.Empty(Frame(input));
	}
}
