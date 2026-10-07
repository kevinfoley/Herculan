namespace HercWorks.Disc;

/// <summary>
/// Consecutive sectors as one seekable, read-only stream: a file's extent on the data track (2,048
/// bytes of user data a sector), or an audio track's PCM (2,352 bytes a sector).
/// </summary>
internal sealed class SectorStream : Stream {
	private readonly DiscImage _image;
	private readonly int _startLba;
	private readonly long _length;
	private readonly bool _audio;
	private readonly int _sectorSize;
	private readonly byte[] _sector;
	private long _bufferedSector = -1;
	private long _position;

	public SectorStream(DiscImage image, int startLba, long length, bool audio) {
		_image = image;
		_startLba = startLba;
		_length = length;
		_audio = audio;
		_sectorSize = audio ? DiscImage.RawSectorSize : DiscImage.UserDataSize;
		_sector = new byte[_sectorSize];
	}

	public override bool CanRead => true;
	public override bool CanSeek => true;
	public override bool CanWrite => false;
	public override long Length => _length;

	public override long Position {
		get => _position;
		set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
	}

	public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

	public override int Read(Span<byte> buffer) {
		if (_position >= _length || buffer.IsEmpty) {
			return 0;
		}

		int total = (int)Math.Min(buffer.Length, _length - _position);
		var remaining = buffer[..total];
		while (!remaining.IsEmpty) {
			long sector = _position / _sectorSize;
			int within = (int)(_position % _sectorSize);
			int copied;
			if (within == 0 && remaining.Length >= _sectorSize) {
				int whole = remaining.Length / _sectorSize;
				ReadSectors(_startLba + (int)sector, whole, remaining);
				copied = whole * _sectorSize;
			} else {
				if (_bufferedSector != sector) {
					ReadSectors(_startLba + (int)sector, 1, _sector);
					_bufferedSector = sector;
				}

				copied = Math.Min(_sectorSize - within, remaining.Length);
				_sector.AsSpan(within, copied).CopyTo(remaining);
			}

			remaining = remaining[copied..];
			_position += copied;
		}

		return total;
	}

	private void ReadSectors(int lba, int count, Span<byte> destination) {
		if (_audio) {
			_image.ReadAudioSectors(lba, count, destination);
		} else {
			_image.ReadSectors(lba, count, destination);
		}
	}

	public override long Seek(long offset, SeekOrigin origin) => Position = origin switch {
		SeekOrigin.Begin => offset,
		SeekOrigin.Current => _position + offset,
		SeekOrigin.End => _length + offset,
		_ => throw new ArgumentOutOfRangeException(nameof(origin)),
	};

	public override void Flush() { }
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
