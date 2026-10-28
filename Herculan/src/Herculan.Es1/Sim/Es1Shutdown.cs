namespace Herculan.Es1.Sim;

/// <summary>
/// <c>Sim_Shutdown</c> (<c>ES1DBSIM 0005b568</c>), which <c>Sim_Main</c> runs once its loop ends
/// (docs/es1/simulation/main-loop.md#the-end--sim_shutdown-0005b568): the results, and the exit code left in
/// <c>data\exit.cfg</c> for the shell.
/// </summary>
public static class Es1Shutdown {
	/// <summary><c>data\exit.cfg</c>, the exit code's file.</summary>
	public const string ExitName = "exit.cfg";

	/// <summary>The exit codes: under <c>DemoMode</c>, with <c>Sim_QuitFlag</c>, the player destroyed with the mode not 0, and otherwise.</summary>
	public const short DemoExitCode = 6, QuitExitCode = 0, DestroyedExitCode = 4, DebriefExitCode = 3;

	/// <summary>The status <c>Assert_Report</c> ends the program with after a fatal assert, <c>__exit(1)</c> (docs/es1/simulation/asserts.md).</summary>
	public const int AssertExitStatus = 1;

	/// <summary>
	/// <c>Sim_Shutdown</c> on <paramref name="simulation"/>, in its order: <c>CockpitMouseLive</c> = 0; the tape and the
	/// <c>.dmp</c> file closed, which this engine never opens (tapes are left out of the slice,
	/// docs/herculan/plan-es1-first-mission.md#what-the-slice-leaves-out); <c>GndTex_Free</c>, which frees the ground
	/// texture's memory, held here in a GL texture the window's close disposes; <see cref="Es1MissionResults.Write"/> on
	/// <c>LocalPlayerMech</c>; <see cref="ExitCode"/> written to <c>data\exit.cfg</c> as an <c>int16</c>;
	/// <c>Sound_Shutdown</c>; and <c>Sim_ShutdownNoOp1</c> and <c>Sim_ShutdownNoOp2</c>, which do nothing. Returns the
	/// exit code.
	///
	/// <para>A file that does not open asserts at severity 5 in retail (<c>world.cpp</c> lines <c>0x58</c> and
	/// <c>0x6d</c>, <c>init.cpp</c> line <c>0x1a4</c>): the assert's hook, <c>Sim_AssertCleanupHook</c> (<c>0005a7b8</c>),
	/// writes an <c>int16</c> 0 to <c>data\exit.cfg</c>, and the program ends with <see cref="AssertExitStatus"/>. Here the
	/// assert is reported on the console, the hook's write is made, nothing after the failed write runs, and
	/// <see cref="AssertExitStatus"/> is returned. A second failure in the hook's own write, where retail asserts again
	/// from inside the hook, is reported and goes no further, an engine guard.</para>
	/// </summary>
	/// <param name="demoMode"><c>DemoMode</c> (<c>000b5bd0</c>), which only a demo tape sets.</param>
	public static int Run(Es1Simulation simulation, string installRoot, bool demoMode = false) {
		if (simulation.ViewChain.Manager?.Mouse is { } mouse) {
			mouse.Live = false;
		}

		// PLACEHOLDER: with TapeFile open, DmpFile and then the tape closed; tapes are left out of the slice, so neither is
		// ever open here.
		// GndTex_Free: nothing to do here; the ground texture is a GL object disposed with the window.

		if (!Es1MissionResults.Write(simulation.LocalPlayer, simulation.Globals, installRoot, out string? error)) {
			Console.Error.WriteLine(error);
			return AssertCleanup(installRoot);
		}

		short code = ExitCode(demoMode, simulation.QuitFlag, simulation.LocalPlayer.Destroyed, simulation.Globals.MissionModeFlag);
		if (!WriteExitCode(installRoot, code)) {
			Console.Error.WriteLine($"data\\{ExitName} does not open for writing in {installRoot} (init.cpp line 0x1a4 asserts here).");
			return AssertCleanup(installRoot);
		}

		// PLACEHOLDER: Sound_Shutdown; the slice leaves ES1's sound out.
		return code;
	}

	/// <summary>
	/// The exit code: <see cref="DemoExitCode"/> under <paramref name="demoMode"/>, <see cref="QuitExitCode"/> with
	/// <paramref name="quitFlag"/> (<c>Sim_QuitFlag</c>), <see cref="DestroyedExitCode"/> when the player is destroyed
	/// (<c>+0x93</c>) and <paramref name="missionModeFlag"/> (<c>MissionModeFlag</c>, <c>000bc4a0</c>) is not 0, and
	/// <see cref="DebriefExitCode"/> otherwise.
	/// </summary>
	public static short ExitCode(bool demoMode, bool quitFlag, bool playerDestroyed, short missionModeFlag) =>
		demoMode ? DemoExitCode
		: quitFlag ? QuitExitCode
		: playerDestroyed && missionModeFlag != 0 ? DestroyedExitCode
		: DebriefExitCode;

	/// <summary><paramref name="code"/> as an <c>int16</c> over the install's <c>data\exit.cfg</c>; false when it does not open.</summary>
	public static bool WriteExitCode(string installRoot, short code) =>
		Es1MissionResults.TryWrite(installRoot, ExitName, [(byte)code, (byte)(code >> 8)]);

	/// <summary>
	/// What follows a fatal assert in <see cref="Run"/>: <c>Sim_AssertCleanupHook</c>'s write of an <c>int16</c> 0 to
	/// <c>data\exit.cfg</c>, its other steps being the sound, the display, the tape and the ground texture this engine
	/// releases with the window; then <see cref="AssertExitStatus"/>.
	/// </summary>
	private static int AssertCleanup(string installRoot) {
		if (!WriteExitCode(installRoot, 0)) {
			Console.Error.WriteLine($"data\\{ExitName} does not open for writing in {installRoot} (init.cpp line 100 asserts here).");
		}

		return AssertExitStatus;
	}
}
