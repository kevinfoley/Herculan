using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HercWorks.Help.Html;
using Xunit;

namespace HercWorks.Help.Tests;

/// <summary>
/// Damaged and doctored copies of the English manual: the parser reports failure rather than
/// throwing or looping, and nothing in the file reaches the page as markup
/// (docs/herculan/online-manual.md#security-posture).
/// </summary>
public class HostileInputTests {
	[Fact]
	public void TruncationAtAnyLengthFailsCleanly() {
		if (RetailManuals.Read("ENGLISH") is not { } bytes) {
			return;
		}

		// Every length through the directory and the start of |SYSTEM, then a spread over the rest.
		var lengths = Enumerable.Range(0, 2048).Concat(Enumerable.Range(0, 400).Select(i => (int)((long)bytes.Length * i / 400)));
		foreach (int length in lengths) {
			var help = HelpFile.Parse(bytes[..length], out string? error);
			Assert.True(help == null || error == null);
		}
	}

	[Fact]
	public void CorruptBytesNeverThrow() {
		if (RetailManuals.Read("ENGLISH") is not { } original) {
			return;
		}

		// The directory, |SYSTEM, |TOPIC and the indexes all sit in the first 185 KB; the rest is pictures.
		var random = new Random(1996);
		for (int i = 0; i < 2000; i++) {
			var bytes = (byte[])original.Clone();
			for (int k = 0; k < 4; k++) {
				bytes[random.Next(0, 185_000)] = (byte)random.Next(256);
			}

			if (HelpFile.Parse(bytes, out _) is { } help) {
				foreach (var topic in help.Topics) {
					help.Locate(topic.Offset);
				}
			}
		}
	}

	[Fact]
	public void CorruptPicturesNeverThrow() {
		if (RetailManuals.Read("ENGLISH") is not { } original) {
			return;
		}

		var help = HelpFile.Parse(original, out _)!;
		int start = Find(original, "|bm24"u8) is var name and >= 0 ? DirectoryOffset(original, name) : -1;
		Assert.True(start > 0);

		var random = new Random(2026);
		for (int i = 0; i < 300; i++) {
			var bytes = (byte[])original.Clone();
			for (int k = 0; k < 3; k++) {
				bytes[start + 9 + random.Next(0, 64)] = (byte)random.Next(256);
			}

			HelpFile.Parse(bytes, out _)?.TryGetPicture(24, out _);
		}

		Assert.NotNull(help.TryGetPicture(24, out _));
	}

	[Fact]
	public void AHugePictureIsRefused() {
		if (RetailManuals.Read("ENGLISH") is not { } bytes) {
			return;
		}

		// |bm0's width and height are 2-byte compressed unsigned longs 8 bytes into its picture header;
		// make both the largest that form holds, 32767, a 3 GB bitmap.
		int file = DirectoryOffset(bytes, Find(bytes, "|bm0\0"u8));
		int picture = file + 9 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(file + 9 + 4));
		Assert.Equal(6, bytes[picture]);
		Assert.Equal(0x30, bytes[picture + 7]);
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(picture + 8), 0xFFFE);
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(picture + 10), 0xFFFE);

		var help = HelpFile.Parse(bytes, out string? error);
		Assert.True(help != null, error);
		Assert.Null(help.TryGetPicture(0, out string? refused));
		Assert.Contains("32767x32767", refused);
	}

	[Fact]
	public void ATopicChainThatLoopsIsRefused() {
		if (RetailManuals.Read("ENGLISH") is not { } bytes) {
			return;
		}

		// The first topic link sits at |TOPIC's offset 12; point its next-link field back at itself.
		int topic = DirectoryOffset(bytes, Find(bytes, "|TOPIC\0"u8)) + 9;
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(topic + 12 + 12), 12);

		Assert.Null(HelpFile.Parse(bytes, out string? error));
		Assert.Contains("backwards", error);
	}

	[Fact]
	public void TopicTextIsNeverMarkup() {
		if (RetailManuals.Read("ENGLISH") is not { } bytes) {
			return;
		}

		// Same length, so every size field still holds.
		Replace(bytes, "This area covers the things"u8, "<script>alert(1)</script>xx"u8);
		Replace(bytes, "EarthSiege 2 On-Line Manual"u8, "</title><img src=x onerror="u8);

		var help = HelpFile.Parse(bytes, out string? error);
		Assert.True(help != null, error);
		string html = HelpHtmlWriter.Write(help);

		Assert.DoesNotContain("<script>alert", html);
		Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
		Assert.DoesNotContain("<img src=x", html);
		Assert.Equal(1, Count(html, "<script"));
		Assert.Contains("script-src '" + HelpHtmlWriter.ScriptHash() + "'", html);
	}

	[Fact]
	public void ThePolicyAdmitsTheScriptAsTheBrowserReadsIt() {
		if (RetailManuals.Read("ENGLISH") is not { } bytes) {
			return;
		}

		// The browser hashes an inline script after its parser has turned CRLF and lone CR into LF.
		string html = HelpHtmlWriter.Write(HelpFile.Parse(bytes, out _)!, "en", "Readme\r\ntext\n");
		int open = html.IndexOf("<script>", StringComparison.Ordinal) + "<script>".Length;
		string script = html[open..html.IndexOf("</script>", open, StringComparison.Ordinal)].Replace("\r\n", "\n").Replace('\r', '\n');
		Assert.Equal(ManualScript.Text, script);
		Assert.Contains("script-src 'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script))) + "'", html);
	}

	[Fact]
	public void TheOnlyUrlsArePictures() {
		if (RetailManuals.Read("ENGLISH") is not { } bytes) {
			return;
		}

		string html = HelpHtmlWriter.Write(HelpFile.Parse(bytes, out _)!);
		foreach (string attribute in new[] { "src=\"", "href=\"" }) {
			int at = 0;
			while ((at = html.IndexOf(attribute, at, StringComparison.Ordinal)) >= 0) {
				at += attribute.Length;
				string value = html.Substring(at, Math.Min(22, html.Length - at));
				Assert.True(attribute == "src=\"" ? value.StartsWith("data:image/png;base64,") : value.StartsWith("#\""), value);
			}
		}
	}

	private static int Count(string text, string part) {
		int count = 0;
		for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal)) {
			count++;
		}

		return count;
	}

	private static int Find(byte[] bytes, ReadOnlySpan<byte> part) => bytes.AsSpan().IndexOf(part);

	// A directory entry is the name, its NUL, then the file header's offset.
	private static int DirectoryOffset(byte[] bytes, int name) {
		int nul = Array.IndexOf(bytes, (byte)0, name);
		return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(nul + 1));
	}

	private static void Replace(byte[] bytes, ReadOnlySpan<byte> find, ReadOnlySpan<byte> with) {
		Assert.Equal(find.Length, with.Length);
		int at = Find(bytes, find);
		Assert.True(at >= 0, Encoding.ASCII.GetString(find));
		with.CopyTo(bytes.AsSpan(at));
	}
}
