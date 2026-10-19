using FluentAssertions;
using TalentFlow.Domain.Rules;
using Xunit;

namespace TalentFlow.Domain.Tests;

public class ResumeScoreCalculatorTests
{
    [Fact]
    public void CalculateScore_With_High_Skill_Match_ShouldReturn_High_Score()
    {
        var jobTitle = "Senior .NET Backend Architect";
        var jobDesc = "Seeking an engineer skilled in C#, .NET, Clean Architecture, CQRS, MediatR, and SQL Server.";
        var coverNote = "I have 5 years experience building C# and .NET enterprise APIs with Clean Architecture, CQRS, and SQL Server.";

        var result = ResumeScoreCalculator.CalculateScore(jobTitle, jobDesc, coverNote, null);

        result.Score.Should().BeGreaterThanOrEqualTo(85);
        result.MatchedSkills.Should().Contain("C#");
        result.MatchedSkills.Should().Contain(".NET");
        result.MatchedSkills.Should().Contain("Clean Architecture");
        result.MatchedSkills.Should().Contain("CQRS");
    }

    [Fact]
    public void CalculateScore_With_Zero_Skill_Match_ShouldReturn_Base_Score()
    {
        var jobTitle = "Go Systems Engineer";
        var jobDesc = "Building high-performance distributed systems in Go with Kubernetes and Linux.";
        var coverNote = "Graphic designer proficient in Photoshop and Figma.";

        var result = ResumeScoreCalculator.CalculateScore(jobTitle, jobDesc, coverNote, null);

        result.Score.Should().BeLessThanOrEqualTo(60);
        result.MatchedSkills.Should().BeEmpty();
    }
}
