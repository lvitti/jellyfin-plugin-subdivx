using System.Text;
using AutoFixture;
using AutoFixture.AutoMoq;
using Jellyfin.Plugin.Subdivx;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Moq;

namespace Jellyfin.Plugin.SubdivxTests;

public class SubdivXProviderTests
{
    private SubdivxProvider _provider;
    private Mock<ILogger<SubdivxProvider>> _logger;
    private Mock<ILibraryManager> _libraryManager;
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
        
        var testConfig = ConfigurationHelper.LoadConfig();
        mockPlugin.Setup(x => x.GetConfiguration()).Returns(testConfig);
        _ = mockPlugin.Object; // Force Instance initialization
        
        _logger = new Mock<ILogger<SubdivxProvider>>();
        _libraryManager = new Mock<ILibraryManager>();
        
        var mockProvider = new Mock<SubdivxProvider>(MockBehavior.Strict, _logger.Object, _libraryManager.Object);
        _provider = mockProvider.Object;
    }

    [TestCase("The Batman", 4, 6, "901212")]
    [TestCase("Dexter: New Blood", 1, 1, "694326")]
    [TestCase("Resident Alien", 2, 5, "801288")]
    public async Task SearchSerie(string serieName, int season, int episode, string id)
    {
        var request = new SubtitleSearchRequest()
        {
            SeriesName = serieName,
            ParentIndexNumber = season,
            IndexNumber = episode,
            ContentType = VideoContentType.Episode,
            Language = "SPA",
        };

        var baseItem = _fixture.Create<Mock<BaseItem>>();
        baseItem.Object.OriginalTitle = serieName;

        _libraryManager
            .Setup(x => x.FindByPath(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(baseItem.Object);

        var subtitles = await this._provider.Search(request, CancellationToken.None);

        Assert.That(subtitles, Is.Not.Null);
        Assert.That(subtitles.FirstOrDefault(p => p.Id == id), Is.Not.Null);
    }

    [TestCase("Bad Boys: Ride or Die", 2024, "752980")]
    public async Task SearchMovie(string movieName, int movieYear, string id)
    {
        var request = new SubtitleSearchRequest()
        {
            Name = movieName,
            ProductionYear = movieYear,
            ContentType = VideoContentType.Movie,
            Language = "SPA",
        };

        var baseItem = _fixture.Create<Mock<BaseItem>>();
        baseItem.Object.OriginalTitle = movieName;

        _libraryManager
            .Setup(x => x.FindByPath(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(baseItem.Object);

        var subtitles = await this._provider.Search(request, CancellationToken.None);

        Assert.That(subtitles, Is.Not.Null);
        Assert.That(subtitles.FirstOrDefault(p => p.Id == id), Is.Not.Null);
    }

    [TestCase("Resident Alien S02E05", "801288", 59526)]
    [TestCase("Dexter: New Blood S01E01", "694326", 42670)]
    [TestCase("The Batman S04E06", "901212", 14902)]
    [TestCase("Bad Boys: Ride or Die 2024", "752980", 121267)]
    public async Task DownloadSubtitle(string testName, string id, int length)
    {
        var subtitleResponse = await this._provider.GetSubtitles(id, CancellationToken.None);
        string subtitle = Encoding.UTF8.GetString((subtitleResponse.Stream as MemoryStream).ToArray());

        Assert.That(length, Is.EqualTo(subtitle.Length));
    }
}