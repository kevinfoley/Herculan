using Xunit;

namespace HercWorks.Video.Tests;

/// <summary>
/// Finds files from the retail install, for the tests that check against them. Each such test
/// passes vacuously when its file is absent, so the suite does not depend on an install.
/// </summary>
internal static class RetailFiles {
	/// <summary>
	/// Walks up from this assembly looking for the file under <c>ES2/</c>, and returns its path or null.
	/// </summary>
	/// <param name="relative">The file's path relative to the <c>ES2</c> folder.</param>
	internal static string? Find(string relative) {
		DirectoryInfo? at = new(AppContext.BaseDirectory);
		while (at is not null) {
			string candidate = Path.Combine(at.FullName, "ES2", relative);
			if (File.Exists(candidate)) {
				return candidate;
			}

			at = at.Parent;
		}

		return null;
	}

	/// <summary>
	/// Maps a virtual address in a PE image to its offset in the file on disk, by finding the
	/// section that contains it.
	/// </summary>
	internal static int VirtualToFileOffset(ReadOnlySpan<byte> image, uint virtualAddress) {
		int peHeader = BitConverter.ToInt32(image[0x3c..0x40]);
		Assert.Equal((byte)'P', image[peHeader]);

		int sectionCount = BitConverter.ToUInt16(image[(peHeader + 6)..]);
		int optionalHeaderSize = BitConverter.ToUInt16(image[(peHeader + 20)..]);
		uint imageBase = BitConverter.ToUInt32(image[(peHeader + 24 + 28)..]);
		uint rva = virtualAddress - imageBase;

		int at = peHeader + 24 + optionalHeaderSize;
		for (int i = 0; i < sectionCount; i++, at += 40) {
			uint virtualSize = BitConverter.ToUInt32(image[(at + 8)..]);
			uint sectionRva = BitConverter.ToUInt32(image[(at + 12)..]);
			uint rawSize = BitConverter.ToUInt32(image[(at + 16)..]);
			uint rawOffset = BitConverter.ToUInt32(image[(at + 20)..]);

			if (rva >= sectionRva && rva < sectionRva + Math.Max(virtualSize, rawSize)) {
				return (int)(rawOffset + (rva - sectionRva));
			}
		}

		throw new InvalidDataException($"No section of the image contains {virtualAddress:x8}.");
	}
}
