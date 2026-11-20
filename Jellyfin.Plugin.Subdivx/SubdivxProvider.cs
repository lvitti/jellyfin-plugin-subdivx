using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jellyfin.Plugin.Subdivx.Configuration;
using MediaBrowser.Common;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Subdivx;

public class SubdivxProvider: ISubtitleProvider, IHasOrder
{
    public string Name => "Subdivx";
    public IEnumerable<VideoContentType> SupportedMediaTypes => new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };
    public int Order => 1;
    
    private readonly ILogger<SubdivxProvider> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IApplicationHost _applicationHost;

    private PluginConfiguration Configuration => SubdivxPlugin.Instance?.GetConfiguration();
    
    public SubdivxProvider(ILogger<SubdivxProvider> logger, ILibraryManager libraryManager, IApplicationHost appHost)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _applicationHost = appHost;
    }
    
    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var json = JsonSerializer.Serialize(request);
            _logger.LogInformation("Subdivx Search | Request-> {Json}", json);
        }, cancellationToken);

        if (!string.Equals(request.Language, "SPA", StringComparison.OrdinalIgnoreCase))
            return [];

        var item = _libraryManager.FindByPath(request.MediaPath, false);

        switch (item)
        {
            case Episode episode:
            {
                var name = episode.Series.Name;
                if (Configuration.UseOriginalTitle)
                    if (!string.IsNullOrWhiteSpace(episode.OriginalTitle))
                        name = episode.OriginalTitle;

                var query = $"{name} S{episode.Season.IndexNumber:D2}E{episode.IndexNumber:D2}";
            
                var seriesImdb = episode.GetProviderId(MetadataProvider.Imdb) ?? episode.Series?.GetProviderId(MetadataProvider.Imdb);
                var seriesTmdb = episode.GetProviderId(MetadataProvider.Tmdb) ?? episode.Series?.GetProviderId(MetadataProvider.Tmdb);
            
                var subtitles = SearchSubtitles(query, seriesImdb, seriesTmdb);
                if (subtitles.Count > 0)
                    return subtitles;
                break;
            }
            case Movie movie:
            {
                var name = movie.Name;
                if (Configuration.UseOriginalTitle)
                    if (!string.IsNullOrWhiteSpace(movie.OriginalTitle))
                        name = movie.OriginalTitle;

                var query = $"{name} {movie.ProductionYear}";

                var imdbId = movie.GetProviderId(MetadataProvider.Imdb);
                var tmdbId = movie.GetProviderId(MetadataProvider.Tmdb);
            
                var subtitles = SearchSubtitles(query, imdbId, tmdbId);
                if (subtitles.Count > 0)
                    return subtitles;
                break;
            }
        }

        return [];
    }

    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configuration = Configuration;
        var url = $"{configuration.SubXApiUrl}/api/subtitles/{id}/download";
        using var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(90),
            MaxResponseContentBufferSize = 32 * 1024 * 1024
        };

        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        if (!string.IsNullOrEmpty(configuration.Token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", configuration.Token);

        using var response = await DownloadArchive(client, url, cancellationToken).ConfigureAwait(false);
        using var archive = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return SubtitleArchive.Extract(archive, cancellationToken);
    }
    
    private class SearchResponse
    {
        public List<ItemResponse> items { get; set; }
    }

    private class ItemResponse
    {
        public string id { get; set; }
        public string title{ get; set; }
        public string? description{ get; set; }
        public int downloads{ get; set; }
        public string uploader_name{ get; set; }
    }
        
    private List<RemoteSubtitleInfo> SearchSubtitles(string query, string? imdbId = null, string? tmdbId = null)
    {
        string url = $"{this.Configuration.SubXApiUrl}/api/subtitles/search";
        var searchParams = new Dictionary<string, string> { { "query", query } };

        if (!string.IsNullOrWhiteSpace(imdbId))
            searchParams.Add("imdb_id", imdbId);
        if (!string.IsNullOrWhiteSpace(tmdbId))
            searchParams.Add("tmdb_id", tmdbId);
        
        var data = GetJson<SearchResponse>(url, "GET",
            searchParams,new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {this.Configuration.Token}" },
                { "accept", "application/json" },
            });
        
        
        var subtitles = new List<RemoteSubtitleInfo>();
        foreach (var x in data?.items ?? [])
        {
            var sub = new RemoteSubtitleInfo()
            {
                Name = "",
                ThreeLetterISOLanguageName = "ESP",
                Id = x.id,
                DownloadCount = x.downloads,
                Author = x.uploader_name,
                ProviderName = Name
            };
            if (Configuration?.ShowTitleInResult == true || Configuration?.ShowUploaderInResult == true)
            {
                if (Configuration.ShowTitleInResult)
                    sub.Name = x.title;
                
                if (Configuration.ShowUploaderInResult)
                    sub.Name += (Configuration.ShowTitleInResult ? " | " : "") + $"Uploader: {x.uploader_name}";
                
                sub.Comment = x.description;
            }
            else
            {
                sub.Name = x.description;
            }

            subtitles.Add(sub);
        }

        return subtitles;
    }

    private static bool TryParseAuthHeader(string value, out string? scheme, out string? parameter)
    {
        scheme = null;
        parameter = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var idx = value.IndexOf(' ');
        if (idx <= 0 || idx >= value.Length - 1) return false;

        scheme = value[..idx];
        parameter = value[(idx + 1)..].Trim();
        return true;
    }
    
    private T? GetJson<T>(
        string urlAddress,
        string method = "POST",
        Dictionary<string, string>? parameters = null,
        Dictionary<string, string>? headers = null)
    {
        _logger.LogInformation("GetJson Url: {UrlAddress} Method: {Method}", urlAddress, method);

        var httpMethod = new HttpMethod((method).ToUpperInvariant());

        if ((httpMethod == HttpMethod.Get || httpMethod == HttpMethod.Delete) && parameters is { Count: > 0 })
        {
            var qs = string.Join("&", parameters.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            urlAddress += (urlAddress.Contains('?') ? "&" : "?") + qs;
        }

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(httpMethod, urlAddress);

        if ((httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put || httpMethod.Method == "PATCH") && parameters is { Count: > 0 })
            request.Content = new FormUrlEncodedContent(parameters);

        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseAuthHeader(value, out var scheme, out var parameter))
                    {
                        if (scheme != null)
                            request.Headers.Authorization = new AuthenticationHeaderValue(scheme, parameter);
                    }
                    else
                        request.Headers.TryAddWithoutValidation(key, value);
                }
                else if (key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Accept.Clear();
                    var parts = value.Split([','], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var v in parts)
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(v.Trim()));
                }
                else if (key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.UserAgent.Clear();
                    request.Headers.UserAgent.ParseAdd(value);
                }
                else if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Host = value;
                }
                else if (!request.Headers.TryAddWithoutValidation(key, value))
                {
                    request.Content ??= new ByteArrayContent([]);
                    request.Content.Headers.TryAddWithoutValidation(key, value);
                }
            }
        }
        
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.ParseAdd(
            $"Jellyfin-Plugin-Subdivx/{SubdivxPlugin.Instance?.Version?.ToString() ?? "unknown"} (Jellyfin {_applicationHost?.ApplicationVersion?.ToString() ?? "unknown" })"
            );

        var response = client.SendAsync(request).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var jsonResponseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<T>(jsonResponseString);
    }
    
    private async Task<HttpResponseMessage> DownloadArchive(HttpClient client, string url, CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _logger.LogInformation("Download subtitle, {SubtitleUrl}", url);
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return response;

                using (response)
                    response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex) when (
                attempt < maxAttempts &&
                !cancellationToken.IsCancellationRequested &&
                ex.HttpRequestError != HttpRequestError.ConfigurationLimitExceeded &&
                (ex.StatusCode is null or HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError))
            {
                _logger.LogInformation(ex, "Subtitle download attempt {Attempt} failed", attempt);
            }
            catch (OperationCanceledException ex) when (
                attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(ex, "Subtitle download attempt {Attempt} timed out", attempt);
            }
        }
    }
}
