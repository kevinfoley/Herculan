using System.Runtime.InteropServices;

namespace Herculan.Engine.Platform;

/// <summary>
/// Turns a request from outside the process to end it into an ordinary window close: SIGTERM, which a session ending
/// or Steam's "Exit game" sends, and SIGINT, [Ctrl+C] in the terminal the host was started from. Left to .NET, either
/// one exits the process from a thread of its own while the main thread is still rendering, tearing GLFW, OpenAL and
/// the GL driver down under it; under WSL that crashes, and on a Steam Deck the process kept running and never exited.
/// Here the first signal is cancelled and only recorded: each <see cref="EngineWindow"/> closes itself on its next
/// update once <see cref="Requested"/> is set, so the host shuts down the way it does when the player closes the
/// window, and a host that runs one window after another checks it before opening the next. A second signal is not
/// cancelled, so a shutdown that stalls can still be ended by sending another.
///
/// <para>Not registered on Windows, where neither signal reaches a windowed application: closing, logging off and
/// shutting down all arrive as window messages, which GLFW already reports as a close.</para>
/// </summary>
public static class TerminationSignal {
	private static PosixSignalRegistration[]? _registrations;
	private static int _requested;

	/// <summary>Whether a termination signal has arrived, so that the window open now should close and no other should open.</summary>
	public static bool Requested => Volatile.Read(ref _requested) != 0;

	/// <summary>Starts catching the signals; called by every <see cref="EngineWindow"/>, and only the first call does anything.</summary>
	internal static void Register() {
		if (OperatingSystem.IsWindows() || _registrations != null) {
			return;
		}

		// Kept in a field: a registration that is disposed, or collected, stops catching its signal.
		_registrations = [
			PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal),
			PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal),
		];
	}

	// Runs on .NET's signal thread, so it only sets the flag; the window acts on it from the main thread.
	private static void OnSignal(PosixSignalContext context) {
		if (Interlocked.Exchange(ref _requested, 1) == 0) {
			Console.Error.WriteLine($"{context.Signal} received; closing.");
			context.Cancel = true;
		}
	}
}
