using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;

namespace PolvorApp.SharedKernel.Http;

/// <summary>
/// A read spreadsheet upload: the <see cref="File"/>, or the <see cref="Problem"/> (a field reason of
/// <c>file</c>) why there is none, and the text fields sent with it.
/// </summary>
public sealed class SpreadsheetUpload
{
    private readonly IFormCollection? _form;

    private SpreadsheetUpload(IFormFile? file, string? problem, IFormCollection? form)
    {
        File = file;
        Problem = problem;
        _form = form;
    }

    /// <summary>The uploaded workbook, buffered in memory; null when <see cref="Problem"/> is set.</summary>
    public IFormFile? File { get; }

    /// <summary>The field reason of <c>file</c> (<c>required</c>, <c>tooLarge</c>); null when there is a file.</summary>
    public string? Problem { get; }

    /// <summary>Whether a non-empty file was sent.</summary>
    [MemberNotNullWhen(true, nameof(File))]
    [MemberNotNullWhen(false, nameof(Problem))]
    public bool HasFile => File is not null;

    /// <summary>The first value of the text field <paramref name="name"/>, or null when it was not sent.</summary>
    public string? Field(string name) =>
        _form is not null && _form.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;

    /// <summary>Every byte of <see cref="File"/>, which is already buffered in memory.</summary>
    public async Task<byte[]> ReadContentAsync(CancellationToken cancellationToken)
    {
        if (!HasFile)
        {
            throw new InvalidOperationException("The upload has no file.");
        }

        var content = new byte[File.Length];
        await using var stream = File.OpenReadStream();
        await stream.ReadExactlyAsync(content, cancellationToken);
        return content;
    }

    internal static SpreadsheetUpload Read(IFormFile file, IFormCollection form) => new(file, null, form);

    internal static SpreadsheetUpload Failed(string problem, IFormCollection? form) => new(null, problem, form);
}
