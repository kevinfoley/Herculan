using HercWorks.Core.Data.File.Dts.Anim;
using Herculan.Engine.Anim;
using Xunit;

namespace Herculan.Engine.Tests;

public class SeekRoundingTests {
	private const short FrameDuration = 50;

	/// <summary>
	/// <see cref="SeekRounding.RoundToNearest"/> lands one animation tick past
	/// <see cref="SeekRounding.Truncate"/> when the product's low 14 bits are at least <c>0x2000</c>, and on
	/// the same tick below that. Catches <see cref="SeekRounding.RoundToNearest"/> wired to the truncating
	/// <see cref="Numerics.SimMath.Q14Multiply"/>, or to anything that rounds a product under one half up.
	/// </summary>
	[Theory]
	[InlineData(0x2000, 49, 50)] // 99 × 0x2000 = 49.5 ticks: the rounded seek crosses into frame 1.
	[InlineData(0x0800, 12, 12)] // 99 × 0x0800 = 12.375 ticks.
	public void RoundToNearestLandsOneTickFurtherOnlyFromHalfATick(short position, int truncated, int rounded) {
		var animation = TwoFrameAnimation();

		Assert.Equal(truncated, SeekTicks(animation, position, SeekRounding.Truncate));
		Assert.Equal(rounded, SeekTicks(animation, position, SeekRounding.RoundToNearest));
	}

	/// <summary>The tick, counted from the sequence's start, a seek parks the thread on.</summary>
	private static int SeekTicks(ShapeAnimation animation, short position, SeekRounding rounding) {
		var thread = new AnimationThread(animation);
		thread.SeekToPosition(0, position, rounding);
		return thread.Frame * FrameDuration + thread.Capture().FrameAccumulator;
	}

	/// <summary>One plain sequence of two 50-tick frames over one node: 100 ticks, so total − 1 is 99.</summary>
	private static ShapeAnimation TwoFrameAnimation() {
		var sequence = new ANSequence {
			Frames = [new ANSequenceFrame { Duration = FrameDuration }, new ANSequenceFrame { Duration = FrameDuration }],
			PartIds = [0],
			TransformIndices = [0, 0],
		};
		var list = new ANAnimList {
			Sequences = [sequence],
			Transforms = [new ANAnimListTransform()],
			DefaultTransforms = [0],
		};
		return ShapeAnimation.From(list)!;
	}
}
