using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sound;

/// <summary>
/// The six values <c>Sound_Init</c> (<c>0004db14</c>) stores over the driver defaults block (<c>000959ec</c>), at their
/// addresses there (docs/es1/formats/sound-samples.md#the-driver); the block's rate and buffer are not among them.
/// </summary>
/// <param name="MidiPort"><c>00095a08</c>.</param>
/// <param name="Port"><c>00095a00</c>, the digital card's.</param>
/// <param name="Dma"><c>000959f8</c>.</param>
/// <param name="Irq"><c>000959fc</c>.</param>
/// <param name="MidiDevice"><c>00095a0c</c>.</param>
/// <param name="DigitalDevice"><c>00095a04</c>; 0 makes no sound manager.</param>
public readonly record struct Es1SoundDevices(ushort MidiPort, ushort Port, byte Dma, byte Irq, ushort MidiDevice, ushort DigitalDevice) {
	/// <summary>The block's values in the image: MIDI port <c>0x388</c>, port <c>0x220</c>, DMA 1, IRQ 7, MIDI device <c>0xa002</c>, digital device <c>0xe000</c>.</summary>
	public static Es1SoundDevices ImageDefaults => new(0x388, 0x220, 1, 7, 0xa002, 0xe000);
}

/// <summary>
/// The sound manager (docs/es1/formats/sound-samples.md#the-sound-manager): the one object that owns every sound, its
/// names, the sizes the volume stream reports for them, the loaded data and its count against a limit, and
/// <see cref="ObjectSlots"/> sound objects. The simulator's and the shell's are one code; each program makes one at
/// start-up (<c>SoundManager_Ctor</c>, <c>0004eb40</c>; the shell's copy is <c>ES1GO 00041f64</c>).
///
/// <para>Retail reads every name through one volume stream on <c>sos\sound.vol</c>; here a name is read through the
/// volumes' reader given at construction, split at its last backslash into a folder and a file name. The driver's calls
/// go to an <see cref="Es1DigitalDriver"/>, this engine's stand-in for HMI's.</para>
/// </summary>
public sealed class Es1SoundManager {
	/// <summary>The sound objects, and the names, the manager holds.</summary>
	public const int ObjectSlots = 60;

	/// <summary><c>+0x23c</c> from the constructor: the distance a placed sound is heard to.</summary>
	public const uint DefaultHearingRange = 100_000;

	/// <summary><c>+0x240</c> from the constructor: the shift placed sounds' distances are taken with.</summary>
	public const byte DefaultDistanceShift = 10;

	/// <summary><c>+0x241</c> from the constructor: the master volume.</summary>
	public const byte DefaultMasterVolume = 90;

	/// <summary><c>+0x4fe</c> from the constructor: the limit of the bytes loaded.</summary>
	public const uint DefaultMemoryLimit = 300_000;

	/// <summary>The most <see cref="SetHearingRange"/> stores.</summary>
	public const uint HearingRangeLimit = 0x3fffc00;

	/// <summary><see cref="FindFreeSlot"/>'s answer with every slot taken.</summary>
	public const int NoSlot = 0xffff;

	private readonly Func<string, string, byte[]?>? _read;
	private readonly Es1SoundObject?[] _objects = new Es1SoundObject?[ObjectSlots];
	private readonly string?[] _names = new string?[ObjectSlots];
	private readonly uint[] _sizes = new uint[ObjectSlots];
	private readonly byte[] _references = new byte[ObjectSlots];
	private readonly byte[]?[] _data = new byte[ObjectSlots][];

	/// <summary>
	/// <c>SoundManager_Ctor</c> (<c>0004eb40</c>): no objects, the defaults above, loading allowed to unload other sounds,
	/// and the manager the driver's callbacks find (<c>SoundManager_Instance</c>, <c>000bd73e</c>), which here is the one
	/// whose objects started the voice.
	/// </summary>
	/// <param name="read">The volumes' reader, (folder, file name) to a file's bytes; none opens no name.</param>
	/// <param name="driver">The digital driver the objects play through.</param>
	public Es1SoundManager(Func<string, string, byte[]?>? read, Es1DigitalDriver driver) {
		_read = read;
		Driver = driver;
	}

	/// <summary>The digital driver the objects play through.</summary>
	public Es1DigitalDriver Driver { get; }

	/// <summary><c>+0x23c</c>: the distance a placed sound is heard to.</summary>
	public uint HearingRange { get; private set; } = DefaultHearingRange;

	/// <summary><c>+0x240</c>: the shift placed sounds' distances are taken with.</summary>
	public byte DistanceShift { get; private set; } = DefaultDistanceShift;

	/// <summary><c>+0x241</c>: the master volume, 0–100, which digital sounds are scaled by.</summary>
	public byte MasterVolume { get; private set; } = DefaultMasterVolume;

	/// <summary><c>+0x4f6</c>: the bytes loaded.</summary>
	public uint BytesLoaded { get; internal set; }

	/// <summary><c>+0x4fa</c>: the bytes loaded's high-water mark.</summary>
	public uint MostBytesLoaded { get; internal set; }

	/// <summary><c>+0x4fe</c>: the limit of the bytes loaded.</summary>
	public uint MemoryLimit { get; private set; } = DefaultMemoryLimit;

	/// <summary>
	/// <c>+0x502</c>: the highest name index, which <see cref="SetSampleNames"/> sets. The constructor leaves it as the
	/// allocation held it; here it is 0 until then.
	/// </summary>
	public byte HighestIndex { get; private set; }

	/// <summary><c>+0x503</c>, 1 from the constructor: loading past the limit may unload other sounds to make room.</summary>
	public bool MayUnloadToFit { get; set; } = true;

	/// <summary><c>+0x508</c>: the name at <paramref name="index"/>, none for an index out of the table.</summary>
	public string? Name(int index) => (uint)index < ObjectSlots ? _names[index] : null;

	/// <summary><c>+0x262</c>: the size recorded for <paramref name="index"/>.</summary>
	public uint Size(int index) => _sizes[index];

	/// <summary><c>+0x352</c>: the objects that hold <paramref name="index"/>'s data loaded.</summary>
	public byte References(int index) => _references[index];

	/// <summary><c>+0x38e</c>: <paramref name="index"/>'s loaded data, none while it is not loaded.</summary>
	public byte[]? Data(int index) => _data[index];

	/// <summary><c>SoundManager_GetObject</c> (<c>0004f660</c>)(manager, <paramref name="slot"/>): the slot's object.</summary>
	public Es1SoundObject? GetObject(int slot) => _objects[slot];

	/// <summary>
	/// <c>SoundManager_FindFreeSlot</c> (<c>0004f4b0</c>)(manager): the first slot holding no object, or
	/// <see cref="NoSlot"/>.
	/// </summary>
	public int FindFreeSlot() {
		int slot = Array.IndexOf(_objects, null);
		return slot < 0 ? NoSlot : slot;
	}

	/// <summary>
	/// <c>SoundManager_CreateObject</c> (<c>0004f4e8</c>)(manager, <paramref name="kind"/>, <paramref name="priority"/>,
	/// <paramref name="index"/>) (docs/es1/formats/sound-samples.md#sound-objects): none for an index above
	/// <see cref="HighestIndex"/> or with no slot free. The index's name, lower-cased in the table, decides the kind: one
	/// holding <c>.hmp</c> is a song, and one holding <c>.sfx</c> or <c>.raw</c> a digital sound, tested in that order;
	/// with no name, <paramref name="kind"/> 2 gives none. A song is an <see cref="Es1MidiSound"/>, any other kind an
	/// <see cref="Es1DigiSound"/>, in the free slot; its volume slot is called with its own volume and the priority stored.
	/// A negative index, which retail's signed test passes and reads before the table with, and one past
	/// <see cref="ObjectSlots"/>, which an empty table's <see cref="HighestIndex"/> of 255 lets through, give none, engine
	/// guards.
	/// </summary>
	public Es1SoundObject? CreateObject(int kind, byte priority, short index) {
		if (index < 0 || index > HighestIndex || index >= ObjectSlots) {
			return null;
		}

		string? name = _names[index];
		int slot = FindFreeSlot();
		if (slot == NoSlot) {
			return null;
		}

		if (string.IsNullOrEmpty(name)) {
			if (kind == 2) {
				return null;
			}

			name = null;
		} else {
			name = _names[index] = name.ToLowerInvariant();
			if (name.Contains(".hmp", StringComparison.Ordinal)) {
				kind = 1;
			}

			if (name.Contains(".sfx", StringComparison.Ordinal) || name.Contains(".raw", StringComparison.Ordinal)) {
				kind = 0;
			}
		}

		Es1SoundObject sound = kind == 1 ? new Es1MidiSound(this, (byte)slot, index, name) : new Es1DigiSound(this, (byte)slot, index, name);
		_objects[slot] = sound;
		sound.SetVolume((sbyte)sound.Volume);
		sound.Priority = priority;
		return sound;
	}

	/// <summary>
	/// <c>SoundManager_SetSampleNames</c> (<c>0004f888</c>)(manager, <paramref name="names"/>)
	/// (docs/es1/formats/sound-samples.md#naming-and-loading): the table, each name's index its position. For each, the
	/// size the reader gives, 0 for an empty name, and its data and reference count cleared; then
	/// <see cref="HighestIndex"/> the count less one. A name that does not open ends it there, leaving
	/// <see cref="HighestIndex"/> as it was. Retail clears the data pointers without freeing them or changing
	/// <see cref="BytesLoaded"/>; here the data is dropped. Names past <see cref="ObjectSlots"/>, which retail writes past
	/// its arrays, are not taken, an engine guard.
	/// </summary>
	public void SetSampleNames(IReadOnlyList<string> names) {
		Array.Clear(_names);
		int count = Math.Min(names.Count, ObjectSlots);
		for (int i = 0; i < count; i++) {
			_names[i] = names[i];
			if (names[i].Length == 0) {
				_sizes[i] = 0;
			} else if (Open(names[i]) is { } file) {
				_sizes[i] = (uint)file.Length;
			} else {
				return;
			}

			_data[i] = null;
			_references[i] = 0;
		}

		HighestIndex = unchecked((byte)(count - 1));
	}

	/// <summary>
	/// <c>SoundManager_SetSampleName</c> (<c>0004f99c</c>)(manager, <paramref name="index"/>, <paramref name="name"/>)
	/// (docs/es1/formats/sound-samples.md#naming-and-loading): false for no name, an index above
	/// <see cref="HighestIndex"/>, or a name, lower-cased, holding none of <c>.sfx</c>, <c>.raw</c> and <c>.hmp</c>, tested
	/// in that order so that <c>.hmp</c> wins. Each object on the index is stopped, unloaded, marked to load and given the
	/// name; one of another kind ends it with false, the objects before it already renamed. Then the name goes into the
	/// table and its size is recorded, true; false when it does not open, with the table and the objects already changed.
	/// A negative index, which retail's signed test passes, and one past <see cref="ObjectSlots"/> give false, engine
	/// guards.
	/// </summary>
	public bool SetSampleName(int index, string? name) {
		if (name == null || index < 0 || index > HighestIndex || index >= ObjectSlots) {
			return false;
		}

		name = name.ToLowerInvariant();
		int kind = 2;
		if (name.Contains(".sfx", StringComparison.Ordinal) || name.Contains(".raw", StringComparison.Ordinal)) {
			kind = 0;
		}

		if (name.Contains(".hmp", StringComparison.Ordinal)) {
			kind = 1;
		}

		if (kind == 2) {
			return false;
		}

		foreach (var sound in _objects) {
			if (sound == null || sound.Index != index) {
				continue;
			}

			if (sound.Kind != kind) {
				return false;
			}

			sound.Stop();
			sound.Unload();
			sound.Flags = (sound.Flags | Es1SoundObject.ToLoad) & ~Es1SoundObject.Loaded;
			sound.Name = name;
		}

		_names[index] = name;
		if (Open(name) is not { } file) {
			return false;
		}

		_sizes[index] = (uint)file.Length;
		_data[index] = null;
		return true;
	}

	/// <summary>
	/// <c>SoundManager_SetMasterVolume</c> (<c>0004fae8</c>)(manager, <paramref name="volume"/>): the volume held to
	/// 0–100, then each object's volume slot given its own volume, so that live voices take the new scale.
	/// </summary>
	public void SetMasterVolume(sbyte volume) {
		MasterVolume = (byte)Math.Clamp(volume, (sbyte)0, (sbyte)100);
		foreach (var sound in _objects) {
			sound?.SetVolume((sbyte)sound.Volume);
		}
	}

	/// <summary>
	/// <c>SoundManager_SetHearingRange</c> (<c>0004ffe0</c>)(manager, <paramref name="range"/>): held to
	/// <see cref="HearingRangeLimit"/>.
	/// </summary>
	public void SetHearingRange(uint range) => HearingRange = Math.Min(range, HearingRangeLimit);

	/// <summary><c>SoundManager_SetDistanceShift</c> (<c>0004fb48</c>)(manager, <paramref name="shift"/>).</summary>
	public void SetDistanceShift(byte shift) => DistanceShift = shift;

	/// <summary><c>SoundManager_SetMemoryLimit</c> (<c>00050008</c>)(manager, <paramref name="limit"/>).</summary>
	public void SetMemoryLimit(uint limit) => MemoryLimit = limit;

	/// <summary>
	/// <c>+0x242</c>: the listener's frame, its rotation and its position, as the last <see cref="UpdateListener"/> gave it;
	/// zero from the allocation until then.
	/// </summary>
	public Transform3 ListenerFrame { get; private set; }

	/// <summary>
	/// <c>SoundManager_UpdateListener</c> (<c>0004fb60</c>)(manager, <paramref name="frame"/>)
	/// (docs/es1/formats/sound-samples.md#placed-sounds): the frame kept, then each of the <see cref="ObjectSlots"/> placed
	/// as <see cref="UpdatePlacement"/> places one, whose steps its loop repeats inline.
	/// </summary>
	public void UpdateListener(Transform3 frame) {
		ListenerFrame = frame;
		for (int slot = 0; slot < ObjectSlots; slot++) {
			UpdatePlacement(slot);
		}
	}

	/// <summary>
	/// <c>SoundObject_UpdatePlacement</c> (<c>0004fdd8</c>)(manager, <paramref name="slot"/>)
	/// (docs/es1/formats/sound-samples.md#placed-sounds): a placed object in the slot faded by its distance from
	/// <see cref="ListenerFrame"/>'s position, to silence at <see cref="HearingRange"/>, both shifted right by
	/// <see cref="DistanceShift"/> when the range is above <c>0xffff</c>; and within the range panned by its direction's
	/// dot product with the frame's rotation of (<c>0xfff</c>, 0, 0), <c>0xffff</c> − 8 × (<i>p</i> + <c>0xfff</c>).
	/// </summary>
	internal void UpdatePlacement(int slot) {
		int range = (int)HearingRange;
		int shift = 0;
		if (range > 0xffff) {
			shift = DistanceShift & 0x1f;
			range >>= shift;
		}

		if (_objects[slot] is not { Placed: true } sound) {
			return;
		}

		var frame = ListenerFrame;
		int x = unchecked(sound.Position.X - frame.X) >> shift;
		int y = unchecked(sound.Position.Y - frame.Y) >> shift;
		int z = unchecked(sound.Position.Z - frame.Z) >> shift;
		int distance = SimMath.FastMagnitude3D(x, y, z);
		if (distance == 0) {
			distance = 1;
		}

		if (distance >= range) {
			sound.SetPlacedVolume(0);
			return;
		}

		sound.SetPlacedVolume((sbyte)(sound.Volume - distance * sound.Volume / range));
		x = unchecked(x * PanAxis) / distance;
		y = unchecked(y * PanAxis) / distance;
		z = unchecked(z * PanAxis) / distance;
		var axis = frame.RotateVector(PanAxis, 0, 0);
		int dot = unchecked(axis.X * x + axis.Y * y + axis.Z * z);
		sound.SetPan(unchecked((ushort)(0xffff - (dot / PanAxis + PanAxis) * 8)));
	}

	/// <summary>The length the placement scales a direction to, and the axis it turns by the listener's rotation.</summary>
	private const int PanAxis = 0xfff;

	/// <summary>
	/// <see cref="Es1Sound.Stereo"/>'s listener: the camera's position and its view's rotation, as the last
	/// <see cref="UpdateStereoListener"/> gave them; not a retail field.
	/// </summary>
	public Transform3 StereoListener { get; private set; } = Transform3.Identity;

	/// <summary>
	/// <see cref="Es1Sound.Stereo"/>'s listener, from the camera every frame: the frame of <paramref name="view"/>'s
	/// angles at <paramref name="position"/>, then each placed object panned against it (<see cref="PanStereo"/>), a voice
	/// already playing among them. Not retail's: the stereo tweak's.
	/// </summary>
	public void UpdateStereoListener(Vec3i position, (short X, short Y, short Z) view) {
		var frame = Transform3.FromEuler(view.X, view.Y, view.Z);
		frame.X = position.X;
		frame.Y = position.Y;
		frame.Z = position.Z;
		StereoListener = frame;
		foreach (var sound in _objects) {
			if (sound is { Placed: true }) {
				PanStereo(sound);
			}
		}
	}

	/// <summary>
	/// <see cref="Es1Sound.Stereo"/>'s pan of a placed <paramref name="sound"/>: the cosine of the angle between its
	/// offset from <see cref="StereoListener"/>'s position and the listener's x axis, the view's right, so that a sound
	/// straight to the right is 1, one straight to the left −1, and one ahead, behind, above, below or at the listener 0;
	/// given to the sound's voice when it plays (<see cref="Es1DigitalDriver.SetPan"/>). The offset is taken unshifted, so
	/// that a sound by the camera, the player's own guns and footfalls, keeps its true side where retail's placement rounds
	/// it to a whole axis. Not retail's: the stereo tweak's.
	/// </summary>
	internal void PanStereo(Es1SoundObject sound) {
		var frame = StereoListener;
		double x = (double)sound.Position.X - frame.X;
		double y = (double)sound.Position.Y - frame.Y;
		double z = (double)sound.Position.Z - frame.Z;
		double length = Math.Sqrt(x * x + y * y + z * z);
		var right = frame.RotateVector(0x4000, 0, 0);
		double rightLength = Math.Sqrt((double)right.X * right.X + (double)right.Y * right.Y + (double)right.Z * right.Z);
		float pan = length == 0 || rightLength == 0
			? 0
			: (float)Math.Clamp((x * right.X + y * right.Y + z * right.Z) / (length * rightLength), -1, 1);
		sound.StereoPan = pan;
		if (sound.Kind == 0 && sound.Handle != -1) {
			Driver.SetPan(sound.Handle, pan);
		}
	}

	/// <summary>
	/// The defaults block (<c>000959ec</c>) as <c>Sound_Init</c> leaves it, which <see cref="LoadDrivers"/> fills the
	/// driver blocks from; the image's values until then.
	/// </summary>
	public Es1SoundDevices Devices { get; set; } = Es1SoundDevices.ImageDefaults;

	/// <summary><c>+0x13a</c>: <c>sos\melodic.bnk</c>, read whole by <see cref="LoadBanks"/>.</summary>
	public byte[]? MelodicBank { get; private set; }

	/// <summary><c>+0x140</c>: <c>sos\drum32.dig</c>, read whole by <see cref="LoadBanks"/>.</summary>
	public byte[]? Drum32Dig { get; private set; }

	/// <summary><c>+0x146</c>: <c>sos\drum.bnk</c>, read whole by <see cref="LoadBanks"/>.</summary>
	public byte[]? DrumBank { get; private set; }

	/// <summary><c>SoundManager_DriversLoaded</c> (<c>000bd8cc</c>): set by <see cref="LoadDrivers"/> when it succeeds.</summary>
	public bool DriversLoaded { get; private set; }

	/// <summary>
	/// <c>SoundManager_LoadDrivers</c> (<c>0004ef8c</c>)(manager) (docs/es1/formats/sound-samples.md#the-driver):
	/// <see cref="DriversLoaded"/> cleared, false unless <see cref="LoadBanks"/> succeeds, then the drivers opened and
	/// <see cref="DriversLoaded"/> set, true.
	///
	/// <para>PLACEHOLDER: the HMI calls between, which item 7's modules D and E port (docs/herculan/plan-es1-first-mission.md):
	/// <c>0006d4f5</c>(0, 1); the timer callback <c>0004e710</c> and its slot <c>+0x134</c>; the MIDI device
	/// <see cref="Es1SoundDevices.MidiDevice"/> opened with <c>HmiMidi_InitDriver</c> and the two banks loaded into it; the
	/// digital driver, hardware and init blocks filled from <see cref="Devices"/>; device <c>0xa005</c> opened with
	/// <see cref="Drum32Dig"/>; and <c>0007225a</c>(0). The stand-in <see cref="Driver"/> plays without them, and none of
	/// their failures, each of which formats its message into a stack buffer and answers false, is reached.</para>
	/// </summary>
	public bool LoadDrivers() {
		DriversLoaded = false;
		if (!LoadBanks()) {
			return false;
		}

		DriversLoaded = true;
		return true;
	}

	/// <summary>
	/// <c>SoundManager_LoadBanks</c> (<c>0004f6d4</c>)(manager) (docs/es1/formats/sound-samples.md#the-banks):
	/// <c>sos\melodic.bnk</c>, <c>sos\drum.bnk</c> and <c>sos\drum32.dig</c> read whole, in that order, into
	/// <see cref="MelodicBank"/>, <see cref="DrumBank"/> and <see cref="Drum32Dig"/>; false at the first that does not open.
	/// </summary>
	public bool LoadBanks() {
		if (Open(@"sos\melodic.bnk") is not { } melodic) {
			return false;
		}

		MelodicBank = melodic;
		if (Open(@"sos\drum.bnk") is not { } drum) {
			return false;
		}

		DrumBank = drum;
		if (Open(@"sos\drum32.dig") is not { } dig) {
			return false;
		}

		Drum32Dig = dig;
		return true;
	}

	/// <summary>
	/// <c>SoundManager_UnloadDrivers</c> (<c>0004f3d4</c>)(manager): while <see cref="DriversLoaded"/> is set, every object
	/// of the <see cref="ObjectSlots"/> stopped through its slot <c>+0x14</c>, then <see cref="DriversLoaded"/> cleared.
	/// PLACEHOLDER: the HMI calls between, <c>00071515</c> and <c>0007144b</c> on both MIDI handles, the timer callback's
	/// removal, <c>00071064</c>, <c>000705c5</c> and <c>0006fe28</c>, are item 7's modules D and E.
	/// </summary>
	public void UnloadDrivers() {
		if (!DriversLoaded) {
			return;
		}

		foreach (var sound in _objects) {
			sound?.Stop();
		}

		DriversLoaded = false;
	}

	/// <summary>
	/// <c>DigiSound_DoneCallback</c> (<c>0004e7fc</c>)(<i>a</i>, <i>b</i>, <paramref name="code"/>, <paramref name="voice"/>),
	/// the start block's callback: with event 2, the first digital object whose voice is <paramref name="voice"/> left
	/// with none.
	/// </summary>
	internal void DigiDoneCallback(int code, int voice) {
		if (code != 2) {
			return;
		}

		foreach (var sound in _objects) {
			if (sound != null && sound.Kind == 0 && sound.Handle == voice) {
				sound.Handle = -1;
				sound.HandleCopy = 0xffff;
				return;
			}
		}
	}

	/// <summary>The slot's object cleared, as <c>SoundObject_Dtor</c> (<c>000500d8</c>) clears its own.</summary>
	internal void ClearSlot(int slot) => _objects[slot] = null;

	/// <summary>
	/// <c>SoundObject_LoadSample</c>'s read (docs/es1/formats/sound-samples.md#naming-and-loading): the object's
	/// <paramref name="name"/>, which retail reads rather than the table's, as <paramref name="index"/>'s data, one
	/// reference counted, <see cref="BytesLoaded"/> raised by the index's size on the first and
	/// <see cref="MostBytesLoaded"/> after it.
	/// </summary>
	internal bool Load(int index, string name) {
		if (Open(name) is not { } file) {
			return false;
		}

		_data[index] = file;
		if (++_references[index] == 1) {
			BytesLoaded += _sizes[index];
		}

		MostBytesLoaded = Math.Max(MostBytesLoaded, BytesLoaded);
		return true;
	}

	/// <summary>
	/// <c>SoundObject_Unload</c>'s count (docs/es1/formats/sound-samples.md#sound-objects): one reference off
	/// <paramref name="index"/>, and at none its data freed and its size off <see cref="BytesLoaded"/>.
	/// </summary>
	internal void Release(int index) {
		if (--_references[index] == 0) {
			_data[index] = null;
			BytesLoaded -= _sizes[index];
		}
	}

	/// <summary>
	/// <c>SoundObject_LoadSample</c>'s choice of a sound to unload: the loaded object whose priority is not 0 and is the
	/// highest, at least <paramref name="priority"/>, and that does not play, ties going to the larger size. Retail reads
	/// that size as the size table's entry at the object's <i>slot</i>, not its name index; the two agree for the catalog's
	/// objects, which take the slots in their rows' order. None when no object qualifies.
	/// </summary>
	internal Es1SoundObject? ChooseToUnload(byte priority) {
		Es1SoundObject? chosen = null;
		uint size = 0;
		for (int slot = 0; slot < ObjectSlots; slot++) {
			var sound = _objects[slot];
			if (sound == null || (sound.Flags & Es1SoundObject.Loaded) == 0 || sound.Priority == 0 || sound.IsPlaying()) {
				continue;
			}

			if (priority < sound.Priority || (sound.Priority == priority && size < _sizes[slot])) {
				priority = sound.Priority;
				size = _sizes[slot];
				chosen = sound;
			}
		}

		return chosen;
	}

	/// <summary><paramref name="name"/>'s bytes through the reader, split at the last backslash; none when it does not open.</summary>
	private byte[]? Open(string name) {
		int cut = name.LastIndexOf('\\');
		return cut < 0 ? _read?.Invoke("", name) : _read?.Invoke(name[..cut], name[(cut + 1)..]);
	}
}
