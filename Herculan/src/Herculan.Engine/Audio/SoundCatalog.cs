using Herculan.Engine.Content;
using HercWorks.Core.Data.File;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Audio;

/// <summary>
/// <c>str\SOUNDS.STR</c> — the table that turns the simulation's integer sound ids into a filename
/// and a set of playback rules. <c>SoundCatalog_Load</c> (<c>00462448</c>) is what this ports.
///
/// <para>The file is an ordinary <c>.STR</c> string table with one group of 57 entries, each a
/// <c>.wav</c> name plus a seven-byte attribute blob; <see cref="StringFile"/> already does that
/// parse. What this type adds is the meaning of the seven bytes, the two <c>0xff</c> defaults the
/// loader patches in, and the three further bytes the original keeps as runtime scratch.</para>
///
/// <para><b>The runtime bytes.</b> The original reads a ten-byte record where the file supplies
/// seven, so bytes 7-9 land in the loaded file buffer past the authored data. They are per-entry
/// mutable state — a suspend/resume flag, a category volume percentage and a play-request counter — and
/// live here as ordinary fields on <see cref="Entry"/> rather than as bytes off the end of a
/// buffer.</para>
/// </summary>
public sealed class SoundCatalog {
	/// <summary>The resource this is read from.</summary>
	public const string ResourceName = "SOUNDS.STR";

	/// <summary>What <c>0xff</c> in the rolloff-start byte means.</summary>
	public const byte DefaultMinRange = 5;

	/// <summary>What <c>0xff</c> in the cutoff byte means.</summary>
	public const byte DefaultMaxRange = 100;

	/// <summary>
	/// World units one unit of <see cref="Entry.MinRange"/> / <see cref="Entry.MaxRange"/> is worth.
	/// <c>Sound_Place</c> shifts both left by ten.
	/// </summary>
	public const int RangeUnit = 1024;

	/// <summary>
	/// The headroom trim every volume goes through — <c>Math_Q16Multiply(volume, 65000)</c>, so a
	/// stored 100 becomes 99. Kept because it is what the original computes, not because it matters.
	/// </summary>
	public const int VolumeTrim = 65000;

	private readonly Entry[] _entries;

	private SoundCatalog(Entry[] entries) {
		_entries = entries;
	}

	/// <summary>One catalog row: the authored attributes, plus the original's runtime scratch.</summary>
	public sealed class Entry {
		internal Entry(int id, string fileName, byte[] attributes) {
			Id = id;
			FileName = fileName;

			// A retail file leaves the four trailing entries with no attribute bytes at all; they are
			// padding and are never opened. Treat any short blob the same way rather than indexing
			// past it.
			if (attributes.Length < 7) {
				return;
			}

			LoopCount = attributes[0];
			Volume = attributes[1];
			Preload = attributes[2] != 0;
			RequestsPerPlay = attributes[3];
			MinRange = attributes[4] == 0xff ? DefaultMinRange : attributes[4];
			MaxRange = attributes[5] == 0xff ? DefaultMaxRange : attributes[5];
			VariationCount = Math.Max((byte)1, attributes[6]);
			HasAttributes = true;
		}

		/// <summary>This row's catalog id.</summary>
		public int Id { get; }

		/// <summary>The <c>.wav</c> name, without a directory.</summary>
		public string FileName { get; }

		/// <summary>False for a padding row the original never opens.</summary>
		public bool HasAttributes { get; }

		/// <summary>
		/// Attribute byte 0, a repeat <b>count</b>: 0 plays forever, 1 plays once, n plays n times.
		/// Handed to <c>Sfx_SetLooping</c>, which is not a boolean despite the name.
		/// </summary>
		public byte LoopCount { get; }

		/// <summary>Attribute byte 1 — the authored volume, 0-100.</summary>
		public byte Volume { get; internal set; }

		/// <summary>Attribute byte 2 — cache the sample at startup rather than on first play.</summary>
		public bool Preload { get; }

		/// <summary>
		/// Attribute byte 3 — how many play requests it takes to sound this row once, so that a sound
		/// fired by many objects in the same tick is heard once rather than once per object. See
		/// <see cref="SoundDirector.ConsumeRequest"/>, which scales it by the detail setting.
		/// </summary>
		public byte RequestsPerPlay { get; }

		/// <summary>Attribute byte 4 — where rolloff starts, in <see cref="RangeUnit"/>s.</summary>
		public byte MinRange { get; }

		/// <summary>Attribute byte 5 — beyond this the sound is not played at all.</summary>
		public byte MaxRange { get; }

		/// <summary>
		/// Attribute byte 6 — how many consecutive ids this one stands for. Playing it actually plays
		/// <c>id + rand(count)</c>, which is how <c>impacts2</c>/<c>3</c>/<c>5</c> vary shot to shot
		/// off a single id.
		/// </summary>
		public byte VariationCount { get; } = 1;

		/// <summary>Whether this row is positional at all — it is, unless both ranges defaulted.</summary>
		public bool IsPositional => MaxRange > 0;

		/// <summary>Runtime byte 7 — set by a suspend so the matching resume knows what to restart.</summary>
		public bool WasPlaying { get; internal set; }

		/// <summary>
		/// Runtime byte 8 — a per-sound category scale in percent, initialised to 100 by the loader
		/// and multiplied through every volume this row computes.
		/// </summary>
		public byte CategoryVolume { get; internal set; } = 100;

		/// <summary>Runtime byte 9 — the play requests counted against <see cref="RequestsPerPlay"/>; wraps at 0x0f.</summary>
		public byte RequestCount { get; internal set; }

		/// <inheritdoc />
		public override string ToString() =>
			$"0x{Id:x2} {FileName} vol={Volume} loop={LoopCount} range={MinRange}-{MaxRange}";
	}

	/// <summary>Every row, indexed by catalog id.</summary>
	public IReadOnlyList<Entry> Entries => _entries;

	/// <summary>How many rows the file held.</summary>
	public int Count => _entries.Length;

	/// <summary>Row <paramref name="id"/>, or null when the id is outside the table.</summary>
	public Entry? this[int id] => id >= 0 && id < _entries.Length ? _entries[id] : null;

	/// <summary>Whether <paramref name="id"/> falls in the music half of the catalog.</summary>
	public static bool IsMusic(int id) => id < SoundId.FirstEffect;

	/// <summary>
	/// Reads the catalog out of the mounted archives, or null when <c>SOUNDS.STR</c> is absent or
	/// does not parse.
	/// </summary>
	public static SoundCatalog? Load(GameContent content) =>
		SimStrings.Load(content, ResourceName) is { } table ? FromTable(table) : null;

	/// <summary>
	/// Builds the catalog from an already-parsed <c>.STR</c>. The file holds exactly one group, so
	/// anything else is treated as the wrong file rather than read anyway.
	/// </summary>
	public static SoundCatalog? FromTable(StringFile table) {
		if (table.GroupCount != 1) {
			return null;
		}

		var group = table.Group(0);
		var entries = new Entry[group.Count];
		for (int i = 0; i < group.Count; i++) {
			entries[i] = new Entry(i, group[i].Text, group[i].Attributes);
		}

		return new SoundCatalog(entries);
	}
}
