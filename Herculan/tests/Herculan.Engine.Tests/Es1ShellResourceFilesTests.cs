using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Bnd;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's menu dialogs (docs/es1/formats/dlg-menu-dialogs.md), tuning files (docs/es1/formats/bnd-notes.md), cursors
/// (docs/es1/formats/dfn-hfn-dci.md#cursors) and HMI banks (docs/es1/formats/sound-samples.md#the-banks) through the Core
/// readers ES1's table binds to them. Each test passes vacuously when the ES1 archives are absent.
/// </summary>
public class Es1ShellResourceFilesTests {
	private static readonly Lazy<IReadOnlyList<VolEntry>?> Shell = new(() => Load("SHELL0.VOL"));
	private static readonly Lazy<IReadOnlyList<VolEntry>?> Sim = new(() => Load("SIMVOL0.VOL"));

	/// <summary>
	/// Every <c>.DLG</c> round-trips through the menu dialog reader, opens with the window, kind 0, and holds only kinds the
	/// builder's switch has a case for; 25 windows carry the menu's number as their id, and the other fourteen the ids the doc
	/// lists.
	/// </summary>
	[Fact]
	public void EveryMenuDialogWalksTheBuildersLayout() {
		if (Shell.Value is not { } shell) {
			return;
		}

		var dialogs = shell.Where(e => HasExtension(e, ".DLG")).ToList();
		Assert.Equal(39, dialogs.Count);
		byte[] kinds = [0, 1, 2, 3, 4, 5, 14, 15, 16, 17, 18];
		var windowIds = new Dictionary<string, short>();
		Assert.All(dialogs, e => {
			var reader = Assert.IsType<Es1MenuDialogTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1));
			var dialog = reader.Parse(e.RawBytes)!;
			Assert.Equal(e.RawBytes, reader.Write(dialog));
			Assert.Equal(0, dialog.Controls[0].Kind);
			Assert.All(dialog.Controls, c => Assert.Contains(c.Kind, kinds));
			windowIds[e.FileName!.Trim()[4..^4]] = dialog.Controls[0].Id;
		});
		var others = windowIds.Where(p => p.Key != p.Value.ToString()).ToDictionary(p => p.Key, p => p.Value);
		Assert.Equal(new Dictionary<string, short> {
			["3000"] = 999, ["3333"] = 999, ["5555"] = 999, ["6666"] = 999, ["7770"] = 999, ["7771"] = 999, ["7772"] = 999,
			["1270"] = 999, ["888"] = 1240, ["1250"] = 1240, ["1260"] = 1240, ["1500"] = 1501, ["1710"] = 1700, ["1820"] = 1800,
		}, others);

		var main = new Es1MenuDialogTransformer().Parse(dialogs.Single(e => e.FileName!.Trim() == "MENU1000.DLG").RawBytes)!;
		Assert.Equal((49, 50, 281, 165), (main.Controls[0].Left, main.Controls[0].Top, main.Controls[0].Right, main.Controls[0].Bottom));
		Assert.Equal(new short[] { 1000, 1499, 1100, 1700, 1200, 1300, 1003, 1002 }, main.Controls.Select(c => c.Id));
		Assert.Equal(new byte[] { 0, 3, 3, 3, 3, 3, 3, 15 }, main.Controls.Select(c => c.Kind));
	}

	/// <summary>
	/// The shell's eight <c>.BND</c> take its start-up settings and tuning readers, the simulator's 62 besides <c>CAM.BND</c> the
	/// opaque reader, and all round-trip; the shell's <c>MAIN.BND</c> holds the two heap sizes <c>Shell_Main</c> tests.
	/// </summary>
	[Fact]
	public void EveryTuningFileRoundTripsThroughItsReader() {
		if (Shell.Value is not { } shell || Sim.Value is not { } sim) {
			return;
		}

		var shellFiles = shell.Where(e => e.Ext == FileType.Bnd).ToList();
		var simFiles = sim.Where(e => e.Ext == FileType.Bnd).ToList();
		Assert.Equal((8, 63), (shellFiles.Count, simFiles.Count));
		foreach (var e in shellFiles.Concat(simFiles)) {
			var reader = TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1)!;
			Type expected = shellFiles.Contains(e)
				? e.FileName!.Trim() == "MAIN.BND" ? typeof(Es1ShellMainBndTransformer) : typeof(Es1ShellTuningFileTransformer)
				: e.FileName!.Trim() == "CAM.BND" ? typeof(Es1CamTransformer) : typeof(Es1OpaqueFileTransformer);
			Assert.IsType(expected, reader);
			Assert.Equal(e.RawBytes, RoundTrip(reader, e.RawBytes!));
		}

		var main = new Es1ShellMainBndTransformer().Parse(shellFiles.Single(e => e.FileName!.Trim() == "MAIN.BND").RawBytes)!;
		Assert.Equal((3_700_000, 2_400_000), (main.LowHeapFlagLimit, main.MinimumHeap));
	}

	/// <summary>
	/// Every <c>.DCI</c> in both archives round-trips through the cursor reader; only <c>PCURSOR.DCI</c> has bytes past its
	/// envelope, and the shell's <c>ICONS.DCI</c> is 16 wide, 11 high, with its hotspot at (1, 1).
	/// </summary>
	[Fact]
	public void EveryCursorRoundTrips() {
		if (Shell.Value is not { } shell || Sim.Value is not { } sim) {
			return;
		}

		var cursors = shell.Concat(sim).Where(e => e.Ext == FileType.Dci || HasExtension(e, ".DCI")).ToList();
		Assert.Equal(11, cursors.Count);
		Assert.All(cursors, e => {
			var reader = Assert.IsType<DynamixCursorTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1));
			var cursor = reader.Parse(e.RawBytes)!;
			Assert.Equal(e.RawBytes, reader.Write(cursor));
			Assert.Equal(e.FileName!.Trim() == "PCURSOR.DCI" ? 96 : 0, cursor.Trailing.Length);
		});

		var icons = new DynamixCursorTransformer().Parse(cursors.Single(e => e.FileName!.Trim() == "ICONS.DCI").RawBytes)!;
		Assert.Equal((16, 11, 1, 1), (icons.Bitmap.Cols, icons.Bitmap.Rows, icons.HotspotX, icons.HotspotY));
	}

	/// <summary>The three banks the sound manager loads take the bank reader, the other two the opaque one, and all round-trip.</summary>
	[Fact]
	public void EveryBankRoundTripsThroughItsReader() {
		if (Shell.Value is not { } shell) {
			return;
		}

		var banks = shell.Where(e => HasExtension(e, ".BNK") || HasExtension(e, ".DIG")).ToList();
		Assert.Equal(5, banks.Count);
		foreach (var e in banks) {
			var reader = TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1)!;
			Type expected = e.FileName!.Trim() is "DONS.BNK" or "DRUM.DIG" ? typeof(Es1OpaqueFileTransformer) : typeof(Es1HmiBankTransformer);
			Assert.IsType(expected, reader);
			Assert.Equal(e.RawBytes, RoundTrip(reader, e.RawBytes!));
		}
	}

	/// <summary>Parses and writes back <paramref name="bytes"/> through whichever of this file's readers <paramref name="reader"/> is.</summary>
	private static byte[]? RoundTrip(ThreeSpaceByteTransformer reader, byte[] bytes) => reader switch {
		Es1ShellMainBndTransformer t => t.Write(t.Parse(bytes)!),
		Es1ShellTuningFileTransformer t => t.Write(t.Parse(bytes)!),
		Es1CamTransformer t => t.Write(t.Parse(bytes)!),
		Es1OpaqueFileTransformer t => t.Write(t.Parse(bytes)!),
		Es1HmiBankTransformer t => t.Write(t.Parse(bytes)!),
		_ => throw new ArgumentException(reader.GetType().Name),
	};

	private static bool HasExtension(VolEntry e, string extension) =>
		e.FileName != null && e.FileName.Trim().EndsWith(extension, StringComparison.OrdinalIgnoreCase);

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
