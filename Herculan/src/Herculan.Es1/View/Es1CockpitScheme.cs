using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;

namespace Herculan.Es1.View;

/// <summary>
/// The chassis' colour scheme <c>CockpitViewManager_LoadViews</c> (<c>ES1DBSIM 00021fb0</c>) installs into the live palette
/// when the cockpit is built (docs/es1/simulation/cockpit-views.md#the-color-scheme): live entries <c>0x20</c>–<c>0x2f</c>
/// and <c>0x40</c>–<c>0x4f</c> take <c>dpl\cockpit</c> entries 32<i>s</i> to 32<i>s</i> + 31, then entry <c>0x40</c> the
/// live colour <c>CockpitScheme_ColorIndices</c> (<c>000925dc</c>) names for <i>s</i>.
/// </summary>
/// <param name="Cockpit"><c>CockpitPaletteSingleton</c> (<c>000925f0</c>), <c>dpl\cockpit</c>.</param>
/// <param name="Scheme"><i>s</i>, the manager's <c>+0x23</c>: the player chassis' type number.</param>
public readonly record struct Es1CockpitScheme(DynamixPalette Cockpit, int Scheme) {
	/// <summary>The colours each window takes.</summary>
	public const int WindowLength = 16;

	/// <summary>Step 1's window, which takes the scheme's first 16 colours.</summary>
	public const int FirstWindow = 0x20;

	/// <summary>Step 2's window, which takes the scheme's last 16; its first entry is step 3's.</summary>
	public const int SecondWindow = 0x40;

	/// <summary>The colours of <c>dpl\cockpit</c> each scheme reads.</summary>
	public const int SchemeLength = 2 * WindowLength;

	/// <summary><c>CockpitScheme_ColorIndices</c>: per scheme, the live entry whose colour entry <see cref="SecondWindow"/> takes.</summary>
	public static IReadOnlyList<short> ColorIndices { get; } = [0x2c, 0x25, 0x2d, 0x27, 0x2d, 0x2d, 0x2d, 0x28];

	/// <summary>
	/// The live palette after the install: a copy of <paramref name="live"/> with steps 1 to 3 applied. Its ramps are
	/// <paramref name="live"/>'s, which the install does not touch.
	///
	/// <para>An entry <c>dpl\cockpit</c> lacks is not copied and the live entry keeps its colour: an engine guard for a
	/// short file, where retail reads past the colours.</para>
	/// </summary>
	public DynamixPalette Install(DynamixPalette live) {
		var colors = new Dictionary<int, ColorBytes>(live.Colors);
		int first = SchemeLength * Scheme;
		for (int k = 0; k < WindowLength; k++) {
			if (Cockpit.Colors.TryGetValue(first + k, out var low)) {
				colors[FirstWindow + k] = low;
			}

			if (Cockpit.Colors.TryGetValue(first + WindowLength + k, out var high)) {
				colors[SecondWindow + k] = high;
			}
		}

		// PLACEHOLDER: a scheme past the table reads its word from CockpitViewShakeArmed and the globals after it, which
		// the doc does not resolve to an entry; step 3 is skipped and entry 0x40 keeps step 2's colour.
		if (Scheme >= 0 && Scheme < ColorIndices.Count && colors.TryGetValue(ColorIndices[Scheme], out var named)) {
			colors[SecondWindow] = named;
		}

		return new DynamixPalette {
			ColorCount = live.ColorCount,
			Scalar = live.Scalar,
			PaletteSizeByte = live.PaletteSizeByte,
			RawIndexBytes = live.RawIndexBytes,
			Colors = colors,
			Index0AlphaKey = live.Index0AlphaKey,
			ShadeRamps = live.ShadeRamps,
			ShadeRampBytes = live.ShadeRampBytes,
		};
	}
}
