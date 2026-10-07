using Herculan.Engine.Content;

namespace Herculan.Engine.Audio;

/// <summary>
/// The catalog together with the samples it names — <c>SoundCatalog_Load</c>'s other half, which
/// resolves each row's filename to a resource and opens it.
///
/// <para><c>Sound_ResolveSamplePath</c> (<c>00462238</c>) is the whole of the path rule: the
/// catalog stores a bare filename and the loader prefixes it with <c>HMI\</c>, or with <c>HMX\</c>
/// when the low-memory mode is on. <c>SIMSOUND.VOL</c> carries both banks — <c>hmx</c> being the
/// same recordings as 8-bit mono 11,025 Hz (docs/retail/formats/audio.md, "Sample banks") — so
/// <see cref="LowMemoryBank"/> selects between them the same way.</para>
///
/// <para>Unlike the original this loads every named sample up front instead of honouring the
/// per-row preload attribute and caching the rest on first play. The whole <c>hmi</c> bank is about
/// 1.5 MB of 8-bit PCM, under the original's own 2,000,000-byte cache budget even after widening to
/// 16-bit, so the eviction machinery that budget exists to drive has nothing to do. The attribute
/// is still parsed and carried on <see cref="SoundCatalog.Entry.Preload"/>.</para>
/// </summary>
public sealed class SoundBank {
	/// <summary>The archive the sample banks live in.</summary>
	public const string ArchiveName = "SIMSOUND.VOL";

	/// <summary>Normal-memory sample folder.</summary>
	public const string StandardBank = "HMI";

	/// <summary>
	/// Low-memory sample folder, selected by the original when it is started with <c>-l</c> or finds
	/// under 12 MB of physical memory. The same recordings as 8-bit mono 11,025 Hz.
	/// </summary>
	public const string LowMemoryBank = "HMX";

	private readonly WaveSample?[] _samples;

	private SoundBank(SoundCatalog catalog, WaveSample?[] samples, IReadOnlyList<string> missing) {
		Catalog = catalog;
		_samples = samples;
		Missing = missing;
	}

	/// <summary>The parsed <c>SOUNDS.STR</c>.</summary>
	public SoundCatalog Catalog { get; }

	/// <summary>
	/// Names the catalog asked for that no mounted archive had. Retail produces one entry —
	/// <c>battle1.wav</c>, which the ten music rows all name and which ships nowhere, because music
	/// is Red Book CD audio rather than a sample. A second appears when the low-memory bank is
	/// selected, since <c>EXPLO5.WAV</c> exists only under <c>hmi</c>.
	/// </summary>
	public IReadOnlyList<string> Missing { get; }

	/// <summary>The decoded sample for a catalog id, or null when the row had no file to load.</summary>
	public WaveSample? Sample(int id) => id >= 0 && id < _samples.Length ? _samples[id] : null;

	/// <summary>
	/// Loads the catalog and every sample it names. Returns null only when <c>SOUNDS.STR</c> itself
	/// is missing or unparseable — an individual sample that will not load is recorded in
	/// <see cref="Missing"/> and leaves that id silent, which is what the original does with a row
	/// whose file it cannot open.
	/// </summary>
	public static SoundBank? Load(GameContent content, bool lowMemory = false) {
		if (SoundCatalog.Load(content) is not { } catalog) {
			return null;
		}

		string folder = lowMemory ? LowMemoryBank : StandardBank;
		var samples = new WaveSample?[catalog.Count];
		var missing = new List<string>();
		var decoded = new Dictionary<string, WaveSample?>(StringComparer.OrdinalIgnoreCase);

		for (int id = 0; id < catalog.Count; id++) {
			var entry = catalog.Entries[id];
			if (!entry.HasAttributes || entry.FileName.Length == 0) {
				continue;
			}

			// Rows sharing a filename share the decode: the ten music rows all name the same file,
			// and the original likewise opens one resource and refcounts it across the voices.
			if (!decoded.TryGetValue(entry.FileName, out var sample)) {
				sample = content.Read(folder, entry.FileName) is { } bytes ? WaveSample.DecodeForSimulator(bytes) : null;
				decoded[entry.FileName] = sample;

				if (sample == null) {
					missing.Add(entry.FileName);
				}
			}

			samples[id] = sample;
		}

		return new SoundBank(catalog, samples, missing);
	}
}
