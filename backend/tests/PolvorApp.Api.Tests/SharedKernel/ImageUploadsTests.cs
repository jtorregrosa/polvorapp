using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Images;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>
/// The shared reading of an image upload (add-comparsa-logos design D2): one <c>file</c> part of a
/// <c>multipart/form-data</c> body, read in memory, or the field reason why there is none.
/// </summary>
public sealed class ImageUploadsTests
{
    private const long MaxFileBytes = 1024;

    [Fact]
    public async Task Reads_the_file_part()
    {
        var request = MultipartRequests.Files(("file", new byte[100]));

        var (file, problem) = await ImageUploads.ReadFileAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(problem);
        Assert.NotNull(file);
        Assert.Equal(100, file.Length);
    }

    [Fact]
    public async Task A_streamed_body_larger_than_the_default_buffer_is_read_in_memory()
    {
        // A request body as Kestrel hands it over: not seekable, so the form reader buffers it. The
        // part is above ASP.NET's 64 KB default threshold, below which nothing would spill to disk.
        const long max = 256 * 1024;
        var request = MultipartRequests.Files(seekable: false, ("file", new byte[100 * 1024]));

        var (file, problem) = await ImageUploads.ReadFileAsync(request, max, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(problem);
        Assert.Equal(100 * 1024, file!.Length);
    }

    [Fact]
    public async Task A_streamed_part_over_the_limit_without_a_declared_length_is_too_large()
    {
        var request = MultipartRequests.Files(seekable: false, ("file", new byte[MaxFileBytes + 1]));
        request.ContentLength = null;

        var (file, problem) = await ImageUploads.ReadFileAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(file);
        Assert.Equal("tooLarge", problem);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(int.MaxValue)]
    public async Task An_impossible_limit_is_refused(long maxFileBytes) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ImageUploads.ReadFileAsync(MultipartRequests.Files(("file", new byte[1])), maxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_body_that_is_not_multipart_has_no_file()
    {
        var request = MultipartRequests.Request("application/json", Encoding.UTF8.GetBytes("{\"file\":\"not an upload\"}"));

        var (file, problem) = await ImageUploads.ReadFileAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(file);
        Assert.Equal("required", problem);
    }

    [Fact]
    public async Task A_form_without_the_file_part_has_no_file()
    {
        var request = MultipartRequests.Files(("other", new byte[10]));

        var (file, problem) = await ImageUploads.ReadFileAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(file);
        Assert.Equal("required", problem);
    }

    [Fact]
    public async Task A_part_over_the_limit_is_too_large()
    {
        var request = MultipartRequests.Files(("file", new byte[MaxFileBytes + 1]));

        var (file, problem) = await ImageUploads.ReadFileAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(file);
        Assert.Equal("tooLarge", problem);
    }

    [Fact]
    public async Task A_malformed_body_has_no_file()
    {
        var request = MultipartRequests.Request($"multipart/form-data; boundary={MultipartRequests.Boundary}", Encoding.ASCII.GetBytes("--synthetic-boundary\r\nno headers"));

        var (file, problem) = await ImageUploads.ReadFileAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(file);
        Assert.Equal("required", problem);
    }

    [Theory]
    [InlineData(ImageRejection.TooLarge, "tooLarge")]
    [InlineData(ImageRejection.UnsupportedFormat, "unsupportedFormat")]
    [InlineData(ImageRejection.TooSmall, "tooSmall")]
    [InlineData(ImageRejection.AspectRatio, "aspectRatio")]
    public void Every_rejection_has_a_field_reason(ImageRejection rejection, string reason) =>
        Assert.Equal(reason, ImageUploads.Reason(rejection));

    [Fact]
    public void No_rejection_is_left_without_a_reason() =>
        Assert.All(Enum.GetValues<ImageRejection>(), rejection => Assert.NotEmpty(ImageUploads.Reason(rejection)));

    [Fact]
    public void The_request_limit_adds_the_multipart_framing() =>
        Assert.Equal(MaxFileBytes + (64 * 1024), ImageUploads.MaxRequestBytes(MaxFileBytes));
}
