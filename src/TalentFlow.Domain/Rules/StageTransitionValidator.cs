using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Domain.Rules;

public class StageTransitionValidationResult
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }

    private StageTransitionValidationResult(bool isValid, string? errorMessage)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    public static StageTransitionValidationResult Success() => new(true, null);
    public static StageTransitionValidationResult Failure(string message) => new(false, message);
}

public static class StageTransitionValidator
{
    public static StageTransitionValidationResult Validate(
        PipelineStage currentStage,
        PipelineStage targetStage,
        bool hasInterviewFeedback,
        bool hasOfferApproval,
        bool allowSkip = false,
        string userRole = "")
    {
        if (currentStage.Id == targetStage.Id)
        {
            return StageTransitionValidationResult.Failure("Application is already in this stage.");
        }

        // Terminal check: Hired is terminal
        if (currentStage.Kind == StageKind.Hired)
        {
            return StageTransitionValidationResult.Failure("Cannot move an application that is already in terminal stage 'Hired'.");
        }

        // Reopening rejected application
        if (currentStage.Kind == StageKind.Rejected)
        {
            if (targetStage.Name.Equals("Applied", StringComparison.OrdinalIgnoreCase))
            {
                if (userRole == Roles.Admin || userRole == Roles.Recruiter)
                {
                    return StageTransitionValidationResult.Success();
                }
                return StageTransitionValidationResult.Failure("Only Admin or Recruiter can reopen a rejected application.");
            }
            return StageTransitionValidationResult.Failure("A rejected application can only be reopened to 'Applied'.");
        }

        // Any non-terminal stage can transition to Rejected
        if (targetStage.Kind == StageKind.Rejected)
        {
            return StageTransitionValidationResult.Success();
        }

        // Standard stage names mapping
        string currentName = currentStage.Name.Trim();
        string targetName = targetStage.Name.Trim();

        bool isDirectNext = (currentName, targetName) switch
        {
            ("Applied", "Screening") => true,
            ("Screening", "Interview") => true,
            ("Interview", "Offer") => true,
            ("Offer", "Hired") => true,
            _ => false
        };

        if (!isDirectNext && !allowSkip)
        {
            return StageTransitionValidationResult.Failure(
                $"Invalid stage transition from '{currentName}' to '{targetName}'. Skipping stages requires administrative override (allowSkip).");
        }

        // Rule: Move to Offer requires at least one interview feedback
        if (targetName.Equals("Offer", StringComparison.OrdinalIgnoreCase))
        {
            if (!hasInterviewFeedback)
            {
                return StageTransitionValidationResult.Failure(
                    "Moving to 'Offer' requires at least one submitted interview feedback.");
            }
        }

        // Rule: Move to Hired requires offer approval
        if (targetName.Equals("Hired", StringComparison.OrdinalIgnoreCase))
        {
            if (!hasOfferApproval)
            {
                return StageTransitionValidationResult.Failure(
                    "Moving to 'Hired' requires offer approval from the hiring manager.");
            }
        }

        return StageTransitionValidationResult.Success();
    }
}
