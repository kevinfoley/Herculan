using System.Buffers.Binary;
using System.Text;
using HercWorks.Help.Html;
using Xunit;

namespace HercWorks.Help.Tests;

/// <summary>
/// The readme behind the manual's <c>Readme</c> action: <see cref="WriteDocument"/> against the retail
/// files and damaged or doctored copies, and the page it lands in
/// (docs/engine/online-manual.md#security-posture).
/// </summary>
public class ReadmeTests {
	// v1.0's README.WRI: the text runs from 128 to fcMac, 30997, in a 37,120-byte file.
	private const int RetailTextEnd = 30997;

	[Theory]
	[MemberData(nameof(RetailManuals.All), MemberType = typeof(RetailManuals))]
	public void ReadsTheRetailReadme(string language) {
		if (RetailManuals.ReadReadme(language) is not { } bytes) {
			return;
		}

		string? text = WriteDocument.ReadText(bytes, out string? error);
		Assert.True(text != null, error);
		Assert.StartsWith("EarthSiege II - \nREADME \n10/10/97", text);
		Assert.EndsWith("Copyright 1996\tDynamix, Inc.\n", text);
		// 30,869 bytes of text, 894 of them the CR of a CRLF.
		Assert.Equal(RetailTextEnd - 128 - 894, text.Length);
		Assert.Contains((char)0x201C, text);
		Assert.All(text, c => Assert.True(c is '\t' or '\n' || !char.IsControl(c)));
	}

	[Fact]
	public void TruncationAtAnyLengthFailsCleanly() {
		byte[] bytes = RetailManuals.ReadReadme("ENGLISH") ?? Document("A readme.\r\nSecond line.\r\n", trailer: 2000);
		int textEnd = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14));

		// Every length through the header and into the text, then a spread over the rest.
		var lengths = Enumerable.Range(0, 512).Concat(Enumerable.Range(0, 400).Select(i => (int)((long)bytes.Length * i / 400)))
			.Concat([textEnd - 1, textEnd, bytes.Length]).Where(l => l <= bytes.Length);
		foreach (int length in lengths) {
			string? text = WriteDocument.ReadText(bytes[..length], out string? error);
			Assert.Equal(length >= textEnd, text != null);
			Assert.Equal(text == null, error != null);
		}
	}

	[Theory]
	[InlineData(0u)]
	[InlineData(127u)]
	[InlineData(136u)]
	[InlineData(0x7FFFFFFFu)]
	[InlineData(0xFFFFFFFFu)]
	public void ATextEndOutsideTheFileIsRefused(uint textEnd) {
		byte[] bytes = Document("Text.\r\n", trailer: 0);
		Assert.Equal(135, bytes.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), textEnd);

		Assert.Null(WriteDocument.ReadText(bytes, out string? error));
		Assert.Contains("the text ends at", error);
	}

	[Fact]
	public void TheTextMayBeEmptyOrRunToTheEnd() {
		byte[] bytes = Document("Text.\r\n", trailer: 0);
		Assert.Equal("Text.\n", WriteDocument.ReadText(bytes, out _));

		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 128);
		Assert.Equal("", WriteDocument.ReadText(bytes, out _));
	}

	[Fact]
	public void AWrongMagicIsRefused() {
		byte[] bytes = Document("Text.\r\n", trailer: 0);
		bytes[0] = 0x32;

		Assert.Null(WriteDocument.ReadText(bytes, out string? error));
		Assert.Contains("magic", error);
	}

	[Fact]
	public void AnOversizeFileIsRefused() {
		byte[] bytes = Document(new string('x', HelpLimits.Default.MaxReadmeBytes - 128 + 1), trailer: 0);
		Assert.Equal(HelpLimits.Default.MaxReadmeBytes + 1, bytes.Length);

		Assert.Null(WriteDocument.ReadText(bytes, out string? error));
		Assert.Contains("limit", error);
		Assert.Null(WriteDocument.ReadText(Document("Text.\r\n", trailer: 0), out error, new HelpLimits { MaxReadmeBytes = 134 }));
		Assert.Contains("limit", error);
	}

	[Fact]
	public void CorruptBytesNeverThrow() {
		byte[] original = RetailManuals.ReadReadme("ENGLISH") ?? Document(string.Concat(Enumerable.Repeat("Line of text.\r\n", 200)), trailer: 500);

		// Half the damage lands in the header, where the magic and the text end are; the rest anywhere.
		var random = new Random(1997);
		for (int i = 0; i < 2000; i++) {
			var bytes = (byte[])original.Clone();
			for (int k = 0; k < 4; k++) {
				bytes[random.Next(0, k < 2 ? 128 : bytes.Length)] = (byte)random.Next(256);
			}

			if (WriteDocument.ReadText(bytes, out _) is { } text) {
				Assert.All(text, c => Assert.True(c is '\t' or '\n' || !char.IsControl(c)));
			}
		}
	}

	[Fact]
	public void ControlCharactersAreDropped() {
		// NUL, escape, form feed, a lone CR, DEL, and 0x81, which Windows-1252 leaves undefined.
		byte[] bytes = Document("a\u0000b\u001B[31mc\u000Cd\re\r\nf\ng\th\u007F\u0081i\r\n", trailer: 0);
		Assert.Equal("ab[31mcde\nf\ng\th" + (char)0xFFFD + "i\n", WriteDocument.ReadText(bytes, out _));
	}

	[Fact]
	public void ReadmeTextIsNeverMarkup() {
		if (RetailManuals.Read("ENGLISH") is not { } help || RetailManuals.ReadReadme("ENGLISH") is not { } bytes) {
			return;
		}

		// Same length, so the text end still holds.
		Replace(bytes, "This document contains last-minute information"u8, "<script>alert(1)</script></pre></template><br>"u8);
		Replace(bytes, "ES2TS.TXT located"u8, "http://x.invalid/"u8);
		string? readme = WriteDocument.ReadText(bytes, out string? error);
		Assert.True(readme != null, error);
		string html = HelpHtmlWriter.Write(HelpFile.Parse(help, out _)!, "en", readme);

		Assert.DoesNotContain("<script>alert", html);
		Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;&lt;/pre&gt;&lt;/template&gt;&lt;br&gt;", html);
		Assert.Equal(1, Count(html, "<script"));
		Assert.Equal(1, Count(html, "</pre>"));
		Assert.Contains("http://x.invalid/", html);
		Assert.DoesNotContain("\"http://x.invalid/", html);
		Assert.Contains("script-src '" + HelpHtmlWriter.ScriptHash() + "'", html);
	}

	[Fact]
	public void TheReadmeActionOpensTheReadmeTopic() {
		if (RetailManuals.Read("ENGLISH") is not { } bytes) {
			return;
		}

		var help = HelpFile.Parse(bytes, out _)!;
		string html = HelpHtmlWriter.Write(help, "en", "First line\n<second>\n");

		// The button and the three hotspots, each sent to the main window.
		Assert.Equal(4, Count(html, "data-go=\"readme\" data-w=\"0\""));
		Assert.Equal(0, Count(html, "data-go=\"note\""));
		Assert.Contains("<template id=\"readme\"><div class=\"scroll\"><pre class=\"readme\">\nFirst line\n&lt;second&gt;\n</pre></div></template>", html);
		Assert.Contains(">Readme</a>", html);
		// Grey, not the page's default black: the main window is black.
		Assert.Contains("#main{position:absolute;inset:0;z-index:1;background:#000000;}", html);
		Assert.Contains(".readme{margin:0;font:10pt 'Courier New',Courier,monospace;white-space:pre;color:#c0c0c0;}", html);

		string withoutReadme = HelpHtmlWriter.Write(help, "en");
		Assert.Equal(4, Count(withoutReadme, "data-go=\"note\" data-note=\"This manual&#39;s viewer does not start programs. The help file asks for esreadme.txt.\""));
		Assert.Equal(0, Count(withoutReadme, "data-go=\"readme\""));
		Assert.DoesNotContain("<template id=\"readme\"", withoutReadme);
	}

	// A Write document: the magic, the text end at offset 14, the text from 128 in Windows-1252 (here
	// Latin-1, which agrees for every byte these tests write), then trailer bytes standing in for the
	// formatting that follows the text.
	private static byte[] Document(string text, int trailer) {
		var bytes = new byte[128 + text.Length + trailer];
		BinaryPrimitives.WriteUInt16LittleEndian(bytes, 0xBE31);
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), (uint)(128 + text.Length));
		Encoding.Latin1.GetBytes(text, bytes.AsSpan(128));
		for (int i = 128 + text.Length; i < bytes.Length; i++) {
			bytes[i] = (byte)i;
		}

		return bytes;
	}

	private static int Count(string text, string part) {
		int count = 0;
		for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal)) {
			count++;
		}

		return count;
	}

	private static void Replace(byte[] bytes, ReadOnlySpan<byte> find, ReadOnlySpan<byte> with) {
		Assert.Equal(find.Length, with.Length);
		int at = bytes.AsSpan().IndexOf(find);
		Assert.True(at >= 0, Encoding.ASCII.GetString(find));
		with.CopyTo(bytes.AsSpan(at));
	}
}
