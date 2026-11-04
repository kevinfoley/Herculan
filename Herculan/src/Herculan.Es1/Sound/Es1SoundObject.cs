namespace Herculan.Es1.Sound;

/// <summary>
/// A sound object (docs/es1/formats/sound-samples.md#sound-objects): one name played through the manager, built by
/// <c>SoundObject_Ctor</c> (<c>00050020</c>) under <see cref="Es1DigiSound"/> or <see cref="Es1MidiSound"/>. Its fields
/// carry their offsets; the virtual members are the vtable's slots that calls go through, with the base's stubs
/// (<c>SoundObjectVtable</c>, <c>00095c46</c>) as their defaults. Slots <c>+0x0c</c> and <c>+0x18</c>–<c>+0x24</c>,
/// which the program's callers do not reach (docs/es1/formats/sound-samples.md#the-vtables), are not ported.
/// </summary>
public abstract class Es1SoundObject {
	/// <summary>Bit 0 of <see cref="Flags"/>: the name's data is loaded for this object.</summary>
	public const int Loaded = 1;

	/// <summary>Bit 1 of <see cref="Flags"/>: the object has a name to load.</summary>
	public const int ToLoad = 2;

	private protected Es1SoundObject(Es1SoundManager manager, byte slot, short index, string? name, byte kind) {
		Manager = manager;
		Slot = slot;
		Index = index;
		Name = name;
		Kind = kind;
		Flags = name != null ? ToLoad : 0;
	}

	/// <summary><c>+0x00</c>: the name.</summary>
	public string? Name { get; internal set; }

	/// <summary><c>+0x08</c>: the object slot.</summary>
	public byte Slot { get; }

	/// <summary><c>+0x09</c>: the name index.</summary>
	public int Index { get; }

	/// <summary><c>+0x0d</c>: <see cref="Loaded"/> and <see cref="ToLoad"/>.</summary>
	public int Flags { get; internal set; }

	/// <summary><c>+0x11</c>: the manager.</summary>
	public Es1SoundManager Manager { get; }

	/// <summary><c>+0x15</c>: set for a sound placed in the world.</summary>
	public bool Placed { get; internal set; }

	/// <summary><c>+0x16</c>, 0 from the constructor, which <c>Snc_Load</c> clears.</summary>
	public byte Byte16 { get; set; }

	/// <summary><c>+0x17</c>: 1 for a song, 0 for a digital sound.</summary>
	public byte Kind { get; }

	/// <summary><c>+0x18</c>: plays even at volume 0.</summary>
	public bool PlaysSilent { get; set; }

	/// <summary><c>+0x19</c>: the voice or song handle, −1 when none.</summary>
	public int Handle { get; internal set; } = -1;

	/// <summary><c>+0x1d</c>: the handle's copy, <c>0xffff</c> when none.</summary>
	public ushort HandleCopy { get; internal set; } = 0xffff;

	/// <summary><c>+0x1f</c>: the pan a placed sound plays with.</summary>
	public ushort Pan { get; internal set; } = 0x8000;

	/// <summary><c>+0x21</c>: the repeat count passed to the driver.</summary>
	public short RepeatCount { get; internal set; }

	/// <summary><c>+0x23</c>: the playing sound's repeat count.</summary>
	public short PlayingRepeatCount { get; internal set; }

	/// <summary><c>+0x31</c>: the volume, 0–100.</summary>
	public byte Volume { get; internal set; } = 100;

	/// <summary><c>+0x32</c>: the placed sound's volume.</summary>
	public short PlacedVolume { get; internal set; }

	/// <summary><c>+0x34</c>: the priority loading evicts by, 0 never; <c>0xff</c> until the manager stores its own.</summary>
	public byte Priority { get; internal set; } = 0xff;

	/// <summary>Slot <c>+0x04</c>, (volume): a placed sound's volume. The base's stub does nothing.</summary>
	public virtual void SetPlacedVolume(sbyte volume) {
	}

	/// <summary>
	/// Slot <c>+0x08</c>, the destructor: <c>SoundObject_Dtor</c> (<c>000500d8</c>), the sample unloaded and the manager's
	/// slot cleared. The classes stop the sound first.
	/// </summary>
	public virtual void Destroy() {
		Unload();
		Manager.ClearSlot(Slot);
	}

	/// <summary>Slot <c>+0x10</c>: play. The base's stub answers false.</summary>
	public virtual bool Play() => false;

	/// <summary>Slot <c>+0x14</c>: stop. The base's stub does nothing.</summary>
	public virtual void Stop() {
	}

	/// <summary>
	/// Slot <c>+0x28</c>, (<paramref name="start"/>, <paramref name="end"/>, <paramref name="count"/>):
	/// <c>SoundObject_SetRepeatCount</c> (<c>00050470</c>), the count to <see cref="PlayingRepeatCount"/> while the sound
	/// plays, else to <see cref="RepeatCount"/>.
	/// </summary>
	public virtual void SetRepeats(int start, int end, short count) {
		if (IsPlaying()) {
			PlayingRepeatCount = count;
		} else {
			RepeatCount = count;
		}
	}

	/// <summary>Slot <c>+0x2c</c>, (volume). The base's stub does nothing.</summary>
	public virtual void SetVolume(sbyte volume) {
	}

	/// <summary>Slot <c>+0x30</c>: plays. The base's stub answers false.</summary>
	public virtual bool IsPlaying() => false;

	/// <summary>
	/// <c>SoundObject_Unload</c> (<c>00050164</c>): the sound stopped when it plays; when loaded, one reference off its
	/// name's data, freed at none with its size off the bytes loaded; then the loaded bit cleared.
	/// </summary>
	public void Unload() {
		if (IsPlaying()) {
			Stop();
		}

		if ((Flags & Loaded) != 0) {
			Manager.Release(Index);
			Flags &= ~Loaded;
		}
	}

	/// <summary>
	/// <c>SoundObject_LoadSample</c> (<c>000501ec</c>) (docs/es1/formats/sound-samples.md#naming-and-loading): false without
	/// a name to load, true at once when loaded. When the bytes loaded plus the name's size would pass the limit, false
	/// unless the manager may unload to fit; then one sound at a time is unloaded, the manager's choice for this object's
	/// priority, until the sample fits, false when none qualifies. Then the object's name is read whole into its index's
	/// data, one reference counted, and the object marked loaded.
	/// </summary>
	public bool LoadSample() {
		if ((Flags & ToLoad) == 0) {
			return false;
		}

		if ((Flags & Loaded) != 0) {
			return true;
		}

		uint size = Manager.Size(Index);
		if (Manager.MemoryLimit < unchecked(Manager.BytesLoaded + size)) {
			if (!Manager.MayUnloadToFit) {
				return false;
			}

			while (Manager.MemoryLimit < unchecked(Manager.BytesLoaded + size)) {
				if (Manager.ChooseToUnload(Priority) is not { } other) {
					return false;
				}

				other.Unload();
			}
		}

		if (Name == null || !Manager.Load(Index, Name)) {
			return false;
		}

		Flags |= Loaded;
		return true;
	}

	/// <summary>
	/// <c>SoundObject_GetVolume</c> (<c>0005049c</c>): <see cref="Volume"/>, or for a placed sound
	/// <see cref="PlacedVolume"/> × 10,000 / the master volume, times <c>0x7fff</c> where the inverse of
	/// <see cref="Es1DigiSound.SetPlacedVolume"/> divides, so that only a placed volume of 0 reads below 100. A master
	/// volume of 0, which retail divides by, reads 0, an engine guard.
	/// </summary>
	public uint GetVolume() {
		if (!Placed) {
			return Volume;
		}

		if (Manager.MasterVolume == 0) {
			return 0;
		}

		return unchecked((uint)((int)((ushort)PlacedVolume * 10000) / Manager.MasterVolume * 0x7fff));
	}

	/// <summary>
	/// <c>SoundObject_IsCategoryEnabled</c> (<c>0004de50</c>)(object): whether the object's kind is switched on, the songs'
	/// switch for a song and the digital sounds' for a digital sound.
	/// </summary>
	public bool IsCategoryEnabled(bool musicEnabled, bool effectsEnabled) => Kind != 0 ? musicEnabled : effectsEnabled;
}

/// <summary>
/// A digital sound (docs/es1/formats/sound-samples.md#playing): <c>DigiSound</c>, <c>0x50</c> bytes, vtable
/// <c>DigiSoundVtable</c> (<c>00095cd6</c>), built by <c>DigiSound_Ctor</c> (<c>000507c8</c>).
/// </summary>
public sealed class Es1DigiSound : Es1SoundObject {
	/// <summary>The busy voices past which <see cref="Play"/> stops a sound to make room.</summary>
	public const int VoiceRoom = 30;

	internal Es1DigiSound(Es1SoundManager manager, byte slot, short index, string? name)
		: base(manager, slot, index, name, 0) {
	}

	/// <summary><c>+0x3f</c>, 2: the start block's <c>+0x10</c>.</summary>
	public int Word3F { get; internal set; } = 2;

	/// <summary><c>+0x48</c>: the loop range's end, −1 for none.</summary>
	public int LoopEnd { get; internal set; } = -1;

	/// <summary><c>+0x4c</c>: the loop range's start, −1 for none.</summary>
	public int LoopStart { get; internal set; } = -1;

	/// <summary>
	/// Slot <c>+0x04</c>, <c>DigiSound_SetPlacedVolume</c> (<c>00050954</c>): <see cref="Es1SoundObject.PlacedVolume"/> =
	/// <paramref name="volume"/> × <c>0x7fff</c> × the master volume / 10,000, signed, given to a live voice.
	/// </summary>
	public override void SetPlacedVolume(sbyte volume) {
		int value = volume * 0x7fff * Manager.MasterVolume / 10000;
		PlacedVolume = (short)value;
		if (Handle != -1) {
			Manager.Driver.SetSampleVolume(Handle, (ushort)value);
		}
	}

	/// <summary>Slot <c>+0x08</c>, <c>DigiSound_Dtor</c> (<c>00050838</c>): the sound stopped, then the base's.</summary>
	public override void Destroy() {
		Stop();
		base.Destroy();
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>DigiSound_Play</c> (<c>000509c4</c>) (docs/es1/formats/sound-samples.md#playing): past
	/// <see cref="VoiceRoom"/> busy voices, the playing digital sound whose <see cref="Es1SoundObject.GetVolume"/> is the
	/// lowest below 100 stopped, false with none; the sample loaded, false when that fails; the volume scaled by the
	/// master volume, or the placed one, and at 0 without <see cref="Es1SoundObject.PlaysSilent"/> true without playing;
	/// then the start block given to the driver, keeping its voice, false when none is free.
	/// </summary>
	public override bool Play() {
		var driver = Manager.Driver;
		if (driver.CountBusyVoices() > VoiceRoom) {
			Es1SoundObject? quietest = null;
			uint lowest = 100;
			for (int slot = 0; slot < Es1SoundManager.ObjectSlots; slot++) {
				if (Manager.GetObject(slot) is { Kind: 0 } sound && sound.IsPlaying() && sound.GetVolume() < lowest) {
					lowest = sound.GetVolume();
					quietest = sound;
				}
			}

			if (quietest == null) {
				return false;
			}

			quietest.Stop();
		}

		if (!LoadSample()) {
			return false;
		}

		ushort volume = Placed ? (ushort)PlacedVolume : (ushort)((uint)(Volume * 0x7fff * Manager.MasterVolume) / 10000);
		if (volume == 0 && !PlaysSilent) {
			return true;
		}

		int flags = Placed ? 0x4300 : 0x4100;
		int loopEnd = 0, loopStart = 0;
		PlayingRepeatCount = RepeatCount;
		if (RepeatCount != 0 && LoopEnd != -1 && LoopStart != -1) {
			loopEnd = LoopEnd;
			loopStart = LoopStart;
			flags = (flags & ~0xff) | Es1DigitalDriver.LoopRangeFlag;
		}

		var block = new Es1SampleStart(Manager.Data(Index) ?? [], Name ?? "", (int)Manager.Size(Index), (ushort)RepeatCount,
			Word3F, volume, 0x1000 + Slot, Manager.DigiDoneCallback, flags, loopEnd, loopStart, Placed ? Pan : (ushort)0);
		HandleCopy = Slot;
		Handle = driver.StartSample(block);
		if (Handle == -1) {
			HandleCopy = 0xffff;
			return false;
		}

		return true;
	}

	/// <summary>
	/// Slot <c>+0x14</c>, <c>DigiSound_Stop</c> (<c>00050c88</c>): a live voice stopped, and the object left with none.
	/// </summary>
	public override void Stop() {
		if (Handle != -1) {
			Manager.Driver.StopSample(Handle);
			Handle = -1;
			HandleCopy = 0xffff;
		}
	}

	/// <summary>
	/// Slot <c>+0x28</c>, <c>DigiSound_SetRepeats</c> (<c>00050cbc</c>)(<paramref name="start"/>, <paramref name="end"/>,
	/// <paramref name="count"/>): while it plays, the count to <see cref="Es1SoundObject.PlayingRepeatCount"/> and to the
	/// voice's repeats; else to both counts, with the range.
	/// </summary>
	public override void SetRepeats(int start, int end, short count) {
		PlayingRepeatCount = count;
		if (IsPlaying()) {
			Manager.Driver.SetVoiceRepeats(Handle, count);
		} else {
			RepeatCount = count;
			LoopEnd = end;
			LoopStart = start;
		}
	}

	/// <summary>
	/// Slot <c>+0x2c</c>, <c>DigiSound_SetVolume</c> (<c>000508c4</c>): below 0, the volume 0; else held to 100; a live
	/// voice given it × <c>0x7fff</c> × the master volume / 10,000.
	/// </summary>
	public override void SetVolume(sbyte volume) {
		Volume = (byte)Math.Clamp(volume, (sbyte)0, (sbyte)100);
		if (Handle != -1) {
			Manager.Driver.SetSampleVolume(Handle, (ushort)((uint)(Volume * 0x7fff * Manager.MasterVolume) / 10000));
		}
	}

	/// <summary>
	/// Slot <c>+0x30</c>, <c>DigiSound_IsPlaying</c> (<c>00050c18</c>): with a voice, whether the driver has it busy.
	/// </summary>
	public override bool IsPlaying() => Handle != -1 && Manager.Driver.IsVoiceDone(Handle) == 0;
}

/// <summary>
/// A song (docs/es1/formats/hmp-music.md#loading-and-playing): <c>MidiSound</c>, <c>0xcf</c> bytes, vtable
/// <c>MidiSoundVtable</c> (<c>00095c82</c>), built by <c>MidiSound_Ctor</c> (<c>000504dc</c>).
///
/// <para>PLACEHOLDER: the HMI song player is item 7's module D (docs/herculan/plan-es1-first-mission.md), so a song plays
/// nothing: <see cref="Play"/> loads the file, which counts against the manager's limit as retail's does, and starts no
/// song, leaving no handle.</para>
/// </summary>
public sealed class Es1MidiSound : Es1SoundObject {
	/// <summary>The entries of the track map at <c>+0x3f</c>.</summary>
	public const int TrackMapEntries = 32;

	internal Es1MidiSound(Es1SoundManager manager, byte slot, short index, string? name)
		: base(manager, slot, index, name, 1) {
		Array.Fill(TrackMap, 0xff);
	}

	/// <summary><c>+0x3f</c>: the track map, <c>0xff</c> each from the constructor.</summary>
	public int[] TrackMap { get; } = new int[TrackMapEntries];

	/// <summary>Slot <c>+0x08</c>, <c>MidiSound_Dtor</c> (<c>0005055c</c>): the song stopped, then the base's.</summary>
	public override void Destroy() {
		Stop();
		base.Destroy();
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>MidiSound_Play</c> (<c>000505c8</c>): false while it plays or when the file does not load.
	/// PLACEHOLDER: the song's start, <c>HmiMidi_InitSong</c> and its callbacks, is module D's; false, with no handle.
	/// </summary>
	public override bool Play() {
		if (!IsPlaying()) {
			LoadSample();
		}

		return false;
	}

	/// <summary>
	/// Slot <c>+0x14</c>, <c>MidiSound_Stop</c> (<c>000506f4</c>): with a song, the object left with none. PLACEHOLDER: the
	/// song player's two calls, <c>0006ead9</c> and <c>0006ea3d</c>, are module D's.
	/// </summary>
	public override void Stop() {
		if (Handle != -1) {
			Handle = -1;
			HandleCopy = 0xffff;
		}
	}

	/// <summary>
	/// Slot <c>+0x2c</c>, <c>MidiSound_SetVolume</c> (<c>0005075c</c>): below 0, the volume 0; else held to 100.
	/// PLACEHOLDER: the songs' volume it then sets, <c>0006edb3</c>(volume × <c>0x7f</c> × the master volume / 10,000),
	/// is module D's.
	/// </summary>
	public override void SetVolume(sbyte volume) => Volume = (byte)Math.Clamp(volume, (sbyte)0, (sbyte)100);

	/// <summary>
	/// Slot <c>+0x30</c>, <c>MidiSound_IsPlaying</c> (<c>00050730</c>): with a song, whether the player has it.
	/// PLACEHOLDER: no song starts until module D, so the handle is always −1 and the answer false.
	/// </summary>
	public override bool IsPlaying() => Handle != -1;
}
