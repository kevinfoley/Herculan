using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary><see cref="MissionExtensions"/>' block after row 17, as <see cref="MissionDocument.SetHerculanOnly"/> writes it.</summary>
public class MissionExtensionsTests {
	/// <summary>
	/// Catches a writer that drops the block or emits it for a mission that says nothing (so a retail file no longer saves
	/// to its own bytes), one that writes anything before row 17 ends, a reader that loses the setting, and an undo that
	/// leaves the block behind.
	/// </summary>
	[Fact]
	public void HerculanOnlyRoundTripsAfterRow17AndUndoRestoresTheRetailBytes() {
		var (mission, _) = NewMission.Build(theater: 1, zone: 111, night: false, zoneWidth: 2_000_000, zoneHeight: 1_500_000);
		var codec = new MissionFileTransformer();
		byte[] retail = codec.Write(mission)!;
		Assert.Equal(retail, codec.Write(codec.Parse(retail)!));

		var document = MissionDocument.Read(retail, null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));
		var edit = document.SetHerculanOnly(true);
		Assert.True(document.Layout.HerculanOnly);

		byte[] marked = codec.Write(document.Mission)!;
		Assert.Equal(retail, marked[..retail.Length]);
		Assert.True(marked.Length > retail.Length);

		var read = codec.Parse(marked)!;
		Assert.True(read.Extensions?.HerculanOnly);
		Assert.Null(read.TrailingBytes);
		Assert.Equal(marked, codec.Write(read));

		document.Undo(edit);
		Assert.False(document.Layout.HerculanOnly);
		Assert.Equal(retail, codec.Write(document.Mission));
	}
}
