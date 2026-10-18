using System.Text;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace HercWorks.Query.Tests;

/// <summary>Query over a hand-built ES1 install: a <c>GO.EXE</c> marker and a <c>ZONES.VOL</c> holding one constructed mission.</summary>
public sealed class Es1InstallTests : IDisposable {
	private readonly string install = Directory.CreateTempSubdirectory("hercworks-query-es1-").FullName;

	public void Dispose() => Directory.Delete(install, recursive: true);

	/// <summary>
	/// Catches <see cref="RetailData.Load"/> reading an ES1 install's missions with ES2's reader: that reader runs off the
	/// end of an ES1 file, or reads its rows as ES2's, and the header patch never reaches <c>patches</c> as ES1's zone.
	/// </summary>
	[Fact]
	public void AnEs1InstallsMissionsAreReadAsEs1s() {
		var mission = new Es1MissionFile {
			Revision = 5,
			HeaderPatches = [new Es1HeaderPatch { ConditionRef = -1, HeaderWords = [1, 1004, 0, 4, 0, 0, 0, 0, 0, 0] }],
			Groups = [new Es1Group { GUID = 7, ConditionRef = -1, MemberKind = 0, Word30 = 3, Word32 = 6, MemberRefs = [-1, -1, -1, -1, -1, -1, -1, -1] }],
		};
		File.WriteAllBytes(Path.Combine(install, RetailGameDetector.Es1Marker), []);
		WriteVol(Path.Combine(install, RetailData.ArchiveFolder, RetailData.MissionArchive), "MSN", "TEST.MSN",
			new Es1MissionFileTransformer().Write(mission)!);

		var output = new StringWriter();
		Assert.Equal(0, Program.Run(["patches", "--install", install], output));

		Assert.Contains("TEST: 1 header patch", output.ToString());
		Assert.Contains("#0, unconditioned, theater unset (1), zone 1004", output.ToString());
	}

	/// <summary>A one-folder archive holding one entry, laid out as docs/retail/formats/vol-archive.md describes.</summary>
	private static void WriteVol(string path, string folder, string name, byte[] content) {
		byte[] folders = Encoding.ASCII.GetBytes(folder + "\\\0");
		var bytes = new List<byte>();
		bytes.AddRange("VOLN"u8.ToArray());
		bytes.AddRange(BitConverter.GetBytes(0u));
		bytes.Add(5);
		bytes.Add(1);
		bytes.AddRange(BitConverter.GetBytes((ushort)folders.Length));
		bytes.AddRange(folders);
		bytes.AddRange(BitConverter.GetBytes((ushort)1));
		bytes.AddRange(BitConverter.GetBytes(18));

		byte[] nameBytes = new byte[13];
		Encoding.ASCII.GetBytes(name).CopyTo(nameBytes, 0);
		bytes.AddRange(nameBytes);
		bytes.Add(0);
		bytes.AddRange(BitConverter.GetBytes(bytes.Count + 4));

		bytes.Add(0x02);
		bytes.AddRange(BitConverter.GetBytes(content.Length));
		bytes.AddRange(new byte[4]);
		bytes.AddRange(content);
		bytes.Add(content[^1]);

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, bytes.ToArray());
	}
}
