using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace AgentsHub.Services;

public sealed record TokenRequestRecord(
    DateTime Timestamp,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string Provider,
    double EstimatedSavingsDollars
);

public class TokenTelemetryDto
{
    [JsonPropertyName("totalTokens")]
    public long TotalTokens { get; set; }

    [JsonPropertyName("promptTokens")]
    public long PromptTokens { get; set; }

    [JsonPropertyName("completionTokens")]
    public long CompletionTokens { get; set; }

    [JsonPropertyName("totalRequests")]
    public long TotalRequests { get; set; }

    [JsonPropertyName("estimatedSavingsDollars")]
    public double EstimatedSavingsDollars { get; set; }

    [JsonPropertyName("requestsPerMinute")]
    public int RequestsPerMinute { get; set; }

    [JsonPropertyName("modelsBreakdown")]
    public List<ModelUsageBreakdownDto> ModelsBreakdown { get; set; } = [];

    [JsonPropertyName("timeSeries")]
    public List<TimeSeriesBucketDto> TimeSeries { get; set; } = [];
}

public class ModelUsageBreakdownDto
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("tokens")]
    public long Tokens { get; set; }

    [JsonPropertyName("requests")]
    public long Requests { get; set; }

    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }
}

public class TimeSeriesBucketDto
{
    [JsonPropertyName("timeLabel")]
    public string TimeLabel { get; set; } = "";

    [JsonPropertyName("tokens")]
    public long Tokens { get; set; }

    [JsonPropertyName("requests")]
    public int Requests { get; set; }
}

public sealed class ProxyTelemetryService
{
    private static readonly Lazy<ProxyTelemetryService> _instance = new(() => new ProxyTelemetryService());
    public static ProxyTelemetryService Instance => _instance.Value;

    private readonly ConcurrentQueue<TokenRequestRecord> _records = new();
    private readonly Lock _sync = new();
    private const int MaxRecords = 10000;

    public event Action? TelemetryUpdated;

    private ProxyTelemetryService() { }

    public void RecordRequest(string model, int promptTokens, int completionTokens, string provider = "OpenRouter")
    {
        int total = promptTokens + completionTokens;
        if (total <= 0) total = 1;

        // Estimated savings based on official pricing baseline (e.g. Claude 3.5 Sonnet ~$3/M tokens)
        double ratePerMillion = model.Contains("haiku", StringComparison.OrdinalIgnoreCase) || model.Contains("mini", StringComparison.OrdinalIgnoreCase)
            ? 0.25
            : 3.00;
        double savings = (total / 1_000_000.0) * ratePerMillion;

        var record = new TokenRequestRecord(
            DateTime.UtcNow,
            string.IsNullOrWhiteSpace(model) ? "General / Free" : model,
            promptTokens,
            completionTokens,
            total,
            provider,
            savings
        );

        _records.Enqueue(record);

        while (_records.Count > MaxRecords && _records.TryDequeue(out _)) { }

        TelemetryUpdated?.Invoke();
    }

    public TokenTelemetryDto GetSummary()
    {
        var records = _records.ToArray();
        var now = DateTime.UtcNow;
        var oneMinAgo = now.AddMinutes(-1);
        var thirtyMinAgo = now.AddMinutes(-30);

        long totalTokens = 0;
        long promptTokens = 0;
        long completionTokens = 0;
        double totalSavings = 0;
        int reqsLastMin = 0;

        var modelCounts = new Dictionary<string, (long Tokens, long Requests)>(StringComparer.OrdinalIgnoreCase);

        // Prepare 15 two-minute buckets for timeSeries chart
        var buckets = new (DateTime Start, DateTime End, long Tokens, int Requests)[15];
        for (int i = 0; i < 15; i++)
        {
            var end = now.AddMinutes(-(14 - i) * 2);
            var start = end.AddMinutes(-2);
            buckets[i] = (start, end, 0, 0);
        }

        foreach (var r in records)
        {
            totalTokens += r.TotalTokens;
            promptTokens += r.PromptTokens;
            completionTokens += r.CompletionTokens;
            totalSavings += r.EstimatedSavingsDollars;

            if (r.Timestamp >= oneMinAgo)
            {
                reqsLastMin++;
            }

            if (!modelCounts.TryGetValue(r.Model, out var current))
            {
                current = (0, 0);
            }
            modelCounts[r.Model] = (current.Tokens + r.TotalTokens, current.Requests + 1);

            if (r.Timestamp >= thirtyMinAgo)
            {
                for (int i = 0; i < 15; i++)
                {
                    if (r.Timestamp >= buckets[i].Start && r.Timestamp < buckets[i].End)
                    {
                        buckets[i].Tokens += r.TotalTokens;
                        buckets[i].Requests++;
                        break;
                    }
                }
            }
        }

        var breakdown = new List<ModelUsageBreakdownDto>();
        foreach (var kvp in modelCounts.OrderByDescending(k => k.Value.Tokens).Take(6))
        {
            double pct = totalTokens > 0 ? (double)kvp.Value.Tokens / totalTokens * 100.0 : 0.0;
            breakdown.Add(new ModelUsageBreakdownDto
            {
                Model = kvp.Key,
                Tokens = kvp.Value.Tokens,
                Requests = kvp.Value.Requests,
                Percentage = Math.Round(pct, 1)
            });
        }

        var timeSeries = new List<TimeSeriesBucketDto>();
        for (int i = 0; i < 15; i++)
        {
            timeSeries.Add(new TimeSeriesBucketDto
            {
                TimeLabel = buckets[i].End.ToLocalTime().ToString("HH:mm"),
                Tokens = buckets[i].Tokens,
                Requests = buckets[i].Requests
            });
        }

        return new TokenTelemetryDto
        {
            TotalTokens = totalTokens,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalRequests = records.Length,
            EstimatedSavingsDollars = Math.Round(totalSavings, 3),
            RequestsPerMinute = reqsLastMin,
            ModelsBreakdown = breakdown,
            TimeSeries = timeSeries
        };
    }

    public void Reset()
    {
        while (_records.TryDequeue(out _)) { }
        TelemetryUpdated?.Invoke();
    }
}
