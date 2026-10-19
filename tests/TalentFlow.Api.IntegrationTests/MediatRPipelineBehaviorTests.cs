using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Features.Auth;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class MediatRPipelineBehaviorTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MediatRPipelineBehaviorTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Pipeline_ShouldExecuteValidationFirst_RejectingInvalidCommandsImmediately()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Empty email & password fails LoginCommandValidator
        var invalidCommand = new LoginCommand("", "");

        var act = async () => await mediator.Send(invalidCommand);

        var exception = await act.Should().ThrowAsync<ValidationException>("ValidationBehavior executes first in pipeline (ARCH-03)");
        exception.Which.Errors.Should().ContainKey("Email");
        exception.Which.Errors.Should().ContainKey("Password");
    }
}
