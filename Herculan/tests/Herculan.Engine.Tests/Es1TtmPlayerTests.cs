using System.Buffers.Binary;
using HercWorks.Core.Data.File;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Es1.Ttm;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The simulator's <c>.TTM</c> run on a constructed script (docs/es1/formats/ttm-scripts.md#the-simulators-run): the wait
/// a page sets, and the once flag a jump keeps.
/// </summary>
public class Es1TtmPlayerTests {
	/// <summary>
	/// Page 0 (label 1) sets a wait of 3; page 1 sets a wait of 5 and jumps to label 1. The jump keeps the once flag, so on
	/// the second pass page 0's wait command does not act and page 0 lasts page 1's 5 ticks, where a step clears the flag
	/// and page 1's command acts on the tick it first runs. Catches a jump that clears the flag, a page run before the
	/// tick's wait is taken, and a wait reloaded after the page runs.
	/// </summary>
	[Fact]
	public void AJumpKeepsTheOnceFlagSoTheJumpingPagesWaitStands() {
		ushort[] words = [
			0x1111, 1, 0x1021, 3, 0x0ff0,
			0x1111, 2, 0x1021, 5, 0x1201, 1, 0x0ff0,
		];
		var manager = new Es1TtmManager(new Es1VideoMemory(), 0, 10, (folder, name) =>
			(folder, name) == ("ttm", "TEST.ttm") ? File(words, pages: 2) : null) { PathPrefix = "ttm\\" };
		manager.AllocHandles(2, new Herculan.Es1.Sim.Es1SimClock());
		var script = Assert.IsType<Es1TtmPlayer>(manager.Script(manager.Open(Es1TtmPlayer.Playing, 0, "TEST.ttm")));

		var pages = new List<int>();
		for (int tick = 0; tick < 14; tick++) {
			script.Run(1);
			pages.Add(script.Page);
		}

		// Tick 1 runs page 0 (wait 3) and steps; page 1 runs at once and sets 5, which counts down to its jump at tick 7;
		// page 0 then lasts 5 ticks, to its step at tick 12, and page 1 sets 5 again when it first runs.
		Assert.Equal([1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1], pages);
		Assert.Equal(((short)5, (short)4), (script.WaitReload, script.Wait));
	}

	/// <summary>A <c>.TTM</c> file holding <paramref name="words"/> as its script, packed as literal runs.</summary>
	private static byte[] File(ushort[] words, ushort pages) {
		var script = new byte[words.Length * 2];
		for (int i = 0; i < words.Length; i++) {
			BinaryPrimitives.WriteUInt16LittleEndian(script.AsSpan(2 * i), words[i]);
		}

		var packed = new List<byte> { 1 };
		packed.AddRange(BitConverter.GetBytes((uint)script.Length));
		foreach (var run in script.Chunk(0x7f)) {
			packed.Add((byte)run.Length);
			packed.AddRange(run);
		}

		var file = new Es1TtmScript {
			Chunks = [
				new Es1TtmChunk { Tag = "VER:", Content = "4.14\0"u8.ToArray() },
				new Es1TtmChunk { Tag = "PAG:", Content = BitConverter.GetBytes(pages) },
				new Es1TtmChunk { Tag = "TT3:", Content = [.. packed] },
			],
		};
		return new Es1TtmScriptTransformer().Write(file)!;
	}
}
