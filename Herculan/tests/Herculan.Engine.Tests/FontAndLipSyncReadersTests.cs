using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Herculan.Engine.Content;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Core's font and lip-sync readers over ES2's retail files (docs/retail/formats/dfn-hfn-dci.md#layout,
/// docs/retail/formats/snc-lip-sync.md#layout), and the engine's <see cref="HudFont"/> and <see cref="SncScript"/> built
/// from them. Each test passes vacuously when ES2's archives are absent.
/// </summary>
public class FontAndLipSyncReadersTests {
	private static readonly Lazy<IReadOnlyList<VolEntry>?> SimVol = new(() => Load("SIMVOL0.VOL"));
	private static readonly Lazy<IReadOnlyList<VolEntry>?> SimSound = new(() => Load("SIMSOUND.VOL"));
	private static readonly Lazy<IReadOnlyList<VolEntry>?> ShellVol = new(() => Load("SHELL0.VOL"));

	/// <summary>Every <c>.DFN</c> and <c>.HFN</c> takes ES2's table's font reader and round-trips through it.</summary>
	[Fact]
	public void EveryFontRoundTrips() {
		if (SimVol.Value is not { } sim || ShellVol.Value is not { } shell) {
			return;
		}

		var fonts = sim.Concat(shell).Where(e => e.Ext is FileType.Dfn or FileType.Hfn).ToList();
		Assert.Equal(54, fonts.Count);
		Assert.All(fonts, e => {
			var reader = Assert.IsType<DynamixFontTransformer>(TransformerRegistry.FindTransformer(e));
			Assert.Equal(e.RawBytes, reader.Write(reader.Parse(e.RawBytes)!));
		});
	}

	/// <summary><c>ACTIVE.HFN</c>'s metrics and glyph widths as docs/retail/formats/dfn-hfn-dci.md gives them, its art in one ink.</summary>
	[Fact]
	public void HudFontKeepsActiveHfnsMetrics() {
		if (SimVol.Value is not { } sim) {
			return;
		}

		var font = HudFont.Parse("ACTIVE", sim.Single(e => e.FileName!.Trim() == "ACTIVE.HFN").RawBytes!)!;
		Assert.Equal((217, 32, 13, 9, 11), (font.Glyphs.Count, font.FirstCharCode, font.CellHeight, font.Baseline, font.InkHeight));
		Assert.Equal((3, 6, 8, 8), (font.Width('1'), font.Width('S'), font.Width('0'), font.Width('A')));
		Assert.All(font.Glyphs, g => Assert.All(g.ImageData!, p => Assert.True(p is 0 or 24)));
	}

	/// <summary>
	/// Every <c>.SNC</c> takes ES2's table's lip-sync reader and round-trips, and the engine's script holds the file's byte
	/// pairs after the length, one event each.
	/// </summary>
	[Fact]
	public void EveryLipSyncScriptRoundTrips() {
		if (SimVol.Value is not { } sim || SimSound.Value is not { } sound) {
			return;
		}

		var scripts = sim.Concat(sound).Where(e => e.Ext == FileType.Snc).ToList();
		Assert.Equal(1112, scripts.Count);
		Assert.All(scripts, e => {
			byte[] b = e.RawBytes!;
			var reader = Assert.IsType<LipSyncScriptTransformer>(TransformerRegistry.FindTransformer(e));
			Assert.Equal(b, reader.Write(reader.Parse(b)!));
			var pairs = Enumerable.Range(0, (b.Length - 4) / 2).Select(i => (b[4 + 2 * i], b[5 + 2 * i]));
			Assert.Equal(pairs, SncScript.Parse(b)!.Events);
		});
	}

	private static IReadOnlyList<VolEntry>? Load(string archive) {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES2", "VOL", archive);
			if (File.Exists(vol)) {
				return VolFileReader.ParseVolFile(vol).FilesSet.ToList();
			}
		}

		return null;
	}
}
