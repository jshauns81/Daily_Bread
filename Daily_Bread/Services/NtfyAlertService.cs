namespace Daily_Bread.Services;

/// <summary>
/// Sends high-priority family alerts (e.g. "child needs help") to a self-hosted
/// ntfy server, which delivers reliable native push to the parents' phones —
/// the channel that must never fall on deaf ears. Configured via env:
/// NTFY_URL (e.g. http://ntfy), NTFY_TOPIC, NTFY_TOKEN. No-ops if unconfigured.
/// </summary>
public interface INtfyAlertService
{
    Task SendHelpAlertAsync(string childName, string choreName, string? reason, int choreLogId);
}

public class NtfyAlertService : INtfyAlertService
{
    private readonly HttpClient _http;
    private readonly ILogger<NtfyAlertService> _logger;

    public NtfyAlertService(HttpClient http, ILogger<NtfyAlertService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task SendHelpAlertAsync(string childName, string choreName, string? reason, int choreLogId)
    {
        var baseUrl = Environment.GetEnvironmentVariable("NTFY_URL");
        var topic = Environment.GetEnvironmentVariable("NTFY_TOPIC");
        var token = Environment.GetEnvironmentVariable("NTFY_TOKEN");

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(token))
        {
            _logger.LogDebug("ntfy not configured (NTFY_URL/TOPIC/TOKEN) - skipping help alert");
            return;
        }

        var body = string.IsNullOrEmpty(reason)
            ? $"Help requested with: {choreName}"
            : $"{choreName}: {reason}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/{topic}")
            {
                Content = new StringContent(body)
            };
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            // ASCII-only headers (emoji via Tags, not Title, to avoid header encoding issues).
            req.Headers.TryAddWithoutValidation("Title", $"{childName} needs help!");
            req.Headers.TryAddWithoutValidation("Priority", "urgent");
            req.Headers.TryAddWithoutValidation("Tags", "sos");
            // Click-through target comes from env so the codebase stays
            // deployment-agnostic; header is omitted when unset.
            var publicBaseUrl = Environment.GetEnvironmentVariable("PUBLIC_BASE_URL")?.TrimEnd('/');
            if (!string.IsNullOrEmpty(publicBaseUrl))
            {
                req.Headers.TryAddWithoutValidation("Click", $"{publicBaseUrl}/?helpRequestId={choreLogId}");
            }

            var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("ntfy help alert sent: child={Child} chore={Chore} status={Status}",
                    childName, choreName, (int)resp.StatusCode);
            }
            else
            {
                var detail = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("ntfy help alert FAILED: status={Status} detail={Detail}",
                    (int)resp.StatusCode, detail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ntfy help alert threw for child={Child} chore={Chore}", childName, choreName);
        }
    }
}
