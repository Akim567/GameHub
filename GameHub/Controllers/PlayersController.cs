using GameHub.Models;
using GameHub.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameHub.Controllers;

[ApiController]
[Route("api/players")]
public sealed class PlayersController(GameHubRedisService redis) : ControllerBase
{
    [HttpPost("{id}")]
    public async Task<ActionResult<PlayerProfile>> UpsertPlayer(
        string id,
        UpsertPlayerRequest request)
    {
        if (HasBlankValue(id, request.Name, request.Region))
        {
            return BadRequest(new { error = "Player id, name and region must not be blank." });
        }

        return Ok(await redis.UpsertPlayerAsync(id, request));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PlayerProfile>> GetPlayer(string id)
    {
        var result = await redis.GetPlayerAsync(id);
        if (result is null)
        {
            return NotFound(new { error = $"Player '{id}' was not found." });
        }

        Response.Headers["X-Cache"] = result.Value.CacheHit ? "HIT" : "MISS";
        return Ok(result.Value.Profile);
    }

    [HttpPatch("{id}/level")]
    public async Task<ActionResult<LevelUpdatedResponse>> UpdateLevel(
        string id,
        UpdateLevelRequest request)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "Player id must not be blank." });
        }

        var level = await redis.UpdatePlayerLevelAsync(id, request.Delta);
        if (level is null)
        {
            return NotFound(new { error = $"Player '{id}' was not found." });
        }

        return Ok(new LevelUpdatedResponse(id, level.Value));
    }

    [HttpPost("{id}/login")]
    public async Task<ActionResult<LoginRecordedResponse>> RecordLogin(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "Player id must not be blank." });
        }

        var count = await redis.RecordLoginAsync(id);
        return Ok(new LoginRecordedResponse(id, count, 24 * 60 * 60));
    }

    [HttpPost("{id}/achievements")]
    public async Task<ActionResult<AchievementResponse>> AddAchievement(
        string id,
        AddAchievementRequest request)
    {
        if (HasBlankValue(id, request.AchievementName))
        {
            return BadRequest(new { error = "Player id and achievement name must not be blank." });
        }

        var added = await redis.AddAchievementAsync(id, request.AchievementName);
        return Ok(new AchievementResponse(id, request.AchievementName, added));
    }

    [HttpGet("{id}/achievements/{name}")]
    public async Task<ActionResult<AchievementMembershipResponse>> HasAchievement(
        string id,
        string name)
    {
        if (HasBlankValue(id, name))
        {
            return BadRequest(new { error = "Player id and achievement name must not be blank." });
        }

        var hasAchievement = await redis.HasAchievementAsync(id, name);
        return Ok(new AchievementMembershipResponse(id, name, hasAchievement));
    }

    [HttpGet("{id1}/achievements/common/{id2}")]
    public async Task<ActionResult<CommonAchievementsResponse>> GetCommonAchievements(
        string id1,
        string id2)
    {
        if (HasBlankValue(id1, id2))
        {
            return BadRequest(new { error = "Player ids must not be blank." });
        }

        var achievements = await redis.GetCommonAchievementsAsync(id1, id2);
        return Ok(new CommonAchievementsResponse(id1, id2, achievements));
    }

    [HttpPost("batch")]
    public async Task<ActionResult<BatchCreatedResponse>> CreateBatch(BatchPlayersRequest request)
    {
        if (request.Players.Any(player =>
                HasBlankValue(player.Id, player.Name, player.Region)))
        {
            return BadRequest(new { error = "Player ids, names and regions must not be blank." });
        }

        var duplicateId = request.Players
            .GroupBy(player => player.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateId is not null)
        {
            return BadRequest(new { error = $"Player id '{duplicateId}' occurs more than once." });
        }

        return Ok(await redis.CreatePlayersBatchAsync(request.Players));
    }

    private static bool HasBlankValue(params string[] values) =>
        values.Any(string.IsNullOrWhiteSpace);
}
