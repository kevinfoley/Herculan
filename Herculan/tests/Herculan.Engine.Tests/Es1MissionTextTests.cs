using System.Text;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="Es1MissionTextTransformer"/> over the 79 <c>.ENG</c> files in ES1's <c>ZONES.VOL</c>
/// (docs/es1/formats/eng-mission-text.md). Each test passes vacuously when <c>ES1/VOL/ZONES.VOL</c> is absent, so the suite
/// does not depend on the install.
/// </summary>
public class Es1MissionTextTests {
	private static readonly Lazy<IReadOnlyDictionary<string, byte[]>?> Texts = new(LoadTexts);

	[Fact]
	public void EveryFileRoundTrips() {
		if (Texts.Value is not { } texts) {
			return;
		}

		Assert.Equal(79, texts.Count);
		var transformer = new Es1MissionTextTransformer();
		foreach (var (name, bytes) in texts) {
			var file = transformer.Parse(bytes)!;
			Assert.True(bytes.AsSpan().SequenceEqual(transformer.Write(file)), $"{name} does not round-trip");
		}
	}

	/// <summary>docs/es1/formats/eng-mission-text.md#the-layout: version 1, the briefing first, eight or nine debriefings, objectives in 72 files, 197 choices none of them in the objectives.</summary>
	[Fact]
	public void EveryFileHoldsABriefingAndItsDebriefings() {
		if (Texts.Value is not { } texts) {
			return;
		}

		var files = texts.Values.Select(b => new Es1MissionTextTransformer().Parse(b)!).ToList();
		Assert.All(files, f => Assert.Equal(1, f.Version));
		Assert.All(files, f => Assert.Equal(Es1MissionText.BriefingTag, f.Blocks[0].Tag));
		Assert.All(files, f => Assert.Single(f.Blocks, b => b.Tag == Es1MissionText.BriefingTag));
		Assert.All(files, f => Assert.InRange(f.Blocks.Count(b => b.Tag == Es1MissionText.DebriefingTag), 8, 9));
		Assert.Equal(72, files.Count(f => f.Blocks.Count(b => b.Tag == Es1MissionText.ObjectivesTag) == 1));
		Assert.All(files, f => Assert.InRange(f.Blocks.Count(b => b.Tag == Es1MissionText.ObjectivesTag), 0, 1));

		var choices = files.SelectMany(f => f.Blocks).SelectMany(b => b.Pieces.OfType<Es1TextChoice>().Select(c => (b.Tag, c))).ToList();
		Assert.Equal(197, choices.Count);
		Assert.DoesNotContain(choices, c => c.Tag == Es1MissionText.ObjectivesTag);
		Assert.All(choices, c => Assert.Equal((0, 0), ((int)c.c.UntestedLow, (int)c.c.UntestedHigh)));
	}

	/// <summary>
	/// docs/es1/formats/eng-mission-text.md#reading-a-block: the shell's byte search for a tag, from the byte after the version,
	/// finds exactly the blocks of that tag, in order, in every file.
	/// </summary>
	[Fact]
	public void TheShellsTagSearchFindsEachBlock() {
		if (Texts.Value is not { } texts) {
			return;
		}

		foreach (var (name, bytes) in texts) {
			var file = new Es1MissionTextTransformer().Parse(bytes)!;
			var starts = new List<(string Tag, int After)>();
			int at = 2;
			foreach (var block in file.Blocks) {
				starts.Add((block.Tag, at + 4));
				at += 8 + BitConverter.ToInt32(bytes, at + 4);
			}

			foreach (string tag in new[] { Es1MissionText.BriefingTag, Es1MissionText.ObjectivesTag, Es1MissionText.DebriefingTag }) {
				Assert.True(starts.Where(s => s.Tag == tag).Select(s => s.After).SequenceEqual(ShellTagSearch(bytes, tag)), $"{name} {tag}");
			}
		}
	}

	/// <summary>The briefing, choice and objectives docs/es1/formats/eng-mission-text.md describes, in two hand-walked files.</summary>
	[Fact]
	public void HandWalkedFilesReadAsTheShellShowsThem() {
		if (Texts.Value is not { } texts) {
			return;
		}

		var b0C00A = new Es1MissionTextTransformer().Parse(texts["B0_C0_0A"])!;
		Assert.StartsWith("Welcome aboard, Soldier! Time is short so let's cut to the chase. We are",
			b0C00A.Find(Es1MissionText.BriefingTag, 0)!.Compose(_ => 0));
		var objectives = b0C00A.Find(Es1MissionText.ObjectivesTag, 0)!.Pieces.Cast<Es1TextLiteral>().Select(l => l.Text).ToList();
		Assert.Equal(6, objectives.Count);
		Assert.Equal("From your drop zone, patrol to way point 1.", objectives[0]);

		var b0C05A = new Es1MissionTextTransformer().Parse(texts["B0_C0_5A"])!;
		var briefing = b0C05A.Find(Es1MissionText.BriefingTag, 0)!;
		var choice = briefing.Pieces.OfType<Es1TextChoice>().Single();
		Assert.Equal(20, choice.FlagIndex);
		Assert.EndsWith("Good Luck!", briefing.Compose(f => (short)(f == 20 ? 0 : -1)));
		Assert.StartsWith("And this time, follow your orders!", choice.Pick(1)!.Text);
		Assert.Null(choice.Pick(2));

		var debriefings = b0C05A.Blocks.Where(b => b.Tag == Es1MissionText.DebriefingTag).ToList();
		Assert.Same(debriefings[^1], b0C05A.Find(Es1MissionText.DebriefingTag, 99));
	}

	/// <summary>docs/es1/formats/eng-mission-text.md#which-file: the two files with no mission, and a third pair, are byte copies.</summary>
	[Fact]
	public void CopiesMatchTheirOriginals() {
		if (Texts.Value is not { } texts) {
			return;
		}

		Assert.Equal(texts["B0_C1_20"], texts["B4_C1_20"]);
		Assert.Equal(texts["B0_C1_30"], texts["B4_C1_30"]);
		Assert.Equal(texts["SM_RCN02"], texts["SM_RCN03"]);
	}

	/// <summary>
	/// The position after each match of <c>Stream_FindTag</c> (<c>ES1GO 0004b658</c>) run again and again from the byte after the
	/// version: a byte that breaks a partial match restarts it without being tested against the tag's first byte.
	/// </summary>
	private static IEnumerable<int> ShellTagSearch(byte[] bytes, string tag) {
		byte[] t = Encoding.ASCII.GetBytes(tag);
		int matched = 0;
		for (int i = 2; i < bytes.Length; i++) {
			matched = bytes[i] == t[matched] ? matched + 1 : 0;
			if (matched == t.Length) {
				yield return i + 1;
				matched = 0;
			}
		}
	}

	private static IReadOnlyDictionary<string, byte[]>? LoadTexts() {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string path = Path.Combine(at.FullName, "ES1", "VOL", "ZONES.VOL");
			if (!File.Exists(path)) {
				continue;
			}

			Voln vol = VolFileReader.ParseVolFile(path);
			return vol.FilesSet
				.Where(e => e.FileName != null && e.FileName.Trim().EndsWith(".ENG", StringComparison.OrdinalIgnoreCase)
					&& vol.Folders.TryGetValue(e.DirIdx, out var dir) && string.Equals(dir.Label, "MSN", StringComparison.OrdinalIgnoreCase))
				.ToDictionary(e => Path.GetFileNameWithoutExtension(e.FileName!.Trim()).ToUpperInvariant(), e => e.RawBytes!);
		}

		return null;
	}
}
