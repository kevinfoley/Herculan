using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Catches an edit to <see cref="Es1CockpitScheme.Install"/>'s windows, its 32-colour stride, or the entry <c>0x40</c>
/// override and the order it reads the live palette in (docs/es1/simulation/cockpit-views.md#the-color-scheme).
/// </summary>
public class Es1CockpitSchemeTests {
	[Fact]
	public void InstallsTheSchemesWindowsAndThenEntry0x40FromTheInstalledColours() {
		var theater = Palette(green: 0);
		var cockpit = Palette(green: 1);
		const int scheme = 2;

		var live = new Es1CockpitScheme(cockpit, scheme).Install(theater);

		int first = 32 * scheme;
		for (int k = 0; k < 16; k++) {
			Assert.Equal(Rgb(cockpit, first + k), Rgb(live, 0x20 + k));
			if (k > 0) {
				Assert.Equal(Rgb(cockpit, first + 16 + k), Rgb(live, 0x40 + k));
			}
		}

		// Scheme 2's word is 0x2d, which step 1 has just filled from the cockpit palette.
		Assert.Equal(Rgb(live, 0x2d), Rgb(live, 0x40));
		Assert.Equal(Rgb(cockpit, first + 0x2d - 0x20), Rgb(live, 0x40));
		Assert.Equal(Rgb(theater, 0x1f), Rgb(live, 0x1f));
		Assert.Equal(Rgb(theater, 0x30), Rgb(live, 0x30));
		Assert.Equal(Rgb(theater, 0x50), Rgb(live, 0x50));
		Assert.Equal(0x20 << 16, Rgb(theater, 0x20));
	}

	private static int Rgb(DynamixPalette palette, int index) => palette.Colors[index].ColorIntRgb();

	/// <summary>256 entries, entry <c>i</c> coloured (<c>i</c>, <paramref name="green"/>, 0) so the two palettes differ everywhere.</summary>
	private static DynamixPalette Palette(byte green) {
		var palette = new DynamixPalette { ColorCount = 256 };
		for (int i = 0; i < 256; i++) {
			var entry = new ColorBytes((byte)i, green, 0, 0);
			entry.SetColor(RgbaColor.FromArgb(255, i, green, 0));
			palette.Colors[i] = entry;
		}

		return palette;
	}
}
