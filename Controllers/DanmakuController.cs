using System.Reflection;
using Jellyfin.Plugin.Danmaku.Models;
using Jellyfin.Plugin.Danmaku.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Danmaku.Controllers;

/// <summary>
/// Provides the web client and normalized danmaku APIs.
/// </summary>
[ApiController]
[Authorize]
[Route("Danmaku")]
public sealed class DanmakuController : ControllerBase
{
    private const string UserIdClaim = "Jellyfin-UserId";
    private readonly DanmakuService _danmakuService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DanmakuController"/> class.
    /// </summary>
    /// <param name="danmakuService">The danmaku service.</param>
    public DanmakuController(DanmakuService danmakuService)
    {
        _danmakuService = danmakuService;
    }

    /// <summary>
    /// Gets the injected web client script.
    /// </summary>
    /// <returns>The JavaScript client.</returns>
    [AllowAnonymous]
    [HttpGet("client.js")]
    [Produces("application/javascript")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public ActionResult GetClientScript()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Jellyfin.Plugin.Danmaku.Web.client.js");
        return stream is null
            ? NotFound()
            : File(stream, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Gets options that are safe to expose to an authenticated web client.
    /// </summary>
    /// <returns>Client options.</returns>
    [HttpGet("options")]
    public ActionResult<ClientOptionsDto> GetOptions()
    {
        return _danmakuService.GetClientOptions();
    }

    /// <summary>
    /// Loads comments for an item and optionally performs automatic matching.
    /// </summary>
    /// <param name="itemId">The Jellyfin item identifier.</param>
    /// <param name="source">An optional source identifier.</param>
    /// <param name="autoMatch">Whether automatic matching is allowed for this request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The match and comments.</returns>
    [HttpGet("items/{itemId:guid}/comments")]
    public async Task<ActionResult<DanmakuLoadResponseDto>> GetComments(
        Guid itemId,
        [FromQuery] string? source,
        [FromQuery] bool autoMatch = true,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return await _danmakuService.LoadAsync(
                userId,
                itemId,
                source,
                autoMatch,
                cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFoundProblem(exception.Message);
        }
        catch (DanmakuSourceException exception)
        {
            return SourceProblem(exception.Message);
        }
    }

    /// <summary>
    /// Searches for manual match candidates.
    /// </summary>
    /// <param name="itemId">The Jellyfin item identifier.</param>
    /// <param name="source">An optional source identifier.</param>
    /// <param name="query">An optional manual title query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Match candidates.</returns>
    [HttpGet("items/{itemId:guid}/matches")]
    public async Task<ActionResult<MatchResponseDto>> FindMatches(
        Guid itemId,
        [FromQuery] string? source,
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return await _danmakuService.FindMatchesAsync(
                userId,
                itemId,
                source,
                query,
                cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFoundProblem(exception.Message);
        }
        catch (DanmakuSourceException exception)
        {
            return SourceProblem(exception.Message);
        }
    }

    /// <summary>
    /// Persists a manual match and loads its comments.
    /// </summary>
    /// <param name="itemId">The Jellyfin item identifier.</param>
    /// <param name="request">The selected match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The selected match and comments.</returns>
    [HttpPost("items/{itemId:guid}/match")]
    public async Task<ActionResult<DanmakuLoadResponseDto>> SetMatch(
        Guid itemId,
        [FromBody] SetMatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return await _danmakuService.SetMatchAsync(
                userId,
                itemId,
                request,
                cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFoundProblem(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "匹配参数无效",
                Detail = exception.Message
            });
        }
        catch (DanmakuSourceException exception)
        {
            return SourceProblem(exception.Message);
        }
    }

    /// <summary>
    /// Removes a persisted manual or automatic match.
    /// </summary>
    /// <param name="itemId">The Jellyfin item identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpDelete("items/{itemId:guid}/match")]
    public async Task<ActionResult> ClearMatch(Guid itemId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            _ = await _danmakuService.ClearMatchAsync(userId, itemId, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (KeyNotFoundException exception)
        {
            return NotFoundProblem(exception.Message);
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        return Guid.TryParse(User.FindFirst(UserIdClaim)?.Value, out userId)
            && !userId.Equals(Guid.Empty);
    }

    private ObjectResult NotFoundProblem(string message)
    {
        return NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "媒体项目不可用",
            Detail = message
        });
    }

    private ObjectResult SourceProblem(string message)
    {
        return StatusCode(
            StatusCodes.Status502BadGateway,
            new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "弹幕源请求失败",
                Detail = message
            });
    }
}
