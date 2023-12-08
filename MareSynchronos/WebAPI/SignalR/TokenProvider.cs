using MareSynchronos.API.Routes;
using MareSynchronos.Services;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Reflection;

namespace MareSynchronos.WebAPI.SignalR;

public sealed class TokenProvider : IDisposable
{
    private readonly DalamudUtilService _dalamudUtil;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TokenProvider> _logger;
    private readonly ServerConfigurationManager _serverManager;
    private readonly ConcurrentDictionary<JwtIdentifier, string> _tokenCache = new();

    public TokenProvider(ILogger<TokenProvider> logger, ServerConfigurationManager serverManager, DalamudUtilService dalamudUtil)
    {
        _logger = logger;
        _serverManager = serverManager;
        _dalamudUtil = dalamudUtil;
        _httpClient = new();
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", ver!.Major + "." + ver!.Minor + "." + ver!.Build));
    }

    private JwtIdentifier CurrentIdentifier => new(_serverManager.CurrentApiUrl, _serverManager.GetSecretKey()!);

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<string> GetNewToken(CancellationToken token)
    {
        Uri tokenUri;
        string response = string.Empty;
        HttpResponseMessage result;

        try
        {
            _logger.LogDebug("GetNewToken: Requesting");

            tokenUri = MareAuth.AuthFullPath(new Uri(_serverManager.CurrentApiUrl
                .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
            var secretKey = _serverManager.GetSecretKey()!;
            var auth = secretKey.GetHash256();
            result = await _httpClient.PostAsync(tokenUri, new FormUrlEncodedContent(new[]
            {
                        new KeyValuePair<string, string>("auth", auth),
                        new KeyValuePair<string, string>("charaIdent", await _dalamudUtil.GetPlayerNameHashedAsync().ConfigureAwait(false)),
            }), token).ConfigureAwait(false);

            response = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
            result.EnsureSuccessStatusCode();
            _tokenCache[CurrentIdentifier] = response;
        }
        catch (HttpRequestException ex)
        {
            _tokenCache.TryRemove(CurrentIdentifier, out _);

            _logger.LogError(ex, "GetNewToken: Failure to get token");

            if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new MareAuthFailureException(response);
            }

            throw;
        }

        _logger.LogTrace("GetNewToken: JWT {token}", response);
        return response;
    }

    public async Task<string?> GetOrUpdateToken(CancellationToken ct)
    {
        if (_tokenCache.TryGetValue(CurrentIdentifier, out var token))
            return token;

        _logger.LogTrace("GetOrUpdate: Getting new token");
        return await GetNewToken(ct).ConfigureAwait(false);
    }
}