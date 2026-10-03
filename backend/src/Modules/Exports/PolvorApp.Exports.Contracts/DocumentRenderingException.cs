namespace PolvorApp.Exports.Contracts;

/// <summary>
/// A document could not be rendered, e.g. a name holds a letter the embedded fonts cannot draw
/// (add-distribution-planning, group 2 review). Its message is fixed and it carries no inner exception,
/// because the PDF library's own message quotes the text it failed on, which may be personal data.
/// </summary>
public sealed class DocumentRenderingException : Exception
{
    public DocumentRenderingException()
        : base("The document could not be rendered.")
    {
    }

    public DocumentRenderingException(string message)
        : base(message)
    {
    }

    public DocumentRenderingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The fixed message with the failing step and the library's exception type, never its message.</summary>
    public static DocumentRenderingException For(string what, Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return new DocumentRenderingException($"The {what} could not be rendered ({cause.GetType().Name}).");
    }
}
