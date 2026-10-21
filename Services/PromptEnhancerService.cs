using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentsHub.Services;

public class PromptEnhancerService
{
    private static readonly Lazy<PromptEnhancerService> _instance = new(() => new PromptEnhancerService());
    public static PromptEnhancerService Instance => _instance.Value;

    private readonly HttpClient _httpClient;

    public PromptEnhancerService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public const double MinTemperature = 0.0;
    public const double MaxTemperature = 1.5;
    public const double DefaultTemperature = 0.6;

    public async Task<string> EnhancePromptAsync(
        AiProvider provider,
        string model,
        string rawPrompt,
        string enhancementStyle = "General & Balanced",
        string? customSystemPrompt = null,
        double? temperature = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPrompt))
        {
            throw new ArgumentException("Please enter a prompt to enhance.");
        }

        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            throw new InvalidOperationException("Selected provider has no base URL configured.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Please select a model to use for enhancement.");
        }

        var endpoint = provider.BaseUrl.Trim().TrimEnd('/');
        if (!endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            endpoint += "/chat/completions";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        if (!string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey.Trim());
        }

        // OpenRouter optional identification headers
        if (provider.BaseUrl.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/ZerroDevs/AgentsHub");
            request.Headers.TryAddWithoutValidation("X-Title", "Agents Hub");
        }

        // A user-supplied system prompt always wins; the built-in style switch is the fallback.
        var systemPrompt = !string.IsNullOrWhiteSpace(customSystemPrompt)
            ? customSystemPrompt.Trim()
            : GetSystemPromptForStyle(enhancementStyle);

        var effectiveTemperature = DefaultTemperature;
        if (temperature.HasValue && !double.IsNaN(temperature.Value))
        {
            effectiveTemperature = Math.Clamp(temperature.Value, MinTemperature, MaxTemperature);
        }

        var requestData = new
        {
            model = model.Trim(),
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Original Prompt to Enhance:\n\n{rawPrompt.Trim()}" }
            },
            temperature = effectiveTemperature
        };

        var requestJson = JsonSerializer.Serialize(requestData);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Exception("Request timed out while waiting for AI provider response.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new Exception($"Network error reaching {provider.Name}: {ex.Message}");
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string parsedError = ExtractErrorMessage(responseBody);
                throw new Exception($"API Error (HTTP {(int)response.StatusCode}): {parsedError}");
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("content", out var contentElem))
                    {
                        var content = contentElem.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(content))
                        {
                            int inT = 0, outT = 0;
                            if (root.TryGetProperty("usage", out var usg))
                            {
                                if (usg.TryGetProperty("prompt_tokens", out var pT)) inT = pT.GetInt32();
                                if (usg.TryGetProperty("completion_tokens", out var cT)) outT = cT.GetInt32();
                            }
                            if (inT == 0 && outT == 0)
                            {
                                inT = rawPrompt.Length / 4;
                                outT = content.Length / 4;
                            }
                            ProxyTelemetryService.Instance.RecordRequest(model, inT, outT, provider.Name);
                            return content;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                throw new Exception("Unable to parse JSON response from AI provider.");
            }

            throw new Exception("Provider returned an empty response with no completion message.");
        }
    }

    public async Task<List<string>> FetchAvailableModelsAsync(AiProvider provider, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            throw new InvalidOperationException("Provider base URL is empty.");
        }

        var endpoint = provider.BaseUrl.Trim().TrimEnd('/');
        if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = endpoint[..^"/chat/completions".Length];
        }
        endpoint += "/models";

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey.Trim());
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to fetch models (HTTP {(int)response.StatusCode}): {ExtractErrorMessage(body)}");
        }

        var models = new List<string>();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataArr.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idElem))
                {
                    var id = idElem.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        models.Add(id);
                    }
                }
            }
        }

        models.Sort(StringComparer.OrdinalIgnoreCase);
        return models;
    }

    private static string GetSystemPromptForStyle(string style)
    {
        return style switch
        {
            "Code & Architecture" =>
                "You are a principal software engineer and expert prompt craftsman. Rewrite the user's prompt to be highly rigorous, specifying programming language, modular architecture, interfaces, type safety, error handling, performance considerations, edge cases, and concrete deliverables. Return ONLY the enhanced prompt without meta-commentary.",

            "Direct & Concise" =>
                "You are an expert prompt engineer. Refine the user's prompt into an unambiguous, concise, and focused set of instructions. Strip redundant words while making instructions and output formats 100% explicit. Return ONLY the enhanced prompt without meta-commentary.",

            "Comprehensive & Structured" =>
                "You are an expert prompt engineer. Transform the user's prompt into a complete, structured master prompt with clear sections: Role, Context, Primary Objective, Detailed Instructions, Constraints, and Expected Output Format. Return ONLY the enhanced prompt without meta-commentary.",

            _ =>
                "You are an elite prompt engineer. Analyze the user's raw prompt and rewrite it to be clear, effective, and structured so advanced AI models produce their best response. Flesh out context, clarify objectives, define constraints, and specify format. Return ONLY the enhanced prompt without preamble or conversational remarks."
        };
    }

    private static string ExtractErrorMessage(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return "No details provided by server.";

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String)
                {
                    return err.GetString() ?? rawJson;
                }
                if (err.TryGetProperty("message", out var msg))
                {
                    return msg.GetString() ?? rawJson;
                }
            }
        }
        catch
        {
            // Not valid JSON
        }

        return rawJson.Length > 200 ? rawJson[..200] + "..." : rawJson;
    }
}
