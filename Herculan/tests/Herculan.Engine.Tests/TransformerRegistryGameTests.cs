using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="TransformerRegistry"/>'s per-game tables, chosen by <see cref="RetailGameDetector"/> from the executable in an
/// install root: temporary roots holding an empty marker file.
/// </summary>
public class TransformerRegistryGameTests {
	[Theory]
	[InlineData("go.exe", typeof(Es1MissionFileTransformer))]
	[InlineData("VSHELL.EXE", typeof(MissionFileTransformer))]
	public void AnInstallsMarkerChoosesItsMissionReader(string marker, Type reader) {
		string root = Directory.CreateTempSubdirectory("herculan-game-").FullName;
		try {
			Directory.CreateDirectory(Path.Combine(root, "VOL"));
			File.WriteAllBytes(Path.Combine(root, marker), []);
			var entry = new VolEntry { FileName = "B0_C2_60.MSN", Ext = FileType.Msn };

			var game = RetailGameDetector.DetectFromArchive(Path.Combine(root, "VOL", "ZONES.VOL"));

			Assert.IsType(reader, TransformerRegistry.FindTransformer(entry, game));
			Assert.Equal("Mission File", TransformerRegistry.FindLabel(entry, game));
		} finally {
			Directory.Delete(root, recursive: true);
		}
	}
}
