using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Features.Auth;

namespace TalentFlow.Api.Controllers;

[Route("api/users")]
[Authorize(Policy = "RequireAdmin")]
public class UsersController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetUsers(CancellationToken cancellationToken = default)
    {
        var users = await Mediator.Send(new GetUsersQuery(), cancellationToken);
        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        var user = await Mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, user);
    }

    [HttpPut("{id}/role")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken = default)
    {
        var success = await Mediator.Send(new UpdateUserRoleCommand(id, request.Role), cancellationToken);
        if (!success) return NotFound();
        return NoContent();
    }
}

public record UpdateRoleRequest(string Role);
