namespace FrostAura.Reel.Domain.Library;

/// <summary>
/// What a Trakt rating was attached to. Movie/Show ratings are the model's training labels;
/// Season/Episode ratings feed the showEngagement auxiliary feature only.
/// </summary>
public enum RatingSubjectType
{
    Movie,
    Show,
    Season,
    Episode,
}
