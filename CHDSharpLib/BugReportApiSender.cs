using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CHDSharp.Models;

namespace CHDSharp;

internal sealed class BugReportApiSender : IDisposable
{
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
    private const string Endpoint = "https://www.purelogiccode.com/bugreport/api/send-bug-report";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http = new();

    internal async Task SendAsync(BugReport report, CancellationToken ct)
    {
        var payload = new BugReportPayload
        {
            Message = report.Message,
            ApplicationName = report.ApplicationName,
            Version = report.Version,
            UserInfo = report.UserInfo,
            Environment = report.Environment,
            StackTrace = report.StackTrace
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Content = JsonContent.Create(payload, options: JsonOptions);
        req.Headers.Add("X-API-KEY", ApiKey);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            await _http.SendAsync(req, cts.Token);
        }
        catch
        {
            // Silently ignore — never let bug reporting affect the caller.
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private sealed class BugReportPayload
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("applicationName")]
        public string? ApplicationName { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("userInfo")]
        public string? UserInfo { get; set; }

        [JsonPropertyName("environment")]
        public string? Environment { get; set; }

        [JsonPropertyName("stackTrace")]
        public string? StackTrace { get; set; }
    }
}
