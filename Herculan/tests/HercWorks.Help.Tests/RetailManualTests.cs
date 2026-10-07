using Xunit;

namespace HercWorks.Help.Tests;

/// <summary>The decoder against the three retail files (docs/retail/formats/winhelp.md).</summary>
public class RetailManualTests {
	[Theory]
	[MemberData(nameof(RetailManuals.All), MemberType = typeof(RetailManuals))]
	public void ParsesEveryTopic(string language) {
		if (RetailManuals.Read(language) is not { } bytes) {
			return;
		}

		var help = HelpFile.Parse(bytes, out string? error);
		Assert.True(help != null, error);
		Assert.Equal(103, help.Topics.Count);
		Assert.Equal(3, help.Windows.Count);
		Assert.Equal(102, help.Contexts.Count);
		Assert.Equal(0u, help.ContentsOffset);
		Assert.Equal(["FocusWindow(`main');CW(`overview')"], help.Topics[0].EntryMacros);
		Assert.Equal(Enumerable.Range(0, 103), help.Topics.Select(t => t.Number));
	}

	[Theory]
	[MemberData(nameof(RetailManuals.All), MemberType = typeof(RetailManuals))]
	public void EveryLinkResolves(string language) {
		if (RetailManuals.Read(language) is not { } bytes) {
			return;
		}

		var help = HelpFile.Parse(bytes, out _)!;
		var inlines = help.Topics.SelectMany(t => t.Blocks).SelectMany(b => b switch {
			HelpParagraphRun run => run.Inlines,
			HelpTableRow row => row.Cells.SelectMany(c => c.Inlines),
			_ => [],
		}).ToList();

		var jumps = inlines.OfType<HelpHotspotStart>().Select(h => h.Link).OfType<HelpJump>().ToList();
		Assert.NotEmpty(jumps);
		Assert.All(jumps, jump => Assert.NotNull(help.LocateContext(jump.Hash)));

		// Every JI context string the file spells out, in buttons and in the startup macros, hashes to an
		// entry — the test of the hash's character table.
		var macros = inlines.OfType<HelpButton>().Select(b => b.Macro).Concat(help.StartupMacros);
		foreach (var call in macros.Select(HelpMacro.Parse).SelectMany(c => c!).Where(c => c.Name == "JI")) {
			Assert.True(ContextHash.TryCompute(call.Arguments[1], out uint hash));
			Assert.True(help.Contexts.ContainsKey(hash), call.Arguments[1]);
		}

		Assert.All(help.Keywords.SelectMany(k => k.Targets), target => Assert.NotNull(help.Locate(target)));
	}

	[Theory]
	[MemberData(nameof(RetailManuals.All), MemberType = typeof(RetailManuals))]
	public void EveryPictureDecodes(string language) {
		if (RetailManuals.Read(language) is not { } bytes) {
			return;
		}

		var help = HelpFile.Parse(bytes, out _)!;
		int hotspots = 0;
		for (int number = 0; number < 82; number++) {
			var picture = help.TryGetPicture(number, out string? error);
			Assert.True(picture != null, error);
			Assert.Equal(picture.Width * picture.Height * 3, picture.Rgb.Length);
			foreach (var spot in picture.Hotspots) {
				Assert.True(ContextHash.TryCompute(spot.Context, out uint hash));
				Assert.Equal(spot.Hash, hash);
				Assert.NotNull(help.LocateContext(hash));
				hotspots++;
			}
		}

		Assert.Equal(73, hotspots);
		Assert.Null(help.TryGetPicture(82, out _));
	}
}
