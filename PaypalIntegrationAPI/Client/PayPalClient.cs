using System.Net.Http.Headers;
using System.Text.Json;
using PayPalIntegrationAPI.Models;

namespace PayPalIntegrationAPI.Client
{
    public interface IPayPalClient
    {
        Task<string> GetAccessTokenAsync();
    }

    public class PayPalClient : IPayPalClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _clientId;
        private readonly string _secret;
        private readonly ILogger<PayPalClient> _logger;

        private string? _cachedToken;
        private DateTime _tokenExpiresAt;

        public PayPalClient(IHttpClientFactory factory, IConfiguration configuration, ILogger<PayPalClient> logger)
        {
            _http = factory.CreateClient("PayPal");
            _baseUrl = configuration["PayPal:BaseUrl"]?.TrimEnd('/') ?? throw new ArgumentException("PayPal:BaseUrl is not configured");
            _clientId = configuration["PayPal:ClientId"] ?? throw new ArgumentException("PayPal:ClientId is not configured");
            _secret = configuration["PayPal:Secret"] ?? throw new ArgumentException("PayPal:Secret is not configured");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogInformation("PayPalClient initialized (BaseUrl={BaseUrl})", _baseUrl);
        }

        public async Task<string> GetAccessTokenAsync()
        {

            if (!string.IsNullOrEmpty(_cachedToken) && _tokenExpiresAt > DateTime.UtcNow)
            {
                _logger.LogDebug("Using cached PayPal access token, expires at {ExpiresAt}", _tokenExpiresAt);
                return _cachedToken;
            }

            var authToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_clientId}:{_secret}"));

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            req.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            _logger.LogDebug("Requesting new PayPal access token from {Url}", req.RequestUri);
            using var response = await _http.SendAsync(req);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to request PayPal access token: {StatusCode} {Reason}. Response: {Response}", (int)response.StatusCode, response.ReasonPhrase, content);
                throw new HttpRequestException($"Error requesting PayPal access token: {(int)response.StatusCode} {response.ReasonPhrase}. Response: {content}");
            }

            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenElem) 
            && doc.RootElement.TryGetProperty("expires_in", out var expiresInElem))
            {
                _cachedToken = tokenElem.GetString()!;
                _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresInElem.GetInt32() - 60);
                _logger.LogInformation("Obtained new PayPal access token, expires at {ExpiresAt}", _tokenExpiresAt);
                return _cachedToken;
            }

            _logger.LogError("PayPal access token not found in response: {Response}", content);
            throw new InvalidOperationException("PayPal access token not found in response.");
        }
        
    }
}