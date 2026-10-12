using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's readers for the five <c>dat\</c> tables in its <c>SIMVOL0.VOL</c> whose layout differs from ES2's under the same
/// name: <c>WEAPONS</c>, <c>BULLETS</c>, <c>BASES</c>, <c>SKIMMER</c> and <c>EXPLOS</c> (docs/es1/formats/weapons-dat-sim.md,
/// bullets-skimmer-dat.md, bases-dat.md, explos-dat.md). Each test passes vacuously when <c>ES1/VOL</c> is absent, so the
/// suite does not depend on the install.
/// </summary>
public class Es1DatTablesTests {
	private static readonly Lazy<IReadOnlyDictionary<string, VolEntry>?> Tables = new(Load);

	private static readonly string[] Names = ["WEAPONS.DAT", "BULLETS.DAT", "BASES.DAT", "SKIMMER.DAT", "EXPLOS.DAT"];

	[Fact]
	public void EveryTableRoundTripsThroughTheEs1Table() {
		if (Tables.Value is not { } tables) {
			return;
		}

		var roundTrips = new Dictionary<string, Func<byte[], byte[]?>> {
			["WEAPONS.DAT"] = b => new Es1WeaponsSimTransformer().Write(new Es1WeaponsSimTransformer().Parse(b)!),
			["BULLETS.DAT"] = b => new Es1BulletDataTransformer().Write(new Es1BulletDataTransformer().Parse(b)!),
			["BASES.DAT"] = b => new Es1BasesDatTransformer().Write(new Es1BasesDatTransformer().Parse(b)!),
			["SKIMMER.DAT"] = b => new Es1FlyerSimDataTransformer().Write(new Es1FlyerSimDataTransformer().Parse(b)!),
			["EXPLOS.DAT"] = b => new Es1ExplosionDataTransformer().Write(new Es1ExplosionDataTransformer().Parse(b)!),
		};

		Assert.Equal(Names.Length, tables.Count);
		foreach (var (name, entry) in tables) {
			Assert.StartsWith("Es1", TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege1)!.GetType().Name);
			Assert.True(entry.RawBytes.AsSpan().SequenceEqual(roundTrips[name](entry.RawBytes!)), $"{name} does not round-trip");
		}
	}

	/// <summary>docs/es1/formats/weapons-dat-sim.md#the-projectile-index: 20 for ECM, 21 for the three pods, 22 for GREN.</summary>
	[Fact]
	public void WeaponsHoldTheDocumentedProjectileIndices() {
		if (Tables.Value is not { } tables) {
			return;
		}

		var weapons = new Es1WeaponsSimTransformer().Parse(tables["WEAPONS.DAT"].RawBytes)!;
		Assert.Equal(new short[] { 0, 1, 2, 15, 6, 22, 3, 4, 5, 21, 21, 21, 14, 20, 16, 17, 18 },
			weapons.Templates.Select(t => t.ProjDatIndex));
		Assert.Equal(19, weapons.Projectiles.Length);
	}

	/// <summary>The bullets' frame intervals, docs/es1/formats/bullets-skimmer-dat.md#datbulletsdat; SKIMMER's bank limit.</summary>
	[Fact]
	public void BulletsAndSkimmerHoldTheDocumentedValues() {
		if (Tables.Value is not { } tables) {
			return;
		}

		var bullets = new Es1BulletDataTransformer().Parse(tables["BULLETS.DAT"].RawBytes)!;
		Assert.Equal(new short[] { 0, 0, 0, 0, 0, 0, 256, 256 }, bullets.Entries.Select(e => e.FrameInterval));

		var skimmer = new Es1FlyerSimDataTransformer().Parse(tables["SKIMMER.DAT"].RawBytes)!;
		Assert.Equal((14000, true), ((int)skimmer.MaxBankAngle, skimmer.Bytes12.All(b => b == 0)));
	}

	/// <summary>docs/es1/formats/bases-dat.md and explos-dat.md: the second shape set's types, and the explosion banks.</summary>
	[Fact]
	public void BasesAndExplosionsHoldTheDocumentedValues() {
		if (Tables.Value is not { } tables) {
			return;
		}

		var bases = new Es1BasesDatTransformer().Parse(tables["BASES.DAT"].RawBytes)!;
		Assert.Equal(new short[] { 5, 6, 8 }, bases.Types.Where(t => t.ShapeSet != 0).Select(t => t.TypeIndex));

		var explosions = new Es1ExplosionDataTransformer().Parse(tables["EXPLOS.DAT"].RawBytes)!;
		Assert.Equal(Enumerable.Range(0, 18), explosions.Types.Select(t => (int)t.ShapeIndex).Order());
		Assert.All(explosions.Types, t => Assert.InRange(t.TextureBankIndex, 1, 8));
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
