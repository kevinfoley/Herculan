using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Es1.Input;

namespace Herculan.Es1.Sim;

/// <summary>
/// The three files the shell hands the simulator in the install's <c>data\</c>, as <c>DBSim_LoadScriptDat</c>
/// (<c>ES1DBSIM 0001d850</c>) opens them (docs/es1/formats/script-dat.md#reading): <c>data\mission.var</c>,
/// <c>data\player.mec</c> and <c>data\script.dat</c>, each under a severity-5 assert when it does not open; and the
/// options of <c>data\prefs.cfg</c> (<see cref="Preferences"/>).
/// </summary>
/// <param name="Flags">The 100 flags of <c>data\mission.var</c>.</param>
/// <param name="Player">The squad of <c>data\player.mec</c>.</param>
/// <param name="Script">The mission of <c>data\script.dat</c>.</param>
public sealed record Es1MissionFiles(short[] Flags, Es1PlayerMec Player, Es1ScriptDat Script) {
	/// <summary>The folder the shell reads missions from, <c>msn\%s</c> (docs/es1/formats/msn-mission-file.md#loading-a-mission).</summary>
	public const string MissionFolder = "msn";

	/// <summary>The install's folder the two programs hand files over in.</summary>
	public const string DataFolder = "data";

	/// <summary>The file names, entries 3, 4 and 5 of the shell's file-name table.</summary>
	public const string ScriptName = "script.dat";
	public const string PlayerName = "player.mec";
	public const string MissionVarName = "mission.var";

	/// <summary><c>data\mission.var</c>'s length, the 100 <c>int16</c> flags.</summary>
	public const int MissionVarLength = 200;

	/// <summary>The preferences file <c>Prefs_Load</c> (<c>0004a744</c>) reads.</summary>
	public const string PrefsName = "prefs.cfg";

	/// <summary>
	/// The 17 option bytes of <c>data\prefs.cfg</c> as <c>Prefs_Init</c> loads them, all 0 when the file is missing; the
	/// simulator's live options, which the preferences panel changes and saves back to the same file.
	/// </summary>
	public Es1Preferences Preferences { get; init; } = Es1Preferences.Default;

	/// <summary>A file in the install's <c>data\</c>, matched ignoring case.</summary>
	public static string DataPath(string installRoot, string name) => CaseInsensitivePath.Combine(installRoot, DataFolder, name);

	/// <summary>
	/// Reads the three files in <c>DBSim_LoadScriptDat</c>'s order. A file that is missing, which retail asserts on, or
	/// that does not read, gives null with the file named in <paramref name="error"/>.
	/// </summary>
	/// <param name="demoMode"><c>DemoMode</c>, under which <c>Prefs_Init</c> loads the preferences twice (<see cref="Es1Preferences.Load"/>).</param>
	public static Es1MissionFiles? Read(string installRoot, out string? error, bool demoMode = false) {
		string varPath = DataPath(installRoot, MissionVarName);
		if (!File.Exists(varPath)) {
			error = $"data\\{MissionVarName} is missing from {installRoot} (world.cpp line 0x8d asserts here).";
			return null;
		}

		// The read takes 200 bytes whatever the file holds; a short file's missing flags are 0 here.
		byte[] varBytes = File.ReadAllBytes(varPath);
		var flags = new short[MissionVarLength / 2];
		Buffer.BlockCopy(varBytes, 0, flags, 0, Math.Min(varBytes.Length, MissionVarLength) & ~1);

		string playerPath = DataPath(installRoot, PlayerName);
		if (!File.Exists(playerPath)) {
			error = $"data\\{PlayerName} is missing from {installRoot} (world.cpp line 0x95 asserts here).";
			return null;
		}

		Es1PlayerMec? player;
		try {
			player = new Es1PlayerMecTransformer().Parse(File.ReadAllBytes(playerPath));
		} catch (InvalidDataException ex) {
			error = $"data\\{PlayerName} does not read: {ex.Message}";
			return null;
		}

		string scriptPath = DataPath(installRoot, ScriptName);
		if (!File.Exists(scriptPath)) {
			error = $"data\\{ScriptName} is missing from {installRoot} (world.cpp line 0xb2 asserts here).";
			return null;
		}

		Es1ScriptDat? script;
		try {
			script = new Es1ScriptDatTransformer().Parse(File.ReadAllBytes(scriptPath));
		} catch (InvalidDataException ex) {
			error = $"data\\{ScriptName} does not read: {ex.Message}";
			return null;
		}

		if (player == null || script == null) {
			error = $"data\\{(player == null ? PlayerName : ScriptName)} is empty.";
			return null;
		}

		string prefsPath = DataPath(installRoot, PrefsName);
		byte[]? prefs = File.Exists(prefsPath) ? File.ReadAllBytes(prefsPath) : null;

		error = null;
		return new Es1MissionFiles(flags, player, script) {
			Preferences = Es1Preferences.Load(prefs, prefsPath, Es1Preferences.VoiceArchiveExists(installRoot), demoMode),
		};
	}
}
