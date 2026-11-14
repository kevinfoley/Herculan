using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;
using Herculan.Es1.Host;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The pitch gradient with the lift's darkening (docs/es1/simulation/lift-start.md#the-darkening--liftstart_darkenpalette-00057cf4):
/// the darkening copies the live palette after the gradient has installed its step 8 into it.
/// </summary>
public class Es1PitchGradientTests {
	/// <summary>
	/// Entries 64–79 black and both of the world file's entries grey: the gradient ends entry 79 on the grey, and the dark
	/// set halves it. Catches the darkening reading a live palette fetched before the gradient's install, which still holds
	/// the black, and the gradient's call dropped from the darkening.
	/// </summary>
	[Fact]
	public void TheLiftsDarkeningCopiesTheGradientsColors() {
		const int grey = 40, worldEntry = 47;
		var live = Palette(worldEntry, grey);
		var installs = new List<(int First, uint[] Entries)>();
		var frames = new Es1FrameLoop(Views(), () => { }, viewCamera: () => new Es1Camera()) {
			World = new Es1WorldData { Header = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, worldEntry, worldEntry] },
			LivePalette = () => live,
		};
		frames.InstallPaletteRange = (first, entries) => {
			installs.Add((first, entries.ToArray()));
			live = Installed(live, first, entries);
		};

		Es1LiftStart.DarkenPalette(frames);

		// The gradient's install, then the dark set's second span.
		var atFirst = installs.Where(install => install.First == Es1PitchGradient.FirstEntry).ToArray();
		Assert.Equal(2, atFirst.Length);
		Assert.Equal(grey / 2 * 0x10101u, atFirst[1].Entries[^1] & 0xffffff);
	}

	/// <summary>256 black entries but <paramref name="entry"/>, grey <paramref name="grey"/> in every channel.</summary>
	private static DynamixPalette Palette(int entry, int grey) {
		var palette = new DynamixPalette { ColorCount = 256 };
		for (int i = 0; i < 256; i++) {
			byte value = (byte)(i == entry ? grey : 0);
			palette.Colors[i] = new ColorBytes(value, value, value, 255);
		}

		return palette;
	}

	/// <summary>A new palette with <paramref name="entries"/> from <paramref name="first"/>, as the presenter sends one.</summary>
	private static DynamixPalette Installed(DynamixPalette live, int first, IReadOnlyList<uint> entries) {
		var palette = new DynamixPalette { ColorCount = live.ColorCount, Colors = new Dictionary<int, ColorBytes>(live.Colors) };
		for (int i = 0; i < entries.Count; i++) {
			palette.Colors[first + i] = new ColorBytes((byte)(entries[i] & 0xff), (byte)(entries[i] >> 8 & 0xff),
				(byte)(entries[i] >> 16 & 0xff), 255);
		}

		return palette;
	}

	/// <summary>The views on four constructed records, as <see cref="Es1FramePacingTests"/> builds them.</summary>
	private static Es1ViewManager Views() {
		var records = Enumerable.Range(0, 4)
			.Select(_ => new Vue.Entry {
				ViewportX0 = 0, ViewportY0 = 0, ViewportX1 = 319, ViewportY1 = 140, CenterX = -160, CenterY = -70,
			})
			.ToArray();
		var clips = records.Select(_ => Es1ClipBlock.FromRect(0, 0, 319, 140)).ToArray();
		var files = new Es1CockpitViews("TEST", records, clips, new DynamixBitmap?[4], null);
		return new Es1ViewManager(files, new Es1VideoMemory());
	}
}
