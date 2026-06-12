using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;

namespace FrostAura.Reel.Infrastructure.Adapters;

/// <summary>
/// OpenAI-compatible /embeddings adapter (api.openai.com by default; EMBEDDINGS_BASE_URL
/// overridable). Returns L2-normalized vectors so pgvector cosine and dot products agree.
/// </summary>
public class OpenAiEmbeddingProvider(HttpClient httpClient, ApiUsageRecorder usageRecorder, IConfiguration configuration)
    : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private string? ApiKey => configuration["OPENAI_API_KEY"] is { Length: > 0 } key ? key : null;

    public bool IsAvailable => ApiKey is not null;

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not configured — embeddings unavailable.");
        }

        if (texts.Count == 0)
        {
            return [];
        }

        usageRecorder.Record(ApiProvider.OpenRouter); // unified LLM-spend ledger bucket

        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(new
            {
                model = configuration["EMBEDDINGS_MODEL"] ?? "text-embedding-3-small",
                input = texts,
            }, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<EmbeddingsResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Empty embeddings response.");

        return dto.Data
            .OrderBy(d => d.Index)
            .Select(d => Normalize(d.Embedding))
            .ToArray();
    }

    private static float[] Normalize(float[] vector)
    {
        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        if (norm <= 0)
        {
            return vector;
        }

        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            result[i] = (float)(vector[i] / norm);
        }

        return result;
    }

    private sealed record EmbeddingsResponse(EmbeddingDatum[] Data);

    private sealed record EmbeddingDatum(int Index, [property: JsonPropertyName("embedding")] float[] Embedding);
}
