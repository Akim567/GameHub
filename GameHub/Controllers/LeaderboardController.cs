using GameHub.Models;
using GameHub.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameHub.Controllers;

[ApiController]
[Route("api/leaderboard")]
public sealed class LeaderboardController(GameHubRedisService redis) : ControllerBase
{
    [HttpPost("score")]
    public async Task<ActionResult<ScoreUpdatedResponse>> AddScore(
        LeaderboardScoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlayerId))
        {
            return BadRequest(new { error = "Player id must not be blank." });
        }

        if (!double.IsFinite(request.Score))
        {
            return BadRequest(new { error = "Score must be a finite number." });
        }

        var score = await redis.AddLeaderboardScoreAsync(request.PlayerId, request.Score);
        return Ok(new ScoreUpdatedResponse(request.PlayerId, score));
    }

    [HttpGet("top")]
    public async Task<ActionResult<LeaderboardEntry[]>> GetTop([FromQuery] int limit = 10)
    {
        if (limit is < 1 or > 100)
        {
            return BadRequest(new { error = "Limit must be between 1 and 100." });
        }

        return Ok(await redis.GetLeaderboardAsync(limit));
    }

    [HttpGet("rank/{playerId}")]
    public async Task<ActionResult<RankResponse>> GetRank(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return BadRequest(new { error = "Player id must not be blank." });
        }

        var rank = await redis.GetLeaderboardRankAsync(playerId);
        if (rank is null)
        {
            return NotFound(new { error = $"Player '{playerId}' is not in the leaderboard." });
        }

        return Ok(new RankResponse(playerId, rank.Value));
    }
}
