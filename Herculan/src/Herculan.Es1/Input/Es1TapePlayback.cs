using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;

namespace Herculan.Es1.Input;

/// <summary>
/// An input tape played back: <c>Sim_ParseCommandLine</c>'s <c>-p</c> and <c>-D</c> (<c>ES1DBSIM 00058e74</c>), which
/// open the tape as <c>TapeFile</c> (<c>000b5b82</c>), set <c>TapePlayback</c> (<c>000b5b76</c>) and unpack the bundle
/// over the install's files (docs/es1/simulation/main-loop.md#the-command-line,
/// docs/es1/formats/tap-input-tape.md#the-bundle); <c>DemoTape_PickRandom</c> (<c>0005771c</c>), <c>-D</c>'s name
/// (docs/es1/formats/tap-input-tape.md#the-demo-tape); and the frame reads <c>Input_ReadPlayerInput</c> makes, with
/// <c>Input_CursorPos</c> and its first frame's latch (docs/es1/simulation/input.md#recording-and-playback). The
/// input read takes the frames (<see cref="Es1PlayerInputReader"/>); <see cref="Playing"/> is <c>TapePlayback</c> for every
/// other reader.
///
/// <para>The tape is read whole through HercWorks.Core (<see cref="Es1InputTapeTransformer"/>), and each frame read
/// takes the next record; running past the last is retail's short read. Core's reader ends the frames at a negative
/// mouse-event count and drops a last record whose events run short, where retail plays both records, the first with
/// no events, and reads on; no retail tape holds either.</para>
/// </summary>
public sealed class Es1TapePlayback {
	/// <summary>The install's folder of tapes, <c>Config_BuildPath</c>'s <c>tapes\</c> while a tape plays.</summary>
	public const string TapesFolder = "tapes";

	/// <summary>The extension <c>Path_WithExtension</c> forces on the tape's name.</summary>
	public const string Extension = "tap";

	/// <summary>The list <c>DemoTape_PickRandom</c> reads, <c>tapes\demolist.str</c> (<c>00096c44</c>).</summary>
	public const string DemoListName = "demolist.str";

	/// <summary>The live command that ends a playback: <c>0x412</c>, <c>Ctrl+E</c>.</summary>
	public const ushort StopCommand = 0x412;

	/// <summary>
	/// What <c>time</c> (<c>000757a0</c>) adds to the count of seconds from 1970 that DOS's date and time give: 25,200,
	/// seven hours.
	/// </summary>
	public const long TimeOffsetSeconds = 25200;

	/// <summary>
	/// The bundle's seven destinations, in bundle order, under the install: the five <c>data\</c> files as named, and
	/// <c>prefs.cfg</c> and <c>keyjoy.cfg</c> through <c>Config_BuildPath</c>, <c>tapes\</c> while a tape plays.
	/// </summary>
	public static readonly IReadOnlyList<string[]> BundlePaths = [
		["data", "script.dat"], ["data", "player.mec"], ["data", "mission.var"], [TapesFolder, "prefs.cfg"],
		["data", "restore.dat"], ["data", "object.str"], [TapesFolder, "keyjoy.cfg"],
	];

	private Es1TapePlayback(Es1InputTape tape, string path, bool demoMode) {
		Tape = tape;
		FilePath = path;
		DemoMode = demoMode;
	}

	/// <summary>The tape as read.</summary>
	public Es1InputTape Tape { get; }

	/// <summary>The tape's file.</summary>
	public string FilePath { get; }

	/// <summary>
	/// <c>DemoMode</c> (<c>000b5bd0</c>) as the command line set it: <c>-D</c>'s tape. The axis keys hold the live flag
	/// (<see cref="Es1AxisKeys.DemoMode"/>), which the simulation's start copies from this.
	/// </summary>
	public bool DemoMode { get; }

	/// <summary><c>TapePlayback</c> (<c>000b5b76</c>): set from the open until the input read ends the tape.</summary>
	public bool Playing { get; private set; } = true;

	/// <summary>The index of the next frame <see cref="Read"/> takes.</summary>
	public int Position { get; private set; }

	/// <summary>
	/// The byte <c>000958be</c>, 1 in the image: the first playback frame reads the cursor's position into
	/// <see cref="CursorPos"/> and clears it.
	/// </summary>
	public bool CursorLatch { get; set; } = true;

	/// <summary>
	/// <c>Input_CursorPos</c> (<c>000b17d8</c>): the point of the last mouse event played, which
	/// <c>Cockpit_PickEdgeCursor</c> reads through <c>Input_GetCursorPos</c> (<c>0004d910</c>) in place of the pointer.
	/// </summary>
	public (int X, int Y) CursorPos { get; set; }

	/// <summary>The next frame, without taking it; none past the last.</summary>
	public Es1InputTape.Frame? Peek() => Position < Tape.Frames.Count ? Tape.Frames[Position] : null;

	/// <summary>The input read's two <c>fread</c>s of a frame: the next frame, or none for a short read.</summary>
	public Es1InputTape.Frame? Read() {
		var frame = Peek();
		if (frame != null) {
			Position++;
		}

		return frame;
	}

	/// <summary>The input read's end of the tape: <c>fclose</c>(<c>TapeFile</c>) and <c>TapePlayback</c> = 0.</summary>
	public void Close() => Playing = false;

	/// <summary>
	/// A frame's hold under plan decision 20: its recorded <c>SimTickDelta</c> in seconds, Q8 with <c>0x100</c> = 100 ms;
	/// 0 for a delta below 0.
	/// </summary>
	public static double SecondsOf(short tickDelta) => Math.Max(0, (int)tickDelta) * 0.1 / 0x100;

	/// <summary>The recorded deltas summed, for the console.</summary>
	public double RecordedSeconds => Tape.Frames.Sum(frame => SecondsOf(frame.TickDelta));

	/// <summary>
	/// <c>Sim_ParseCommandLine</c>'s <c>-p</c> and <c>-D</c> on the host's flags, the tape opened, read and unpacked into
	/// <paramref name="installRoot"/>. With <paramref name="demo"/> and no <paramref name="stem"/>, <c>-D</c>: the name
	/// from <see cref="PickDemo"/> and the tape <c>tapes\&lt;name&gt;.tap</c>; when there is no name or the tape does not
	/// open, <paramref name="tape"/> is none and the run goes on with no tape, both flags cleared, as retail's does. With
	/// <paramref name="stem"/>, <c>-p</c>, in demo mode with <paramref name="demo"/>: the tape <see cref="Resolve"/> finds.
	/// Answers false, with <paramref name="error"/>, when a named tape is not found or a tape does not read: an engine
	/// guard, since retail's <c>-p</c> reads its bundle from a tape it never checks opened, and <c>Tape_UnpackFile</c>
	/// unpacks whatever a short tape's reads leave.
	/// </summary>
	public static bool FromCommandLine(string installRoot, string? stem, bool demo, out Es1TapePlayback? tape, out string? error) {
		tape = null;
		error = null;
		string? path;
		if (stem == null) {
			short lastPick = 0;
			if (PickDemo(installRoot, ref lastPick, RetailTime) is not { } name) {
				Console.Error.WriteLine($"No demo tape: {Path.Combine(TapesFolder, DemoListName)} is missing or lists none; the mission runs with no tape.");
				return true;
			}

			path = CaseInsensitivePath.Combine(installRoot, TapesFolder, WithExtension(name));
			if (!File.Exists(path)) {
				Console.Error.WriteLine($"No demo tape at {path}; the mission runs with no tape.");
				return true;
			}
		} else if ((path = Resolve(installRoot, stem)) == null) {
			error = $"No tape at {stem}, {WithExtension(stem)}, or {WithExtension(stem)} in {installRoot} or its {TapesFolder} folder.";
			return false;
		}

		if (new Es1InputTapeTransformer().Parse(File.ReadAllBytes(path)) is not { } read) {
			error = $"{path} is not an input tape: its bundle runs past the file's end.";
			return false;
		}

		tape = new Es1TapePlayback(read, path, demo);
		tape.Unpack(installRoot);
		return true;
	}

	/// <summary>
	/// Where the host finds <c>--play</c>'s tape, as ES2's <c>--play</c> does (ES2's <c>InputTapePlayer</c>):
	/// the first that exists of <paramref name="stem"/> as given, with the extension forced to <see cref="Extension"/>,
	/// and that name in <paramref name="installRoot"/>, where retail's <c>-p</c> opens <c>&lt;name&gt;.tap</c> from the
	/// simulator's own folder, and in its <see cref="TapesFolder"/>, matched ignoring case. None when none exists.
	/// </summary>
	public static string? Resolve(string installRoot, string stem) {
		string named = WithExtension(stem);
		string[] candidates = [
			stem, named, CaseInsensitivePath.Combine(installRoot, named), CaseInsensitivePath.Combine(installRoot, TapesFolder, named),
		];
		return candidates.FirstOrDefault(File.Exists);
	}

	/// <summary>
	/// <c>DemoTape_PickRandom</c>: group 0 of <c>tapes\demolist.str</c>, read through HercWorks.Core; none when the list
	/// does not open or the group is empty. Otherwise <paramref name="now"/> modulo the count, drawn again while it equals
	/// <paramref name="lastPick"/> (<c>DemoTape_LastPick</c>, <c>00096c42</c>, 0 at the start) and the count is above 1;
	/// the draw becomes <paramref name="lastPick"/> and its name is the answer. Retail's redraw spins until
	/// <c>time</c> reaches the next second; this one sleeps 10 ms between draws, the same draws, an engine choice.
	/// </summary>
	/// <param name="now"><c>time</c>: whole seconds, <see cref="RetailTime"/> in the host.</param>
	public static string? PickDemo(string installRoot, ref short lastPick, Func<long> now) {
		string listPath = CaseInsensitivePath.Combine(installRoot, TapesFolder, DemoListName);
		var names = File.Exists(listPath) ? new StringFileTransformer().Parse(File.ReadAllBytes(listPath))?.Group(0) : null;
		if (names is not { Count: > 0 }) {
			return null;
		}

		short pick;
		while (true) {
			pick = (short)(now() % names.Count);
			if (pick != lastPick || names.Count <= 1) {
				break;
			}

			Thread.Sleep(10);
		}

		lastPick = pick;
		return names[pick].Text;
	}

	/// <summary>
	/// <c>time</c> (<c>000757a0</c>) on the host: the local wall clock, which DOS's date and time give, in whole seconds
	/// from 1970, plus <see cref="TimeOffsetSeconds"/>.
	/// </summary>
	public static long RetailTime() =>
		(long)(DateTime.Now - new DateTime(1970, 1, 1)).TotalSeconds + TimeOffsetSeconds;

	/// <summary>
	/// The seven <c>Tape_UnpackFile</c> (<c>0005756c</c>) calls: each bundle file written over its
	/// <see cref="BundlePaths"/> destination in <paramref name="installRoot"/>, matched ignoring case. A destination that
	/// does not open for writing, such as one in a folder the install lacks, is skipped, as <c>Tape_UnpackFile</c> reads a
	/// file it has no destination for and writes nothing.
	/// </summary>
	public void Unpack(string installRoot) {
		for (int i = 0; i < BundlePaths.Count; i++) {
			string path = CaseInsensitivePath.Combine(installRoot, BundlePaths[i]);
			try {
				File.WriteAllBytes(path, Tape.Bundle[i] ?? []);
			} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Console.Error.WriteLine($"{path} does not open for writing; the tape's copy is not unpacked there.");
			}
		}
	}

	/// <summary>
	/// <paramref name="name"/> with its extension made <see cref="Extension"/>, as <c>Path_WithExtension</c>
	/// (<c>00060644</c>) forces it; retail's cuts the name at its first <c>.</c>, this the host's at the last one in the
	/// file name, so a host path through a folder with a <c>.</c> in its name keeps its folders.
	/// </summary>
	private static string WithExtension(string name) => Path.ChangeExtension(name, Extension);
}
