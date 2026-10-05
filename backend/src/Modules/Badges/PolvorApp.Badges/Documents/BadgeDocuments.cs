using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.Badges.Batches;
using PolvorApp.Exports.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Storage;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Badges.Documents;

/// <summary>
/// Builds and returns a badge sheet (specs: Badge batches, Badge access and document handling, Badge
/// downloads are audited; design D3, D4, D6, D7). The route is Admin-only, so the unscoped registry
/// contracts may take the request's ids (BR-12). The order per request is: rules → people → slot →
/// logo and photos → render → audit → file; no audit, no file. A photo is never left out silently: a
/// storage failure answers 503, and a photo the registry holds but that cannot be read or scaled
/// answers 409 naming the arquebusiers (maintainer decision, group 3 review).
/// </summary>
internal sealed partial class BadgeDocuments(
    IArquebusierRoster roster,
    IIdPhotoReader photos,
    ICatalogDirectory catalog,
    IFederationSettings federation,
    IImageNormalizer images,
    IDocumentRenderer renderer,
    IAuditLog auditLog,
    BadgeSlots slots,
    TimeProvider time,
    ILogger<BadgeDocuments> logger)
{
    /// <summary>
    /// The photo slot is 20 × 26.67 mm: 300 × 400 px prints at about 380 dpi, above NFR-15's 300 dpi, and
    /// keeps a 200-badge sheet to a few MB (design D3).
    /// </summary>
    public static readonly ImageRules PrintRules = new()
    {
        MaxInputBytes = IdPhoto.MaxBytes,

        // Stored ID photos are at most 1200 × 1600 (NFR-15): anything far larger is not one the registry wrote.
        MaxInputPixels = 2_000_000,
        FixedAspect = new ImageAspect(3, 4),
        MaxWidth = 300,
        MaxHeight = 400,
        Output = ImageOutputFormat.Jpeg,
    };

    public async Task<Results<FileContentHttpResult, ProblemHttpResult>> SheetAsync(BadgeSheetRequest request, CancellationToken cancellationToken)
    {
        var parsed = BadgeBatch.Parse(request);
        if (parsed.Batch is not { } batch)
        {
            return ProblemResults.Invalid(parsed.Errors);
        }

        var (subjects, comparsaName, problem) = await SubjectsAsync(request, batch, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        using var slot = await slots.EnterAsync(cancellationToken);
        if (slot is null)
        {
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, BadgeProblems.Busy);
        }

        DocumentImage? logo;
        PrintPhotos printPhotos;
        try
        {
            logo = await catalog.ReadFederationLogoAsync(cancellationToken) is { } image ? DocumentImage.FromPng(image.Png) : null;
            printPhotos = await PhotosAsync(subjects!, cancellationToken);
        }
        catch (StorageUnavailableException exception)
        {
            LogStorageUnavailable(logger, exception, batch.Kind, subjects!.Count);
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, BadgeProblems.StorageUnavailable);
        }
        catch (ImageProcessingBusyException)
        {
            // The image pipeline is shared with uploads: retryable, as a busy slot.
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, BadgeProblems.Busy);
        }

        if (printPhotos.Unreadable.Count > 0)
        {
            return ProblemResults.Problem(
                StatusCodes.Status409Conflict,
                BadgeProblems.PhotoUnreadable,
                new Dictionary<string, object?> { ["arquebusierIds"] = printPhotos.Unreadable });
        }

        var people = subjects!.Select(s => new BadgePerson(
            s.Arquebusier.LastName, s.Arquebusier.FirstName, s.Arquebusier.NationalId, s.Arquebusier.FederationId, s.ComparsaName,
            s.Arquebusier.License, printPhotos.Scaled.GetValueOrDefault(s.Arquebusier.Id))).ToList();
        var federationName = (await federation.GetAsync(cancellationToken)).OfficialName(BadgeTexts.For(batch.Language).NameForm);
        var document = renderer.RenderBadgeSheet(BadgeSheetBuilder.Build(
            new BadgeSheetContent(batch.Kind, comparsaName, batch.Language, federationName, FederationCalendar.Today(time), people, logo)));
        return await DeliverAsync(document, batch, [.. subjects!.Select(s => s.Arquebusier.Id)], cancellationToken);
    }

    /// <summary>The batch's arquebusiers in print order with their comparsa names, or the problem that stops it.</summary>
    private async Task<(IReadOnlyList<Subject>? Subjects, string? ComparsaName, ProblemHttpResult? Problem)> SubjectsAsync(
        BadgeSheetRequest request, ValidBadgeBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Kind == BadgeBatchKind.Comparsa)
        {
            if (await catalog.FindComparsaAsync(batch.ComparsaId!.Value, cancellationToken) is not { } comparsa)
            {
                return (null, null, ProblemResults.NotFound(BadgeProblems.NotFound));
            }

            var members = await roster.ListByComparsaAsync(comparsa.Id, cancellationToken);
            return members.Count switch
            {
                0 => (null, null, ProblemResults.Conflict(BadgeProblems.NothingToPrint)),
                > DocumentBadgeSheet.MaxBadges => (null, null, ProblemResults.Conflict(BadgeProblems.TooMany)),
                _ => (Order(members.Select(a => new Subject(a, comparsa.Name))), comparsa.Name, null),
            };
        }

        var found = (await roster.FindManyAsync(batch.ArquebusierIds, cancellationToken)).ToDictionary(a => a.Id);
        var missing = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < request.ArquebusierIds!.Count; index++)
        {
            if (!found.ContainsKey(request.ArquebusierIds[index]))
            {
                missing[$"arquebusierIds[{index}]"] = BadgeProblems.ArquebusierNotFound;
            }
        }

        if (missing.Count > 0)
        {
            return (null, null, ProblemResults.Invalid(missing));
        }

        var names = (await catalog.FindComparsasAsync([.. found.Values.Select(a => a.ComparsaId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.Name);
        return (Order(found.Values.Select(a => new Subject(
            a, names.TryGetValue(a.ComparsaId, out var name) ? name : throw new InvalidOperationException($"Comparsa {a.ComparsaId} of arquebusier {a.Id} is missing from the catalogue.")))), null, null);
    }

    private static IReadOnlyList<Subject> Order(IEnumerable<Subject> subjects)
    {
        var byId = subjects.ToDictionary(s => s.Arquebusier.Id);
        return [.. BadgeBatch.Order(byId.Values.Select(s => new BadgeSubject(s.Arquebusier.Id, s.ComparsaName, s.Arquebusier.LastName, s.Arquebusier.FirstName)))
            .Select(s => byId[s.ArquebusierId])];
    }

    /// <summary>
    /// The ID photos scaled for print, by arquebusier, and in print order the arquebusiers whose photo the
    /// registry holds but which could not be read or scaled. One at a time: the registry's reader shares
    /// the request's database context, and the image pipeline handles one image at a time anyway.
    /// </summary>
    private async Task<PrintPhotos> PhotosAsync(IReadOnlyList<Subject> subjects, CancellationToken cancellationToken)
    {
        var scaled = new Dictionary<Guid, DocumentImage>();
        var unreadable = new List<Guid>();
        foreach (var subject in subjects.Where(s => s.Arquebusier.HasIdPhoto))
        {
            var id = subject.Arquebusier.Id;
            if (await photos.ReadAsync(id, cancellationToken) is { } photo && await ScaleAsync(id, photo, cancellationToken) is { } image)
            {
                scaled[id] = image;
            }
            else
            {
                unreadable.Add(id);
            }
        }

        if (unreadable.Count > 0)
        {
            LogPhotosUnreadable(logger, unreadable.Count, string.Join(',', unreadable));
        }

        return new PrintPhotos(scaled, unreadable);
    }

    /// <summary>A stored ID photo the scaler refuses is not one the registry wrote: reported, and the sheet refused.</summary>
    private async Task<DocumentImage?> ScaleAsync(Guid arquebusierId, IdPhoto photo, CancellationToken cancellationToken)
    {
        using var input = new MemoryStream(photo.Jpeg.ToArray(), writable: false);
        if (await images.NormalizeAsync(input, PrintRules, cancellationToken) is NormalizedImage normalized)
        {
            return DocumentImage.FromJpeg(normalized.Content);
        }

        LogPhotoRefused(logger, arquebusierId);
        return null;
    }

    /// <summary>Audits the download with ids, counts and the language only, then returns the file; no audit, no file.</summary>
    private async Task<Results<FileContentHttpResult, ProblemHttpResult>> DeliverAsync(
        RenderedDocument document, ValidBadgeBatch batch, IReadOnlyList<Guid> arquebusierIds, CancellationToken cancellationToken)
    {
        var data = new
        {
            batch = batch.Kind == BadgeBatchKind.Comparsa ? "COMPARSA" : "SELECTION",
            comparsaId = batch.ComparsaId,
            language = EnumCodes.ToCode(batch.Language),
            count = arquebusierIds.Count,
            arquebusierIds,
            version = BadgeSheetBuilder.Version,
        };
        try
        {
            await auditLog.RecordAsync(
                new AuditRecord(BadgesAuditActions.BadgesDownloaded, BadgesAuditActions.EntityType, Data: data, ComparsaId: batch.ComparsaId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAuditFailed(logger, exception, batch.Kind, arquebusierIds.Count);
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, BadgeProblems.AuditUnavailable);
        }

        return TypedResults.File(document.Content.ToArray(), document.ContentType, document.FileName);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Badge sheet ({Kind}, {Count} badges) refused: the storage is unavailable")]
    private static partial void LogStorageUnavailable(ILogger logger, Exception exception, BadgeBatchKind kind, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The ID photo of arquebusier {ArquebusierId} cannot be scaled for print")]
    private static partial void LogPhotoRefused(ILogger logger, Guid arquebusierId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Badge sheet refused: {Count} ID photo(s) held by the registry cannot be read, arquebusiers {ArquebusierIds}")]
    private static partial void LogPhotosUnreadable(ILogger logger, int count, string arquebusierIds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Badge sheet ({Kind}, {Count} badges) not returned: the download could not be audited")]
    private static partial void LogAuditFailed(ILogger logger, Exception exception, BadgeBatchKind kind, int count);

    private sealed record Subject(RosterArquebusier Arquebusier, string ComparsaName);

    private sealed record PrintPhotos(IReadOnlyDictionary<Guid, DocumentImage> Scaled, IReadOnlyList<Guid> Unreadable);
}
