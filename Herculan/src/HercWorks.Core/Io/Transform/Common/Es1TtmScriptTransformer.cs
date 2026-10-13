using System.Buffers.Binary;
using System.Text;
using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes ES1's <c>.TTM</c> scripts (<see cref="Es1TtmScript"/>; docs/es1/formats/ttm-scripts.md). The chunks are
/// walked from the start of the file, a container's content as chunks, and each is kept whole. The script is unpacked as
/// <c>TtmScript_Load</c> (<c>ES1GO 00047c28</c>) unpacks it: from the first <c>TT3:</c> after the first <c>PAG:</c>, run-length
/// decoded whatever its method byte, to the length its header gives.
///
/// <para>The loaders find the two chunks by searching the bytes for their tags; this reader takes them from the chunk walk,
/// which finds the same chunks in every retail file. It rejects a chunk that overruns its parent or the file, and a script
/// whose packed bytes end before its unpacked length, where the loader would read on into the next chunk.</para>
/// </summary>
public class Es1TtmScriptTransformer : ByteTransformer<Es1TtmScript> {
	/// <exception cref="InvalidDataException">A chunk does not fit, or the script does not unpack within its chunk.</exception>
	public override Es1TtmScript? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		var ttm = new Es1TtmScript { Chunks = ReadChunks(inputArray, 0, inputArray.Length) };
		bool afterPage = false;
		foreach (var chunk in Flatten(ttm.Chunks)) {
			if (!afterPage && chunk.Tag == "PAG:" && chunk.Content.Length >= 2) {
				ttm.PageCount = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Content);
				afterPage = true;
			} else if (afterPage && chunk.Tag == "TT3:" && chunk.Content.Length >= 5) {
				ttm.PackMethod = chunk.Content[0];
				ttm.UnpackedLength = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Content.AsSpan(1));
				ttm.Script = Unpack(chunk.Content.AsSpan(5), checked((int)ttm.UnpackedLength), out int read);
				ttm.PackedBytesRead = read;
				break;
			}
		}

		return ttm;
	}

	public override byte[]? Write(Es1TtmScript source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		WriteChunks(w, source.Chunks);
		w.Flush();
		return o.ToArray();
	}

	/// <summary>
	/// Run-length decodes <paramref name="length"/> bytes as <c>RLERStream_Read</c> does (docs/retail/runtime-library.md#the-decompression-filters):
	/// a control byte with bit 7 set is a run of its low seven bits' copies of the byte after it, one with bit 7 clear that many
	/// literal bytes. It reads no control byte once the length is reached.
	/// </summary>
	/// <param name="read">The packed bytes read.</param>
	/// <exception cref="InvalidDataException">The packed bytes end first.</exception>
	public static byte[] Unpack(ReadOnlySpan<byte> packed, int length, out int read) {
		var output = new byte[length];
		int o = 0;
		int i = 0;
		int run = 0;
		byte value = 0;
		while (true) {
			int n = Math.Min(run, length - o);
			output.AsSpan(o, n).Fill(value);
			o += n;
			if (o == length) {
				break;
			}

			if (i >= packed.Length) {
				throw new InvalidDataException($"The packed script ends after {o} of its {length} bytes.");
			}

			byte control = packed[i++];
			if ((control & 0x80) == 0) {
				int literal = Math.Min(control, length - o);
				if (i + literal > packed.Length) {
					throw new InvalidDataException($"The packed script ends inside a literal run after {o} of its {length} bytes.");
				}

				packed.Slice(i, literal).CopyTo(output.AsSpan(o));
				i += literal;
				o += literal;
				run = 0;
				continue;
			}

			if (i >= packed.Length) {
				throw new InvalidDataException($"The packed script ends before a run's value after {o} of its {length} bytes.");
			}

			value = packed[i++];
			run = control & 0x7f;
		}

		read = i;
		return output;
	}

	/// <summary>
	/// Where each of <paramref name="pageCount"/> pages starts in an unpacked <paramref name="script"/>, found as
	/// <c>TtmScript_IndexPages</c> (<c>ES1GO 000480d1</c>) finds them; null where <see cref="ReadPages"/> is.
	/// </summary>
	public static int[]? IndexPages(byte[] script, int pageCount) =>
		ReadPages(script, pageCount)?.Select(page => page[0].Offset).ToArray();

	/// <summary>
	/// The commands of each of <paramref name="pageCount"/> pages of an unpacked <paramref name="script"/>, each page ending with
	/// its <see cref="Es1TtmCommand.EndFrame"/>, divided as <c>TtmScript_IndexPages</c> (<c>ES1GO 000480d1</c>) divides them
	/// (docs/es1/formats/ttm-scripts.md#pages); null where that walk fails, or where a command's arguments run past the script.
	/// A string argument is decoded as Latin-1 up to its NUL.
	/// </summary>
	public static List<List<Es1TtmCommand>>? ReadPages(byte[] script, int pageCount) {
		var pages = new List<List<Es1TtmCommand>>(pageCount);
		int p = 0;
		for (int page = 0; page < pageCount; page++) {
			var commands = new List<Es1TtmCommand>();
			pages.Add(commands);
			while (true) {
				if (p >= script.Length - 1) {
					return null;
				}

				var command = new Es1TtmCommand { Offset = p, Opcode = BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(p)) };
				commands.Add(command);
				p += 2;
				if (command.Opcode == Es1TtmCommand.EndFrame) {
					break;
				}

				int args = command.Opcode & 0xf;
				int words;
				if (args <= 7) {
					words = args;
				} else if (args < 0xf) {
					return null;
				} else if (command.Opcode is 0xaf1f or 0xaf2f) {
					if (p + 2 > script.Length) {
						return null;
					}

					words = 1 + 2 * BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(p));
				} else {
					int end = SkipString(script, p);
					int nul = Array.IndexOf(script, (byte)0, p);
					command.Text = Encoding.Latin1.GetString(script, p, (nul < 0 ? script.Length : nul) - p);
					p = end;
					continue;
				}

				if (p + 2 * words > script.Length) {
					return null;
				}

				command.Arguments = new ushort[words];
				for (int i = 0; i < words; i++) {
					command.Arguments[i] = BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(p + 2 * i));
				}

				p += 2 * words;
			}
		}

		return pages;
	}

	/// <summary>
	/// The <c>TAG:</c> chunk's table as the bytes lay it out: a word count, then per tag a word id and a NUL-terminated name;
	/// null when the content is not exactly that. Neither loader reads it (docs/es1/formats/ttm-scripts.md#open).
	/// </summary>
	public static List<(ushort Id, string Name)>? ReadTags(byte[] content) {
		if (content.Length < 2) {
			return null;
		}

		int count = BinaryPrimitives.ReadUInt16LittleEndian(content);
		var tags = new List<(ushort, string)>(count);
		int at = 2;
		for (int i = 0; i < count; i++) {
			if (at + 2 > content.Length) {
				return null;
			}

			ushort id = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(at));
			int end = Array.IndexOf(content, (byte)0, at + 2);
			if (end < 0) {
				return null;
			}

			tags.Add((id, Encoding.ASCII.GetString(content, at + 2, end - at - 2)));
			at = end + 1;
		}

		return at == content.Length ? tags : null;
	}

	/// <summary>Past a string argument and its NUL, and one byte more when the string's length is even, as <c>TtmScript_SkipString</c> (<c>ES1GO 00047571</c>) skips it.</summary>
	private static int SkipString(byte[] script, int p) {
		int start = p;
		while (p < script.Length && script[p] != 0) {
			p++;
		}

		return (p - start) % 2 == 0 ? p + 2 : p + 1;
	}

	private static List<Es1TtmChunk> ReadChunks(byte[] b, int at, int end) {
		var chunks = new List<Es1TtmChunk>();
		while (at < end) {
			if (at + 8 > end) {
				throw new InvalidDataException($"A chunk header at {at} overruns its parent's end at {end}.");
			}

			string tag = Encoding.ASCII.GetString(b, at, 4);
			if (tag[3] != ':') {
				throw new InvalidDataException($"The chunk at {at} has the tag \"{tag}\", which does not end in a colon.");
			}

			uint word = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at + 4));
			bool container = (word & Es1TtmChunk.ContainerFlag) != 0;
			uint size = word & ~Es1TtmChunk.ContainerFlag;
			int start = at + 8;
			if (size > (uint)(end - start)) {
				throw new InvalidDataException($"The {tag} chunk at {at}, {size} bytes, overruns its parent's end at {end}.");
			}

			int stop = start + (int)size;
			chunks.Add(container
				? new Es1TtmChunk { Tag = tag, IsContainer = true, Children = ReadChunks(b, start, stop) }
				: new Es1TtmChunk { Tag = tag, Content = b[start..stop] });
			at = stop;
		}

		return chunks;
	}

	private static void WriteChunks(BinaryWriter w, List<Es1TtmChunk> chunks) {
		foreach (var chunk in chunks) {
			if (chunk.Tag.Length != 4) {
				throw new InvalidDataException($"The tag \"{chunk.Tag}\" is not 4 characters.");
			}

			w.Write(Encoding.ASCII.GetBytes(chunk.Tag));
			if (!chunk.IsContainer) {
				w.Write(checked((uint)chunk.Content.Length));
				w.Write(chunk.Content);
				continue;
			}

			using var inner = new MemoryStream();
			var iw = new BinaryWriter(inner);
			WriteChunks(iw, chunk.Children);
			iw.Flush();
			w.Write(checked((uint)inner.Length) | Es1TtmChunk.ContainerFlag);
			w.Write(inner.ToArray());
		}
	}

	private static IEnumerable<Es1TtmChunk> Flatten(List<Es1TtmChunk> chunks) {
		foreach (var chunk in chunks) {
			yield return chunk;
			foreach (var child in Flatten(chunk.Children)) {
				yield return child;
			}
		}
	}
}
