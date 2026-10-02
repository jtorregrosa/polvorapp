using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>
/// The shared reading of a spreadsheet upload (add-registry-import design D3): one <c>file</c> part
/// and plain text fields of a <c>multipart/form-data</c> body, read in memory, or the field reason
/// why there is no file.
/// </summary>
public sealed class SpreadsheetUploadsTests
{
    private const long MaxFileBytes = 1024;

    [Fact]
    public async Task Reads_the_file_and_the_text_fields()
    {
        var request = MultipartRequests.Form([("file", new byte[100])], [("comparsaId", "0199a000-0000-7000-8000-000000000001")]);

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(upload.Problem);
        Assert.True(upload.HasFile);
        Assert.Equal(100, upload.File.Length);
        Assert.Equal("0199a000-0000-7000-8000-000000000001", upload.Field("comparsaId"));
    }

    [Fact]
    public async Task A_missing_text_field_reads_as_null()
    {
        var request = MultipartRequests.Files(("file", new byte[10]));

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(upload.Field("comparsaId"));
    }

    [Fact]
    public async Task A_body_that_is_not_multipart_has_no_file()
    {
        var request = MultipartRequests.Request("application/json", Encoding.UTF8.GetBytes("{\"file\":\"not an upload\"}"));

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(upload.File);
        Assert.Equal("required", upload.Problem);
    }

    [Fact]
    public async Task A_form_without_the_file_part_has_no_file()
    {
        var request = MultipartRequests.Form([], [("comparsaId", "x")]);

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(upload.File);
        Assert.Equal("required", upload.Problem);
        Assert.Equal("x", upload.Field("comparsaId"));
    }

    [Fact]
    public async Task An_empty_file_is_no_file()
    {
        var request = MultipartRequests.Files(("file", []));

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(upload.File);
        Assert.Equal("required", upload.Problem);
    }

    [Fact]
    public async Task A_part_over_the_limit_is_too_large()
    {
        var request = MultipartRequests.Files(("file", new byte[MaxFileBytes + 1]));

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(upload.File);
        Assert.Equal("tooLarge", upload.Problem);
    }

    [Fact]
    public async Task A_streamed_part_over_the_limit_without_a_declared_length_is_too_large()
    {
        var request = MultipartRequests.Files(seekable: false, ("file", new byte[MaxFileBytes + 1]));
        request.ContentLength = null;

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Equal("tooLarge", upload.Problem);
    }

    [Fact]
    public async Task A_malformed_body_has_no_file()
    {
        var request = MultipartRequests.Request(
            $"multipart/form-data; boundary={MultipartRequests.Boundary}", Encoding.ASCII.GetBytes("--synthetic-boundary\r\nno headers"));

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Null(upload.File);
        Assert.Equal("required", upload.Problem);
    }

    [Fact]
    public async Task Reading_the_content_returns_every_byte()
    {
        var content = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
        var request = MultipartRequests.Files(("file", content));
        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        var read = await upload.ReadContentAsync(TestContext.Current.CancellationToken);

        Assert.Equal(content, read);
    }

    [Fact]
    public async Task A_file_of_exactly_the_limit_is_read()
    {
        var request = MultipartRequests.Files(("file", new byte[MaxFileBytes]));

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.True(upload.HasFile);
    }

    [Fact]
    public async Task A_text_field_named_file_is_not_a_file()
    {
        var request = MultipartRequests.Form([], [("file", "not a workbook")]);

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.False(upload.HasFile);
        Assert.Equal("required", upload.Problem);
    }

    [Fact]
    public async Task A_text_field_keeps_non_ascii_characters()
    {
        var request = MultipartRequests.Form([("file", new byte[10])], [("note", "Pólvora")]);

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Equal("Pólvora", upload.Field("note"));
    }

    [Fact]
    public async Task A_text_field_over_the_length_limit_makes_the_form_unreadable()
    {
        // Only a hand-made request sends such a field; the reason is not specified, only the refusal.
        var request = MultipartRequests.Form([("file", new byte[10])], [("comparsaId", new string('a', 2048))]);

        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.False(upload.HasFile);
        Assert.Null(upload.Field("comparsaId"));
    }

    [Fact]
    public async Task Reading_the_content_without_a_file_is_refused()
    {
        var request = MultipartRequests.Files(("other", new byte[10]));
        var upload = await SpreadsheetUploads.ReadAsync(request, MaxFileBytes, NullLogger.Instance, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => upload.ReadContentAsync(TestContext.Current.CancellationToken));
    }
}
