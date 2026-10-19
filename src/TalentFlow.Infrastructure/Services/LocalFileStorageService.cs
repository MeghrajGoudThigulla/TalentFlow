using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;

namespace TalentFlow.Infrastructure.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _storageDirectory;
    private readonly ILogger<LocalFileStorageService> _logger;
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB cap (FR-9)

    public LocalFileStorageService(IWebHostEnvironment environment, IConfiguration configuration, ILogger<LocalFileStorageService> logger)
    {
        _logger = logger;
        var configuredPath = configuration["FileStorage:BasePath"];
        _storageDirectory = !string.IsNullOrWhiteSpace(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, "uploads", "resumes");

        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
        }
    }

    public async Task<(string StoredPath, string OriginalFileName)> SaveResumeAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate file extension
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension != ".pdf" && extension != ".docx")
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "Resume", new[] { "Only .pdf and .docx file formats are allowed." } }
            });
        }

        // 2. Validate length
        if (fileStream.Length > MaxFileSizeBytes)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "Resume", new[] { "Resume file size exceeds the 5 MB limit." } }
            });
        }

        // 3. Inspect magic bytes for upload hardening
        byte[] buffer = new byte[8];
        long originalPosition = fileStream.Position;
        int read = await fileStream.ReadAsync(buffer, 0, 8, cancellationToken);
        fileStream.Seek(originalPosition, SeekOrigin.Begin);

        if (read < 4)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "Resume", new[] { "The uploaded file is empty or corrupted." } }
            });
        }

        bool isPdf = buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46; // %PDF
        bool isDocx = buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04; // PK..

        if (extension == ".pdf" && !isPdf)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "Resume", new[] { "File header does not match valid PDF content." } }
            });
        }

        if (extension == ".docx" && !isDocx)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "Resume", new[] { "File header does not match valid DOCX content." } }
            });
        }

        // 4. Randomized storage name to prevent traversal or collision
        var safeFileName = $"{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(_storageDirectory, safeFileName);

        using (var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fileStream.CopyToAsync(output, cancellationToken);
        }

        _logger.LogInformation("Stored resume {OriginalName} as {SafeName}", fileName, safeFileName);

        return (safeFileName, fileName);
    }

    public Task<(Stream ContentStream, string ContentType, string FileName)?> GetResumeAsync(
        string storedPath,
        CancellationToken cancellationToken = default)
    {
        var safeFileName = Path.GetFileName(storedPath);
        var fullPath = Path.Combine(_storageDirectory, safeFileName);

        if (!File.Exists(fullPath))
        {
            return Task.FromResult<(Stream ContentStream, string ContentType, string FileName)?>(null);
        }

        var ext = Path.GetExtension(safeFileName).ToLowerInvariant();
        var contentType = ext == ".pdf" ? "application/pdf" : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        return Task.FromResult<(Stream ContentStream, string ContentType, string FileName)?>((stream, contentType, safeFileName));
    }
}
