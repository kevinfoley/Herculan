namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// The library's table of open MIDI drivers (docs/es1/sound/song-player.md#the-drivers): five slots, each holding a
/// device id (<c>000a130c</c>, 0 for a free slot) and the driver's functions, and the rule by which a song's tracks pick
/// a slot (docs/es1/sound/song-player.md#loading, step 6).
/// </summary>
public sealed class Es1MidiDrivers {
	/// <summary>The open drivers at most: the size of the table.</summary>
	public const int Slots = 5;

	/// <summary>The device ids a track lists at most: the entries of <c>Es1HmpSong.TrackDevices</c> a track has.</summary>
	public const int TrackDeviceIds = 5;

	private readonly int[] _ids = new int[Slots];
	private readonly IEs1MidiDriver?[] _drivers = new IEs1MidiDriver?[Slots];
	private readonly bool[,] _channels = new bool[Slots, 16];

	/// <summary>
	/// The channels each device's master volume reaches (<c>000a55d8</c>, 16 bytes a device from <c>0xa000</c>): every
	/// channel but 9 for the devices <c>0xa000</c>–<c>0xa003</c> and <c>0xa005</c>–<c>0xa00a</c>, channels 1–7 for the
	/// MT-32, <c>0xa004</c>. A device outside <c>0xa000</c>–<c>0xa00a</c>, whose table retail reads from whatever follows,
	/// has none.
	/// </summary>
	private static bool UsesChannel(int deviceId, int channel) => deviceId switch {
		0xa004 => channel is >= 1 and <= 7,
		>= 0xa000 and <= 0xa00a => channel != 9,
		_ => false,
	};

	/// <summary>
	/// Whether the master volume reaches <paramref name="channel"/> of the driver in <paramref name="slot"/>, from the
	/// table its device had when it was added (<c>000a5688</c>); channel 9 is reached whatever the table says
	/// (<see cref="Es1HmiPlayer.SetMasterVolume"/>).
	/// </summary>
	public bool ReachesChannel(int slot, int channel) => _channels[slot, channel];

	/// <summary>The device id in <paramref name="slot"/>, 0 when the slot is free.</summary>
	public int DeviceId(int slot) => _ids[slot];

	/// <summary>The driver in <paramref name="slot"/>, null when the slot is free.</summary>
	public IEs1MidiDriver? Driver(int slot) => _drivers[slot];

	/// <summary>
	/// <c>HmiMidi_InitDriver</c> (<c>00071076</c>)'s choice of a slot: <paramref name="driver"/>, which has opened as
	/// <paramref name="deviceId"/>, takes the first free slot, answered in <paramref name="slot"/>. False, retail's error
	/// <c>0xb</c>, when all five are taken. Retail keeps the id of a slot whose open then fails; here the caller adds a
	/// driver once it has opened.
	/// </summary>
	public bool TryAdd(int deviceId, IEs1MidiDriver driver, out int slot) {
		for (slot = 0; slot < Slots; slot++) {
			if (_ids[slot] == 0) {
				_ids[slot] = deviceId;
				_drivers[slot] = driver;
				for (int channel = 0; channel < 16; channel++) {
					_channels[slot, channel] = UsesChannel(deviceId, channel);
				}

				return true;
			}
		}

		slot = -1;
		return false;
	}

	/// <summary>
	/// <c>HmiMidi_CloseDriver</c> (<c>0007144b</c>): the driver's close called and the slot freed. False, retail's answer 1,
	/// for a slot that is not open.
	/// </summary>
	public bool Close(int slot) {
		if (_drivers[slot] is not { } driver) {
			return false;
		}

		driver.Close(slot);
		_ids[slot] = 0;
		_drivers[slot] = null;
		return true;
	}

	/// <summary>
	/// <c>HmiMidi_ResetDriver</c> (<c>00071515</c>): on each of the 16 channels in turn, controller <c>0x79</c> then
	/// <c>0x7b</c>, both 0, sent to the driver; then the driver's reset.
	/// </summary>
	public void Reset(int slot) {
		if (_drivers[slot] is not { } driver) {
			return;
		}

		Span<byte> controller = stackalloc byte[3];
		for (int channel = 0; channel < 16; channel++) {
			controller[0] = (byte)(0xb0 | channel);
			controller[1] = 0x79;
			controller[2] = 0;
			driver.SendEvent(controller, slot);
			controller[1] = 0x7b;
			driver.SendEvent(controller, slot);
		}

		driver.Reset(slot);
	}

	/// <summary>
	/// Whether an open driver of id <paramref name="driverId"/> plays what a track lists as <paramref name="trackId"/>:
	/// <c>0xa000</c> is answered by <c>0xa000</c>, <c>0xa001</c> or <c>0xa008</c>, <c>0xa002</c> by <c>0xa002</c> or
	/// <c>0xa009</c>, and any other id by itself.
	/// </summary>
	public static bool Answers(int driverId, int trackId) => trackId switch {
		0xa000 => driverId is 0xa000 or 0xa001 or 0xa008,
		0xa002 => driverId is 0xa002 or 0xa009,
		_ => driverId == trackId,
	};

	/// <summary>
	/// The slot <c>HmiMidi_InitSong</c> (<c>0006e4c1</c>) routes a track to (docs/es1/sound/song-player.md#loading, step 6):
	/// 0 for a track whose first id is 0; otherwise, of <paramref name="trackDevices"/> in order up to a 0, the first id
	/// that an open driver answers, and the first slot of those that do. −1 when no open driver answers any, the track
	/// that is then dropped.
	/// </summary>
	public int Route(ReadOnlySpan<int> trackDevices) {
		if (trackDevices.Length == 0 || trackDevices[0] == 0) {
			return 0;
		}

		for (int i = 0; i < Math.Min(trackDevices.Length, TrackDeviceIds) && trackDevices[i] != 0; i++) {
			for (int slot = 0; slot < Slots; slot++) {
				if (_ids[slot] != 0 && Answers(_ids[slot], trackDevices[i])) {
					return slot;
				}
			}
		}

		return -1;
	}
}
