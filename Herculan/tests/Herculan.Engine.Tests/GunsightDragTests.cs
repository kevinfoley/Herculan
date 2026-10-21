using Herculan.Engine.Input;
using Herculan.Engine.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The gunsight drag (<see cref="GunsightDrag"/>) and the retail mouse-record bit that feeds it with the tweak off
/// (<see cref="CockpitInput.RightPressedLast"/>). The arming rect is 0,0 - 640,314, OUTLAW's at 640x480.
/// </summary>
public class GunsightDragTests {
	private static void Step(GunsightDrag drag, bool right, int x, int y) =>
		drag.Update(new GunsightPointer(right, x, y), 0, 0, 640, 314);

	/// <summary>Right of and above the press turns the turret right and up; the far side the other way, squared.</summary>
	[Fact]
	public void TermsGrowWithTheSquareOfTheDistanceFromThePress() {
		var drag = new GunsightDrag();
		Step(drag, right: true, 300, 200);
		Assert.Equal((0, 0), (drag.Twist, drag.Pitch));

		Step(drag, right: true, 316, 192);
		Assert.True(drag.Twist > 0 && drag.Pitch > 0);
		int nearTwist = drag.Twist;

		Step(drag, right: true, 268, 216);
		Assert.True(drag.Twist < 0 && drag.Pitch < 0);
		Assert.Equal(4 * nearTwist, -drag.Twist);

		var controls = drag.AddTo(new MechControls(0, 0, TorsoTwist: 10, TorsoPitch: 10));
		Assert.Equal((short)(10 + drag.Twist), controls.TorsoTwist);
		Assert.Equal((short)(10 + drag.Pitch), controls.TorsoPitch);
	}

	/// <summary>A long drag turns the turret as fast as a stick pushed all the way and no faster; an axis no term moves is left alone.</summary>
	[Fact]
	public void TheCapHoldsADraggedAxisAtAStick() {
		var drag = new GunsightDrag();
		Step(drag, right: true, 100, 200);
		Step(drag, right: true, 600, 200);
		Assert.True(drag.Twist > GunsightDrag.StickFull);

		var controls = drag.AddTo(new MechControls(0, 0, TorsoTwist: 40, TorsoPitch: 300));
		Assert.Equal(GunsightDrag.StickFull, controls.TorsoTwist);
		Assert.Equal(300, controls.TorsoPitch);
	}

	/// <summary>
	/// Held below the complex's rect the drag waits, and arms where the pointer first enters it; letting go zeroes the
	/// terms.
	/// </summary>
	[Fact]
	public void ArmsWhereTheHeldPointerEntersTheRectAndLettingGoZeroes() {
		var drag = new GunsightDrag();
		Step(drag, right: true, 300, 400);
		Step(drag, right: true, 400, 300);
		Assert.Equal((0, 0), (drag.Twist, drag.Pitch));

		Step(drag, right: true, 450, 300);
		Assert.NotEqual(0, drag.Twist);

		Step(drag, right: false, 450, 300);
		Assert.Equal((0, 0), (drag.Twist, drag.Pitch));
	}

	/// <summary>Retail's record keeps the right button only until the next mouse event, a move included.</summary>
	[Fact]
	public void TheRecordLosesTheRightButtonAtTheNextEvent() {
		var input = new CockpitInput();
		input.Enqueue(10, 10, CockpitMouseButtons.Right);
		Assert.True(input.RightPressedLast);

		input.Enqueue(12, 10, CockpitMouseButtons.Right);
		Assert.False(input.RightPressedLast);
	}
}
