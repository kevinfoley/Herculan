namespace HercWorks.Disc;

/// <summary>
/// How a track's sectors are stored in the image file. The names follow the cue sheet's
/// <c>TRACK</c> modes; a raw image read without a cue sheet is told apart by its sync pattern and
/// the mode byte of its header.
/// </summary>
public enum TrackFormat {
	/// <summary><c>AUDIO</c>: 2,352 bytes of 44.1 kHz 16-bit stereo PCM per sector, little-endian unless the file is <c>MOTOROLA</c>.</summary>
	Audio,

	/// <summary><c>MODE1/2048</c>: the 2,048 bytes of user data only, as in a plain <c>.iso</c>.</summary>
	Mode1Cooked,

	/// <summary><c>MODE1/2352</c>: 12-byte sync, 4-byte header, 2,048 bytes of user data, 288 bytes of EDC/ECC.</summary>
	Mode1Raw,

	/// <summary><c>MODE2/2352</c>: sync and header, then the 8-byte XA subheader and a Form 1 sector's 2,048 bytes of user data.</summary>
	Mode2Raw,

	/// <summary><c>MODE2/2336</c>: a <see cref="Mode2Raw"/> sector without its sync and header.</summary>
	Mode2Xa,
}

internal static class TrackFormatExtensions {
	/// <summary>Bytes one sector of this format takes in the image file.</summary>
	public static int SectorSize(this TrackFormat format) => format switch {
		TrackFormat.Mode1Cooked => DiscImage.UserDataSize,
		TrackFormat.Mode2Xa => 2336,
		_ => DiscImage.RawSectorSize,
	};

	/// <summary>Where a sector's 2,048 bytes of user data start within it; 0 for audio, which has none.</summary>
	public static int UserDataOffset(this TrackFormat format) => format switch {
		TrackFormat.Mode1Raw => 16,
		TrackFormat.Mode2Raw => 24,
		TrackFormat.Mode2Xa => 8,
		_ => 0,
	};
}
