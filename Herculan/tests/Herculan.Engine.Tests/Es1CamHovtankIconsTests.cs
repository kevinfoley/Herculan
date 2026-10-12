using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Bnd;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's readers for <c>bnd\CAM.BND</c> (docs/es1/formats/bnd-notes.md#bndcambnd), <c>dmg\HOVTANK.DMG</c>, kept opaque
/// (docs/es1/formats/dmg-damage-file.md#hovtankdmg), and <c>RES32\ICONS.DBA</c>
/// (docs/es1/formats/dfn-hfn-dci.md#the-old-bitmap-record). Each test passes vacuously when the ES1 archives are absent.
/// </summary>
public class Es1CamHovtankIconsTests {
	private static readonly Lazy<IReadOnlyList<VolEntry>?> SimVol = new(() => Load("SIMVOL0.VOL"));
	private static readonly Lazy<IReadOnlyList<VolEntry>?> ShellVol = new(() => Load("SHELL0.VOL"));

	[Fact]
	public void CamBndRoundTripsWithTheCamerasConstants() {
		if (SimVol.Value is not { } entries) {
			return;
		}

		var entry = entries.Single(e => e.FileName!.Trim().Equals("CAM.BND", StringComparison.OrdinalIgnoreCase));
		var transformer = Assert.IsType<Es1CamTransformer>(TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege1));
		var cam = transformer.Parse(entry.RawBytes)!;
		Assert.True(entry.RawBytes.AsSpan().SequenceEqual(transformer.Write(cam)));
		Assert.Equal(
			new short[] { 2500, 30000, 0x800, 0xc0, 0x400, 0x50, 0x3000, 100 },
			new[] { cam.MinOrbitDistance, cam.MaxOrbitDistance, cam.AngleRateScale, cam.AngleRateStep, cam.ZoomRateScale, cam.ZoomRateStep, cam.PitchLimit, cam.MinEyeHeight });
	}

	[Fact]
	public void HovtankDmgIsKeptOpaqueAndTheOtherDmgStayOnEs2sReader() {
		if (SimVol.Value is not { } entries) {
			return;
		}

		var dmgs = entries.Where(e => e.Ext == FileType.Dmg).ToList();
		var hovtank = dmgs.Single(e => e.FileName!.Trim().Equals("HOVTANK.DMG", StringComparison.OrdinalIgnoreCase));
		var transformer = Assert.IsType<Es1OpaqueFileTransformer>(TransformerRegistry.FindTransformer(hovtank, RetailGame.Earthsiege1));
		Assert.True(hovtank.RawBytes.AsSpan().SequenceEqual(transformer.Write(transformer.Parse(hovtank.RawBytes)!)));
		Assert.All(dmgs.Where(e => e != hovtank), e => Assert.IsType<HercWorks.Core.Io.Transform.Dbsim.HercDamageFileTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1)));
	}

	[Fact]
	public void IconsDbaRoundTripsAsElevenOldBitmapsAndTheOtherDbaStayOnEs2sReader() {
		if (ShellVol.Value is not { } entries) {
			return;
		}

		var dbas = entries.Where(e => e.Ext == FileType.Dba).ToList();
		var old = dbas.Where(e => TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1) is Es1OldBitmapArrayTransformer).ToList();
		var icons = Assert.Single(old);
		Assert.Equal(4686, icons.RawBytes!.Length);
		Assert.All(dbas.Except(old), e => Assert.IsType<DynamixBitmapArrayTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1)));

		var transformer = new Es1OldBitmapArrayTransformer();
		var array = transformer.Parse(icons.RawBytes)!;
		Assert.True(icons.RawBytes.AsSpan().SequenceEqual(transformer.Write(array)));
		Assert.Equal(11, array.Items.Length);
		Assert.All(array.Items, b => Assert.Equal(14 + 4 * b.Pixels.Length, b.DeclaredSize));
		Assert.Equal((11, 16, 718 - 12 - 176), (array.Items[0].Rows, array.Items[0].Cols, array.Items[0].Tail.Length));
		Assert.Equal(2, array.Items[10].Tail.Length);
	}

	private static IReadOnlyList<VolEntry>? Load(string archive) {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES1", "VOL", archive);
			if (File.Exists(vol)) {
				return VolFileReader.ParseVolFile(vol).FilesSet.ToList();
			}
		}

		return null;
	}
}
