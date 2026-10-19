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
    public async Task<ActionResult<List<UserDto>>> GetUsers()
    {
        var users = await Mediator.Send(new GetUsersQuery());
        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserCommand command)
    {
        var user = await Mediator.Send(command);
        return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, user);
    }

    [HttpPut("{id}/role")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateRoleRequest request)
    {
        var success = await Mediator.Send(new UpdateUserRoleCommand(id, request.Role));
        if (!success) return NotFound();
        return NoContent();
    }
}

public record UpdateRoleRequest(string Role);
