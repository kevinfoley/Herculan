using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Scene;

/// <summary>
/// The theater's <c>hzline</c> as ES1's simulator fills it from the world file's shorts 0–7: the sky's bands above the
/// horizon line and the ground's bands below it (docs/es1/rendering/ground-and-sky.md#the-horizon-line-and-the-sky,
/// #the-banded-ground), with the line's placement for a view. Measurements are in the original's pixels.
/// </summary>
public sealed class Es1Horizon {
	/// <summary>
	/// ES1's focal length in its own pixels, <c>2^8</c>: the main view's <c>TSCamera_Ctor</c> passes shift 8
	/// (docs/es1/rendering/view-transform-and-projection.md#differences-from-es2).
	/// </summary>
	public const float FocalPixels = 256f;

	/// <summary>The most bands either run carries; a file asking for more is clamped to this.</summary>
	public const int MaxBands = 32;

	private Es1Horizon(Vector3[] sky, Vector3[] ground, Es1WorldData world) {
		SkyColors = sky;
		GroundColors = ground;
		SkyBandHeight = world.SkyBandHeight;
		HorizonGap = world.HorizonGap >> 1;
		GroundBandHeight = world.GroundBandHeight;
		LineOffset = world.HorizonOffset;
	}

	/// <summary>
	/// The sky's colours outward from the line: <c>[k]</c> is the horizon colour <c>+0x28</c> (short 1 + short 3 − 1)
	/// less <c>k</c>, the last the zenith colour, short 1.
	/// </summary>
	public Vector3[] SkyColors { get; }

	/// <summary>
	/// The banded ground's colours down from the line: <c>[k]</c> is <i>top</i> (short 4 + short 6 − 1) less <c>k</c>,
	/// short 6 − 1 bands and then the last, short 4, which fills to the bottom of the view.
	/// </summary>
	public Vector3[] GroundColors { get; }

	/// <summary>Short 2, <c>+0x54</c>: rows per sky band.</summary>
	public int SkyBandHeight { get; }

	/// <summary>Short 0 halved, <c>+0x6c &gt;&gt; 1</c>: <i>t</i>, the rows the first band of either run spans.</summary>
	public int HorizonGap { get; }

	/// <summary>Short 5, <c>+0x5c</c>: rows per ground band after the first.</summary>
	public int GroundBandHeight { get; }

	/// <summary>Short 7, <c>+0x30</c>: the line's offset, which the line is built with and then moved down by half again.</summary>
	public int LineOffset { get; }

	/// <summary>
	/// The rows the sky's line lies below the projected horizon: <see cref="LineOffset"/> from
	/// <c>Hzline_BuildHorizon</c> and half of it again from <c>Hzline_DrawWithOffset</c>.
	/// </summary>
	public int LineDrop => LineOffset + (LineOffset >> 1);

	/// <summary>
	/// Builds the horizon from a theater's world file and palette, or returns null when the palette lacks an entry
	/// either run names or the band fields cannot draw.
	/// </summary>
	public static Es1Horizon? From(Es1WorldData world, DynamixPalette? palette) {
		int skyCount = Math.Min((int)world.SkyBandCount, MaxBands);
		int groundCount = Math.Min(world.GroundBandCount - 1, MaxBands - 1) + 1;
		if (palette == null || skyCount < 1 || world.SkyBandHeight < 1 || groundCount < 1) {
			return null;
		}

		int horizonColor = world.ZenithColor + world.SkyBandCount - 1;
		int groundTop = world.GroundColor + world.GroundBandCount - 1;
		var sky = new Vector3[skyCount];
		var ground = new Vector3[groundCount];
		for (int k = 0; k < skyCount; k++) {
			if (ColorOf(palette, horizonColor - k) is not { } color) {
				return null;
			}

			sky[k] = color;
		}

		for (int k = 0; k < groundCount; k++) {
			if (ColorOf(palette, groundTop - k) is not { } color) {
				return null;
			}

			ground[k] = color;
		}

		return new Es1Horizon(sky, ground, world);
	}

	/// <summary>
	/// How far the projected horizon lies below the projection centre, in the original's pixels along the rolled
	/// screen vertical: <c>f · tan(pitch)</c>, as <c>Hzline_BuildHorizon</c> (<c>ES1DBSIM 00026348</c>) places the
	/// line's midpoint, with a pitch whose cosine is 0 taking the cosine of the pitch less one unit.
	/// </summary>
	/// <param name="pitch">The camera's pitch, positive looking up, as a binary angle.</param>
	public static float HorizonDrop(short pitch) {
		float cos = SimTrig.Cos(pitch);
		if (cos == 0f) {
			cos = SimTrig.Cos(unchecked((short)(pitch - 1)));
		}

		return FocalPixels * SimTrig.Sin(pitch) / cos;
	}

	/// <summary>Palette entry <paramref name="index"/> as a colour, or null when the palette lacks it.</summary>
	internal static Vector3? ColorOf(DynamixPalette palette, int index) {
		if (!palette.Colors.TryGetValue(index, out var entry)) {
			return null;
		}

		var color = entry.GetColor();
		return new Vector3(color.R / 255f, color.G / 255f, color.B / 255f);
	}
}
