using FluentAssertions;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Domain.Rules;
using Xunit;

namespace TalentFlow.Domain.Tests;

public class StageTransitionValidatorTests
{
    private readonly PipelineStage _applied = PipelineStage.Create("Applied", 1);
    private readonly PipelineStage _screening = PipelineStage.Create("Screening", 2);
    private readonly PipelineStage _interview = PipelineStage.Create("Interview", 3);
    private readonly PipelineStage _offer = PipelineStage.Create("Offer", 4);
    private readonly PipelineStage _hired = PipelineStage.Create("Hired", 5, StageKind.Hired, isTerminal: true);
    private readonly PipelineStage _rejected = PipelineStage.Create("Rejected", 6, StageKind.Rejected);

    [Fact]
    public void Applied_To_Screening_ShouldSucceed()
    {
        var result = StageTransitionValidator.Validate(_applied, _screening, hasInterviewFeedback: false, hasOfferApproval: false);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Applied_To_Rejected_ShouldSucceed()
    {
        var result = StageTransitionValidator.Validate(_applied, _rejected, hasInterviewFeedback: false, hasOfferApproval: false);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Applied_To_Offer_WithoutSkip_ShouldFail()
    {
        var result = StageTransitionValidator.Validate(_applied, _offer, hasInterviewFeedback: true, hasOfferApproval: false, allowSkip: false);
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid stage transition");
    }

    [Fact]
    public void Applied_To_Offer_WithSkip_AndFeedback_ShouldSucceed()
    {
        var result = StageTransitionValidator.Validate(_applied, _offer, hasInterviewFeedback: true, hasOfferApproval: false, allowSkip: true);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Interview_To_Offer_WithoutFeedback_ShouldFail()
    {
        var result = StageTransitionValidator.Validate(_interview, _offer, hasInterviewFeedback: false, hasOfferApproval: false);
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("interview feedback");
    }

    [Fact]
    public void Interview_To_Offer_WithFeedback_ShouldSucceed()
    {
        var result = StageTransitionValidator.Validate(_interview, _offer, hasInterviewFeedback: true, hasOfferApproval: false);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Offer_To_Hired_WithoutApproval_ShouldFail()
    {
        var result = StageTransitionValidator.Validate(_offer, _hired, hasInterviewFeedback: true, hasOfferApproval: false);
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("offer approval");
    }

    [Fact]
    public void Offer_To_Hired_WithApproval_ShouldSucceed()
    {
        var result = StageTransitionValidator.Validate(_offer, _hired, hasInterviewFeedback: true, hasOfferApproval: true);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Moving_From_Hired_ShouldFail()
    {
        var result = StageTransitionValidator.Validate(_hired, _rejected, hasInterviewFeedback: true, hasOfferApproval: true);
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("terminal");
    }

    [Fact]
    public void Reopening_Rejected_To_Applied_By_Admin_ShouldSucceed()
    {
        var result = StageTransitionValidator.Validate(_rejected, _applied, hasInterviewFeedback: false, hasOfferApproval: false, userRole: Roles.Admin);
        result.IsValid.Should().BeTrue();
    }
}
