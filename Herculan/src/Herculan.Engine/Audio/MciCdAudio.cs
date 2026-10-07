using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Herculan.Engine.Audio;

/// <summary>
/// <c>Music_PlayTrack</c> and its three siblings, as the original spells them: Win32
/// <c>mciSendCommand</c> against MCI device type <c>cdaudio</c>, time format TMSF, play from a
/// track's start to that track's own length. See docs/retail/formats/audio.md's "CD audio".
///
/// <para><b>The fallback transport.</b> <see cref="CdAudio.Open"/> prefers
/// <see cref="StreamedCdAudio"/> and comes here only for a drive that refuses raw CD-DA reads or a
/// machine with no digital output device.</para>
///
/// <para><b>Two deliberate divergences</b>, both marked at the member that makes them:</para>
/// <list type="bullet">
/// <item>The loop is polled rather than notified — <see cref="Update"/>.</item>
/// <item>The drive can be named — <see cref="TryCreate"/>. The original names none; it opens the
/// device type alone, which is whichever CD drive MCI picks first — see docs/retail/formats/audio.md,
/// "No drive is named".</item>
/// </list>
/// </summary>
public sealed class MciCdAudio : ICdAudio {
	// mmsystem.h. The four commands the original sends, and the flags it sends them with: the play is
	// 0xd = MCI_NOTIFY | MCI_FROM | MCI_TO, the position query 0x102 = MCI_WAIT | MCI_STATUS_ITEM and
	// the track-length query 0x112, which adds MCI_TRACK.
	private const uint MciOpen = 0x0803;
	private const uint MciClose = 0x0804;
	private const uint MciPlay = 0x0806;
	private const uint MciStop = 0x0808;
	private const uint MciSet = 0x080d;
	private const uint MciStatus = 0x0814;

	private const uint MciWait = 0x00000002;
	private const uint MciFrom = 0x00000004;
	private const uint MciTo = 0x00000008;
	private const uint MciTrack = 0x00000010;
	private const uint MciStatusItem = 0x00000100;
	private const uint MciOpenElement = 0x00000200;
	private const uint MciSetTimeFormat = 0x00000400;
	private const uint MciOpenType = 0x00002000;

	private const uint MciStatusLength = 1;
	private const uint MciStatusPosition = 2;
	private const uint MciStatusNumberOfTracks = 3;
	private const uint MciStatusMode = 4;
	private const uint MciStatusMediaPresent = 6;

	private const uint MciFormatTmsf = 10;

	private const uint MciModePlay = 526;
	private const uint MciModeSeek = 528;
	private const uint MciModeNotReady = 527;

	/// <summary>
	/// The device type the original opens, and the only one it opens. Passed as
	/// <c>MCI_OPEN_TYPE</c>'s string rather than as a type id, as <c>s_cdaudio_004a0e4c</c> is.
	/// </summary>
	public const string DeviceType = "cdaudio";

	/// <summary>
	/// How often <see cref="Update"/> asks the device whether the track has run out. Each ask is a
	/// blocking <c>MCI_STATUS</c> against a spinning disc, so it is not something to do every frame;
	/// a track boundary a fifth of a second late is inaudible against a CD's own seek.
	/// </summary>
	public static readonly TimeSpan LoopPollInterval = TimeSpan.FromMilliseconds(200);

	private readonly string? _element;
	private readonly Stopwatch _sinceLastPoll = Stopwatch.StartNew();
	private uint _deviceId = NoDevice;
	private int _playingTrack;
	private int _endFrames;
	private bool _disposed;

	private const uint NoDevice = 0xffffffff;

	private MciCdAudio(string? element, int trackCount, string status) {
		_element = element;
		TrackCount = trackCount;
		Status = status;
	}

	/// <summary>How many tracks the disc reported when the device was probed.</summary>
	public int TrackCount { get; }

	/// <inheritdoc />
	public bool IsAvailable => TrackCount > 0;

	/// <inheritdoc />
	public bool IsIdle => _deviceId == NoDevice;

	/// <inheritdoc />
	public string Status { get; }

	/// <summary>
	/// Opens the device once to see whether there is anything to play, then closes it again — the
	/// original holds the device only while a track is sounding, and so does this. Never throws:
	/// every failure comes back as a <see cref="NullCdAudio"/> carrying the reason.
	/// </summary>
	/// <param name="drive">
	/// Which drive to open, as a letter or as <c>F:</c>. Null opens the device type alone, which is
	/// what the original does and what MCI answers with its own first CD drive. <b>Naming a drive is
	/// this engine's own</b>; nothing in retail selects one, and nothing here enumerates them — a
	/// named drive is opened, and an unnamed one is left to MCI.
	/// </param>
	public static ICdAudio TryCreate(string? drive = null) {
		// Which implementation a machine gets is CdAudio.Open's to decide, and it never sends a
		// non-Windows one here. This is the entry point's own precondition: called directly off
		// Windows, the first mciSendCommandW would fail to resolve winmm.dll.
		if (!OperatingSystem.IsWindows()) {
			return new NullCdAudio("CD music needs MCI, which is Windows-only");
		}

		string? element = NormaliseDrive(drive);
		if (drive != null && element == null) {
			return new NullCdAudio($"'{drive}' is not a drive letter");
		}

		string named = element == null ? "the default CD drive" : element;

		uint deviceId;
		uint error = Open(element, out deviceId);
		if (error != 0) {
			return new NullCdAudio($"MCI would not open {named}: {ErrorText(error)}");
		}

		try {
			if (Query(deviceId, MciStatusMediaPresent, 0, out nuint present) != 0 || present == 0) {
				return new NullCdAudio($"no disc in {named}");
			}

			if (Query(deviceId, MciStatusNumberOfTracks, 0, out nuint tracks) != 0 || tracks == 0) {
				return new NullCdAudio($"{named} holds a disc with no tracks");
			}

			return new MciCdAudio(element, (int)tracks,
				$"{DeviceType} on {named}, {tracks} tracks");
		} finally {
			SendCommand(deviceId, MciClose, 0, IntPtr.Zero);
		}
	}

	/// <summary>
	/// <c>Music_PlayTrack</c> (<c>00473b3c</c>). Opens the device, sets TMSF, asks the track how long
	/// it is and plays it from its own start to its own end; any step failing closes the device
	/// again, which is exactly the original's unwind.
	/// </summary>
	public bool PlayTrack(int track) {
		if (track <= 0 || _disposed) {
			return false;
		}

		if (!OpenForPlayback()) {
			return false;
		}

		// MCI_STATUS_LENGTH with MCI_TRACK, under the TMSF format, answers with the track's length as
		// minutes/seconds/frames in the low three bytes -- the track byte of a TMSF word is not part
		// of a length. 00473c78 is this query on its own.
		if (Query(_deviceId, MciStatusLength, (uint)track, out nuint length) != 0 || length == 0) {
			Stop();
			return false;
		}

		uint from = MakeTmsf((uint)track, 0, 0, 0);
		uint to = MakeTmsf((uint)track,
			(uint)length & 0xff, ((uint)length >> 8) & 0xff, ((uint)length >> 16) & 0xff);

		if (!Play(from, to)) {
			return false;
		}

		_playingTrack = track;
		_endFrames = FramesInMsf((uint)length);
		_sinceLastPoll.Restart();
		return true;
	}

	/// <summary>
	/// <c>Music_ResumeAt</c> (<c>00473cc0</c>). The end of the play is the same track end
	/// <see cref="PlayTrack"/> last worked out — the original keeps it in <c>Music_TrackLength</c> (<c>006b5610</c>) across
	/// the two calls rather than querying it again, which is why resuming only ever follows a play.
	/// </summary>
	public bool ResumeAt(int packedTmsf) {
		if (packedTmsf == 0 || _playingTrack <= 0 || _disposed) {
			return false;
		}

		if (!OpenForPlayback()) {
			return false;
		}

		if (Query(_deviceId, MciStatusLength, (uint)_playingTrack, out nuint length) != 0
			|| length == 0) {
			Stop();
			return false;
		}

		uint to = MakeTmsf((uint)packedTmsf & 0xff,
			(uint)length & 0xff, ((uint)length >> 8) & 0xff, ((uint)length >> 16) & 0xff);

		if (!Play((uint)packedTmsf, to)) {
			return false;
		}

		_endFrames = FramesInMsf((uint)length);
		_sinceLastPoll.Restart();
		return true;
	}

	/// <inheritdoc />
	public int GetPosition() =>
		_deviceId != NoDevice && Query(_deviceId, MciStatusPosition, 0, out nuint position) == 0
			? (int)position
			: 0;

	/// <inheritdoc />
	public void Stop() {
		if (_deviceId == NoDevice) {
			return;
		}

		SendCommand(_deviceId, MciStop, 0, IntPtr.Zero);
		SendCommand(_deviceId, MciClose, 0, IntPtr.Zero);
		_deviceId = NoDevice;
	}

	/// <summary>
	/// Restarts the track when it has run out, which is what makes the mission's music loop.
	///
	/// <para><b>Polled, where the original is notified.</b> <c>Music_PlayTrack</c> asks for
	/// <c>MCI_NOTIFY</c> and <c>sfxWndProc</c> re-issues the play when <c>MM_MCINOTIFY</c> arrives
	/// with <c>MCI_NOTIFY_SUCCESSFUL</c>; that wants an <c>HWND</c> with a Win32 window procedure of
	/// its own, and this engine's window is Silk.NET's. Asking the device every
	/// <see cref="LoopPollInterval"/> and restarting when the track has run out gives the same loop,
	/// up to that interval's worth of gap at the seam.</para>
	///
	/// <para><b>The play head, not the mode, is what says the track ended.</b> An MCI CD device that
	/// has played to its <c>MCI_TO</c> goes on reporting <c>MCI_MODE_PLAY</c> and a frozen position
	/// indefinitely, so a mode poll alone never fires. The position is compared against the track's
	/// own length instead; the mode is still consulted, because a device that genuinely stops — an
	/// ejected disc, a drive gone — freezes no position to compare.</para>
	/// </summary>
	public void Update() {
		if (_deviceId == NoDevice || _playingTrack <= 0 || _disposed
			|| _sinceLastPoll.Elapsed < LoopPollInterval) {
			return;
		}

		_sinceLastPoll.Restart();

		if (!TrackHasEnded()) {
			return;
		}

		int track = _playingTrack;
		Stop();
		PlayTrack(track);
	}

	/// <summary>
	/// Whether the play head has left the track this device was told to play — either by running past
	/// its end or by having moved on to another track. A position of 0 is "not yet known", which is
	/// what a drive still spinning up answers.
	/// </summary>
	private bool TrackHasEnded() {
		if (Query(_deviceId, MciStatusPosition, 0, out nuint position) == 0 && position != 0) {
			return ((uint)position & 0xff) != (uint)_playingTrack
				|| FramesInMsf((uint)position >> 8) >= _endFrames;
		}

		// No usable position, so fall back on the mode. Seeking and not-ready are both "about to be
		// playing again"; anything else means the device has given up.
		return Query(_deviceId, MciStatusMode, 0, out nuint mode) == 0
			&& mode is not (MciModePlay or MciModeSeek or MciModeNotReady);
	}

	/// <summary>
	/// An MSF triple — minutes, seconds, frames, one byte each from the low end — as a frame count.
	/// Red Book has 75 frames to the second.
	/// </summary>
	private static int FramesInMsf(uint msf) =>
		(int)((msf & 0xff) * 60 * FramesPerSecond
			+ ((msf >> 8) & 0xff) * FramesPerSecond
			+ ((msf >> 16) & 0xff));

	/// <summary>Red Book frames in a second, which is what the F of an MSF or TMSF word counts.</summary>
	public const int FramesPerSecond = 75;

	/// <inheritdoc />
	public void Dispose() {
		if (_disposed) {
			return;
		}

		Stop();
		_disposed = true;
	}

	/// <summary>
	/// A drive as MCI wants it in <c>MCI_OPEN_ELEMENT</c>: <c>F:</c>. Accepts <c>f</c>, <c>F:</c> and
	/// <c>F:\</c>, and answers null for anything that is not a single drive letter.
	/// </summary>
	private static string? NormaliseDrive(string? drive) {
		if (drive == null) {
			return null;
		}

		string trimmed = drive.Trim().TrimEnd('\\', '/');
		if (trimmed.EndsWith(':')) {
			trimmed = trimmed[..^1];
		}

		return trimmed.Length == 1 && char.IsAsciiLetter(trimmed[0])
			? $"{char.ToUpperInvariant(trimmed[0])}:"
			: null;
	}

	/// <summary>
	/// The open-and-set pair both play entry points begin with. A device already open is left alone,
	/// so a restart that follows a <see cref="Stop"/> pays for one open and no more.
	/// </summary>
	private bool OpenForPlayback() {
		if (_deviceId != NoDevice) {
			return true;
		}

		if (Open(_element, out uint deviceId) != 0) {
			return false;
		}

		_deviceId = deviceId;

		var set = new MciSetParms { TimeFormat = MciFormatTmsf };
		if (SendCommand(_deviceId, MciSet, MciSetTimeFormat, ref set) != 0) {
			Stop();
			return false;
		}

		return true;
	}

	private bool Play(uint from, uint to) {
		// The original's own flag word is MCI_NOTIFY | MCI_FROM | MCI_TO. The notify is dropped here
		// because there is no window to send it to -- see Update.
		var play = new MciPlayParms { From = from, To = to };
		if (SendCommand(_deviceId, MciPlay, MciFrom | MciTo, ref play) == 0) {
			return true;
		}

		Stop();
		return false;
	}

	/// <summary>TMSF, the packing <c>MCI_FORMAT_TMSF</c> takes: track, minute, second, frame.</summary>
	private static uint MakeTmsf(uint track, uint minute, uint second, uint frame) =>
		(track & 0xff) | ((minute & 0xff) << 8) | ((second & 0xff) << 16) | ((frame & 0xff) << 24);

	private static uint Open(string? element, out uint deviceId) {
		deviceId = NoDevice;

		IntPtr type = Marshal.StringToHGlobalUni(DeviceType);
		IntPtr elementPtr = element == null ? IntPtr.Zero : Marshal.StringToHGlobalUni(element);
		try {
			var open = new MciOpenParms {
				DeviceType = type,
				ElementName = elementPtr,
			};

			uint flags = MciOpenType | (elementPtr == IntPtr.Zero ? 0 : MciOpenElement);
			uint error = SendCommand(0, MciOpen, flags, ref open);
			if (error == 0) {
				deviceId = open.DeviceId;
			}

			return error;
		} finally {
			Marshal.FreeHGlobal(type);
			if (elementPtr != IntPtr.Zero) {
				Marshal.FreeHGlobal(elementPtr);
			}
		}
	}

	private static uint Query(uint deviceId, uint item, uint track, out nuint value) {
		var status = new MciStatusParms { Item = item, Track = track };
		uint flags = MciWait | MciStatusItem | (track == 0 ? 0 : MciTrack);
		uint error = SendCommand(deviceId, MciStatus, flags, ref status);
		value = error == 0 ? status.Return : 0;
		return error;
	}

	private static string ErrorText(uint error) {
		var text = new char[128];
		return mciGetErrorStringW(error, text, (uint)text.Length)
			? new string(text).TrimEnd('\0')
			: $"MCI error {error}";
	}

	// mmsystem.h wraps every MMSYSTEM structure in pshpack1.h, so these are byte-packed and NOT
	// naturally aligned. It only shows on 64-bit, and it only shows here: MCI_OPEN_PARMS puts
	// lpstrDeviceType immediately after the 4-byte device id, at offset 12, where natural alignment
	// would put it at 16. Getting that wrong hands the CD driver a garbage type pointer, and it
	// dereferences it.
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct MciOpenParms {
		public IntPtr Callback;
		public uint DeviceId;
		public IntPtr DeviceType;
		public IntPtr ElementName;
		public IntPtr Alias;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct MciSetParms {
		public IntPtr Callback;
		public uint TimeFormat;
		public uint Audio;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct MciPlayParms {
		public IntPtr Callback;
		public uint From;
		public uint To;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct MciStatusParms {
		public IntPtr Callback;
		public nuint Return;
		public uint Item;
		public uint Track;
	}

	/// <summary>
	/// Every parameter block here is a local, so it is already on the stack and needs no pinning; the
	/// call takes its address directly.
	/// </summary>
	private static unsafe uint SendCommand<T>(uint deviceId, uint command, uint flags, ref T parms)
			where T : unmanaged =>
		mciSendCommandW(deviceId, command, (UIntPtr)flags, (IntPtr)Unsafe.AsPointer(ref parms));

	private static uint SendCommand(uint deviceId, uint command, uint flags, IntPtr parms) =>
		mciSendCommandW(deviceId, command, (UIntPtr)flags, parms);

	[DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern uint mciSendCommandW(uint deviceId, uint command, UIntPtr flags,
		IntPtr parms);

	[DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern bool mciGetErrorStringW(uint error, [Out] char[] text, uint length);
}
