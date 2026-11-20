using MediaBrowser.Controller.Subtitles;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

namespace Jellyfin.Plugin.Subdivx;

internal sealed class SubtitleArchive
{
    private const int MaxNestedDepth = 3;
    private const int MaxEntries = 4096;
    private const int MaxExpandedBytes = 64 * 1024 * 1024;

    private readonly byte[] _buffer = new byte[8192];
    private int _remainingBytes = MaxExpandedBytes;
    private int _remainingEntries = MaxEntries;
    private (string Format, MemoryStream Stream)? _subtitle;

    public static SubtitleResponse Extract(Stream archive, CancellationToken cancellationToken)
    {
        var extraction = new SubtitleArchive();
        try
        {
            extraction.Scan(archive, 0, cancellationToken);
            if (extraction._subtitle is not { } subtitle)
                throw new InvalidDataException("The archive contains no supported subtitle.");

            return new SubtitleResponse
            {
                Format = subtitle.Format,
                IsForced = false,
                Language = "ES",
                Stream = subtitle.Stream
            };
        }
        catch
        {
            extraction._subtitle?.Stream.Dispose();
            throw;
        }
    }

    private void Scan(Stream stream, int depth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isSevenZip = SevenZipArchive.IsSevenZipFile(stream);
        stream.Position = 0;

        var options = new ReaderOptions { LeaveStreamOpen = true };
        using var sevenZip = isSevenZip ? SevenZipArchive.Open(stream, options) : null;
        using var reader = sevenZip?.ExtractAllEntries() ?? ReaderFactory.Open(stream, options);
        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--_remainingEntries < 0)
                throw new InvalidDataException("The subtitle archive contains too many entries.");

            var entry = reader.Entry;
            if (entry.IsDirectory || entry.IsEncrypted || entry.LinkTarget is not null)
                continue;

            var path = entry.Key?.Replace('\\', '/');
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            var isMetadata = path is null || path.Split('/').Any(part =>
                part.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("._", StringComparison.Ordinal));
            var isSubtitle = !isMetadata && extension is ".srt" or ".ass" or ".ssa" or ".sub" or ".vtt";
            var isNestedArchive = !isMetadata && depth < MaxNestedDepth && extension is ".zip" or ".rar" or ".7z";

            // Consume ignored entries through the same limit, including entries in solid archives.
            using var entryStream = reader.OpenEntryStream();
            using var contents = isSubtitle || isNestedArchive ? new MemoryStream() : null;
            CopyEntry(entryStream, contents ?? Stream.Null, cancellationToken);
            if (contents is null || contents.Length == 0)
                continue;

            if (isNestedArchive)
            {
                contents.Position = 0;
                Scan(contents, depth + 1, cancellationToken);
            }
            else if (_subtitle is null || contents.Length > _subtitle.Value.Stream.Length)
            {
                // Keep the first entry when sizes tie and preserve the original bytes.
                _subtitle?.Stream.Dispose();
                _subtitle = (extension![1..], new MemoryStream(contents.GetBuffer(), 0, (int)contents.Length, writable: false));
            }
        }
    }

    private void CopyEntry(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toRead = Math.Min(_buffer.Length, _remainingBytes + 1);
            var read = source.Read(_buffer, 0, toRead);
            if (read == 0)
                return;

            _remainingBytes -= read;
            if (_remainingBytes < 0)
                throw new InvalidDataException("The subtitle archive exceeds the decompressed size limit.");

            destination.Write(_buffer, 0, read);
        }
    }
}
