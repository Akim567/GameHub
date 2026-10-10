using System.ComponentModel.DataAnnotations;

namespace GameHub.Models;

public class UpsertPlayerRequest
{
    [Required, StringLength(100)]
    public required string Name { get; init; }

    [Range(0, int.MaxValue)]
    public int Level { get; init; }

    [Required, StringLength(100)]
    public required string Region { get; init; }
}

public sealed class UpdateLevelRequest
{
    [Range(-1_000_000, 1_000_000)]
    public int Delta { get; init; }
}

public sealed class LeaderboardScoreRequest
{
    [Required, StringLength(100)]
    public required string PlayerId { get; init; }

    public double Score { get; init; }
}

public sealed class AddAchievementRequest
{
    [Required, StringLength(200)]
    public required string AchievementName { get; init; }
}

public sealed class BatchPlayerRequest : UpsertPlayerRequest
{
    [Required, StringLength(100)]
    public required string Id { get; init; }
}

public sealed class BatchPlayersRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public required BatchPlayerRequest[] Players { get; init; }
}

public sealed record LevelUpdatedResponse(string PlayerId, long Level);

public sealed record LoginRecordedResponse(string PlayerId, long Count, long TtlSeconds);

public sealed record ScoreUpdatedResponse(string PlayerId, double Score);

public sealed record RankResponse(string PlayerId, long Rank);

public sealed record AchievementResponse(string PlayerId, string AchievementName, bool Added);

public sealed record AchievementMembershipResponse(
    string PlayerId,
    string AchievementName,
    bool HasAchievement);

public sealed record CommonAchievementsResponse(
    string FirstPlayerId,
    string SecondPlayerId,
    string[] Achievements);

public sealed record BatchCreatedResponse(int Created, long ElapsedMilliseconds);
