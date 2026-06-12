using FrostAura.Reel.Application.Ingestion;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Ports.Trakt;

namespace FrostAura.Reel.Tests.Ingestion;

public class IngestMapperTests
{
    [Fact]
    public void Movie_with_plays_is_fully_watched()
    {
        Assert.True(IngestMapper.IsFullyWatched(MediaType.Movie, plays: 1, distinctWatchedEpisodes: 0, airedEpisodes: null));
        Assert.False(IngestMapper.IsFullyWatched(MediaType.Movie, plays: 0, distinctWatchedEpisodes: 0, airedEpisodes: null));
    }

    [Fact]
    public void Show_caught_up_is_fully_watched_until_a_new_season_airs()
    {
        // Caught up: 62 of 62 aired.
        Assert.True(IngestMapper.IsFullyWatched(MediaType.Show, 62, 62, 62));
        // New season airs → 62 of 72: flips back to in-progress (eligible again). The locked
        // eligibility rule depends on this recompute-on-sync behaviour.
        Assert.False(IngestMapper.IsFullyWatched(MediaType.Show, 62, 62, 72));
    }

    [Fact]
    public void Show_with_unknown_aired_count_is_treated_as_in_progress()
    {
        Assert.False(IngestMapper.IsFullyWatched(MediaType.Show, 10, 10, null));
        Assert.False(IngestMapper.IsFullyWatched(MediaType.Show, 10, 10, 0));
    }

    [Fact]
    public void Distinct_episode_count_ignores_replays_and_zero_play_rows()
    {
        var watched = new TraktWatchedShow(12, DateTime.UtcNow, DateTime.UtcNow, null,
            Show("Landman", 174314),
            [
                new TraktWatchedSeason(1, [new TraktWatchedEpisode(1, 3, DateTime.UtcNow), new TraktWatchedEpisode(2, 1, DateTime.UtcNow), new TraktWatchedEpisode(3, 0, null)]),
                new TraktWatchedSeason(2, [new TraktWatchedEpisode(1, 1, DateTime.UtcNow)]),
            ]);

        Assert.Equal(3, IngestMapper.CountDistinctWatchedEpisodes(watched));
    }

    [Fact]
    public void Resume_likelihood_prefers_recent_mid_progress_shows()
    {
        var now = DateTime.UtcNow;
        var recentMidway = IngestMapper.ResumeLikelihood(now.AddDays(-3), 0.6m, now);
        var staleStart = IngestMapper.ResumeLikelihood(now.AddDays(-400), 0.05m, now);
        var neverWatched = IngestMapper.ResumeLikelihood(null, 0.5m, now);

        Assert.True(recentMidway > staleStart);
        Assert.Equal(0m, neverWatched);
    }

    [Fact]
    public void ApplyMovie_maps_export_shape_and_preserves_existing_values_on_nulls()
    {
        var title = new Title { Id = Guid.NewGuid(), Overview = "existing overview" };
        var movie = new TraktMovie(
            "Happy Gilmore", 1996, new TraktIds(4890, "happy-gilmore-1996", "tt0116483", 9614),
            "He doesn't play golf... he destroys it.", null, new DateTime(1996, 2, 16, 0, 0, 0, DateTimeKind.Utc),
            92, "us", "released", 7.27878m, 9635, "https://youtube.com/watch?v=VJFdK0Owxlc",
            "en", ["comedy"], ["sports"], null);

        IngestMapper.ApplyMovie(title, movie);

        Assert.Equal(MediaType.Movie, title.MediaType);
        Assert.Equal(4890, title.TraktId);
        Assert.Equal(9614, title.TmdbId);
        Assert.Equal("Happy Gilmore", title.Name);
        Assert.Equal(92, title.RuntimeMinutes);
        Assert.Equal("existing overview", title.Overview); // null overview must not clobber
        Assert.Equal(["comedy"], title.Genres);
    }

    [Fact]
    public void ApplyShow_maps_aired_episodes_for_the_fully_watched_rule()
    {
        var title = new Title { Id = Guid.NewGuid() };
        var show = new TraktShow(
            "Landman", 2024, new TraktIds(174314, "landman", "tt14186672", 157741),
            null, "West-Texas oil…", new DateTime(2024, 11, 17, 0, 0, 0, DateTimeKind.Utc),
            55, "us", "returning series", 8.25818m, 4888, null, "en",
            ["drama"], [], "TV-MA", "Paramount+", 20);

        IngestMapper.ApplyShow(title, show);

        Assert.Equal(MediaType.Show, title.MediaType);
        Assert.Equal(20, title.AiredEpisodes);
        Assert.Equal("Paramount+", title.Network);
        Assert.Equal("returning series", title.Status);
    }

    private static TraktShow Show(string name, long traktId) => new(
        name, 2024, new TraktIds(traktId, null, null, null), null, null, null,
        null, null, null, null, 0, null, null, null, null, null, null, null);
}
