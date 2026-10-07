using Herculan.Engine.Cockpit;
using Herculan.Engine.Input;
using Herculan.Engine.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The energy rows' charge bar as a slider, under the ChargeBarPowerLevel tweak — see
/// <see cref="ChargeBarSlider"/>. Built from its measurements rather than from a loaded cockpit, at
/// the retail value field's size: 0x12 GAU columns from 0x24 to 0x35, two device pixels each.
/// </summary>
public class ChargeBarSliderTests {
	private const int Left = 200;

	// A row at GAU x 100 puts the value field's last column at (100 + 0x35) * 2.
	private static readonly ChargeBarSlider Bar = new(Left, 340, Left + 0x11 * 2, 350, knobWidth: 2);

	/// <summary>The knob is one GAU unit wide and the track one column short of the field: 32 pixels of travel.</summary>
	[Fact]
	public void TravelIsTheFieldLessOneKnob() => Assert.Equal(32, Bar.Travel);

	/// <summary>
	/// <c>SliderWidget_GetValueH</c> over <c>SliderWidget_RecomputeScaleH</c>'s scale: 32 pixels span
	/// 0..0x400, so each pixel is 32 units and the knob's last position reads the whole range.
	/// </summary>
	[Theory]
	[InlineData(Left, 0)]
	[InlineData(Left + 1, 32)]
	[InlineData(Left + 16, 0x200)]
	[InlineData(Left + 32, 0x400)]
	public void PositionIsLinearAcrossTheTravel(float x, int expected) =>
		Assert.Equal(expected, Bar.PositionAt(x));

	/// <summary>
	/// <c>SliderWidget_DragToPointH</c> clamps the pointer into the track, so a release past either
	/// end, or on the field's last column where the knob cannot go, pins the slider there.
	/// </summary>
	[Theory]
	[InlineData(Left - 50, 0)]
	[InlineData(Left + 34, 0x400)]
	[InlineData(Left + 500, 0x400)]
	public void APointerPastTheTrackPinsTheEnd(float x, int expected) =>
		Assert.Equal(expected, Bar.PositionAt(x));

	/// <summary>The widget is the whole value field, inclusive of its last GAU unit, and draggable.</summary>
	[Fact]
	public void TheWidgetCoversTheValueFieldAndDrags() {
		var widget = Bar.Widget(3);

		Assert.Equal(CockpitWidgetId.ChargeBar(3), widget.Id);
		Assert.Equal(3, widget.Id.AsWeaponChargeBar);
		Assert.Null(widget.Id.AsWeaponRow);
		Assert.True(widget.Draggable);
		Assert.Equal((Left, 340, Left + 0x11 * 2 + 1, 351), (widget.X0, widget.Y0, widget.X1, widget.Y1));
		Assert.Equal(0x12 * 2, widget.Width);
	}

	/// <summary>
	/// The read-back's <c>position * 1200 &gt;&gt; 10</c>: the whole bar is the 1200 the capacitor is
	/// scaled against, and the bar then fills to where it was released, give or take the rounding.
	/// </summary>
	[Theory]
	[InlineData(0, 0)]
	[InlineData(0x200, 600)]
	[InlineData(819, 959)]
	[InlineData(0x400, 1200)]
	[InlineData(-5, 0)]
	[InlineData(5000, 1200)]
	public void APositionReadsBackAsAChargeTarget(int position, short expected) =>
		Assert.Equal(expected, WeaponMount.ChargeTargetForBarPosition(position));

	/// <summary>
	/// Only the release of a capture is flagged — the original reads and commits the slider's value
	/// there and nowhere else, so a charge bar acts on it alone.
	/// </summary>
	[Fact]
	public void OnlyTheReleaseOfACaptureIsFlagged() {
		var input = new CockpitInput();
		input.Enqueue(Left + 4, 345, CockpitMouseButtons.Left);
		input.Enqueue(Left + 10, 345, CockpitMouseButtons.Left);
		input.Enqueue(Left + 20, 345, CockpitMouseButtons.None);

		input.Drain(1 / 60d, (x, y) => Bar.Widget(0).Contains(x, y) ? Bar.Widget(0) : null);

		Assert.Equal(new[] { false, false, true }, input.Drags.Select(d => d.Released));
		Assert.Equal(Left + 20, input.Drags[^1].ArtX);
	}
}
