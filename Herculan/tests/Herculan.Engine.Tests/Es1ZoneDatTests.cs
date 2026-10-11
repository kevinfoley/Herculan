using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="Es1ZoneDatTransformer"/> over the <c>ZONE*.DAT</c> in ES1's <c>SIMVOL0.VOL</c> and <c>ZONES.VOL</c>
/// (docs/es1/formats/zone-terrain.md). Each test passes vacuously when <c>ES1/VOL</c> is absent, so the suite does
/// not depend on the install.
/// </summary>
public class Es1ZoneDatTests {
	private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, VolEntry>>?> Archives = new(LoadZones);

	[Fact]
	public void EveryZoneFileRoundTrips() {
		if (Archives.Value is not { } archives) {
			return;
		}

		Assert.Equal(15, archives["SIMVOL0"].Count);
		Assert.Equal(91, archives["ZONES"].Count);
		var transformer = new Es1ZoneDatTransformer();
		foreach (var (archive, zones) in archives) {
			foreach (var (name, entry) in zones) {
				var file = transformer.Parse(entry.RawBytes)!;
				Assert.True(entry.RawBytes.AsSpan().SequenceEqual(transformer.Write(file)), $"{archive} {name} does not round-trip");
			}
		}
	}

	/// <summary>The values docs/es1/formats/zone-terrain.md#the-retail-files gives for every record.</summary>
	[Fact]
	public void RecordsHoldTheDocumentedValues() {
		if (Archives.Value is not { } archives) {
			return;
		}

		var files = archives.Values.SelectMany(z => z.Values).Select(e => new Es1ZoneDatTransformer().Parse(e.RawBytes)!).ToList();
		var shapes = files.SelectMany(f => f.Shapes).ToList();

		Assert.Equal(5129, shapes.Count);
		Assert.All(files, f => Assert.InRange(f.Word00, 0, 3));
		Assert.All(shapes, s => {
			Assert.InRange(s.ShapeIndex, 0, 12);
			Assert.Equal(0, s.Z);
			Assert.Equal(0, s.Angles[0]);
			Assert.Equal(0, s.Angles[1]);
			Assert.Equal(0, s.Angles[2] % 0x800);
		});
	}

	/// <summary>The names both archives hold: <c>ZONE6</c> and <c>ZONE8</c> differ, the other six are the same bytes.</summary>
	[Fact]
	public void SharedNamesDifferOnlyInZone6AndZone8() {
		if (Archives.Value is not { } archives) {
			return;
		}

		var simvol0 = archives["SIMVOL0"];
		var zones = archives["ZONES"];
		var differing = simvol0.Keys.Where(zones.ContainsKey).Where(n => !simvol0[n].RawBytes.AsSpan().SequenceEqual(zones[n].RawBytes)).ToList();

		Assert.Equal(8, simvol0.Keys.Count(zones.ContainsKey));
		Assert.Equal(new[] { "ZONE6.DAT", "ZONE8.DAT" }, differing.Order());
	}

	/// <summary><c>ZONE7.DAT</c>, walked by hand: five records, the first shape 0 turned -0x2000.</summary>
	[Fact]
	public void Zone7ReadsItsFiveRecords() {
		if (Archives.Value is not { } archives) {
			return;
		}

		var file = new Es1ZoneDatTransformer().Parse(archives["SIMVOL0"]["ZONE7.DAT"].RawBytes)!;

		Assert.Equal(0, file.Word00);
		Assert.Equal(5, file.Shapes.Length);
		var first = file.Shapes[0];
		Assert.Equal((0, -31641, 33984, 0), ((int)first.ShapeIndex, first.X, first.Y, first.Z));
		Assert.Equal(new short[] { 0, 0, -8192 }, first.Angles);
	}

	/// <summary>ES1's table binds its zone reader to the entry; ES2's binds ES2's.</summary>
	[Fact]
	public void TheEs1TableBindsTheEs1Reader() {
		if (Archives.Value is not { } archives) {
			return;
		}

		var entry = archives["ZONES"]["ZONE1110.DAT"];
		Assert.IsType<Es1ZoneDatTransformer>(TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege1));
		Assert.IsType<ZoneDatTransformer>(TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege2));
	}

	private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, VolEntry>>? LoadZones() {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string dir = Path.Combine(at.FullName, "ES1", "VOL");
			if (!File.Exists(Path.Combine(dir, "ZONES.VOL"))) {
				continue;
			}

			return new[] { "SIMVOL0", "ZONES" }.ToDictionary(name => name, name => {
				Voln vol = VolFileReader.ParseVolFile(Path.Combine(dir, name + ".VOL"));
				return (IReadOnlyDictionary<string, VolEntry>)vol.FilesSet
					.Where(e => e.FileName != null && e.FileName.Trim().StartsWith("ZONE", StringComparison.OrdinalIgnoreCase)
						&& e.FileName.Trim().EndsWith(".DAT", StringComparison.OrdinalIgnoreCase))
					.ToDictionary(e => e.FileName!.Trim().ToUpperInvariant(), e => e);
			});
		}

		return null;
	}
}
