namespace HercWorks.Video.Tests;

/// <summary>
/// Builds AVI files in memory for the tests to parse.
///
/// <para>The files in <c>ES2/AVI/</c> are not in the repository, so the tests that depend on
/// parsing behaviour are built on these instead, and the retail checks reached through
/// <see cref="RetailFiles"/> are the extra ones. Assembling files byte by byte also makes it
/// possible to build the malformed cases — a chunk that lies about its length, lists nested two
/// hundred deep — that no real file contains.</para>
/// </summary>
internal sealed class SyntheticAvi {
	private readonly List<byte> _movi = [];

	/// <summary>Width written into the stream format header.</summary>
	internal int Width { get; set; } = 16;

	/// <summary>Height written into the stream format header. Negative means top-down rows.</summary>
	internal int Height { get; set; } = 8;

	/// <summary>Bits per pixel written into the stream format header.</summary>
	internal ushort BitCount { get; set; } = 8;

	/// <summary>Compression written into the stream format header; 1 is <c>BI_RLE8</c>.</summary>
	internal uint Compression { get; set; } = 1;

	/// <summary>Frame period in microseconds. 100000 is 10 fps.</summary>
	internal int MicrosecondsPerFrame { get; set; } = 100_000;

	/// <summary>Palette entries appended after the format header, as BGRX quads.</summary>
	internal List<byte> Palette { get; } = [];

	/// <summary>Whether to emit an audio stream alongside the video one.</summary>
	internal bool WithAudio { get; set; }

	/// <summary>Channel count for the audio stream.</summary>
	internal int AudioChannels { get; set; } = 1;

	/// <summary>Sample rate for the audio stream.</summary>
	internal int AudioSampleRate { get; set; } = 11025;

	/// <summary>Bits per sample for the audio stream.</summary>
	internal ushort AudioBits { get; set; } = 8;

	/// <summary>Adds a video packet, as a <c>00dc</c> chunk.</summary>
	internal SyntheticAvi AddVideo(params byte[] data) {
		AddChunk(_movi, "00dc", data);
		return this;
	}

	/// <summary>Adds an audio packet, as a <c>01wb</c> chunk.</summary>
	internal SyntheticAvi AddAudio(params byte[] data) {
		AddChunk(_movi, "01wb", data);
		return this;
	}

	/// <summary>Adds a <c>rec </c> list holding a video packet and then an audio packet, as the retail files group them.</summary>
	internal SyntheticAvi AddRecord(byte[] video, byte[] audio) {
		var record = new List<byte>();
		record.AddRange("rec "u8.ToArray());
		AddChunk(record, "00dc", video);
		AddChunk(record, "01wb", audio);
		AddList(_movi, [.. record]);
		return this;
	}

	/// <summary>Adds a video packet wrapped in <paramref name="depth"/> nested <c>rec </c> lists.</summary>
	internal SyntheticAvi AddNestedVideo(int depth, params byte[] data) {
		var payload = new List<byte>();
		AddChunk(payload, "00dc", data);
		for (int i = 0; i < depth; i++) {
			var record = new List<byte>();
			record.AddRange("rec "u8.ToArray());
			record.AddRange(payload);
			payload.Clear();
			AddList(payload, [.. record]);
		}

		_movi.AddRange(payload);
		return this;
	}

	/// <summary>Sets a 256-entry greyscale palette, so index n resolves to the grey (n, n, n).</summary>
	internal SyntheticAvi WithGreyPalette() {
		Palette.Clear();
		for (int i = 0; i < 256; i++) {
			Palette.Add((byte)i);
			Palette.Add((byte)i);
			Palette.Add((byte)i);
			Palette.Add(0);
		}

		return this;
	}

	/// <summary>Assembles the file.</summary>
	internal byte[] Build() {
		var hdrl = new List<byte>();
		hdrl.AddRange("hdrl"u8.ToArray());

		var avih = new List<byte>();
		AddI32(avih, MicrosecondsPerFrame);
		// The remaining 13 fields of avih are not read by this parser; zero is fine.
		for (int i = 0; i < 13; i++) {
			AddI32(avih, 0);
		}

		AddChunk(hdrl, "avih", [.. avih]);
		hdrl.AddRange(BuildVideoStreamList());

		if (WithAudio) {
			hdrl.AddRange(BuildAudioStreamList());
		}

		var body = new List<byte>();
		body.AddRange("AVI "u8.ToArray());
		AddList(body, [.. hdrl]);

		var movi = new List<byte>();
		movi.AddRange("movi"u8.ToArray());
		movi.AddRange(_movi);
		AddList(body, [.. movi]);

		var file = new List<byte>();
		file.AddRange("RIFF"u8.ToArray());
		AddI32(file, body.Count);
		file.AddRange(body);
		return [.. file];
	}

	private byte[] BuildVideoStreamList() {
		var strl = new List<byte>();
		strl.AddRange("strl"u8.ToArray());

		var strh = new List<byte>();
		strh.AddRange("vids"u8.ToArray());
		for (int i = 0; i < 13; i++) {
			AddI32(strh, 0);
		}

		AddChunk(strl, "strh", [.. strh]);

		var strf = new List<byte>();
		AddI32(strf, 40);
		AddI32(strf, Width);
		AddI32(strf, Height);
		AddI16(strf, 1);
		AddI16(strf, BitCount);
		AddI32(strf, (int)Compression);
		for (int i = 0; i < 5; i++) {
			AddI32(strf, 0);
		}

		strf.AddRange(Palette);
		AddChunk(strl, "strf", [.. strf]);

		var list = new List<byte>();
		AddList(list, [.. strl]);
		return [.. list];
	}

	private byte[] BuildAudioStreamList() {
		var strl = new List<byte>();
		strl.AddRange("strl"u8.ToArray());

		var strh = new List<byte>();
		strh.AddRange("auds"u8.ToArray());
		for (int i = 0; i < 13; i++) {
			AddI32(strh, 0);
		}

		AddChunk(strl, "strh", [.. strh]);

		var strf = new List<byte>();
		AddI16(strf, 1);
		AddI16(strf, (ushort)AudioChannels);
		AddI32(strf, AudioSampleRate);
		AddI32(strf, AudioSampleRate * AudioChannels * (AudioBits / 8));
		AddI16(strf, (ushort)(AudioChannels * (AudioBits / 8)));
		AddI16(strf, AudioBits);
		AddChunk(strl, "strf", [.. strf]);

		var list = new List<byte>();
		AddList(list, [.. strl]);
		return [.. list];
	}

	private static void AddList(List<byte> into, byte[] body) {
		into.AddRange("LIST"u8.ToArray());
		AddI32(into, body.Length);
		into.AddRange(body);
		if ((body.Length & 1) != 0) {
			into.Add(0);
		}
	}

	private static void AddChunk(List<byte> into, string id, byte[] data) {
		foreach (char ch in id) {
			into.Add((byte)ch);
		}

		AddI32(into, data.Length);
		into.AddRange(data);
		if ((data.Length & 1) != 0) {
			into.Add(0);
		}
	}

	private static void AddI32(List<byte> into, int value) {
		into.Add((byte)value);
		into.Add((byte)(value >> 8));
		into.Add((byte)(value >> 16));
		into.Add((byte)(value >> 24));
	}

	private static void AddI16(List<byte> into, int value) {
		into.Add((byte)value);
		into.Add((byte)(value >> 8));
	}
}
