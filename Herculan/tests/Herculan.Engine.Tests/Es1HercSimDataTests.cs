using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="Es1HercSimDataTransformer"/> over the chassis files in ES1's <c>SIMVOL0.VOL</c>, the <c>dat\</c> files its
/// <c>nam\MECHS.NAM</c> names (docs/es1/formats/chassis-dat.md). Each test passes vacuously when <c>ES1/VOL</c> is absent, so
/// the suite does not depend on the install.
/// </summary>
public class Es1HercSimDataTests {
	private static readonly Lazy<(IReadOnlyList<VolEntry> All, IReadOnlyDictionary<string, VolEntry> Chassis)?> Archive = new(Load);

	[Fact]
	public void EveryChassisFileRoundTrips() {
		if (Archive.Value is not { } archive) {
			return;
		}

		Assert.Equal(16, archive.Chassis.Count);
		var transformer = new Es1HercSimDataTransformer();
		foreach (var (name, entry) in archive.Chassis) {
			var file = transformer.Parse(entry.RawBytes)!;
			Assert.True(entry.RawBytes.AsSpan().SequenceEqual(transformer.Write(file)), $"{name} does not round-trip");
		}
	}

	/// <summary>The values docs/es1/formats/chassis-dat.md#the-layout gives for every file, and the two chassis it marks out.</summary>
	[Fact]
	public void TheFilesHoldTheDocumentedValues() {
		if (Archive.Value is not { } archive) {
			return;
		}

		var files = archive.Chassis.ToDictionary(p => p.Key, p => new Es1HercSimDataTransformer().Parse(p.Value.RawBytes)!);
		Assert.All(files.Values, f => Assert.Equal((600, 2, 750), ((int)f.SpeedTurn, (int)f.LowMemoryRootSkip, (int)f.BodyRadius)));

		var bulldog = files["BULLDOG"];
		Assert.Equal(("Bulldog", 4, 32767, 20000, 0),
			(bulldog.Name, (int)bulldog.LegCount, (int)bulldog.TorsoTwistLimit, (int)bulldog.Mass, (int)bulldog.WeaponMountsDestructible));

		var spider = files["SPIDER"];
		Assert.Equal((0, 0, 1, 0), ((int)spider.LegCount, (int)spider.Mass, (int)spider.VanishesOnDeath, (int)spider.ShieldMaxTotal));
	}

	/// <summary>The texture groups in type order: 0 for types 0 and 1, 1 for 2 to 4, 2 for 5 to 7, 3 for the rest.</summary>
	[Fact]
	public void TheTextureGroupsFollowTheTypeOrder() {
		if (Archive.Value is not { } archive) {
			return;
		}

		string[] order = ["ROADRUNN", "GUNSLING", "RAPTOR", "TOMAHAWK", "PATRIOT", "PANTHER", "HERCULES", "COLOSSUS",
			"MONGOOSE", "SCORPION", "MIRIMAC", "RAMSES", "ACHILLES", "APOLLO", "BULLDOG", "SPIDER"];
		var groups = order.Select(n => (int)new Es1HercSimDataTransformer().Parse(archive.Chassis[n].RawBytes)!.TextureGroup);
		Assert.Equal(new[] { 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3 }, groups);
	}

	/// <summary>
	/// ES1's table binds its chassis reader to exactly the files <c>MECHS.NAM</c> names; ES2's binds none of them, its match
	/// being ES2's files' timestamps.
	/// </summary>
	[Fact]
	public void TheEs1TableBindsTheChassisFiles() {
		if (Archive.Value is not { } archive) {
			return;
		}

		var bound = archive.All.Where(e => TransformerRegistry.FindLabel(e, RetailGame.Earthsiege1) == TransformerRegistry.HercSimDataLabel);
		Assert.Equal(archive.Chassis.Values.OrderBy(e => e.FileName), bound.OrderBy(e => e.FileName));
		Assert.All(archive.Chassis.Values, e => {
			Assert.IsType<Es1HercSimDataTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1));
			Assert.Null(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege2));
		});
		Assert.IsType<HercSimDataTransformer>(TransformerRegistry.CreateByLabel(TransformerRegistry.HercSimDataLabel, RetailGame.Earthsiege2));
	}

	private static (IReadOnlyList<VolEntry>, IReadOnlyDictionary<string, VolEntry>)? Load() {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES1", "VOL", "SIMVOL0.VOL");
			if (!File.Exists(vol)) {
				continue;
			}

			var archive = VolFileReader.ParseVolFile(vol);
			string Folder(VolEntry e) => archive.Folders.TryGetValue(e.DirIdx, out var dir) ? dir.Label : "";
			var entries = archive.FilesSet.ToList();
			var names = new NameListTransformer().Parse(entries.Single(e => e.FileName!.Trim().Equals("MECHS.NAM", StringComparison.OrdinalIgnoreCase)).RawBytes)!.Names;
			var chassis = entries
				.Where(e => Folder(e).Equals("dat", StringComparison.OrdinalIgnoreCase)
					&& names.Contains(Path.GetFileNameWithoutExtension(e.FileName!.Trim()), StringComparer.OrdinalIgnoreCase))
				.ToDictionary(e => Path.GetFileNameWithoutExtension(e.FileName!.Trim()).ToUpperInvariant(), e => e);
			return (entries, chassis);
		}

		return null;
	}
}
