using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LakeCompanion.Application;
using Microsoft.Extensions.Options;

namespace LakeCompanion.Infrastructure;

/// <summary>Configuration for a local Ollama daemon. No cloud endpoint is supported.</summary>
public sealed class OllamaOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ollama";

    /// <summary>Gets or sets the Ollama HTTP base address.</summary>
    public Uri Endpoint { get; set; } = new("http://localhost:11434/");

    /// <summary>Gets or sets the installed local chat model name.</summary>
    public string Model { get; set; } = "llama3.2:3b";

    /// <summary>Gets or sets the maximum time for one complete local generation, including response-body reading.</summary>
    public int GenerationTimeoutSeconds { get; set; } = 25;
}

/// <summary>Small, provider-isolated client for Ollama's non-streaming generation endpoint.</summary>
public sealed class OllamaClient(HttpClient httpClient, IOptions<OllamaOptions> options) : ILocalLanguageModel
{
    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var configuration = options.Value;
        var requestUri = new Uri(configuration.Endpoint, "api/generate");
        using var generationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        generationTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.GenerationTimeoutSeconds, 5, 120)));

        using var response = await httpClient.PostAsJsonAsync(
            requestUri,
            new OllamaGenerateRequest(configuration.Model, prompt, Stream: false),
            generationTimeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(generationTimeout.Token).ConfigureAwait(false);
        return payload?.Response ?? string.Empty;
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OllamaGenerateResponse([property: JsonPropertyName("response")] string? Response);
}
