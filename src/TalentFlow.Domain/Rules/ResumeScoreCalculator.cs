using System.Text.RegularExpressions;

namespace TalentFlow.Domain.Rules;

public record ResumeMatchResult(int Score, string MatchedSkillsSummary, List<string> MatchedSkills);

public static class ResumeScoreCalculator
{
    private static readonly string[] SkillTaxonomy = new[]
    {
        "C#", ".NET", "ASP.NET", "ASP.NET Core", "Entity Framework", "EF Core",
        "SQL Server", "PostgreSQL", "SQL", "Clean Architecture", "CQRS", "MediatR",
        "SignalR", "WebSockets", "Docker", "Kubernetes", "Redis", "REST", "API",
        "Python", "FastAPI", "React", "Next.js", "TypeScript", "JavaScript",
        "Flutter", "Microservices", "CI/CD", "GitHub Actions", "xUnit", "Testing",
        "MongoDB", "Linux", "Security", "JWT", "Performance"
    };

    public static ResumeMatchResult CalculateScore(
        string jobTitle,
        string jobDescription,
        string? coverNote,
        string? resumeText)
    {
        var jobText = $"{jobTitle} {jobDescription}".ToLowerInvariant();
        var candidateText = $"{coverNote ?? ""} {resumeText ?? ""}".ToLowerInvariant();

        var jobRequiredSkills = new List<string>();
        foreach (var skill in SkillTaxonomy)
        {
            if (ContainsKeyword(jobText, skill.ToLowerInvariant()))
            {
                jobRequiredSkills.Add(skill);
            }
        }

        if (jobRequiredSkills.Count == 0)
        {
            // Default baseline if job description didn't match standard taxonomy
            return new ResumeMatchResult(80, "General Alignment", new List<string> { "General Alignment" });
        }

        var matchedSkills = new List<string>();
        foreach (var skill in jobRequiredSkills)
        {
            if (ContainsKeyword(candidateText, skill.ToLowerInvariant()))
            {
                matchedSkills.Add(skill);
            }
        }

        // Calculate score percentage with realistic baseline weighting
        double matchRatio = (double)matchedSkills.Count / jobRequiredSkills.Count;
        
        // Base score ranges realistically between 60% and 98% based on skill coverage
        int calculatedScore;
        if (matchedSkills.Count == 0)
        {
            calculatedScore = 55;
        }
        else
        {
            calculatedScore = (int)Math.Round(60.0 + (matchRatio * 38.0));
        }

        calculatedScore = Math.Clamp(calculatedScore, 40, 99);

        var summary = matchedSkills.Count > 0
            ? string.Join(", ", matchedSkills)
            : "General Alignment";

        return new ResumeMatchResult(calculatedScore, summary, matchedSkills);
    }

    private static bool ContainsKeyword(string text, string keyword)
    {
        if (keyword == "c#")
        {
            return text.Contains("c#") || text.Contains("csharp");
        }
        if (keyword == ".net")
        {
            return text.Contains(".net") || text.Contains("dotnet");
        }
        
        var pattern = $@"\b{Regex.Escape(keyword)}\b";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }
}
