namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>
/// Why a file cannot be read at all (spec: Import file reading), as the reason of the <c>file</c>
/// field, with the field keys of the columns concerned for <c>missingColumns</c> and <c>duplicateColumns</c>.
/// </summary>
internal sealed record ImportFileProblem(string Reason, IReadOnlyList<string> Columns)
{
    public const string Invalid = "invalid";
    public const string Empty = "empty";
    public const string TooManyRows = "tooManyRows";
    public const string MissingColumns = "missingColumns";
    public const string DuplicateColumns = "duplicateColumns";

    public static ImportFileProblem Of(string reason) => new(reason, []);
}
