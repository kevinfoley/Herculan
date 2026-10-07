using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.World;

namespace Herculan.Engine.Input;

/// <summary>
/// One <c>.TAP</c> input tape being recorded — DBSIM's <c>-r</c>. The format, and what DBSIM writes
/// into each field, are docs/retail/formats/tap-input-tape.md's; this class assembles frames out of the
/// engine's own input types and appends them. The host decides when a frame goes out, because only the
/// host knows when a tick ran and when a modal panel is up.
///
/// <para>Input comes in two kinds. <b>Discrete</b> input — key presses, mouse events, the stick button
/// that fired and the hat's views — is queued with <see cref="AddPress"/>, <see cref="AddMouse"/> and
/// <see cref="SetDiscreteStick"/> and goes out on the next frame written, whenever that is.
/// <b>Held</b> input — the axes and the trigger — is replaced every host frame by
/// <see cref="SetHeld"/> and goes out on every frame, as the input block it snapshots does.</para>
///
/// <para>Like <c>-r</c>, the file is written as it goes: the bundle when the recorder is created, the
/// capability block ahead of the first frame, and each frame as it is emitted.</para>
/// </summary>
public sealed class InputTapeRecorder : IDisposable {
	private readonly FileStream _stream;
	private readonly InputTapeTransformer _transformer = new();
	private readonly List<int> _presses = new();
	private readonly List<InputTape.MouseEvent> _mouse = new();
	private bool _capabilitiesWritten;
	private byte _buttonBank;
	private PilotAxes _axes = PilotAxes.Centred;
	private bool _trigger;
	private byte _rawButtons;

	private InputTapeRecorder(FileStream stream, string path) {
		_stream = stream;
		Path = path;
	}

	/// <summary>Where the tape is being written.</summary>
	public string Path { get; }

	/// <summary>How many frames have been written.</summary>
	public int FrameCount { get; private set; }

	/// <summary>
	/// The <c>SimTickDelta</c> of the last frame that ticked, which a frame recorded under a modal panel
	/// repeats — the panel's loop never refreshes it.
	/// </summary>
	public short LastTickDelta { get; private set; } = Numerics.SimMath.VanillaTickDelta;

	/// <summary>
	/// Starts a tape at <paramref name="path"/> and writes its bundle: the mission file itself, its
	/// lance file (<see cref="MissionLoader.PlayerPathFor"/>), and the other five files from
	/// <paramref name="dataDirectory"/>. A file that is not there is an empty entry, as
	/// <c>Tape_PackFile</c> writes one it cannot open.
	/// </summary>
	public static InputTapeRecorder Create(string path, string scriptPath, string? dataDirectory) {
		var names = InputTapePlayer.BundleFileNames;
		var bundle = new byte[InputTape.BundleFileCount][];
		for (int i = 0; i < InputTape.BundleFileCount; i++) {
			string? source = names[i] == MissionLoader.ScriptFileName ? scriptPath
				: names[i] == MissionLoader.PlayerFileName ? MissionLoader.PlayerPathFor(scriptPath)
				: dataDirectory is null ? null
				: System.IO.Path.Combine(dataDirectory, names[i]);
			bundle[i] = source is not null && File.Exists(source) ? File.ReadAllBytes(source) : Array.Empty<byte>();
		}

		string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
		if (directory is not null) {
			Directory.CreateDirectory(directory);
		}

		var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
		var recorder = new InputTapeRecorder(stream, path);
		recorder.Append(recorder._transformer.WriteBundle(bundle));
		return recorder;
	}

	/// <summary>
	/// <c>Input_QueryCapabilities</c>' eight bytes for <paramref name="capabilities"/>: <c>+0</c>, the button
	/// count at <c>+2</c>, and the throttle, rudder and hat flags at <c>+4</c>, <c>+5</c> and <c>+6</c> —
	/// the hat as <c>JOYCAPS_HASPOV</c>'s own <c>0x10</c>, which is what the retail tapes carry.
	///
	/// <para><c>+0</c> is 2 when the original enumerated a second stick and 1 otherwise, with or
	/// without a first; this engine reads one device, so it is always 1.</para>
	/// </summary>
	public static byte[] CapabilityBlock(JoystickCapabilities capabilities) {
		var block = new byte[InputTape.CapabilityBlockSize];
		block[0] = SingleStickFlag;
		if (capabilities.Present) {
			block[2] = (byte)Math.Min(capabilities.ButtonCount, JoystickCapabilities.MaxButtons);
			block[4] = (byte)(capabilities.HasThrottle ? 1 : 0);
			block[5] = (byte)(capabilities.HasRudder ? 1 : 0);
			block[6] = (byte)(capabilities.HasHat ? 0x10 : 0);
		}

		return block;
	}

	/// <summary>Capability <c>+0</c> with no second stick enumerated.</summary>
	private const byte SingleStickFlag = 1;

	/// <summary>
	/// Queues one key press as a command code: a set-1 scancode, with <see cref="InputTapePlayer.AltBit"/>
	/// and <see cref="InputTapePlayer.CtrlBit"/> for the modifiers held with it.
	/// </summary>
	public void AddPress(int code) => _presses.Add(code);

	/// <summary>Queues one mouse event, already in the game's screen space.</summary>
	public void AddMouse(InputTape.MouseEvent mouseEvent) => _mouse.Add(mouseEvent);

	/// <summary>
	/// Queues the stick's discrete half for this host frame: the bound button (0-based) that fired, and
	/// the hat's views. Only buttons 1-4 have a bit on the tape.
	/// </summary>
	public void SetDiscreteStick(int firedButton, JoystickHat views) {
		if (firedButton is >= 0 and < InputTapePlayer.RecordedButtonCount) {
			_buttonBank |= (byte)(1 << firedButton);
		}

		_buttonBank |= (byte)(((int)views & 0xf) << 4);
	}

	/// <summary>
	/// This host frame's held input: the four axes before <c>Backturn</c>, the trigger, and the stick's
	/// eight raw buttons.
	/// </summary>
	public void SetHeld(PilotAxes axes, bool trigger, byte rawButtons) {
		_axes = axes;
		_trigger = trigger;
		_rawButtons = rawButtons;
	}

	/// <summary>
	/// Writes one frame that ticked with <paramref name="tickDelta"/>, carrying everything queued since
	/// the last frame.
	/// </summary>
	public void EmitTick(short tickDelta, JoystickCapabilities capabilities) {
		LastTickDelta = tickDelta;
		Emit(tickDelta, capabilities);
	}

	/// <summary>Writes one frame recorded under a modal panel, which repeats the last tick's delta.</summary>
	public void EmitPanel(JoystickCapabilities capabilities) => Emit(LastTickDelta, capabilities);

	private void Emit(short tickDelta, JoystickCapabilities capabilities) {
		if (!_capabilitiesWritten) {
			Append(CapabilityBlock(capabilities));
			_capabilitiesWritten = true;
		}

		// The command word is one key event; the rest of the frame's presses ride in the command queue,
		// which the player presses in the same host frame.
		var frame = new InputTape.Frame {
			CommandWord = (short)(_presses.Count > 0 ? _presses[0] : 0),
			Axes = new[] { _axes.Steer, _axes.Throttle, _axes.TorsoTwist, _axes.TorsoPitch },
			TickDelta = tickDelta,
			ButtonBank = _buttonBank,
			RawButtonBank = _rawButtons,
			TriggerBank = (byte)(_trigger ? 1 : 0),
			MouseEvents = new List<InputTape.MouseEvent>(_mouse),
			Commands = _presses.Skip(1).Select(code => (short)code).ToArray(),
		};

		Append(_transformer.WriteFrame(frame));
		FrameCount++;

		_presses.Clear();
		_mouse.Clear();
		_buttonBank = 0;
	}

	private void Append(byte[] bytes) {
		_stream.Write(bytes, 0, bytes.Length);
		_stream.Flush();
	}

	public void Dispose() => _stream.Dispose();
}
