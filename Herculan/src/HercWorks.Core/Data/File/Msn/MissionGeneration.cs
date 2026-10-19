using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// What a <see cref="MissionDocument"/> hands the generator besides the mission's files, the same for every generation
/// until <see cref="MissionDocument.SetGeneration"/> replaces it, so each regeneration resolves the mission the same way
/// (docs/herculan/handoff-editor-document.md#conditions-and-variants): the
/// campaign flag array as the load is given it, copied for each run because row 2 clears flags in it; a fresh clear list
/// per run; the draws, from a roll <see cref="NewRoll"/> makes anew for each run, so each draws the same sequence; and
/// the header words the launch writes over the loaded header, as a practice load writes its options
/// (<c>MsnGen_LoadMission</c> (<c>0041c73d</c>), docs/retail/formats/script-dat.md#the-training-fields). The editor's
/// preview of a variant table's alternatives rides with it too (<see cref="Picks"/>), which no launch has.
/// </summary>
public sealed class MissionGeneration {
	private readonly short[] _flags;
	private readonly short?[] _header;
	private readonly Dictionary<short, short> _picks;

	/// <param name="flags">The <see cref="MissionGenerator.CampaignFlagCount"/> campaign flags, copied.</param>
	/// <param name="header">For each of the ten header words, the value the launch writes over it, or null to keep the loaded one.</param>
	/// <param name="newRoll">Makes a roll, a draw in <c>[0, n)</c>, starting the same sequence each time.</param>
	public MissionGeneration(IReadOnlyList<short> flags, IReadOnlyList<short?> header, Func<Func<short, int>> newRoll)
		: this(flags, header, newRoll, new Dictionary<short, short>()) {
	}

	private MissionGeneration(IReadOnlyList<short> flags, IReadOnlyList<short?> header, Func<Func<short, int>> newRoll,
			Dictionary<short, short> picks) {
		if (flags.Count != MissionGenerator.CampaignFlagCount) {
			throw new ArgumentException($"The campaign flag array holds {MissionGenerator.CampaignFlagCount} entries.", nameof(flags));
		}

		if (header.Count != HeaderWords) {
			throw new ArgumentException($"The header holds {HeaderWords} words.", nameof(header));
		}

		_flags = flags.ToArray();
		_header = header.ToArray();
		NewRoll = newRoll;
		_picks = picks;
	}

	/// <summary>How many words the header has, <see cref="MissionGenerator.Header"/>'s length.</summary>
	public const int HeaderWords = 10;

	public IReadOnlyList<short> Flags => _flags;

	public IReadOnlyList<short?> Header => _header;

	public Func<Func<short, int>> NewRoll { get; }

	/// <summary>
	/// The alternative each variant table named shows, by type-3 GUID: the GUID of the range whose alternative it picks,
	/// though it still draws, so every later draw is unchanged (<see cref="MissionGenerator.Load"/>'s forced picks). Empty
	/// for every launch's generation; the editor's preview sets it outside the undo history.
	/// </summary>
	public IReadOnlyDictionary<short, short> Picks => _picks;

	/// <summary>These inputs, but variant table <paramref name="table"/> picking range <paramref name="range"/>'s alternative, or drawing as it draws for null.</summary>
	public MissionGeneration WithPick(short table, short? range) {
		var picks = new Dictionary<short, short>(_picks);
		if (range is { } forced) {
			picks[table] = forced;
		} else {
			picks.Remove(table);
		}

		return new MissionGeneration(_flags, _header, NewRoll, picks);
	}

	/// <summary>
	/// These inputs, but writing nothing over header words <paramref name="words"/>, which then keep what the mission's
	/// row 2 patched.
	/// </summary>
	public MissionGeneration WithoutOverwrite(params int[] words) {
		var header = _header.ToArray();
		foreach (int word in words) {
			header[word] = null;
		}

		return new MissionGeneration(_flags, header, NewRoll, _picks);
	}

	/// <summary>These inputs, but handing the load the campaign flags <paramref name="flags"/>.</summary>
	public MissionGeneration WithFlags(IReadOnlyList<short> flags) => new(flags, _header, NewRoll, _picks);

	/// <summary>Loads <paramref name="msn"/> and its <c>.ENG</c> <paramref name="text"/> with these inputs, the header words written over.</summary>
	public MissionGenerator Run(byte[] msn, byte[]? text) {
		var generator = MissionGenerator.Load(msn, text, _flags.ToArray(), new short[MissionGenerator.ClearListLength], NewRoll(), _picks);
		WriteHeader(generator.Header, _header);
		return generator;
	}

	/// <summary>Writes each of <paramref name="words"/> that is set over the same word of <paramref name="header"/>.</summary>
	public static void WriteHeader(short[] header, IReadOnlyList<short?> words) {
		for (int word = 0; word < words.Count && word < header.Length; word++) {
			if (words[word] is { } value) {
				header[word] = value;
			}
		}
	}
}
