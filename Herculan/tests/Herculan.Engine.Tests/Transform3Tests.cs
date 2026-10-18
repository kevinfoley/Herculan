using Herculan.Engine.Numerics;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Checks the ported fixed-point trigonometry and transform against ordinary floating-point
/// arithmetic. The port reproduces DBSIM's own quantization (angles snap to 1/4096 of a turn,
/// products round in Q14), so the tolerances below are the quantization, not slack.
/// </summary>
public class Transform3Tests {
	private const double TurnsPerBam = 1.0 / BinaryAngle.FullTurn;

	/// <summary>
	/// Within half a table step, which is what rounding the angle to the nearest 16-BAM entry leaves.
	/// 15 and 16399 sit one BAM short of a step, where truncating the angle instead misses by a whole
	/// step.
	/// </summary>
	[Theory]
	[InlineData(0)]
	[InlineData(15)]
	[InlineData(1820)]
	[InlineData(8192)]
	[InlineData(16384)]
	[InlineData(16399)]
	[InlineData(30000)]
	[InlineData(-8192)]
	[InlineData(-30000)]
	public void CosAndSinMatchRealTrigonometry(int angle) {
		double radians = angle * TurnsPerBam * 2 * System.Math.PI;

		Assert.InRange(SimTrig.Cos((short)angle) / (double)SimTrig.One,
			System.Math.Cos(radians) - 0.001, System.Math.Cos(radians) + 0.001);
		Assert.InRange(SimTrig.Sin((short)angle) / (double)SimTrig.One,
			System.Math.Sin(radians) - 0.001, System.Math.Sin(radians) + 0.001);
	}

	/// <summary>
	/// A point on every axis and in every octant. The table covers 0 to 45 degrees only, so each
	/// octant is a separate fold of it.
	/// </summary>
	[Theory]
	[InlineData(1, 0)]
	[InlineData(0, 1)]
	[InlineData(-1, 0)]
	[InlineData(0, -1)]
	[InlineData(2, 1)]
	[InlineData(1, 2)]
	[InlineData(-1, 2)]
	[InlineData(-2, 1)]
	[InlineData(-2, -1)]
	[InlineData(-1, -2)]
	[InlineData(1, -2)]
	[InlineData(2, -1)]
	public void Atan2MatchesRealTrigonometryInEveryOctant(int x, int y) {
		double expected = System.Math.Atan2(y, x) / (2 * System.Math.PI) * BinaryAngle.FullTurn;

		int error = (short)(SimTrig.Atan2(y * 1000, x * 1000) - (int)System.Math.Round(expected));
		Assert.InRange(error, -8, 8);
	}

	/// <summary>
	/// Both tables and both signs: the coarse one below a sine of 0.875, the fine one above it.
	/// </summary>
	[Theory]
	[InlineData(0)]
	[InlineData(4096)]
	[InlineData(8192)]
	[InlineData(16384)]
	[InlineData(-8192)]
	[InlineData(-11200)]
	public void AsinInvertsSin(int angle) {
		short sine = SimTrig.Sin((short)angle);
		Assert.InRange((short)SimTrig.Asin(sine), angle - 8, angle + 8);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1820)]
	[InlineData(0x9000)]
	public void RotatingForwardMatchesTheHeadingConvention(int heading) {
		// Model forward is +Y. A heading of zero points along world +Y, and increasing it turns the
		// machine anticlockwise -- toward -X -- which is what MissionLoader's formation spread and
		// the render transform both assume.
		var transform = Transform3.FromEuler(0, 0, (short)heading);
		var forward = transform.RotateVector(0, 10000, 0);

		double radians = heading * TurnsPerBam * 2 * System.Math.PI;
		Assert.InRange(forward.X, -10000 * System.Math.Sin(radians) - 30,
			-10000 * System.Math.Sin(radians) + 30);
		Assert.InRange(forward.Y, 10000 * System.Math.Cos(radians) - 30,
			10000 * System.Math.Cos(radians) + 30);
	}

	/// <summary>
	/// A general rotation rolls about the forward axis (Y), then pitches about X, then turns about Z,
	/// checked against those three turns made one at a time in floating point. The angles are whole
	/// table steps, so only Q14 rounding is left. The second row has no X angle and must still not
	/// take the Z-only shape, whose fast paths ignore the rest of the matrix.
	/// </summary>
	[Theory]
	[InlineData(0x0800, 0, 0x1400)]
	[InlineData(0, 0x0c00, 0x2a00)]
	[InlineData(-0x0600, 0x1a00, -0x3000)]
	public void FromEulerRollsThenPitchesThenTurns(int pitch, int roll, int heading) {
		var transform = Transform3.FromEuler((short)pitch, (short)roll, (short)heading);

		foreach (var (x, y, z) in new[] { (10000, 0, 0), (0, 10000, 0), (0, 0, 10000) }) {
			var expected = RollPitchTurn(x, y, z, pitch, roll, heading);
			var actual = transform.TransformPoint(x, y, z);

			Assert.InRange(actual.X, expected.X - 6, expected.X + 6);
			Assert.InRange(actual.Y, expected.Y - 6, expected.Y + 6);
			Assert.InRange(actual.Z, expected.Z - 6, expected.Z + 6);
		}
	}

	/// <summary>
	/// Each of the shape pairs <see cref="Transform3.Concat"/> handles separately — a translation-only
	/// transform on either side, two Z-only rotations, a Z-only one on either side of a general one,
	/// and two general ones — applied to points as the two transforms one after the other.
	/// </summary>
	[Theory]
	[InlineData(Translated, General)]
	[InlineData(General, Translated)]
	[InlineData(Turned, Turned)]
	[InlineData(General, Turned)]
	[InlineData(Turned, General)]
	[InlineData(General, General)]
	public void ConcatAppliesTheFirstArgumentFirst(int firstShape, int secondShape) {
		var first = Shaped(firstShape, (0x0700, -0x0500, 0x2345), (700, -300, 40));
		var second = Shaped(secondShape, (-0x0300, 0x0900, -0x1234), (-1200, 500, 90));
		var combined = Transform3.Concat(first, second);

		foreach (var (x, y, z) in new[] { (1000, 2000, 3000), (-4000, 500, -250) }) {
			var step = first.TransformPoint(x, y, z);
			var expected = second.TransformPoint(step.X, step.Y, step.Z);
			var actual = combined.TransformPoint(x, y, z);

			Assert.InRange(actual.X, expected.X - 4, expected.X + 4);
			Assert.InRange(actual.Y, expected.Y - 4, expected.Y + 4);
			Assert.InRange(actual.Z, expected.Z - 4, expected.Z + 4);
		}
	}

	[Theory]
	[InlineData(0, 0, 0x1234)]
	[InlineData(0x0700, -0x0500, 0x2345)]
	public void InvertedUndoesTheTransform(int pitch, int roll, int heading) {
		var transform = Transform3.FromEuler((short)pitch, (short)roll, (short)heading);
		transform.X = 700;
		transform.Y = -300;
		transform.Z = 40;

		var moved = transform.TransformPoint(1000, -2000, 3000);
		var back = transform.Inverted().TransformPoint(moved.X, moved.Y, moved.Z);

		Assert.InRange(back.X, 1000 - 3, 1000 + 3);
		Assert.InRange(back.Y, -2000 - 3, -2000 + 3);
		Assert.InRange(back.Z, 3000 - 3, 3000 + 3);
	}

	/// <summary>Away from the gimbal lock, the three angles come back as they went in.</summary>
	[Theory]
	[InlineData(0, 0, 0x3000)]
	[InlineData(0x0800, 0x0c00, 0x2a00)]
	[InlineData(-0x0600, 0x1a00, -0x3000)]
	public void EulerSurvivesARoundTrip(int pitch, int roll, int heading) {
		var (x, y, z) = Transform3.FromEuler((short)pitch, (short)roll, (short)heading).ToEuler();

		Assert.InRange(x, pitch - 16, pitch + 16);
		Assert.InRange(y, roll - 16, roll + 16);
		Assert.InRange(z, heading - 16, heading + 16);
	}

	private const int Translated = 0;
	private const int Turned = 1;
	private const int General = 2;

	private static Transform3 Shaped(int shape, (int X, int Y, int Z) angles, (int X, int Y, int Z) translation) {
		var transform = shape switch {
			Translated => Transform3.FromEuler(0, 0, 0),
			Turned => Transform3.FromEuler(0, 0, (short)angles.Z),
			_ => Transform3.FromEuler((short)angles.X, (short)angles.Y, (short)angles.Z),
		};

		transform.X = translation.X;
		transform.Y = translation.Y;
		transform.Z = translation.Z;
		return transform;
	}

	/// <summary>The three turns one at a time, as row vectors, in floating point.</summary>
	private static (int X, int Y, int Z) RollPitchTurn(double x, double y, double z, int pitch, int roll, int heading) {
		double Radians(int bam) => bam * TurnsPerBam * 2 * System.Math.PI;

		double c = System.Math.Cos(Radians(roll)), s = System.Math.Sin(Radians(roll));
		(x, z) = (x * c + z * s, -x * s + z * c);

		c = System.Math.Cos(Radians(pitch));
		s = System.Math.Sin(Radians(pitch));
		(y, z) = (y * c - z * s, y * s + z * c);

		c = System.Math.Cos(Radians(heading));
		s = System.Math.Sin(Radians(heading));
		(x, y) = (x * c - y * s, x * s + y * c);

		return ((int)System.Math.Round(x), (int)System.Math.Round(y), (int)System.Math.Round(z));
	}
}
