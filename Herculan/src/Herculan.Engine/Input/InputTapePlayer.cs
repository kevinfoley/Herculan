using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.World;
using HercWorks.Core.Data.File.Cfg;

namespace Herculan.Engine.Input;

/// <summary>
/// One <c>.TAP</c> input tape being played back — DBSIM's <c>-p</c>, and <c>-D</c> when
/// <see cref="DemoMode"/> is set. The tape is read a frame at a time; what each frame means and how
/// DBSIM consumes it are docs/retail/formats/tap-input-tape.md's, and this class only decodes it into the
/// engine's own input types. The host owns the loop that feeds it, because only the host knows when a
/// modal panel is up.
///
/// <para><b>Paced, where retail is not.</b> DBSIM skips its 40 ms frame wait for the whole of a
/// playback and runs as fast as it can draw, so a retail demo is over in a fraction of its recorded
/// length. This plays each simulation frame for the time its own <c>SimTickDelta</c> says it took
/// (<see cref="SecondsOf"/>), which is the recording's own real time — a divergence from retail.</para>
/// </summary>
public sealed class InputTapePlayer {
	/// <summary>The folder inside an install root that the shipped tapes and their list sit in.</summary>
	public const string TapesFolderName = "TAPES";

	/// <summary>The tape list <c>DemoTape_PickRandom</c> (<c>0045ce9c</c>) reads.</summary>
	public const string DemoListFileName = "demolist.str";

	/// <summary>The extension <c>-p</c>, <c>-r</c> and <c>-D</c> all force onto a tape's stem.</summary>
	public const string Extension = ".tap";

	/// <summary>The bundle's seven files, in bundle order, as <c>-r</c> names them in <c>data\</c>.</summary>
	public static readonly IReadOnlyList<string> BundleFileNames = new[] {
		MissionLoader.ScriptFileName, MissionLoader.PlayerFileName, MissionLoader.CountersFileName,
		Prefs.FileName, "restore.dat", "object.str", Keyjoy.FileName,
	};

	/// <summary>
	/// How long a frame recorded while a modal panel was up is held for — <b>this engine's estimate</b>.
	/// A panel's loop calls <c>Input_BuildPlayerDevice</c> without the frame wait, so those frames
	/// came as fast as the recording machine could repaint the panel, and their <c>SimTickDelta</c> is
	/// the stale value of the frame that raised it. The retail tapes' mouse timestamps put that loop
	/// at 5-7 ms a frame: 71 frames across 22 coarse ticks in <c>DEMO1</c>, 27 across 11 and 29
	/// across 12 in <c>DEMO3</c>.
	/// </summary>
	public const double PanelFrameSeconds = 0.006;

	/// <summary>
	/// <c>SimCommandMask</c> (<c>0049eae0</c>), which <c>Input_BuildPlayerDevice</c> ANDs into every
	/// queued command before dispatch.
	/// </summary>
	public const int CommandMask = 0x47ff;

	/// <summary>A command code's release bit.</summary>
	public const int ReleaseBit = 0x80;

	/// <summary>A command code's Alt bit.</summary>
	public const int AltBit = 0x200;

	/// <summary>A command code's Ctrl bit.</summary>
	public const int CtrlBit = 0x400;

	/// <summary>
	/// The live stop key, <c>[Ctrl]+[E]</c> — command <c>0x412</c>, tested against the live keyboard's
	/// command word and never the tape's.
	/// </summary>
	public const int StopCommand = CtrlBit | 0x12;

	/// <summary>The fewest frames in a row that <see cref="InferredPanelSpans"/> reports.</summary>
	private const int MinimumPanelSpan = 8;

	/// <summary>The upper clamp <c>Time_BeginSimTick</c> puts on <c>SimTickDelta</c>.</summary>
	private const short TickDeltaClamp = 0x1c2;

	private InputTapePlayer(InputTape tape, string name, bool demoMode) {
		Tape = tape;
		Name = name;
		DemoMode = demoMode;
		Capabilities = CapabilitiesOf(tape.Capabilities);
		InferredPanelSpans = FindPanelSpans(tape.Frames);
	}

	/// <summary>The tape as read.</summary>
	public InputTape Tape { get; }

	/// <summary>The tape's file name, for the log.</summary>
	public string Name { get; }

	/// <summary>
	/// <c>-D</c>'s <c>DemoMode</c> (<c>004d25b4</c>): the player touching the keyboard ends the
	/// mission, and so does the tape running out.
	/// </summary>
	public bool DemoMode { get; }

	/// <summary>The recording machine's stick, from the tape's own capability block.</summary>
	public JoystickCapabilities Capabilities { get; }

	/// <summary>The index of the next frame <see cref="Take"/> returns.</summary>
	public int Position { get; private set; }

	/// <summary>Whether playback has ended, by the tape running out or by <see cref="Stop"/>.</summary>
	public bool Finished { get; private set; }

	/// <summary>
	/// Runs of frames that were probably recorded while a modal panel was up — <b>a heuristic, for the
	/// log</b>. A panel's loop never refreshes <c>SimTickDelta</c>, so such a run repeats one value
	/// exactly; a machine hitting the 40 ms cap produces a steady 81 and a slow one a steady clamp, so
	/// neither of those counts. Where this engine's own panels open and close at different frames, the
	/// replay has diverged from the recording.
	/// </summary>
	public IReadOnlyList<(int First, int Last)> InferredPanelSpans { get; }

	/// <summary>
	/// The tape's recorded length, for the log: <see cref="SecondsOf"/> summed over the frames that
	/// ticked the simulation, which leaves out every frame of an <see cref="InferredPanelSpans"/> run
	/// after its first, the frame whose tick raised the panel. It rests on the same heuristic as the
	/// spans. See docs/retail/formats/tap-input-tape.md, "Timing".
	/// </summary>
	public double RecordedSeconds {
		get {
			var frames = Tape.Frames;
			double seconds = frames.Sum(frame => SecondsOf(frame.TickDelta));
			foreach (var (first, last) in InferredPanelSpans) {
				for (int i = first + 1; i <= last; i++) {
					seconds -= SecondsOf(frames[i].TickDelta);
				}
			}

			return seconds;
		}
	}

	/// <summary>Reads a tape. Null when the file is not one.</summary>
	public static InputTapePlayer? Load(string path, bool demoMode) {
		var tape = new InputTapeTransformer().Parse(File.ReadAllBytes(path));
		return tape is null ? null : new InputTapePlayer(tape, Path.GetFileName(path), demoMode);
	}

	/// <summary>
	/// <c>DemoTape_PickRandom</c> (<c>0045ce9c</c>): a stem out of <c>tapes\demolist.str</c>, entry
	/// <c>time() % count</c>. Null when the list is missing or empty.
	/// </summary>
	public static string? PickDemo(string tapesDirectory) {
		string listPath = CaseInsensitivePath.Combine(tapesDirectory, DemoListFileName);
		if (!File.Exists(listPath)) {
			return null;
		}

		var entries = new StringFileTransformer().Parse(File.ReadAllBytes(listPath))?.Group(0)
			.Where(entry => entry is { Text.Length: > 0 })
			.ToArray();
		if (entries is not { Length: > 0 }) {
			return null;
		}

		long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		return entries[(int)(now % entries.Length)].Text;
	}

	/// <summary>
	/// Lays the tape's starting state out in <paramref name="directory"/> the way <c>-p</c> leaves the
	/// install's own <c>data\</c>, and returns the mission file to load from it.
	///
	/// <para>DBSIM unpacks the bundle over the live <c>data\</c>; this copies that folder somewhere
	/// else first and unpacks over the copy, so a playback leaves the install untouched. Everything
	/// the bundle does not carry is therefore the install's own, as it is in retail — notably
	/// <c>mission.str</c>, so the mission's text is whatever mission the install last flew.</para>
	///
	/// <para>Two of the seven are not what they look like. <c>prefs.cfg</c> is the tape's, with the
	/// install's own bytes 0-3 and 8-10 copied over it — sound, music, the two message modes and the
	/// three detail settings, which is <c>-p</c>'s own reconciliation. <c>keyjoy.cfg</c> is ignored:
	/// <c>-p</c> unpacks it into <c>tapes\</c>, and <c>Keyjoy_LoadConfig</c> reads
	/// <c>data\keyjoy.cfg</c> whatever is playing.</para>
	/// </summary>
	public string ExtractBundle(string directory, string? installDataDirectory) {
		Directory.CreateDirectory(directory);
		foreach (string stale in Directory.GetFiles(directory)) {
			File.Delete(stale);
		}

		if (installDataDirectory is not null && Directory.Exists(installDataDirectory)) {
			foreach (string file in Directory.GetFiles(installDataDirectory)) {
				File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), overwrite: true);
			}
		}

		var names = BundleFileNames;
		for (int i = 0; i < InputTape.BundleFileCount; i++) {
			if (names[i] == Keyjoy.FileName) {
				continue;
			}

			byte[] contents = Tape.Bundle[i];
			if (names[i] == Prefs.FileName) {
				contents = ReconcilePreferences(contents, CaseInsensitivePath.Combine(directory, names[i]));
			}

			File.WriteAllBytes(CaseInsensitivePath.Combine(directory, names[i]), contents);
		}

		return CaseInsensitivePath.Combine(directory, MissionLoader.ScriptFileName);
	}

	/// <summary>The next frame, without taking it.</summary>
	public InputTape.Frame? Peek() => Finished || Position >= Tape.Frames.Count ? null : Tape.Frames[Position];

	/// <summary>Takes the next frame. Running off the end is what finishes the playback.</summary>
	public InputTape.Frame? Take() {
		var frame = Peek();
		if (frame is null) {
			Finished = true;
			return null;
		}

		Position++;
		return frame;
	}

	/// <summary>Ends the playback early — <c>[Ctrl]+[E]</c>.</summary>
	public void Stop() => Finished = true;

	/// <summary>How long a frame is held for: its own tick, or <see cref="PanelFrameSeconds"/> under a panel.</summary>
	public static double DurationOf(InputTape.Frame frame, bool underPanel) =>
		underPanel ? PanelFrameSeconds : SecondsOf(frame.TickDelta);

	/// <summary><c>SimTickDelta</c> in seconds: Q8, with 1.0 being 125 ms.</summary>
	public static double SecondsOf(short tickDelta) => tickDelta * 0.125 / 256;

	/// <summary>The four game axes as the frame recorded them.</summary>
	public static PilotAxes AxesOf(InputTape.Frame frame) =>
		new(frame.Axes[0], frame.Axes[1], frame.Axes[2], frame.Axes[3]);

	/// <summary>The trigger, <c>004d2357</c>.</summary>
	public static bool TriggerOf(InputTape.Frame frame) => (frame.TriggerBank & 1) != 0;

	/// <summary>How many of the eight bound buttons a frame records — only the first four.</summary>
	public const int RecordedButtonCount = 4;

	/// <summary>
	/// Whether bound button <paramref name="index"/> (0-3) fired this frame. The frame is written after
	/// the press-once latch has masked the build, so a bit is set on the one frame its action acts on.
	/// </summary>
	public static bool ButtonOf(InputTape.Frame frame, int index) =>
		index is >= 0 and < RecordedButtonCount && (frame.ButtonBank & (1 << index)) != 0;

	/// <summary>The hat's four bytes, which are non-zero only under HAT = VIEWS.</summary>
	public static JoystickHat HatOf(InputTape.Frame frame) => (JoystickHat)((frame.ButtonBank >> 4) & 0xf);

	/// <summary>
	/// Every key the frame presses: the command word when it is a key-down, then the command queue in
	/// order. Releases press nothing. Each comes back masked with <see cref="CommandMask"/>.
	/// </summary>
	public static IEnumerable<int> PressesOf(InputTape.Frame frame) {
		int word = frame.CommandWord & CommandMask;
		if ((word & 0xff) != 0 && (word & ReleaseBit) == 0) {
			yield return word;
		}

		foreach (short code in frame.Commands) {
			int masked = code & CommandMask;
			if ((masked & 0xff) != 0 && (masked & ReleaseBit) == 0) {
				yield return masked;
			}
		}
	}

	/// <summary>
	/// Whether the frame carries anything that acts once — a key press, a button action, a hat view
	/// or a mouse event — rather than only the held state every frame carries.
	/// </summary>
	public static bool HasDiscreteInput(InputTape.Frame frame) =>
		PressesOf(frame).Any() || (frame.ButtonBank & 0xff) != 0 || frame.MouseEvents.Count > 0;

	/// <summary>
	/// The capability block as <see cref="JoystickCapabilities"/>. Field <c>+0</c> is never 0, so it
	/// says nothing about presence; a tape records the device block whether or not a stick answered,
	/// and a stick that did not has no buttons, throttle, rudder or hat to report.
	/// </summary>
	private static JoystickCapabilities CapabilitiesOf(byte[] block) {
		if (block.Length < InputTape.CapabilityBlockSize) {
			return JoystickCapabilities.None;
		}

		return new JoystickCapabilities(Present: true,
			ButtonCount: Math.Min(block[2] | block[3] << 8, JoystickCapabilities.MaxButtons),
			HasThrottle: block[4] != 0, HasRudder: block[5] != 0, HasHat: block[6] != 0);
	}

	/// <summary><c>-p</c>'s reconciliation: the install's bytes 0-3 and 8-10 over the tape's copy.</summary>
	private static byte[] ReconcilePreferences(byte[] taped, string installCopy) {
		if (!File.Exists(installCopy)) {
			return taped;
		}

		byte[] install = File.ReadAllBytes(installCopy);
		byte[] merged = (byte[])taped.Clone();
		for (int i = 0; i < merged.Length && i < install.Length; i++) {
			if (i < 4 || i - 8 is >= 0 and < 3) {
				merged[i] = install[i];
			}
		}

		return merged;
	}

	private static List<(int First, int Last)> FindPanelSpans(List<InputTape.Frame> frames) {
		var spans = new List<(int, int)>();
		int start = 0;
		for (int i = 1; i <= frames.Count; i++) {
			if (i < frames.Count && frames[i].TickDelta == frames[start].TickDelta) {
				continue;
			}

			short delta = frames[start].TickDelta;
			if (i - start >= MinimumPanelSpan && delta != TickDeltaClamp
				&& delta != Numerics.SimMath.VanillaTickDelta) {
				spans.Add((start, i - 1));
			}

			start = i;
		}

		return spans;
	}
}
