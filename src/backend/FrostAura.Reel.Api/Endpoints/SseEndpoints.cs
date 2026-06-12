using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Application.Tenancy;

namespace FrostAura.Reel.Api.Endpoints;

public static class SseEndpoints
{
    /// <summary>
    /// The single pipeline event channel (onboarding build-up + steady-state sync narration).
    /// nginx's /sse/ location runs unbuffered with a 24h read timeout; heartbeats every ≤10s
    /// keep intermediaries from reaping idle streams.
    /// </summary>
    public static void MapSseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sse/pipeline", async (HttpContext http, IAccountContext accountContext, IPipelineEventHub hub, CancellationToken ct) =>
        {
            if (accountContext.AccountId is not { } accountId)
            {
                return Results.Unauthorized();
            }

            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers.Connection = "keep-alive";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            await http.Response.Body.FlushAsync(ct);

            try
            {
                await foreach (var sseEvent in hub.SubscribeAsync(accountId, ct))
                {
                    await http.Response.WriteAsync($"event: {sseEvent.Type}\ndata: {sseEvent.JsonData}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // client went away — normal stream teardown
            }

            return Results.Empty;
        });
    }
}
