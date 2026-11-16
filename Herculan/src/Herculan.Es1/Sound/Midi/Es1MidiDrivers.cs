using HercWorks.Core.Data.File;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Audio;
using Herculan.Engine.Audio.Opl;

namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// The library's table of open MIDI drivers (docs/es1/sound/song-player.md#the-drivers): five slots, each holding a
/// device id (<c>000a130c</c>, 0 for a free slot), the driver's functions and the image its code came from, and the rule by
/// which a song's tracks pick a slot (docs/es1/sound/song-player.md#loading, step 6).
/// </summary>
/// <param name="open">The install's reader, a path to a file's bytes, null when it does not open; none opens no file.</param>
/// <param name="fmChip">The chip the FM driver writes to; none makes a <see cref="NukedOplChip"/>.</param>
/// <param name="digital">The digital driver device <c>0xa005</c> plays its samples on; none makes one over a
/// <see cref="NullAudioBackend"/>.</param>
public sealed class Es1MidiDrivers(Func<string, byte[]?>? open = null, IOplChip? fmChip = null, Es1DigitalDriver? digital = null) {
	/// <summary>The open drivers at most: the size of the table.</summary>
	public const int Slots = 5;

	/// <summary>The device ids a track lists at most: the entries of <c>Es1HmpSong.TrackDevices</c> a track has.</summary>
	public const int TrackDeviceIds = 5;

	/// <summary>The container of MIDI driver images, under <see cref="DriverPath"/> (docs/es1/formats/hmi-driver-container.md).</summary>
	public const string ContainerName = "hmimdrv.386";

	/// <summary>Retail's error for no image in the container for the device.</summary>
	public const int NoImage = 7;

	/// <summary>Retail's error for a device outside <c>0xa000</c>–<c>0xa200</c>.</summary>
	public const int DeviceOutOfRange = 6;

	/// <summary>Retail's error for a slot that already holds an image.</summary>
	public const int SlotHoldsImage = 9;

	/// <summary>Retail's error for a slot past the table, or, freeing an image, a slot that holds none.</summary>
	public const int BadSlot = 10;

	/// <summary>Retail's error for a container that does not open.</summary>
	public const int NoContainer = 0xf;

	/// <summary>Retail's error when all <see cref="Slots"/> slots hold a device.</summary>
	public const int SlotsTaken = 0xb;

	/// <summary>
	/// <c>HmiMidi_DriverArg</c> (<c>000a5984</c>): the third argument the library passes a driver's open, close and reset, a
	/// dword 0 in the image that <c>es2_xref --decode</c> finds no write of (docs/es1/sound/drum-device.md#open).
	/// </summary>
	public const int DriverArg = 0;

	private readonly int[] _ids = new int[Slots];
	private readonly IEs1MidiDriver?[] _drivers = new IEs1MidiDriver?[Slots];
	private readonly bool[,] _channels = new bool[Slots, 16];
	private readonly bool[] _inUse = new bool[Slots];
	private readonly int[] _imageIds = new int[Slots];
	private readonly byte[]?[] _images = new byte[Slots][];

	/// <summary>
	/// The device id of the entry header the loader read last (<c>000a2568</c>, in the buffer <c>000a2540</c>), kept from one
	/// load to the next as retail's buffer is.
	/// </summary>
	private int _entryDevice;

	/// <summary>
	/// The driver path (<c>000a575c</c>), which <c>HmiMidi_InitSystem</c> (<c>00070ed8</c>) copies from its argument, an empty
	/// string for a null pointer; <see cref="LoadDriverImage"/> appends <see cref="ContainerName"/> to it.
	/// </summary>
	public string DriverPath { get; set; } = "";

	/// <summary>The chip the FM driver (<see cref="Es1FmDriver"/>) writes to, whichever slot it opens in.</summary>
	public IOplChip FmChip { get; } = fmChip ?? new NukedOplChip();

	/// <summary>Device <c>0xa005</c>, the drums, whichever slots it opens in (<c>HmiDigiMidi_Functions</c>, <c>000a3028</c>).</summary>
	public Es1DigiMidiDriver DigiMidi { get; } = new(digital ?? new Es1DigitalDriver(new NullAudioBackend()));

	/// <summary>Device <c>0xa003</c>, whichever slots it opens in (<c>HmiCallbackMidi_Functions</c>, <c>000a2574</c>).</summary>
	public Es1CallbackMidiDriver CallbackMidi { get; } = new();

	/// <summary>
	/// Whether <paramref name="slot"/> is in use (<c>000a12e4</c>): set by <see cref="LoadDriverImage"/>, whether or not it
	/// finds an image, and by a driver's open; cleared by <see cref="FreeDriverImage"/> and <see cref="Close"/>.
	/// </summary>
	public bool InUse(int slot) => _inUse[slot];

	/// <summary>
	/// The device id <see cref="LoadDriverImage"/> left for <paramref name="slot"/> (<c>000a12f8</c>): that of the last entry
	/// header it read, the matching entry's or, with none matching, the container's last.
	/// </summary>
	public int ImageDeviceId(int slot) => _imageIds[slot];

	/// <summary>
	/// The image the driver in <paramref name="slot"/> was loaded from, null for none. It stands for retail's copy of the image
	/// and the two selectors over it (<c>000a127c</c>, <c>000a129c</c>, <c>000a12bc</c>, <c>000a12d0</c>).
	/// </summary>
	public byte[]? Image(int slot) => _images[slot];

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
	/// <paramref name="deviceId"/>, takes the first free slot, answered in <paramref name="slot"/>, and marks it in use. False,
	/// retail's error <c>0xb</c>, when all five are taken. Retail keeps the id of a slot whose open then fails; here the
	/// caller adds a driver once it has opened.
	/// </summary>
	public bool TryAdd(int deviceId, IEs1MidiDriver driver, out int slot) {
		if (!Claim(deviceId, out slot)) {
			return false;
		}

		_drivers[slot] = driver;
		_inUse[slot] = true;
		return true;
	}

	/// <summary>
	/// <c>HmiMidi_InitDriver</c> (<c>00071076</c>) (docs/es1/sound/song-player.md#the-drivers), for a device whose code is
	/// an image: <paramref name="deviceId"/> takes the first free slot, answered in <paramref name="slot"/>, and its channel
	/// table; the image is the one <paramref name="block"/> holds or, with none, the one <see cref="LoadDriverImage"/> loads,
	/// which the block then keeps; the driver is made for the image with the port block, and the slot is marked in use,
	/// answering 0. <see cref="SlotsTaken"/> when no slot is free; a load's error as it answers it, the slot keeping the device
	/// id and the load's marks, as retail's does.
	///
	/// <para>Retail's reuse of the block's image stores its data far pointer, address and allocation in the slot's tables
	/// but not its code far pointer, which the load's path stores. Retail then has the image answer request 1 with its five
	/// functions (<c>HmiDriver_GetFunctions</c>, <c>00081687</c>) and calls the open function with the port block; here the
	/// driver's constructor stands for both (<see cref="IEs1MidiDriver"/>). Its call of <c>HmiMidi_IsDriverSlotFree</c>,
	/// whose answer it does not read, has no counterpart. Device <c>0xa00a</c>'s timer function, which retail asks the image
	/// for and registers at 120 calls a second, is not ported: the sound manager opens <c>0xa002</c> for every MIDI device
	/// (decision 25 of docs/herculan/plan-es1-first-mission.md).</para>
	///
	/// <para>The image of <c>0xa002</c> opens as an <see cref="Es1FmDriver"/> on <see cref="FmChip"/>; any other image opens
	/// with no functions, its events dropped, since the sound manager opens no other.</para>
	///
	/// <para>Devices <c>0xa005</c> and <c>0xa003</c> are the program's own (docs/es1/sound/drum-device.md#the-devices-in-the-program):
	/// the slot takes <see cref="DigiMidi"/> or <see cref="CallbackMidi"/>, and <see cref="DigiMidi"/>'s open reads the block's
	/// <see cref="Es1MidiDriverBlock.Digital"/>, its error answered with the slot keeping the device and the driver but not
	/// marked in use, as retail's. No image is loaded and the block's is left alone. <see cref="CallbackMidi"/>'s open
	/// answers 0 and is not called.</para>
	/// </summary>
	/// <param name="deviceId">The device to open.</param>
	/// <param name="portBlock">The caller's port block, three dwords; the FM image reads the low word of <c>+0</c>, the port.</param>
	/// <param name="block">The caller's block, which keeps the image.</param>
	/// <param name="slot">The slot the device took, −1 when none was free.</param>
	public int InitDriver(int deviceId, int[] portBlock, Es1MidiDriverBlock block, out int slot) {
		if (!Claim(deviceId, out slot)) {
			return SlotsTaken;
		}

		if (deviceId == 0xa005) {
			_drivers[slot] = DigiMidi;
			int error = DigiMidi.Open(block.Digital, slot);
			if (error != 0) {
				return error;
			}
		} else if (deviceId == 0xa003) {
			_drivers[slot] = CallbackMidi;
		} else {
			byte[]? image = block.Image;
			if (image == null) {
				int error = LoadDriverImage(deviceId, slot, out image);
				if (error != 0) {
					return error;
				}

				block.Image = image;
			}

			_images[slot] = image;
			_drivers[slot] = DriverFor(deviceId, image!, portBlock);
		}

		_inUse[slot] = true;
		return 0;
	}

	/// <summary>
	/// The driver for an image, made with what its open function takes: the FM driver for <c>0xa002</c>, which runs in
	/// <paramref name="image"/>, and none for any other device.
	/// </summary>
	private IEs1MidiDriver? DriverFor(int deviceId, byte[] image, int[] portBlock) =>
		deviceId == 0xa002 ? new Es1FmDriver(image, FmChip, portBlock) : null;

	/// <summary>
	/// <c>HmiMidi_LoadBank</c> (<c>00072275</c>)(slot, bank, <i>x</i>): <paramref name="bank"/> given to the bank function of
	/// the driver in <paramref name="slot"/>, answering what it answers. Retail calls the slot's entry whatever it holds;
	/// a slot with no driver answers 0 here, an engine guard.
	/// </summary>
	public int LoadBank(int slot, byte[] bank) => _drivers[slot]?.LoadBank(bank, slot) ?? 0;

	/// <summary>
	/// <c>HmiMidi_InitDriver</c>'s choice of a slot: the first whose device id is 0, given <paramref name="deviceId"/> and
	/// that device's channel table (<c>000a55d8</c> to <c>000a5688</c>); false when all are taken.
	/// </summary>
	private bool Claim(int deviceId, out int slot) {
		for (slot = 0; slot < Slots; slot++) {
			if (_ids[slot] == 0) {
				_ids[slot] = deviceId;
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
	/// <c>HmiMidi_LoadDriverImage</c> (<c>00081365</c>) (docs/es1/formats/hmi-driver-container.md#loading-an-image): the
	/// image for <paramref name="deviceId"/> read out of <see cref="ContainerName"/> under <see cref="DriverPath"/> into
	/// <paramref name="image"/>, answering 0. <see cref="BadSlot"/> for a slot past the table, <see cref="SlotHoldsImage"/> for
	/// a slot in use, <see cref="DeviceOutOfRange"/> for a device outside <c>0xa000</c>–<c>0xa200</c> and
	/// <see cref="NoContainer"/> when the container does not open, each with nothing changed. Otherwise the entries are read
	/// in turn up to the first whose device is <paramref name="deviceId"/>, the slot is marked in use and given the last entry's
	/// device as its <see cref="ImageDeviceId"/>, and the answer is <see cref="NoImage"/> when no entry matched.
	///
	/// <para>Retail answers 10 for a slot above 5, so slot 5, past the five-slot table, passes the test and reads its flag
	/// from the next table; here it answers <see cref="BadSlot"/>. Retail reads one entry header more than the image count
	/// when none matches; every retail container ends with its last image, so that read gets no bytes and leaves the last
	/// entry's header in the buffer, and it is not made here. A container the reader refuses counts as one with no entries,
	/// an engine guard: retail reads whatever the file holds. Retail's error 5, for an allocation that fails, has no
	/// counterpart.</para>
	/// </summary>
	public int LoadDriverImage(int deviceId, int slot, out byte[]? image) {
		image = null;
		if (slot is < 0 or >= Slots) {
			return BadSlot;
		}

		if (_inUse[slot]) {
			return SlotHoldsImage;
		}

		if (deviceId is < 0xa000 or > 0xa200) {
			return DeviceOutOfRange;
		}

		if (open?.Invoke(DriverPath + ContainerName) is not { } bytes) {
			return NoContainer;
		}

		Es1HmiDriverContainer? container;
		try {
			container = new Es1HmiDriverContainerTransformer().Parse(bytes);
		} catch (InvalidDataException) {
			container = null;
		}

		foreach (var entry in container?.Images ?? []) {
			_entryDevice = entry.DeviceId;
			if (entry.DeviceId == deviceId) {
				image = entry.Image;
				break;
			}
		}

		_imageIds[slot] = _entryDevice;
		_inUse[slot] = true;
		return image == null ? NoImage : 0;
	}

	/// <summary>
	/// <c>HmiMidi_FreeDriverImage</c> (<c>00081599</c>): the slot's image freed and the slot no longer in use, answering 0;
	/// <see cref="BadSlot"/> for a slot past the table or not in use. Retail's test lets slot 5 through, as
	/// <see cref="LoadDriverImage"/>'s does.
	/// </summary>
	public int FreeDriverImage(int slot) {
		if (slot is < 0 or >= Slots || !_inUse[slot]) {
			return BadSlot;
		}

		_images[slot] = null;
		_inUse[slot] = false;
		return 0;
	}

	/// <summary>
	/// <c>HmiMidi_CloseDriver</c> (<c>0007144b</c>): for a slot in use, the driver's close called, with
	/// <paramref name="freeImage"/> its image freed unless its device is <c>0xa003</c> or <c>0xa005</c>, whose code is the
	/// program's, then the slot freed and no longer in use. False, retail's answer 1, for a slot not in use. A slot with no
	/// driver functions has no close to call; retail calls the slot's entry whatever it holds. Retail also removes device
	/// <c>0xa00a</c>'s timer event, which no open here registers.
	/// </summary>
	public bool Close(int slot, bool freeImage) {
		if (!_inUse[slot]) {
			return false;
		}

		_drivers[slot]?.Close(slot);
		if (freeImage && _ids[slot] is not (0xa003 or 0xa005)) {
			FreeDriverImage(slot);
		}

		_images[slot] = null;
		_ids[slot] = 0;
		_drivers[slot] = null;
		_inUse[slot] = false;
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
