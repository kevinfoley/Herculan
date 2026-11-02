using System.Runtime.CompilerServices;

namespace Herculan.Engine.Numerics;

/// <summary>
/// DBSIM's 0x20-byte rigid transform: a Q14 3x3 rotation, a one-byte kind tag, and an integer
/// translation. Every animated node transform, every object world matrix and every root-motion
/// delta is one of these.
///
/// <para><b>Storage order is not row-major.</b> The nine rotation shorts hold the 2x2 XY block
/// first, then the third column, then the third row:</para>
/// <code>
///        | m0 m1 m4 |
///   M =  | m2 m3 m5 |
///        | m6 m7 m8 |
/// </code>
/// <para>which is what lets <see cref="Kind"/> 1 — rotation about Z alone, by far the commonest
/// case — touch just the first four entries and skip the rest. Recovered from
/// <c>Transform_Concat</c> (<c>0047f914</c>)'s index pattern and confirmed against <c>BuildEulerRotationMatrixQ14</c>'s
/// Z-only fast path.</para>
///
/// <para>Vectors are <b>row vectors</b>: <c>p' = p * M + t</c> (<c>Transform_ApplyToPoint</c>, <c>00480330</c>), so
/// <see cref="Concat"/>'s result applies its first argument before its second.</para>
/// </summary>
public struct Transform3 {
	/// <summary>Rotation is identity — only the translation means anything.</summary>
	public const byte KindTranslationOnly = 0;

	/// <summary>Rotation is about Z alone; entries 4-7 are zero and entry 8 is 1.0.</summary>
	public const byte KindZOnly = 1;

	/// <summary>Rotation is general. Every test in the original is <c>&lt; 1</c>, <c>&lt; 2</c> or
	/// <c>== 1</c>, so <see cref="KindFullTilted"/> behaves identically everywhere it is read.</summary>
	public const byte KindFull = 2;

	/// <summary>
	/// What <c>BuildEulerRotationMatrixQ14</c> tags a general rotation whose X euler angle is
	/// nonzero. Preserved because the original stores it, not because anything branches on it.
	/// </summary>
	public const byte KindFullTilted = 3;

	/// <summary>The nine Q14 rotation entries, in the storage order the type summary describes.</summary>
	public Mat9 M;

	/// <summary>Which of the shapes the rotation has — see the <c>Kind*</c> constants.</summary>
	public byte Kind;

	/// <summary>Translation, in whatever units the caller's space uses.</summary>
	public int X;

	/// <inheritdoc cref="X" />
	public int Y;

	/// <inheritdoc cref="X" />
	public int Z;

	/// <summary>The identity transform.</summary>
	public static Transform3 Identity {
		get {
			var result = default(Transform3);
			result.M[0] = SimTrig.One;
			result.M[3] = SimTrig.One;
			result.M[8] = SimTrig.One;
			return result;
		}
	}

	/// <summary>
	/// <c>BuildEulerRotationMatrixQ14</c> (<c>0047eaac</c>) — the rotation for an XYZ euler triple,
	/// picking the cheapest of the <see cref="Kind"/> shapes. The general branch is transcribed as
	/// written: it evaluates product-to-sum identities against the cosine table rather than
	/// composing three matrices, so it reaches the same Q14 values including their rounding.
	/// </summary>
	public static Transform3 FromEuler(short angleX, short angleY, short angleZ) => FromEuler(angleX, angleY, angleZ, SimTrig.Cos);

	/// <summary>
	/// <see cref="FromEuler(short, short, short)"/> reading its cosines through <paramref name="cos"/>: a binary
	/// program whose rotation differs only in its cosine lookup passes its own.
	/// </summary>
	public static Transform3 FromEuler(short angleX, short angleY, short angleZ, Func<short, short> cos) {
		var result = default(Transform3);

		if (angleX == 0 && angleY == 0) {
			if (angleZ == 0) {
				result.M[0] = SimTrig.One;
				result.M[3] = SimTrig.One;
				result.M[8] = SimTrig.One;
				result.Kind = KindTranslationOnly;
				return result;
			}

			short cosZ = cos(angleZ);
			short sinZ = cos(Quarter(angleZ, -1));
			result.M[0] = cosZ;
			result.M[3] = cosZ;
			result.M[1] = sinZ;
			result.M[2] = (short)-sinZ;
			result.M[8] = SimTrig.One;
			result.Kind = KindZOnly;
			return result;
		}

		result.Kind = angleX == 0 ? KindFull : KindFullTilted;

		int sinX = cos(Quarter(angleX, -1));
		result.M[5] = (short)sinX;

		// Each of the four cross terms is the half-sum and half-difference of two cosines of summed
		// angles -- the product-to-sum form of the cos*sin products the entries would otherwise be.
		int cosPair = cos(Sum(angleY, (short)-angleZ));
		int cosPair2 = cos(Sum(angleZ, angleY));
		int sum = (cosPair + cosPair2) >> 1;
		int difference = (cosPair - cosPair2) >> 1;
		result.M[0] = (short)-((short)((sinX * difference + 0x2000) >> 14) - (short)sum);
		result.M[7] = (short)-((short)((sinX * sum + 0x2000) >> 14) - (short)difference);

		cosPair = cos(Quarter(Sum(angleY, angleZ), -1));
		cosPair2 = cos(Quarter(Sum(angleZ, (short)-angleY), -1));
		sum = (cosPair + cosPair2) >> 1;
		difference = (cosPair - cosPair2) >> 1;
		result.M[1] = (short)((short)((sinX * difference + 0x2000) >> 14) + (short)sum);
		result.M[6] = (short)((short)((sinX * sum + 0x2000) >> 14) + (short)difference);

		result.M[3] = (short)((cos(Sum(angleZ, angleX)) + cos(Sum(angleX, (short)-angleZ))) >> 1);
		result.M[8] = (short)((cos(Sum(angleY, angleX)) + cos(Sum(angleX, (short)-angleY))) >> 1);

		// Note these two differ in where the negation falls relative to the shift, and the original
		// really does spell them differently -- they round apart by one unit at odd sums.
		result.M[4] = (short)(-(cos(Quarter(Sum(angleY, angleX), -1))
			+ cos(Quarter(Sum(angleY, (short)-angleX), -1))) >> 1);
		result.M[2] = (short)-(short)((cos(Quarter(Sum(angleZ, angleX), -1))
			+ cos(Quarter(Sum(angleZ, (short)-angleX), -1))) >> 1);

		return result;
	}

	/// <summary>
	/// <c>Transform_Concat</c> (<c>0047f914</c>) — composes two transforms. <paramref name="first"/> applies before
	/// <paramref name="second"/> (row-vector convention).
	/// </summary>
	public static Transform3 Concat(in Transform3 first, in Transform3 second) {
		var result = default(Transform3);

		if (first.Kind < KindZOnly) {
			result.M = second.M;
			result.Kind = second.Kind;
			result.X = second.TransformX(first.X, first.Y, first.Z);
			result.Y = second.TransformY(first.X, first.Y, first.Z);
			result.Z = second.TransformZ(first.X, first.Y, first.Z);
			return result;
		}

		if (second.Kind < KindZOnly) {
			// The original returns here rather than falling into the shared translation code, so
			// the translation is a plain component-wise sum.
			result.M = first.M;
			result.Kind = first.Kind;
			result.X = first.X + second.X;
			result.Y = first.Y + second.Y;
			result.Z = first.Z + second.Z;
			return result;
		}

		if (second.Kind < KindFull) {
			result.M[0] = Q14(first.M[0] * second.M[0] + first.M[1] * second.M[2]);
			result.M[1] = Q14(first.M[0] * second.M[1] + first.M[1] * second.M[3]);
			result.M[2] = Q14(first.M[2] * second.M[0] + first.M[3] * second.M[2]);
			result.M[3] = Q14(first.M[2] * second.M[1] + first.M[3] * second.M[3]);

			if (first.Kind == KindZOnly) {
				result.M[8] = SimTrig.One;
				result.Kind = KindZOnly;
			} else {
				result.M[6] = Q14(first.M[6] * second.M[0] + first.M[7] * second.M[2]);
				result.M[7] = Q14(first.M[6] * second.M[1] + first.M[7] * second.M[3]);
				result.M[4] = first.M[4];
				result.M[5] = first.M[5];
				result.M[8] = first.M[8];
				result.Kind = KindFull;
			}
		} else if (first.Kind == KindZOnly) {
			result.M[0] = Q14(first.M[0] * second.M[0] + first.M[1] * second.M[2]);
			result.M[1] = Q14(first.M[0] * second.M[1] + first.M[1] * second.M[3]);
			result.M[4] = Q14(first.M[0] * second.M[4] + first.M[1] * second.M[5]);
			result.M[2] = Q14(first.M[2] * second.M[0] + first.M[3] * second.M[2]);
			result.M[3] = Q14(first.M[2] * second.M[1] + first.M[3] * second.M[3]);
			result.M[5] = Q14(first.M[2] * second.M[4] + first.M[3] * second.M[5]);
			result.M[6] = second.M[6];
			result.M[7] = second.M[7];
			result.M[8] = second.M[8];
			result.Kind = KindFull;
		} else {
			result.M[0] = Q14(first.M[0] * second.M[0] + first.M[1] * second.M[2] + first.M[4] * second.M[6]);
			result.M[1] = Q14(first.M[0] * second.M[1] + first.M[1] * second.M[3] + first.M[4] * second.M[7]);
			result.M[4] = Q14(first.M[0] * second.M[4] + first.M[1] * second.M[5] + first.M[4] * second.M[8]);
			result.M[2] = Q14(first.M[2] * second.M[0] + first.M[3] * second.M[2] + first.M[5] * second.M[6]);
			result.M[3] = Q14(first.M[2] * second.M[1] + first.M[3] * second.M[3] + first.M[5] * second.M[7]);
			result.M[5] = Q14(first.M[2] * second.M[4] + first.M[3] * second.M[5] + first.M[5] * second.M[8]);
			result.M[6] = Q14(first.M[6] * second.M[0] + first.M[7] * second.M[2] + first.M[8] * second.M[6]);
			result.M[7] = Q14(first.M[6] * second.M[1] + first.M[7] * second.M[3] + first.M[8] * second.M[7]);
			result.M[8] = Q14(first.M[6] * second.M[4] + first.M[7] * second.M[5] + first.M[8] * second.M[8]);
			result.Kind = KindFull;
		}

		// Whatever shape the rotations had, the shared tail always uses the full 3x3 form.
		result.X = second.TransformX(first.X, first.Y, first.Z);
		result.Y = second.TransformY(first.X, first.Y, first.Z);
		result.Z = second.TransformZ(first.X, first.Y, first.Z);
		return result;
	}

	/// <summary><c>Transform_ApplyToPoint</c> (<c>00480330</c>) — <c>p * M + t</c>, in this transform's space.</summary>
	public readonly Vec3i TransformPoint(int x, int y, int z) {
		if (Kind < KindZOnly) {
			return new Vec3i(X + x, Y + y, Z + z);
		}

		if (Kind == KindZOnly) {
			return new Vec3i(
				Q14Long((long)x * M[0] + (long)y * M[2]) + X,
				Q14Long((long)x * M[1] + (long)y * M[3]) + Y,
				Z + z);
		}

		return new Vec3i(TransformX(x, y, z), TransformY(x, y, z), TransformZ(x, y, z));
	}

	/// <summary><c>Transform_RotatePoint</c> (<c>004801f8</c>) — the same product with the translation left out.</summary>
	public readonly Vec3i RotateVector(int x, int y, int z) {
		if (Kind < KindZOnly) {
			return new Vec3i(x, y, z);
		}

		if (Kind == KindZOnly) {
			return new Vec3i(
				Q14Long((long)x * M[0] + (long)y * M[2]),
				Q14Long((long)x * M[1] + (long)y * M[3]),
				z);
		}

		return new Vec3i(
			Q14Long((long)x * M[0] + (long)y * M[2] + (long)z * M[6]),
			Q14Long((long)x * M[1] + (long)y * M[3] + (long)z * M[7]),
			Q14Long((long)x * M[4] + (long)y * M[5] + (long)z * M[8]));
	}

	/// <summary>
	/// <c>Transform_RotateVector2D</c> (<c>0047fe80</c>) — rotates a 2D vector by the XY block alone, dropping Z entirely. Used by
	/// the terrain-slope term, which only ever needs a ground-plane heading.
	/// </summary>
	public readonly (short X, short Y) RotateVector2D(short x, short y) => (
		(short)((x * M[0] + y * M[2] + 0x2000) >> 14),
		(short)((x * M[1] + y * M[3] + 0x2000) >> 14));

	/// <summary>
	/// <c>Transform_TransposeRotation</c> (<c>0047de0d</c>) — transposes the rotation in place, which inverts it. It swaps only the
	/// three off-diagonal pairs and leaves <see cref="Kind"/> alone, so a Z-only rotation stays one.
	/// </summary>
	public void TransposeRotation() {
		(M[1], M[2]) = (M[2], M[1]);
		(M[4], M[6]) = (M[6], M[4]);
		(M[5], M[7]) = (M[7], M[5]);
	}

	/// <summary>
	/// The inverse of a rigid transform — the two-step idiom the original inlines wherever it has to
	/// bring a world point into an object's own space: transpose the rotation (<c>Transform_TransposeRotation</c>),
	/// then rotate the negated translation through it (<c>Transform_RotatePoint</c>). Both the shared raycast
	/// (<c>Sim_RaycastObjectList</c>) and the direct-fire shield test
	/// (<c>Mech_ShieldAbsorb_DirectFire</c>) build it exactly that way, in place, on a copy.
	/// </summary>
	public readonly Transform3 Inverted() {
		var result = this;
		result.TransposeRotation();

		var translation = result.RotateVector(-X, -Y, -Z);
		result.X = translation.X;
		result.Y = translation.Y;
		result.Z = translation.Z;
		return result;
	}

	/// <summary>
	/// <c>Transform_RotationToEuler</c> (<c>0047f894</c>) — recovers the XYZ euler triple from the rotation, with the usual
	/// gimbal-lock branch when the X angle reaches a quarter turn.
	/// </summary>
	public readonly (short X, short Y, short Z) ToEuler() {
		short angleX = (short)SimTrig.Asin(M[5]);

		int sign = angleX >> 15;
		if ((ushort)((angleX ^ sign) - sign) == BinaryAngle.QuarterTurn) {
			return (angleX, 0, (short)SimTrig.Atan2(M[1], M[0]));
		}

		return (angleX, (short)SimTrig.Atan2(-M[4], M[8]), (short)SimTrig.Atan2(-M[2], M[3]));
	}

	private readonly int TransformX(int x, int y, int z) =>
		Q14Long((long)x * M[0] + (long)y * M[2] + (long)z * M[6]) + X;

	private readonly int TransformY(int x, int y, int z) =>
		Q14Long((long)x * M[1] + (long)y * M[3] + (long)z * M[7]) + Y;

	private readonly int TransformZ(int x, int y, int z) =>
		Q14Long((long)x * M[4] + (long)y * M[5] + (long)z * M[8]) + Z;

	/// <summary>Angle sum, wrapped to 16 bits the way the original's <c>short</c> arithmetic does.</summary>
	private static short Sum(short a, short b) => unchecked((short)(a + b));

	/// <summary>Adds <paramref name="quarters"/> quarter turns, wrapped to 16 bits.</summary>
	private static short Quarter(short angle, int quarters) =>
		unchecked((short)(angle + quarters * BinaryAngle.QuarterTurn));

	private static short Q14(int product) => (short)((product + 0x2000) >> 14);

	private static int Q14Long(long product) => (int)((product + 0x2000) >> 14);

	/// <summary>The nine Q14 rotation entries of a <see cref="Transform3"/>.</summary>
	[InlineArray(9)]
	public struct Mat9 {
		private short _element;
	}
}
