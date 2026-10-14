using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using Herculan.Engine.World;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// A real install's <c>script.dat</c> files — <c>DATA\script.dat</c> and every save slot's <c>sav\script%d.dat</c> —
/// parsed and written back. The written file is a prefix of the one read: the shell's writer does not truncate, so a
/// file keeps whatever a longer mission before it left past block 13's end, and the reader stops there
/// (docs/retail/formats/script-dat.md#fixed-size-file-structure). Skips silently without an install.
/// </summary>
public class ScriptDatRoundTripTests {
	[Fact]
	public void RewritesEveryInstalledScriptThroughBlock13() {
		if (GameInstall.Locate(null) is not { } root) {
			return;
		}

		var paths = new List<string>();
		string live = MissionLoader.DefaultScriptPath(root);
		if (File.Exists(live)) {
			paths.Add(live);
		}

		string slots = ShellSaveSlots.Directory(root);
		if (Directory.Exists(slots)) {
			paths.AddRange(Directory.EnumerateFiles(slots, "script*.dat",
				new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }));
		}

		var transformer = new ScriptDatTransformer();
		foreach (string path in paths) {
			byte[] original = File.ReadAllBytes(path);
			var parsed = transformer.Parse(original);
			Assert.True(parsed != null, path);

			byte[] written = transformer.Write(parsed)!;
			Assert.True(written.Length <= original.Length, $"{path}: wrote {written.Length} of {original.Length} bytes");
			Assert.True(written.AsSpan().SequenceEqual(original.AsSpan(0, written.Length)), path);
		}
	}
}
