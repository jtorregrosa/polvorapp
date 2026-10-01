namespace PolvorApp.SharedKernel.Images;

/// <summary>
/// The image processor stayed busy longer than a request should wait (design D3: one image at a
/// time). The caller answers a retryable 503.
/// </summary>
public sealed class ImageProcessingBusyException : Exception
{
    public ImageProcessingBusyException()
        : base("The image processor is busy.")
    {
    }

    public ImageProcessingBusyException(string message)
        : base(message)
    {
    }

    public ImageProcessingBusyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
