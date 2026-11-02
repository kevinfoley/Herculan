using Herculan.Engine.Numerics;

namespace Herculan.Es1.Shell.Map;

/// <summary>
/// The shell's own fixed-point trigonometry, which differs from the simulator's: its cosine table truncates where
/// <c>ES1DBSIM</c>'s rounds, its table index truncates the angle, and its arctangent and point rotation are coarser
/// (docs/es1/shell/map.md#the-shapes). The tables are regenerated here; each formula gives every entry of the image's
/// table.
/// </summary>
public static class Es1ShellTrig {
	/// <summary>
	/// <c>Math_CosTableQ14</c> (<c>0008b648</c>): 1025 <c>int16</c>, entry <i>i</i> floor(16384 cos(<i>i</i> / 4096 turn)).
	/// </summary>
	private static readonly short[] Cosine = BuildCosine();

	/// <summary>
	/// The byte table <c>0008708c</c>, 513 signed bytes: entry <i>i</i> is floor(atan(<i>i</i> / 512) in 1/4096 turns)
	/// less <i>i</i>.
	/// </summary>
	private static readonly sbyte[] ArctangentCorrection = BuildArctangentCorrection();

	private static short[] BuildCosine() {
		var table = new short[0x401];
		for (int i = 0; i < table.Length; i++) {
			table[i] = (short)Math.Floor(Math.Cos(i * (2.0 * Math.PI / 0x1000)) * 0x4000);
		}

		return table;
	}

	private static sbyte[] BuildArctangentCorrection() {
		var table = new sbyte[0x201];
		for (int i = 0; i < table.Length; i++) {
			table[i] = (sbyte)(Math.Floor(Math.Atan(i / 512.0) * (0x1000 / (2.0 * Math.PI))) - i);
		}

		return table;
	}

	/// <summary>
	/// The cosine of a binary angle as <c>Math_BuildRotation2D</c> (<c>0005533c</c>) reads it: the angle <c>SAR 4</c> with
	/// its sign removed, entry <i>k</i> below <c>0x400</c>, otherwise entry <c>0x800</c> − <i>k</i> negated.
	/// </summary>
	public static short Cos(short angle) {
		int index = angle >> 4;
		index = Math.Abs(index);
		return (short)index < 0x400 ? Cosine[index] : (short)-Cosine[0x800 - index];
	}

	/// <summary>
	/// <c>BuildEulerRotationMatrixQ14</c> (<c>000589c4</c>)(<paramref name="x"/>, <paramref name="y"/>,
	/// <paramref name="z"/>): the simulator's rotation (<see cref="Transform3.FromEuler(short, short, short)"/>) with
	/// every cosine read through <see cref="Cos"/> (docs/es1/shell/map.md#the-shapes).
	/// </summary>
	public static Transform3 Rotation(short x, short y, short z) => Transform3.FromEuler(x, y, z, Cos);

	/// <summary>
	/// <c>Math_BuildRotation2D</c> (<c>0005533c</c>)(<paramref name="angle"/>): {cos <i>a</i>, sin <i>a</i>, −sin <i>a</i>,
	/// cos <i>a</i>} in Q14, the sine being <see cref="Cos"/> of the angle less <c>0x4000</c>.
	/// </summary>
	public static (short M0, short M1, short M2, short M3) Rotation2D(short angle) {
		short cos = Cos(angle);
		short sin = Cos(unchecked((short)(angle - 0x4000)));
		return (cos, sin, (short)-sin, cos);
	}

	/// <summary>
	/// <c>000553bc</c>(<paramref name="x"/>, <paramref name="y"/>): atan2 as a binary angle. With <paramref name="y"/> 0
	/// the angle is 0; otherwise the smaller magnitude shifted left 9 over the larger is an index <i>q</i>, the angle
	/// <i>q</i> plus entry <i>q</i> of the correction table, or <c>0x400</c> less that when |<paramref name="x"/>| is the
	/// smaller. A negative <paramref name="x"/> makes it <c>0x800</c> less the angle, then a negative <paramref name="y"/>
	/// <c>0x1000</c> less it, and the result is shifted left 4, unmasked.
	/// </summary>
	public static int Atan2(int x, int y) {
		int absX = (x ^ x >> 31) - (x >> 31), absY = (y ^ y >> 31) - (y >> 31);
		int angle = 0;
		if (absY != 0) {
			if (absX < absY) {
				int q = (int)((long)absX * 0x200 / absY);
				angle = 0x400 - (q + ArctangentCorrection[q]);
			} else {
				int q = (int)((long)absY * 0x200 / absX);
				angle = q + ArctangentCorrection[q];
			}
		}

		if (x < 0) {
			angle = 0x800 - angle;
		}

		if (y < 0) {
			angle = 0x1000 - angle;
		}

		return angle << 4;
	}

	/// <summary>
	/// <c>000552e0</c>(point, matrix, out): an <c>int16</c> point through a 2×2 Q14 matrix, each sum rounded at Q14 in 32
	/// bits and stored as an <c>int16</c>.
	/// </summary>
	public static (short X, short Y) Rotate(short x, short y, (short M0, short M1, short M2, short M3) m) => (
		unchecked((short)(x * m.M0 + y * m.M2 + 0x2000 >> 14)),
		unchecked((short)(x * m.M1 + y * m.M3 + 0x2000 >> 14)));
}
