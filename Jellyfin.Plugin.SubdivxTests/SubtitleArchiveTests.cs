using System.IO.Compression;
using Jellyfin.Plugin.Subdivx;

namespace Jellyfin.Plugin.SubdivxTests;

public class SubtitleArchiveTests
{
    private static readonly byte[] Srt = "1\n00:00:00,000 --> 00:00:01,000\nHola\n"u8.ToArray();
    private static readonly byte[] Ass = "[Script Info]\nScriptType: v4.00+\n[Events]\nDialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,Hola\n"u8.ToArray();

    [Test]
    public void IgnoresReadmeAndMetadataAndReturnsRealFormat()
    {
        using var archive = CreateZip(
            ("README.txt", new byte[1024]),
            ("__MACOSX/._episode.srt", new byte[1024]),
            ("folder/._episode.srt", new byte[1024]),
            ("episode.ASS", Ass));

        var result = SubtitleArchive.Extract(archive, CancellationToken.None);

        using var stream = result.Stream;
        Assert.That(result.Format, Is.EqualTo("ass"));
        Assert.That(stream.Position, Is.Zero);
        Assert.That(((MemoryStream)stream).ToArray(), Is.EqualTo(Ass));
    }

    [TestCase("subtitle.rar")]
    [TestCase("subtitle.7z")]
    public void ExtractsNestedArchives(string fileName)
    {
        var nested = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fileName));
        using var archive = CreateZip((fileName.ToUpperInvariant(), nested), ("README.txt", [1]));

        var result = SubtitleArchive.Extract(archive, CancellationToken.None);

        using var stream = result.Stream;
        Assert.That(result.Format, Is.EqualTo("srt"));
        Assert.That(((MemoryStream)stream).ToArray(), Is.EqualTo(Srt));
    }

    [Test]
    public void ExtractsTopLevelSevenZip()
    {
        using var archive = File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "subtitle.7z"));

        var result = SubtitleArchive.Extract(archive, CancellationToken.None);

        using var stream = result.Stream;
        Assert.That(result.Format, Is.EqualTo("srt"));
        Assert.That(((MemoryStream)stream).ToArray(), Is.EqualTo(Srt));
    }

    [Test]
    public void KeepsLargestSubtitleAndFirstInATie()
    {
        using var nested = CreateZip(("episode.ass", Ass));
        using var archive = CreateZip(
            ("episode.srt", Srt),
            ("nested.zip", nested.ToArray()),
            ("other.ssa", new byte[Ass.Length]));

        var result = SubtitleArchive.Extract(archive, CancellationToken.None);

        using var stream = result.Stream;
        Assert.That(result.Format, Is.EqualTo("ass"));
        Assert.That(((MemoryStream)stream).ToArray(), Is.EqualTo(Ass));
    }

    [Test]
    public void FailsWhenOnlyUnsupportedOrEmptyEntriesExist()
    {
        using var archive = CreateZip(
            ("README.txt", "Release credits"u8.ToArray()),
            ("movie.srt", []),
            ("__MACOSX/movie.srt", Srt));

        Assert.That(() => SubtitleArchive.Extract(archive, CancellationToken.None),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("no supported subtitle"));
    }

    [Test]
    public void SkipsArchivesBeyondTheDepthLimit()
    {
        using var inner = CreateZip(("episode.srt", Srt));
        var bytes = inner.ToArray();
        for (var level = 0; level < 4; level++)
        {
            using var wrapper = CreateZip(("nested.zip", bytes));
            bytes = wrapper.ToArray();
        }

        using var archive = new MemoryStream(bytes);
        Assert.That(() => SubtitleArchive.Extract(archive, CancellationToken.None),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("no supported subtitle"));
    }

    [Test]
    public void CountsIgnoredEntriesAgainstTheExpandedSizeLimit()
    {
        using var archive = CreateZip(("README.txt", new byte[64 * 1024 * 1024]), ("episode.srt", Srt));

        Assert.That(() => SubtitleArchive.Extract(archive, CancellationToken.None),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("decompressed size limit"));
    }

    internal static MemoryStream CreateZip(params (string Name, byte[] Contents)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, contents) in entries)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(contents);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
