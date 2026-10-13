using System.Buffers.Binary;
using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Herculan.Engine.Content;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The engine's ES2 readers for <c>.SNC</c> (<see cref="SncScript"/>), <c>.DFN</c> (<see cref="HudFont"/>) and <c>.ED0</c>–<c>.ED3</c>
/// (<see cref="CockpitClipRegions"/>) over every ES1 file of those types, and ES1's clip-region reader
/// (docs/es1/formats/snc-lip-sync.md, dfn-hfn-dci.md#fonts, hd-ed-clip-regions.md). Each test passes vacuously when the ES1
/// archives are absent.
/// </summary>
public class Es1EngineReadersTests {
	private static readonly Lazy<IReadOnlyList<VolEntry>?> SimVol = new(() => Load("SIMVOL0.VOL"));
	private static readonly Lazy<IReadOnlyList<VolEntry>?> ShellVol = new(() => Load("SHELL0.VOL"));
	private static readonly Lazy<IReadOnlyList<VolEntry>?> DetectVol = new(() => Load("DETECT.VOL"));

	/// <summary>The simulator's scripts are ES2's layout; the shell's are an <c>int16</c> length and 4-byte records, which ES2's reader refuses.</summary>
	[Fact]
	public void SimulatorScriptsReadAsEs2sAndShellScriptsDoNot() {
		if (SimVol.Value is not { } sim || ShellVol.Value is not { } shell) {
			return;
		}

		var simScripts = sim.Where(e => e.Ext == FileType.Snc).ToList();
		Assert.Equal(556, simScripts.Count);
		Assert.All(simScripts, e => {
			Assert.NotNull(SncScript.Parse(e.RawBytes!));
			Assert.Equal(e.RawBytes!.Length - 4, BinaryPrimitives.ReadInt32LittleEndian(e.RawBytes));
		});

		var shellScripts = shell.Where(e => e.Ext == FileType.Snc).ToList();
		Assert.Equal(162, shellScripts.Count);
		Assert.All(shellScripts, e => {
			Assert.Null(SncScript.Parse(e.RawBytes!));
			short length = BinaryPrimitives.ReadInt16LittleEndian(e.RawBytes);
			Assert.Equal(e.RawBytes!.Length - 2, length);
			Assert.Equal(0, length % 4);
		});
	}

	/// <summary>Every font reads through ES2's reader, and its header's lengths walk it to its end.</summary>
	[Fact]
	public void EveryFontReadsThroughEs2sReader() {
		if (SimVol.Value is not { } sim || ShellVol.Value is not { } shell) {
			return;
		}

		var fonts = sim.Concat(shell).Where(e => e.Ext == FileType.Dfn).ToList();
		Assert.Equal(30, fonts.Count);
		Assert.All(fonts, e => {
			byte[] b = e.RawBytes!;
			var font = HudFont.Parse(e.FileName!, b);
			Assert.NotNull(font);
			Assert.Equal(223, font!.Glyphs.Count);
			Assert.Equal((short)-1, BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(0x10)));
			Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(0x1c)));
			int end = 0x22 + BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x1e)) + 5 * font.Glyphs.Count;
			Assert.Equal(8 + BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4)), end);
			Assert.InRange(b.Length - end, 0, 1);
		});
	}

	/// <summary>
	/// ES2's engine reader takes the files with no rect and refuses the six with one, whose rects are two rows where ES2's are
	/// four values; ES1's reader gives back every <c>.ED*</c> and <c>.EDG</c> whole.
	/// </summary>
	[Fact]
	public void ClipRegionsReadThroughEs1sLayout() {
		if (SimVol.Value is not { } sim || DetectVol.Value is not { } detect) {
			return;
		}

		var views = sim.Concat(detect).Where(e => e.Ext is FileType.Ed0 or FileType.Ed1 or FileType.Ed2 or FileType.Ed3).ToList();
		Assert.Equal(33, views.Count);
		var refused = views.Where(e => CockpitClipRegions.Parse(e.RawBytes!) == null).Select(e => e.FileName!.Trim()).Order();
		Assert.Equal(["COLOSSUS.ED1", "PATRIOT.ED3", "RAPTOR.ED2", "RAPTOR.ED3", "ROADRUNN.ED2", "ROADRUNN.ED3"], refused);

		var clips = sim.Where(e => e.Ext == FileType.Edg).ToList();
		Assert.Equal(4, clips.Count);
		Assert.All(clips, e => Assert.IsType<Es1ClipRegionsTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1)));

		var reader = new Es1ClipRegionsTransformer();
		foreach (var e in views.Concat(clips)) {
			var regions = reader.Parse(e.RawBytes)!;
			Assert.True(e.RawBytes.AsSpan().SequenceEqual(reader.Write(regions)), $"{e.FileName} does not round-trip");
			Assert.Equal(e.FileName!.Trim() == "COLOSSUS.ED1" ? 30 : 2, regions.Unread.Length);
			if (regions.Unread.Length == 2) {
				Assert.Equal(new byte[2], regions.Unread);
			}
		}

		Assert.Equal(sim.Single(e => e.FileName!.Trim() == "COLOSSUS.ED0").RawBytes, detect.Single(e => e.Ext == FileType.Ed0).RawBytes);
		var colossus = reader.Parse(views.First(e => e.FileName!.Trim() == "COLOSSUS.ED1").RawBytes)!;
		Assert.Equal((194, 199, 0), (colossus.Rects.Single().Top, colossus.Rects.Single().Bottom, colossus.Blocks.Length));
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
