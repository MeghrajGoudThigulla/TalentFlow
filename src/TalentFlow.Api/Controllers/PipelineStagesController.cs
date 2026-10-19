using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.PipelineStages;

namespace TalentFlow.Api.Controllers;

[Route("api/pipeline-stages")]
[Authorize]
public class PipelineStagesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PipelineStageDto>>> GetStages()
    {
        var result = await Mediator.Send(new GetPipelineStagesQuery());
        return Ok(result);
    }

    [HttpPut]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<ActionResult<List<PipelineStageDto>>> UpdateStages([FromBody] List<PipelineStageDto> stages)
    {
        var result = await Mediator.Send(new UpdatePipelineStagesCommand(stages));
        return Ok(result);
    }
}
