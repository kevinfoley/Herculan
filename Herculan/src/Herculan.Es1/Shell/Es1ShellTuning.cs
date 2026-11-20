using HercWorks.Core.Data.File.Bnd;
using Herculan.Engine.Content;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's eight <c>bnd\</c> files, which subsystem phase 1 reads at the start-up's step 5, each whole into its global
/// by its <c>*_LoadBnd</c> through <c>Ovl_ReadFile</c> (docs/es1/formats/bnd-notes.md#the-shells-files). Kept here are
/// the four whose fields the ported code reads; <c>IMDEVICE</c>, <c>MAIN</c>, <c>CAL_ALRT</c> and <c>ALERT</c> are read
/// as retail reads them and their bytes let go, since the readers of their fields are unported or not found
/// (docs/es1/formats/bnd-notes.md#open) and <c>MAIN.BND</c>'s heap sizes are tested against a heap this engine does not
/// have.
/// </summary>
public sealed class Es1ShellTuning {
	/// <summary>The files in the order the phase reads them, each with the bytes its loader reads.</summary>
	private static readonly (string Name, int Length)[] Files = [
		("IMDEVICE.BND", 10), ("MAIN.BND", 20), ("CTL_MGR.BND", Es1CtlMgrBnd.Length), ("SHELLMAP.BND", Es1ShellMapBnd.Length),
		("MAP.BND", Es1MapBnd.Length), ("CAL_ALRT.BND", 70), ("CTL_ALRT.BND", Es1CtlAlertBnd.Length), ("ALERT.BND", 6),
	];

	private Es1ShellTuning() {
	}

	/// <summary><c>BND\MAP.BND</c> at <c>0009a420</c>, the war map's base class's constants.</summary>
	public Es1MapBnd MapBnd { get; private init; } = new(null);

	/// <summary><c>BND\SHELLMAP.BND</c> at <c>0009a44c</c>, the war map's map's constants.</summary>
	public Es1ShellMapBnd ShellMapBnd { get; private init; } = new(null);

	/// <summary><c>BND\CTL_ALRT.BND</c> at <c>0009a34c</c>, <c>CtlAlert_Bnd</c>, the controls panel's numbers.</summary>
	public Es1CtlAlertBnd CtlAlertBnd { get; private init; } = new(null);

	/// <summary><c>BND\CTL_MGR.BND</c> at <c>0009adec</c>, with the war map's mouse queue size.</summary>
	public Es1CtlMgrBnd CtlMgrBnd { get; private init; } = new(null);

	/// <summary>
	/// Subsystem phase 1 on <paramref name="content"/>'s <c>bnd\</c>. Null when a file does not open or holds fewer bytes
	/// than its loader reads, where <c>Ovl_ReadFile</c>'s fatal assert ends the program before any assert hook is added;
	/// the assert is written to the error stream.
	/// </summary>
	/// <param name="content">The shell's volumes.</param>
	/// <param name="installRoot">The install, named in the assert.</param>
	public static Es1ShellTuning? Load(GameContent content, string installRoot) {
		var read = new Dictionary<string, byte[]>();
		foreach (var (name, length) in Files) {
			if (content.Read("bnd", name) is not { } bytes) {
				Console.Error.WriteLine($"bnd\\{name} does not open in {installRoot} (util\\ovl.cpp line 0x15 asserts here).");
				return null;
			}

			if (bytes.Length < length) {
				Console.Error.WriteLine($"bnd\\{name} in {installRoot} holds {bytes.Length} of the {length} bytes its loader reads (util\\ovl.cpp line 0x17 asserts here).");
				return null;
			}

			read[name] = bytes;
		}

		return new Es1ShellTuning {
			MapBnd = new Es1MapBnd(read["MAP.BND"]),
			ShellMapBnd = new Es1ShellMapBnd(read["SHELLMAP.BND"]),
			CtlAlertBnd = new Es1CtlAlertBnd(read["CTL_ALRT.BND"]),
			CtlMgrBnd = new Es1CtlMgrBnd(read["CTL_MGR.BND"]),
		};
	}
}
