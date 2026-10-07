using System.Text;

namespace HercWorks.Disc;

/// <summary>
/// A parsed cue sheet: the <c>FILE</c>, <c>TRACK</c>, <c>INDEX</c>, <c>PREGAP</c> and <c>POSTGAP</c>
/// commands that lay a disc out over its image files. Metadata commands (<c>REM</c>, <c>TITLE</c>,
/// <c>PERFORMER</c>, <c>FLAGS</c>, <c>ISRC</c>, <c>CATALOG</c>, ...) are skipped.
///
/// <para>Parsing touches no file: <see cref="CueFile.Name"/> is the text the sheet holds, which
/// <see cref="DiscImage"/> reduces to a bare file name beside the sheet before it opens anything.</para>
/// </summary>
internal sealed class CueSheet {
	/// <summary>The largest cue sheet read. A 99-track sheet with full metadata is a few kilobytes.</summary>
	public const int MaxBytes = 1 << 20;

	/// <summary>The most tracks a disc holds.</summary>
	public const int MaxTracks = 99;

	public List<CueFile> Files { get; } = new();

	/// <summary>One <c>FILE</c> command and the tracks that follow it.</summary>
	public sealed class CueFile {
		public CueFile(string name, bool bigEndian) {
			Name = name;
			BigEndian = bigEndian;
		}

		/// <summary>The file name as the sheet gives it.</summary>
		public string Name { get; }

		/// <summary>The file is <c>MOTOROLA</c>: its audio samples are big-endian.</summary>
		public bool BigEndian { get; }

		public List<CueTrack> Tracks { get; } = new();
	}

	/// <summary>One <c>TRACK</c> command and what follows it up to the next.</summary>
	public sealed class CueTrack {
		public CueTrack(int number, TrackFormat format) {
			Number = number;
			Format = format;
		}

		public int Number { get; }

		public TrackFormat Format { get; }

		/// <summary><c>PREGAP</c>: sectors of silence before the track that the file does not hold.</summary>
		public int Pregap { get; set; }

		/// <summary><c>POSTGAP</c>: sectors after the track that the file does not hold.</summary>
		public int Postgap { get; set; }

		/// <summary><c>INDEX</c> number to its position in the file, in sectors, in ascending order of both.</summary>
		public SortedList<int, int> Indexes { get; } = new();

		/// <summary>The first sector of the file that belongs to this track: its lowest index.</summary>
		public int FirstSector => Indexes.Values[0];

		public int Index1 => Indexes[1];
	}

	/// <summary>Decodes a cue sheet's bytes, as UTF-8 when they are valid UTF-8 and as Latin-1 otherwise.</summary>
	public static CueSheet Parse(byte[] bytes) {
		if (bytes.Length > MaxBytes) {
			throw new DiscFormatException($"The cue sheet is {bytes.Length} bytes; the most read is {MaxBytes}.");
		}

		string text;
		try {
			text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
		} catch (DecoderFallbackException) {
			text = Encoding.Latin1.GetString(bytes);
		}

		return Parse(text);
	}

	public static CueSheet Parse(string text) {
		var sheet = new CueSheet();
		CueFile? file = null;
		CueTrack? track = null;
		int lineNumber = 0;

		foreach (string rawLine in text.Split('\n')) {
			lineNumber++;
			var tokens = Tokenize(rawLine);
			if (tokens.Count == 0) {
				continue;
			}

			string command = tokens[0].ToUpperInvariant();
			switch (command) {
				case "FILE": {
					Expect(tokens, 3, lineNumber);
					CloseTrack(track, lineNumber);
					CloseFile(file, lineNumber);
					string type = tokens[2].ToUpperInvariant();
					if (type is not ("BINARY" or "MOTOROLA")) {
						throw new DiscFormatException(
							$"Cue sheet line {lineNumber}: FILE type {tokens[2]} is not supported; only BINARY and MOTOROLA images are read.");
					}

					file = new CueFile(tokens[1], type == "MOTOROLA");
					sheet.Files.Add(file);
					track = null;
					break;
				}

				case "TRACK": {
					Expect(tokens, 3, lineNumber);
					if (file == null) {
						throw new DiscFormatException($"Cue sheet line {lineNumber}: TRACK before any FILE.");
					}

					CloseTrack(track, lineNumber);
					int number = ParseNumber(tokens[1], 1, MaxTracks, "track number", lineNumber);
					int previous = sheet.Files.SelectMany(f => f.Tracks).Select(t => t.Number).LastOrDefault();
					if (number <= previous) {
						throw new DiscFormatException(
							$"Cue sheet line {lineNumber}: track {number} follows track {previous}.");
					}

					track = new CueTrack(number, ParseFormat(tokens[2], lineNumber));
					file.Tracks.Add(track);
					break;
				}

				case "INDEX": {
					Expect(tokens, 3, lineNumber);
					var current = RequireTrack(track, command, lineNumber);
					if (current.Postgap != 0) {
						throw new DiscFormatException($"Cue sheet line {lineNumber}: INDEX after POSTGAP.");
					}

					int number = ParseNumber(tokens[1], 0, 99, "index number", lineNumber);
					int position = ParseMsf(tokens[2], lineNumber);
					if (current.Indexes.Count == 0 && number > 1) {
						throw new DiscFormatException($"Cue sheet line {lineNumber}: a track's first index must be 00 or 01.");
					}

					if (current.Indexes.Count > 0
							&& (number <= current.Indexes.Keys[^1] || position < current.Indexes.Values[^1])) {
						throw new DiscFormatException($"Cue sheet line {lineNumber}: INDEX {tokens[1]} is out of order.");
					}

					int previousEnd = file!.Tracks.Count > 1 ? file.Tracks[^2].Indexes.Values[^1] : 0;
					if (current.Indexes.Count == 0 && file.Tracks.Count > 1 && position < previousEnd) {
						throw new DiscFormatException(
							$"Cue sheet line {lineNumber}: track {current.Number} starts before the previous track's last index.");
					}

					current.Indexes.Add(number, position);
					break;
				}

				case "PREGAP": {
					Expect(tokens, 2, lineNumber);
					var current = RequireTrack(track, command, lineNumber);
					if (current.Indexes.Count > 0) {
						throw new DiscFormatException($"Cue sheet line {lineNumber}: PREGAP after INDEX.");
					}

					current.Pregap = ParseMsf(tokens[1], lineNumber);
					break;
				}

				case "POSTGAP": {
					Expect(tokens, 2, lineNumber);
					RequireTrack(track, command, lineNumber).Postgap = ParseMsf(tokens[1], lineNumber);
					break;
				}
			}
		}

		CloseTrack(track, lineNumber);
		CloseFile(file, lineNumber);
		if (sheet.Files.Count == 0) {
			throw new DiscFormatException("The cue sheet names no FILE.");
		}

		return sheet;
	}

	private static void CloseTrack(CueTrack? track, int lineNumber) {
		if (track != null && !track.Indexes.ContainsKey(1)) {
			throw new DiscFormatException($"Cue sheet line {lineNumber}: track {track.Number} has no INDEX 01.");
		}
	}

	private static void CloseFile(CueFile? file, int lineNumber) {
		if (file != null && file.Tracks.Count == 0) {
			throw new DiscFormatException($"Cue sheet line {lineNumber}: FILE {file.Name} holds no TRACK.");
		}
	}

	private static CueTrack RequireTrack(CueTrack? track, string command, int lineNumber) =>
		track ?? throw new DiscFormatException($"Cue sheet line {lineNumber}: {command} before any TRACK.");

	private static void Expect(List<string> tokens, int count, int lineNumber) {
		if (tokens.Count < count) {
			throw new DiscFormatException($"Cue sheet line {lineNumber}: {tokens[0]} needs {count - 1} arguments.");
		}
	}

	private static TrackFormat ParseFormat(string token, int lineNumber) => token.ToUpperInvariant() switch {
		"AUDIO" => TrackFormat.Audio,
		"MODE1/2048" => TrackFormat.Mode1Cooked,
		"MODE1/2352" => TrackFormat.Mode1Raw,
		"MODE2/2352" => TrackFormat.Mode2Raw,
		"MODE2/2336" => TrackFormat.Mode2Xa,
		_ => throw new DiscFormatException($"Cue sheet line {lineNumber}: track mode {token} is not supported."),
	};

	private static int ParseNumber(string token, int min, int max, string what, int lineNumber) {
		if (token.Length is 0 or > 3 || !token.All(char.IsAsciiDigit)) {
			throw new DiscFormatException($"Cue sheet line {lineNumber}: {what} '{token}' is not a number.");
		}

		int value = int.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
		if (value < min || value > max) {
			throw new DiscFormatException($"Cue sheet line {lineNumber}: {what} {value} is outside {min} to {max}.");
		}

		return value;
	}

	/// <summary>An <c>mm:ss:ff</c> time as a count of sectors, 75 to the second.</summary>
	private static int ParseMsf(string token, int lineNumber) {
		string[] parts = token.Split(':');
		if (parts.Length != 3) {
			throw new DiscFormatException($"Cue sheet line {lineNumber}: '{token}' is not mm:ss:ff.");
		}

		int minutes = ParseNumber(parts[0], 0, 999, "minutes", lineNumber);
		int seconds = ParseNumber(parts[1], 0, 59, "seconds", lineNumber);
		int frames = ParseNumber(parts[2], 0, DiscImage.SectorsPerSecond - 1, "frames", lineNumber);
		return (minutes * 60 + seconds) * DiscImage.SectorsPerSecond + frames;
	}

	/// <summary>Splits a line on whitespace, keeping a double-quoted run (without its quotes) as one token.</summary>
	private static List<string> Tokenize(string line) {
		var tokens = new List<string>();
		int i = 0;
		while (i < line.Length) {
			if (char.IsWhiteSpace(line[i])) {
				i++;
				continue;
			}

			if (line[i] == '"') {
				// An unterminated quote runs to the end of the line, which leaves a FILE line short of its
				// type argument; a metadata line with a stray quote is skipped either way.
				int close = line.IndexOf('"', i + 1);
				int end = close < 0 ? line.Length : close;
				tokens.Add(line.Substring(i + 1, end - i - 1));
				i = end + 1;
			} else {
				int start = i;
				while (i < line.Length && !char.IsWhiteSpace(line[i])) {
					i++;
				}

				tokens.Add(line.Substring(start, i - start));
			}

			// A REM line's remainder is free text, possibly with an odd number of quotes.
			if (tokens.Count == 1 && tokens[0].Equals("REM", StringComparison.OrdinalIgnoreCase)) {
				break;
			}
		}

		return tokens;
	}
}
