using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Input;

/// <summary>
/// An input tape recorded: <c>Sim_ParseCommandLine</c>'s <c>-r</c> (<c>ES1DBSIM 00058e74</c>, its case at
/// <c>000590cb</c>), which opens <c>&lt;TapeStem&gt;.tap</c> (<c>TapeStem</c>, <c>000b5b7a</c>) in mode <c>wb</c>,
/// packs the bundle with <c>Tape_PackFile</c> (<c>000574a0</c>), closes it and sets <c>TapeRecording</c>
/// (<c>000b5b78</c>) (docs/es1/simulation/main-loop.md#the-command-line, docs/es1/formats/tap-input-tape.md#the-bundle);
/// and the input build's step 8, which appends each frame (docs/es1/simulation/input.md#recording-and-playback,
/// docs/es1/formats/tap-input-tape.md#the-frames). The input build writes the frames (<see cref="Es1InputBuild"/>);
/// <c>TapeRecording</c> is never cleared once set, so a recording runs to the simulator's end. The bundle and the frames
/// are written through HercWorks.Core (<see cref="Es1InputTapeTransformer"/>).
/// </summary>
public sealed class Es1TapeRecording {
	/// <summary>
	/// The bundle's seven sources, in bundle order, under the install, as <c>-r</c> names them: six from <c>data\</c>,
	/// <c>prefs.cfg</c> among them, and <c>keyjoy.cfg</c> with no folder, from the simulator's own folder. Playback
	/// unpacks the two configuration files into <c>tapes\</c> instead (<see cref="Es1TapePlayback.BundlePaths"/>).
	/// </summary>
	public static readonly IReadOnlyList<string[]> PackPaths = [
		["data", "script.dat"], ["data", "player.mec"], ["data", "mission.var"], ["data", "prefs.cfg"],
		["data", "restore.dat"], ["data", "object.str"], ["keyjoy.cfg"],
	];

	/// <summary>The assert the input build raises when the tape does not open for appending: <c>appinput.cpp</c> line <c>0x2bd</c>, severity 5.</summary>
	public const int AppendAssertLine = 0x2bd;

	private readonly Es1InputTapeTransformer _transformer = new();

	private Es1TapeRecording(string path) => FilePath = path;

	/// <summary>The tape's file, <c>Path_WithExtension</c>(<c>tap</c>, <c>TapeStem</c>).</summary>
	public string FilePath { get; }

	/// <summary>How many frames <see cref="Append"/> has written.</summary>
	public int FrameCount { get; private set; }

	/// <summary>
	/// <c>SimTickDelta</c> (<c>000bdbd0</c>) as it stands when the input build runs, which each frame records; the runner
	/// wires it to the tick's clock. 0 with none.
	/// </summary>
	public Func<short>? TickDelta { get; set; }

	/// <summary>
	/// <c>-r</c> on the host's flag: the tape <paramref name="stem"/> names, with its extension made
	/// <see cref="Es1TapePlayback.Extension"/>, created and the bundle packed into it from <see cref="PackPaths"/> in
	/// <paramref name="installRoot"/>, matched ignoring case, each file as <c>Tape_PackFile</c> packs it
	/// (<see cref="Pack"/>). Answers none, with <paramref name="error"/>, when the tape does not open for writing: an
	/// engine guard, since retail packs into a stream it never checks opened.
	/// </summary>
	public static Es1TapeRecording? Start(string installRoot, string stem, out string? error) {
		error = null;
		string path = Path.ChangeExtension(stem, Es1TapePlayback.Extension);
		var bundle = PackPaths.Select(parts => Pack(CaseInsensitivePath.Combine(installRoot, parts))).ToArray();
		try {
			File.WriteAllBytes(path, new Es1InputTapeTransformer().WriteBundle(bundle));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			error = $"Cannot record to {path}: {ex.Message}";
			return null;
		}

		return new Es1TapeRecording(path);
	}

	/// <summary>
	/// <c>Tape_PackFile</c>'s file: its bytes, or none, packed as size 0, when it does not open (<c>fopen</c> mode
	/// <c>rb</c> failing). One that opens and then fails to read is also packed as size 0, an engine guard.
	/// </summary>
	public static byte[] Pack(string path) {
		try {
			return File.Exists(path) ? File.ReadAllBytes(path) : [];
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return [];
		}
	}

	/// <summary>
	/// The input build's step 8: <paramref name="block"/>'s command word and four axes, <see cref="TickDelta"/>, the
	/// button word (<see cref="ButtonWord"/>), the count of <paramref name="mouseEvents"/>, what
	/// <c>CockpitMouse_ProcessQueue</c> returned, and the events, appended to the tape, which is opened in mode
	/// <c>ab</c> and closed again. A tape that does not open is retail's severity-5 assert (<see cref="AppendAssertLine"/>),
	/// which ends the program (docs/es1/simulation/asserts.md): thrown as <see cref="Es1FatalAssertException"/>, nothing
	/// after it running.
	/// </summary>
	/// <param name="sourceButtons">The four source buttons the build read: the live block's <c>+0x1d</c>–<c>+0x20</c>, or the playing tape's bits 8–11.</param>
	public void Append(Es1PlayerInputBlock block, IReadOnlyList<byte> sourceButtons, IReadOnlyList<Es1MouseEvent> mouseEvents) {
		var frame = new Es1InputTape.Frame {
			CommandWord = (short)block.CommandWord,
			Axes = (short[])block.Axes.Clone(),
			TickDelta = TickDelta?.Invoke() ?? 0,
			Buttons = ButtonWord(block.Buttons, sourceButtons),
		};
		foreach (var (record, time) in mouseEvents) {
			frame.MouseEvents.Add(new TapeMouseEvent { X = record.X, Y = record.Y, Buttons = record.Buttons, Time = (int)time });
		}

		byte[] bytes = _transformer.WriteFrame(frame);
		try {
			using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write);
			stream.Write(bytes);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			throw new Es1FatalAssertException(
				$"{FilePath} does not open for appending: {ex.Message} (appinput.cpp line 0x{AppendAssertLine:x} asserts here).");
		}

		FrameCount++;
	}

	/// <summary>
	/// The frame's button word as step 8 builds it: bit <i>i</i> for each of the block's buttons 0–7 that is not 0, and
	/// bits 8–11 for each of the four <paramref name="sourceButtons"/> that is not 0; bits 12–15 clear.
	/// </summary>
	public static Es1InputTape.Button ButtonWord(IReadOnlyList<byte> buttons, IReadOnlyList<byte> sourceButtons) {
		int word = 0;
		for (int i = 0; i < 8; i++) {
			if (buttons[i] != 0) {
				word |= 1 << i;
			}
		}

		for (int i = 0; i < 4; i++) {
			if (sourceButtons[i] != 0) {
				word |= 1 << (8 + i);
			}
		}

		return (Es1InputTape.Button)word;
	}
}
