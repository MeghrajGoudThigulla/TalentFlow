using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.Candidates;
using TalentFlow.Application.Features.Outbox;
using TalentFlow.Application.Features.PipelineStages;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.Persistence;
using Xunit;

namespace TalentFlow.Application.Tests;

public class CandidateAndStageTests
{
    private readonly ApplicationDbContext _context;

    public CandidateAndStageTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetCandidatesQuery_ReturnsPagedList_WithSearchFilter()
    {
        _context.Candidates.Add(new Candidate { Id = Guid.NewGuid(), FullName = "Alice Wonderland", Email = "alice@test.local", Phone = "123" });
        _context.Candidates.Add(new Candidate { Id = Guid.NewGuid(), FullName = "Bob Builder", Email = "bob@test.local", Phone = "456" });
        _context.Candidates.Add(new Candidate { Id = Guid.NewGuid(), FullName = "Charlie Chocolate", Email = "charlie@test.local", Phone = "789" });
        await _context.SaveChangesAsync();

        var handler = new SearchCandidatesQueryHandler(_context);
        var result = await handler.Handle(new SearchCandidatesQuery("Wonderland", null, null, 1, 10), CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items.First().FullName.Should().Be("Alice Wonderland");
    }

    [Fact]
    public async Task GetCandidateByIdQuery_ReturnsCandidate_OrThrowsNotFound()
    {
        var candidate = new Candidate { Id = Guid.NewGuid(), FullName = "David Copperfield", Email = "david@test.local" };
        _context.Candidates.Add(candidate);
        await _context.SaveChangesAsync();

        var handler = new GetCandidateByIdQueryHandler(_context);
        var found = await handler.Handle(new GetCandidateByIdQuery(candidate.Id), CancellationToken.None);

        found.Should().NotBeNull();
        found.FullName.Should().Be("David Copperfield");

        var act = async () => await handler.Handle(new GetCandidateByIdQuery(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task PipelineStages_GetAndUpdate_WorksProperly()
    {
        var stage1 = PipelineStage.Create("Applied", 1);
        var stage2 = PipelineStage.Create("Interview", 2);
        _context.PipelineStages.AddRange(stage1, stage2);
        await _context.SaveChangesAsync();

        var getHandler = new GetPipelineStagesQueryHandler(_context);
        var stages = await getHandler.Handle(new GetPipelineStagesQuery(), CancellationToken.None);

        stages.Should().HaveCount(2);

        var updateHandler = new UpdatePipelineStagesCommandHandler(_context);
        var updatedList = new List<PipelineStageDto>
        {
            new(stage1.Id, "Applied (New)", 1, false, StageKind.Normal),
            new(stage2.Id, "Interview (New)", 2, false, StageKind.Normal)
        };

        var updateResult = await updateHandler.Handle(new UpdatePipelineStagesCommand(updatedList), CancellationToken.None);
        updateResult.Should().HaveCount(2);
        updateResult.First(s => s.Id == stage1.Id).Name.Should().Be("Applied (New)");
    }

    [Fact]
    public async Task OutboxCommands_GetFailedAndRetry_WorkProperly()
    {
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "EmailNotification",
            ToAddress = "test@example.com",
            Subject = "Test Subject",
            Body = "Test Body",
            Status = OutboxMessageStatus.Failed,
            Attempts = 5,
            LastError = "SMTP Timeout"
        };
        _context.OutboxMessages.Add(msg);
        await _context.SaveChangesAsync();

        var getHandler = new GetFailedOutboxQueryHandler(_context);
        var failedList = await getHandler.Handle(new GetFailedOutboxQuery(), CancellationToken.None);

        failedList.Should().HaveCount(1);
        failedList.First().Id.Should().Be(msg.Id);

        var retryHandler = new RetryOutboxMessageCommandHandler(_context);
        await retryHandler.Handle(new RetryOutboxMessageCommand(msg.Id), CancellationToken.None);

        var updatedMsg = await _context.OutboxMessages.FindAsync(msg.Id);
        updatedMsg.Should().NotBeNull();
        updatedMsg!.Status.Should().Be(OutboxMessageStatus.Pending);
        updatedMsg.Attempts.Should().Be(0);
    }
}
