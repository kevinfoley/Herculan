using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="Es1WorldDataTransformer"/> over the <c>.WLD</c> in ES1's <c>SIMVOL0.VOL</c> (docs/es1/formats/wld-world.md).
/// Each test passes vacuously when <c>ES1/VOL</c> is absent, so the suite does not depend on the install.
/// </summary>
public class Es1WorldDataTests {
	private static readonly Lazy<IReadOnlyDictionary<string, VolEntry>?> Worlds = new(LoadWorlds);

	[Fact]
	public void EveryWorldFileRoundTrips() {
		if (Worlds.Value is not { } worlds) {
			return;
		}

		Assert.Equal(24, worlds.Count);
		var transformer = new Es1WorldDataTransformer();
		foreach (var (name, entry) in worlds) {
			var file = transformer.Parse(entry.RawBytes)!;
			Assert.True(entry.RawBytes.AsSpan().SequenceEqual(transformer.Write(file)), $"{name} does not round-trip");
		}
	}

	/// <summary>The values docs/es1/formats/wld-world.md#the-layout gives for every file.</summary>
	[Fact]
	public void EveryFileHoldsTheDocumentedValues() {
		if (Worlds.Value is not { } worlds) {
			return;
		}

		Assert.All(worlds.Values.Select(e => new Es1WorldDataTransformer().Parse(e.RawBytes)!), w => {
			Assert.Equal((16, 16), (w.DistanceBandsA.Length, w.DistanceBandsB.Length));
			Assert.Equal((16, 8, 8), ((int)w.RampRows, (int)w.RampColumns, (int)w.BetweenRampTables));
			Assert.Equal((20, 250), ((int)w.CloudCount, (int)w.PointCount));
			Assert.Equal(("clouds2", "impact1"), (w.CloudBank, w.ImpactPalette));
		});
	}

	/// <summary>
	/// The pair pattern and the theaters (docs/es1/formats/wld-world.md#which-file): world N with N even names its
	/// sky bank <c>worldN</c> and fades by <c>tex</c>, N odd names none and fades by <c>tex_lo</c> (<c>tex_lo2</c>
	/// in <c>WORLD9</c>); <c>urban</c>, N 18 to 23, wears <c>cracks</c>; the flat set is <c>flat2</c> in 16 and 18 to 21.
	/// </summary>
	[Fact]
	public void TheFilesFollowTheDocumentedPattern() {
		if (Worlds.Value is not { } worlds) {
			return;
		}

		for (int n = 0; n < 24; n++) {
			var w = new Es1WorldDataTransformer().Parse(worlds[$"WORLD{n}.WLD"].RawBytes)!;
			bool lo = n % 2 == 1;
			Assert.Equal(lo ? "" : $"world{n}", w.SkyBank);
			Assert.Equal(lo ? (n == 9 ? "tex_lo2" : "tex_lo") : "tex", w.GroundFadeFile);
			Assert.Equal(n >= 18 ? "cracks" : "trenches", w.GroundTextureBank);
			Assert.Equal(n is 16 or (>= 18 and <= 21), w.FlatSetSelector != 0);
		}
	}

	/// <summary><c>WORLD0.WLD</c>, walked by hand: its twelve shorts and its distance offsets.</summary>
	[Fact]
	public void World0ReadsItsHeader() {
		if (Worlds.Value is not { } worlds) {
			return;
		}

		var w = new Es1WorldDataTransformer().Parse(worlds["WORLD0.WLD"].RawBytes)!;

		Assert.Equal(new short[] { 2, 214, 1, 10, 247, 1, 8, 0, 233, 0, 47, 219 }, w.Header);
		Assert.Equal((30000, 57000), (w.DistanceBandsA[0], w.DistanceBandsA[^1]));
		Assert.Equal((-20000, -50000), (w.DistanceOffsetSmall, w.DistanceOffsetLarge));
	}

	/// <summary>ES1's table binds its world reader to the entry; ES2's binds ES2's.</summary>
	[Fact]
	public void TheEs1TableBindsTheEs1Reader() {
		if (Worlds.Value is not { } worlds) {
			return;
		}

		var entry = worlds["WORLD0.WLD"];
		Assert.IsType<Es1WorldDataTransformer>(TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege1));
		Assert.IsType<WorldDataTransformer>(TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege2));
	}

	private static IReadOnlyDictionary<string, VolEntry>? LoadWorlds() {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES1", "VOL", "SIMVOL0.VOL");
			if (!File.Exists(vol)) {
				continue;
			}

			return VolFileReader.ParseVolFile(vol).FilesSet
				.Where(e => e.FileName != null && e.FileName.Trim().EndsWith(".WLD", StringComparison.OrdinalIgnoreCase))
				.ToDictionary(e => e.FileName!.Trim().ToUpperInvariant(), e => e);
		}

		return null;
	}
}
