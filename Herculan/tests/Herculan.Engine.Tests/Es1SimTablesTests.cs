using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's readers for the ten <c>dat\</c> tables of its <c>SIMVOL0.VOL</c> that ES2's table has no label for:
/// <c>STYLES</c>, <c>FFORMS</c>, <c>MFORMS</c>, <c>BFORMS</c>, <c>BASECOL</c>, <c>DEBRIS</c> and the four theater shape
/// tables (docs/es1/formats/styles-dat.md, formations-dat.md, bases-dat.md, debris-dat.md, zone-terrain.md). Each test
/// passes vacuously when <c>ES1/VOL</c> is absent, so the suite does not depend on the install.
/// </summary>
public class Es1SimTablesTests {
	private static readonly Lazy<IReadOnlyDictionary<string, VolEntry>?> Tables = new(Load);

	private static readonly string[] Names = [
		"STYLES.DAT", "FFORMS.DAT", "MFORMS.DAT", "BFORMS.DAT", "BASECOL.DAT", "DEBRIS.DAT",
		"DESERT.DAT", "TEMPER.DAT", "ICE.DAT", "URBAN.DAT",
	];

	[Fact]
	public void EveryTableRoundTripsThroughTheEs1Table() {
		if (Tables.Value is not { } tables) {
			return;
		}

		Assert.Equal(Names.Length, tables.Count);
		foreach (var (name, entry) in tables) {
			var transformer = TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege1)!;
			Assert.StartsWith("Es1", transformer.GetType().Name);
			object model = transformer.ParseToObject(entry.RawBytes)!;
			var write = transformer.GetType().GetMethod("Write", [model.GetType()])!;
			Assert.True(entry.RawBytes.AsSpan().SequenceEqual((byte[])write.Invoke(transformer, [model])!), $"{name} does not round-trip");
		}
	}

	/// <summary>docs/es1/formats/zone-terrain.md#dattheater-the-shapes-dividing-lines: which shapes have a line.</summary>
	[Fact]
	public void TheaterTablesGiveTheDocumentedShapesALine() {
		if (Tables.Value is not { } tables) {
			return;
		}

		var expected = new Dictionary<string, int[]> {
			["DESERT.DAT"] = [0, 1, 2, 9], ["TEMPER.DAT"] = [0, 2, 9], ["ICE.DAT"] = [0, 1, 2, 4, 9], ["URBAN.DAT"] = [2],
		};
		foreach (var (name, shapes) in expected) {
			var lines = new Es1TheaterShapeLinesTransformer().Parse(tables[name].RawBytes)!.Lines;
			Assert.Equal(13, lines.Length);
			Assert.Equal(shapes, Enumerable.Range(0, 13).Where(i => lines[i].StartZ >= 0));
			Assert.All(lines.Where(l => l.StartZ >= 0), l => Assert.Equal((0, 0), (l.StartZ, l.EndZ)));
			Assert.All(lines.Where(l => l.StartZ < 0), l => Assert.Equal((0, 0, -1, 0, 0, -1),
				(l.StartX, l.StartY, l.StartZ, l.EndX, l.EndY, l.EndZ)));
		}
	}

	/// <summary>docs/es1/formats/bases-dat.md#the-collision-models and debris-dat.md#the-layout.</summary>
	[Fact]
	public void CollisionModelsAndDebrisHoldTheDocumentedCounts() {
		if (Tables.Value is not { } tables) {
			return;
		}

		var models = new Es1BaseCollisionModelsTransformer().Parse(tables["BASECOL.DAT"].RawBytes)!.Models;
		Assert.Equal(new[] { 0, 0, 0, 0, 0, 2, 4, 0, 2 }, models.Select(m => m.Nodes!.Length));

		var debris = new Es1DebrisDatTransformer().Parse(tables["DEBRIS.DAT"].RawBytes)!.Groups;
		Assert.Equal(new short[] { 3, 3, 5, 7 }, debris.Select(g => g.ThrowCount));
		Assert.Equal(["0", "0 1", "0 1 2", "2 3 4"], debris.Select(g => string.Join(' ', g.Pieces.Select(p => p.ShapeIndex))));
		Assert.All(debris.SelectMany(g => g.Pieces), p => Assert.Equal(20, p.Weight));
	}

	/// <summary>docs/es1/formats/formations-dat.md#the-layouts: five formations each; the flyers' heights; BFORMS row 3.</summary>
	[Fact]
	public void FormationsHoldTheDocumentedValues() {
		if (Tables.Value is not { } tables) {
			return;
		}

		Assert.Equal(5, new Es1MechFormationsTransformer().Parse(tables["MFORMS.DAT"].RawBytes)!.Formations.Length);
		var flyers = new Es1FlyerFormationsTransformer().Parse(tables["FFORMS.DAT"].RawBytes)!.Formations;
		Assert.All(flyers, f => Assert.Equal(new short[] { 400, 800, 1200 }, f.Slots.Select(s => s.Z)));
		var bases = new Es1BaseFormationsTransformer().Parse(tables["BFORMS.DAT"].RawBytes)!.Formations;
		Assert.Equal(5, bases.Length);
		Assert.Equal(new[] { 40000, -40000, 80000, -80000, 120000, -120000, 160000 }, bases[3].Slots.Select(s => s.X));
		Assert.Equal(4, new Es1WidgetStylesTransformer().Parse(tables["STYLES.DAT"].RawBytes)!.Styles.Length);
	}

	private static IReadOnlyDictionary<string, VolEntry>? Load() {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES1", "VOL", "SIMVOL0.VOL");
			if (!File.Exists(vol)) {
				continue;
			}

			var archive = VolFileReader.ParseVolFile(vol);
			string Folder(VolEntry e) => archive.Folders.TryGetValue(e.DirIdx, out var dir) ? dir.Label : "";
			return archive.FilesSet
				.Where(e => Folder(e).Equals("dat", StringComparison.OrdinalIgnoreCase)
					&& Names.Contains(e.FileName!.Trim(), StringComparer.OrdinalIgnoreCase))
				.ToDictionary(e => e.FileName!.Trim().ToUpperInvariant(), e => e);
		}

		return null;
	}
}
