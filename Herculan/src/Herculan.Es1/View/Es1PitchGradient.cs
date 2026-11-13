using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Sim;

namespace Herculan.Es1.View;

/// <summary>
/// The pitch gradient (docs/es1/rendering/ground-and-sky.md#the-pitch-gradient-palette-entries-6479):
/// <c>Palette_BlendRangeByPitch</c> (<c>ES1DBSIM 00025fe4</c>) and the palette module's
/// <c>Palette_InterpolateColours</c> (<c>000283b0</c>), on the gradient's globals: the four colors read once
/// (<c>000afdd4</c>–<c>000afde0</c>), the 16 built from two of them (<c>000afde4</c>), the built flag
/// (<c>00092a3e</c>), the 16 installed (<c>000afe24</c>) and the impact flag (<c>000928eb</c>). Its two switches are the
/// simulation's (<see cref="Es1SimGlobals.PitchGradientEnabled"/>, <see cref="Es1SimGlobals.PitchGradientForcedStep"/>).
/// Colors are retail's palette dwords (<see cref="Es1PaletteFade.Entries"/>).
/// </summary>
public sealed class Es1PitchGradient {
	/// <summary>
	/// The entry whose color the gradient starts from and the first it rewrites: <c>000bc5ce</c> as the load stores it,
	/// and <c>Palette_InstallRange</c>'s first entry.
	/// </summary>
	public const int FirstEntry = 64;

	/// <summary>How many entries the gradient rewrites, and how many colors it builds from the world file's two.</summary>
	public const int EntryCount = 16;

	/// <summary><c>000bc5d4</c> as the load stores it: the entry whose color ends the gradient while <see cref="ImpactShowing"/> is set.</summary>
	public const int ImpactEntry = 98;

	/// <summary>The highest step the camera's pitch gives.</summary>
	public const int MaxStep = 15;

	private readonly uint[] _steps = new uint[EntryCount];
	private readonly uint[] _installed = new uint[EntryCount];
	private uint _start, _impactEnd;

	/// <summary>The built flag (<c>00092a3e</c>), which the first <see cref="Blend"/> sets once it has read its colors.</summary>
	public bool Built { get; private set; }

	/// <summary>
	/// <c>000928eb</c>, set while the impact palette shows. Its writers are the impact palette's switches
	/// (<see cref="Es1ImpactPalette"/>).
	/// </summary>
	public bool ImpactShowing { get; set; }

	/// <summary>
	/// <c>Palette_BlendRangeByPitch</c> for a camera pitched <paramref name="pitch"/> (the camera's <c>+0xc</c>), while
	/// <see cref="Es1SimGlobals.PitchGradientEnabled"/> is set. The first time, the colors of <see cref="FirstEntry"/>,
	/// <paramref name="world"/>'s <see cref="Es1WorldData.PitchDownEntry"/> and <see cref="Es1WorldData.PitchUpEntry"/>
	/// and <see cref="ImpactEntry"/> are read from <paramref name="live"/>, and <see cref="EntryCount"/> colors built from
	/// the down entry's to the up entry's. Then the step: <see cref="Es1SimGlobals.PitchGradientForcedStep"/> when it is
	/// not negative, which then becomes −1; otherwise ((pitch + <c>0x2000</c>) &amp; <c>0xffff</c>) &gt;&gt; 10, at most
	/// <see cref="MaxStep"/>. The end color is <see cref="ImpactEntry"/>'s while <see cref="ImpactShowing"/> is set,
	/// otherwise built color number step; <see cref="EntryCount"/> colors from <see cref="FirstEntry"/>'s to it are
	/// installed from <see cref="FirstEntry"/> through <paramref name="install"/>. A forced step is not range-checked, as
	/// in retail, where only 8 is stored. With no live palette before the first read, which retail always has, nothing is
	/// done: an engine guard.
	/// </summary>
	public void Blend(Es1SimGlobals globals, short pitch, Es1WorldData world, DynamixPalette? live,
			Action<int, IReadOnlyList<uint>>? install) {
		if (!globals.PitchGradientEnabled) {
			return;
		}

		if (!Built) {
			if (live == null) {
				return;
			}

			var entries = Es1PaletteFade.Entries(live);
			_start = Entry(entries, FirstEntry);
			uint down = Entry(entries, world.PitchDownEntry), up = Entry(entries, world.PitchUpEntry);
			_impactEnd = Entry(entries, ImpactEntry);
			Interpolate(_steps, up, down);
			Built = true;
		}

		int step;
		if (globals.PitchGradientForcedStep < 0) {
			step = Math.Min((ushort)(pitch + 0x2000) >> 10, MaxStep);
		} else {
			step = globals.PitchGradientForcedStep;
			globals.PitchGradientForcedStep = -1;
		}

		uint end = ImpactShowing ? _impactEnd : _steps[step];
		Interpolate(_installed, end, _start);
		install?.Invoke(FirstEntry, _installed);
	}

	/// <summary>
	/// <c>Palette_InterpolateColours</c>(<paramref name="output"/>, its length, 1, <paramref name="to"/>,
	/// <paramref name="from"/>): the length less one colors from <paramref name="from"/>'s, each channel read as a signed
	/// byte and stepping in 8.8 by ((to − from) × 256) / (length − 1), truncated to an <c>int16</c>, each written with 1 in
	/// its top byte; then <paramref name="to"/>'s dword unchanged. Retail's count of color pairs is 1 at both of the
	/// gradient's calls, so this takes one pair.
	/// </summary>
	public static void Interpolate(uint[] output, uint to, uint from) {
		unchecked {
			int steps = output.Length - 1;
			int fromRed = (sbyte)from, fromGreen = (sbyte)(from >> 8), fromBlue = (sbyte)(from >> 16);
			int red = 0, green = 0, blue = 0;
			for (int i = 0; i < steps; i++) {
				output[i] = (uint)(((red >> 8) + fromRed) & 0xff) | (uint)(((green >> 8) + fromGreen) & 0xff) << 8
					| (uint)(((blue >> 8) + fromBlue) & 0xff) << 16 | 0x1000000;
				red += (short)(((sbyte)to - fromRed) * 0x100 / steps);
				green += (short)(((sbyte)(to >> 8) - fromGreen) * 0x100 / steps);
				blue += (short)(((sbyte)(to >> 16) - fromBlue) * 0x100 / steps);
			}

			if (output.Length > 0) {
				output[^1] = to;
			}
		}
	}

	/// <summary><c>Palette_GetEntry</c>: entry <paramref name="index"/>'s dword; an entry past the palette reads 0, an engine guard.</summary>
	private static uint Entry(uint[] entries, int index) => index >= 0 && index < entries.Length ? entries[index] : 0;
}
