using System.Net;
using System.Net.Sockets;
using System.Text;
using AutoFixture;
using AutoFixture.AutoMoq;
using Jellyfin.Plugin.Subdivx;
using Jellyfin.Plugin.Subdivx.Configuration;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Moq;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.SubdivxTests;

public class SubdivXProviderTests
{
    private SubdivxProvider _provider;
    private PluginConfiguration _configuration = null!;
    private Mock<ILogger<SubdivxProvider>> _logger;
    private FakeLibraryManager _libraryManager;
    private readonly IFixture _fixture = new Fixture().Customize(new AutoMoqCustomization());

    [SetUp]
    public void Setup()
    {
        var _applicationPaths = new Mock<IApplicationPaths>();
        _applicationPaths.SetupGet(x => x.ProgramDataPath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.WebPath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.ProgramSystemPath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.DataPath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.ImageCachePath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.PluginsPath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.PluginConfigurationsPath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.LogDirectoryPath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.ConfigurationDirectoryPath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.SystemConfigurationFilePath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.CachePath).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.TempDirectory).Returns(Path.GetTempPath());
        _applicationPaths.SetupGet(x => x.VirtualDataPath).Returns(Path.GetTempPath());
        
        var _xmlSerializer = new Mock<IXmlSerializer>();
        _xmlSerializer
            .Setup(x => x.DeserializeFromBytes(It.IsAny<Type>(), It.IsAny<byte[]>()))
            .Returns((Type type, byte[] buffer) => null);

        _xmlSerializer
            .Setup(x => x.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>()))
            .Returns((Type type, string file) => null);

        _xmlSerializer
            .Setup(x => x.DeserializeFromStream(It.IsAny<Type>(), It.IsAny<Stream>()))
            .Returns((Type type, Stream stream) => null);

        _xmlSerializer
            .Setup(x => x.SerializeToFile(It.IsAny<object>(), It.IsAny<string>()))
            .Verifiable();

        _xmlSerializer
            .Setup(x => x.SerializeToStream(It.IsAny<object>(), It.IsAny<Stream>()))
            .Verifiable();
        
        var mockPlugin = new Mock<SubdivxPlugin>(MockBehavior.Strict, _applicationPaths.Object, _xmlSerializer.Object);
        
        _configuration = ConfigurationHelper.LoadConfig();
        mockPlugin.Setup(x => x.GetConfiguration()).Returns(_configuration);
        _ = mockPlugin.Object; // Force Instance initialization
        
        _logger = new Mock<ILogger<SubdivxProvider>>();
        _libraryManager = new FakeLibraryManager();
        BaseItem.LibraryManager = _libraryManager;
        
        var applicationHost = new Mock<IApplicationHost>();
        
        var mockProvider = new Mock<SubdivxProvider>(MockBehavior.Strict, _logger.Object, _libraryManager, applicationHost.Object);
        _provider = mockProvider.Object;
    }

    [TestCase("The Batman", 4, 6, "56b1a743-de52-46da-91a1-2c91c9b1427a")]
    [TestCase("Dexter: New Blood", 1, 1, "098d1c77-508f-4cfb-913e-d34e541fb65c")]
    [TestCase("Resident Alien", 2, 5, "6b9f7cd4-f60d-4b1d-8f33-1fed1f3f999b")]
    public async Task SearchSerie(string serieName, int seasonNumber, int episodeNumber, string id)
    {
        var serie = new Series
        {
            Id = Guid.NewGuid(),
            Path = $"/Shows/{serieName}",
            OriginalTitle = serieName,
            Name = serieName
        };
        serie.SetProviderId(MetadataProvider.Imdb, "ttShowImdbId");
        _libraryManager.AddToLibrary(serie);
        
        var season = new Season()
        {
            Id = Guid.NewGuid(),
            SeriesId = serie.Id,
            Path = $"/Shows/{serieName}/Season {seasonNumber}",
            IndexNumber = seasonNumber
        };
        _libraryManager.AddToLibrary(season);
        
        var episode = new Episode
        {
            Id = Guid.NewGuid(),
            Path = $"/Shows/{serieName}/Season {seasonNumber}/{serieName} S{seasonNumber:00}E{episodeNumber:00}.mkv",
            SeriesId = serie.Id,
            SeasonId = season.Id,
            IndexNumber = episodeNumber,
            OriginalTitle = serieName,
        };
        episode.SetProviderId(MetadataProvider.Imdb, "ttEpisodeImdbId");
        _libraryManager.AddToLibrary(episode);
        
        var request = new SubtitleSearchRequest()
        {
            MediaPath = episode.Path,
            SeriesName = serieName,
            ParentIndexNumber = seasonNumber,
            IndexNumber = episodeNumber,
            ContentType = VideoContentType.Episode,
            Language = "SPA",
        };
        
        var subtitles = await this._provider.Search(request, CancellationToken.None);

        Assert.That(subtitles, Is.Not.Null);
        Assert.That(subtitles.FirstOrDefault(p => p.Id == id), Is.Not.Null);
    }

    [TestCase("Bad Boys: Ride or Die", 2024, "f117211a-63f3-4485-bb1e-d347750891be")]
    public async Task SearchMovie(string movieName, int movieYear, string id)
    {
        var movie = new Movie()
        {
            Id = Guid.NewGuid(),
            Path = $"/Movies/{movieName} ({movieYear}).mkv",
            OriginalTitle = movieName,
            Name = movieName,
            ProductionYear = movieYear,
        };
        _libraryManager.AddToLibrary(movie);
        
        var request = new SubtitleSearchRequest()
        {
            MediaPath = movie.Path,
            Name = movieName,
            ProductionYear = movieYear,
            ContentType = VideoContentType.Movie,
            Language = "SPA",
        };
        
        var subtitles = await this._provider.Search(request, CancellationToken.None);
        Assert.That(subtitles, Is.Not.Empty);
        Assert.That(subtitles.FirstOrDefault(p => p.Id == id), Is.Not.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DownloadSubtitleFromLocalArchive(bool hasSubtitle)
    {
        var subtitle = "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nHola\n"u8.ToArray();
        var entry = hasSubtitle ? ("movie.vtt", subtitle) : ("README.txt", "Release credits"u8.ToArray());
        using var archive = SubtitleArchiveTests.CreateZip(entry);
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        _configuration.SubXApiUrl = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serveArchive = ServeArchive(server, archive, timeout.Token);
        if (hasSubtitle)
        {
            var result = await _provider.GetSubtitles("subtitle-id", timeout.Token);

            using var stream = result.Stream;
            Assert.That(result.Format, Is.EqualTo("vtt"));
            Assert.That(((MemoryStream)stream).ToArray(), Is.EqualTo(subtitle));
        }
        else
        {
            Assert.That(async () => await _provider.GetSubtitles("subtitle-id", timeout.Token),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("no supported subtitle"));
        }

        await serveArchive;
    }

    private static async Task ServeArchive(TcpListener server, Stream archive, CancellationToken cancellationToken)
    {
        using var client = await server.AcceptTcpClientAsync(cancellationToken);
        using var stream = client.GetStream();
        using var request = new StreamReader(stream, leaveOpen: true);
        while (await request.ReadLineAsync(cancellationToken) is { Length: > 0 })
        {
        }

        var headers = $"HTTP/1.1 200 OK\r\nContent-Length: {archive.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
        await archive.CopyToAsync(stream, cancellationToken);
    }

    [TestCase("Resident Alien S02E05", "6b9f7cd4-f60d-4b1d-8f33-1fed1f3f999b", 59526)]
    [TestCase("Dexter: New Blood S01E01", "06cbdb1f-e034-4537-a007-5cdc30d6c869", 42670)]
    [TestCase("The Batman S04E06", "56b1a743-de52-46da-91a1-2c91c9b1427a", 14902)]
    [TestCase("Bad Boys: Ride or Die 2024", "6373f0a7-5011-4b31-a6f8-59884cc7b4fd", 121267)]
    [TestCase("El último aplauso (2009)", "ab9f9cdd-ab48-4c55-abf9-1500864b45ea", 90322)]
    public async Task DownloadSubtitle(string testName, string id, int length)
    {
        var subtitleResponse = await this._provider.GetSubtitles(id, CancellationToken.None);
        var subtitle = Encoding.UTF8.GetString((subtitleResponse.Stream as MemoryStream)?.ToArray() ?? []);

        Assert.That(length, Is.EqualTo(subtitle.Length));
    }
}