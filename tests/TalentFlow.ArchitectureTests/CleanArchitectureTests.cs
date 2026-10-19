using FluentAssertions;
using NetArchTest.Rules;
using TalentFlow.Api.Controllers;
using TalentFlow.Application.Features.Jobs.Commands;
using TalentFlow.Domain.Entities;
using TalentFlow.Infrastructure.Persistence;
using Xunit;

namespace TalentFlow.ArchitectureTests;

public class CleanArchitectureTests
{
    private static readonly System.Reflection.Assembly DomainAssembly = typeof(Job).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAssembly = typeof(CreateJobCommand).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAssembly = typeof(ApplicationDbContext).Assembly;
    private static readonly System.Reflection.Assembly ApiAssembly = typeof(JobsController).Assembly;

    [Fact]
    public void Domain_Should_Not_Have_Dependency_On_Other_Projects()
    {
        var otherProjects = new[]
        {
            "TalentFlow.Application",
            "TalentFlow.Infrastructure",
            "TalentFlow.Api"
        };

        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(otherProjects)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain layer must not depend on Application, Infrastructure, or Api: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Application_Should_Not_Have_Dependency_On_Infrastructure_Or_Api()
    {
        var forbiddenProjects = new[]
        {
            "TalentFlow.Infrastructure",
            "TalentFlow.Api"
        };

        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenProjects)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Application layer must not depend on Infrastructure or Api: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Infrastructure_Should_Not_Have_Dependency_On_Api()
    {
        var forbiddenProjects = new[]
        {
            "TalentFlow.Api"
        };

        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenProjects)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Infrastructure layer must not depend on Api: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Handlers_Should_Have_Name_Ending_With_Handler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(MediatR.IRequestHandler<,>))
            .Should()
            .HaveNameEndingWith("Handler")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Handlers must end with 'Handler': {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Controllers_Should_Inherit_From_ApiControllerBase()
    {
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("TalentFlow.Api.Controllers")
            .And()
            .HaveNameEndingWith("Controller")
            .And()
            .DoNotHaveName("ApiControllerBase")
            .Should()
            .Inherit(typeof(ApiControllerBase))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"All controllers must inherit from ApiControllerBase: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }
}
