using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PolvorApp.ArquebusierRegistry.Import;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>
/// The package checks made before ClosedXML opens an upload (spec: Import file reading; design D3):
/// only a macro-free xlsx workbook whose parts really decompress within the limit, declare no
/// document type, nest shallowly and hold a bounded number of rows, cells and strings passes.
/// </summary>
public sealed class WorkbookPackageTests
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string WorkbookContentTypes =
        """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/></Types>""";

    [Fact]
    public void A_synthetic_workbook_passes() =>
        Assert.Null(WorkbookPackage.Rejection(ImportWorkbookBuilder.Template().ValidRow().Build()));

    [Fact]
    public void A_full_template_sized_workbook_passes()
    {
        var builder = ImportWorkbookBuilder.Template();
        for (var row = 0; row < 1000; row++)
        {
            builder.ValidRow();
        }

        Assert.Null(WorkbookPackage.Rejection(builder.Build()));
    }

    [Fact]
    public void Bytes_that_are_not_a_zip_are_refused() =>
        Assert.NotNull(WorkbookPackage.Rejection(Encoding.ASCII.GetBytes("APELLIDOS;NOMBRE;DNI\r\n")));

    [Fact]
    public void A_pdf_renamed_as_a_workbook_is_refused() =>
        Assert.NotNull(WorkbookPackage.Rejection(Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF")));

    [Fact]
    public void A_truncated_workbook_is_refused()
    {
        var content = ImportWorkbookBuilder.Template().ValidRow().Build();

        Assert.NotNull(WorkbookPackage.Rejection(content[..(content.Length / 2)]));
    }

    [Fact]
    public void A_macro_enabled_workbook_is_refused()
    {
        var content = Zip(("[Content_Types].xml", Encoding.UTF8.GetBytes(WorkbookContentTypes.Replace(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
            "application/vnd.ms-excel.sheet.macroEnabled.main+xml", StringComparison.Ordinal))));

        Assert.Equal("notWorkbook", WorkbookPackage.Rejection(content));
    }

    [Fact]
    public void A_package_without_a_workbook_part_is_refused()
    {
        var content = Zip(("[Content_Types].xml", Encoding.UTF8.GetBytes(WorkbookContentTypes.Replace(
            "spreadsheetml.sheet.main+xml", "wordprocessingml.document.main+xml", StringComparison.Ordinal))));

        Assert.Equal("notWorkbook", WorkbookPackage.Rejection(content));
    }

    [Fact]
    public void A_zip_without_content_types_is_refused() =>
        Assert.Equal("notWorkbook", WorkbookPackage.Rejection(Zip(("xl/workbook.xml", "<workbook/>"u8.ToArray()))));

    [Fact]
    public void Parts_that_decompress_past_the_limit_are_refused()
    {
        var content = Workbook(("xl/media/image1.bin", new byte[10 * WorkbookPackage.MaxUncompressedBytes]));

        Assert.True(content.Length < 1024 * 1024, "The bomb must be small when compressed.");
        Assert.Equal("package", WorkbookPackage.Rejection(content));
    }

    [Fact]
    public void Headers_that_understate_the_sizes_do_not_hide_a_bomb()
    {
        var content = Workbook(("xl/media/image1.bin", new byte[WorkbookPackage.MaxUncompressedBytes + 1]));

        Assert.NotNull(WorkbookPackage.Rejection(UnderstateSizes(content)));
    }

    [Fact]
    public void Too_many_entries_are_refused()
    {
        var entries = Enumerable.Range(0, WorkbookPackage.MaxEntries)
            .Select(index => ($"xl/extra{index}.bin", new byte[1]))
            .ToArray();

        Assert.Equal("tooManyEntries", WorkbookPackage.Rejection(Workbook(entries)));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-32")]
    public void A_part_with_a_document_type_is_refused(string encoding)
    {
        var sheet = $"""<?xml version="1.0" encoding="{encoding}"?><!DOCTYPE worksheet [<!ENTITY a "aaaaaaaaaa">]><worksheet>&a;</worksheet>""";
        var content = Workbook(("xl/worksheets/sheet1.xml", Encoding.GetEncoding(encoding).GetPreamble().Concat(Encoding.GetEncoding(encoding).GetBytes(sheet)).ToArray()));

        Assert.Equal("xml", WorkbookPackage.Rejection(content));
    }

    [Theory]
    [InlineData("xl/sharedStrings.xml")]
    [InlineData("xl/styles.xml")]
    [InlineData("xl/_rels/workbook.xml.rels")]
    public void Deeply_nested_elements_are_refused_before_any_parser_recurses(string part)
    {
        const int depth = 100_000;
        var xml = new StringBuilder($"<sst xmlns=\"{Main}\"><si>");
        xml.Insert(xml.Length, "<x>", depth).Insert(xml.Length, "</x>", depth).Append("</si></sst>");

        Assert.Equal("tooDeep", WorkbookPackage.Rejection(Workbook((part, Encoding.UTF8.GetBytes(xml.ToString())))));
    }

    [Fact]
    public void Too_many_rows_are_refused_before_loading()
    {
        var xml = new StringBuilder($"<worksheet xmlns=\"{Main}\"><sheetData>");
        xml.Insert(xml.Length, "<row/>", WorkbookPackage.MaxRowElements + 1).Append("</sheetData></worksheet>");

        Assert.Equal("tooManyRows", WorkbookPackage.Rejection(Workbook(("xl/worksheets/sheet1.xml", Encoding.UTF8.GetBytes(xml.ToString())))));
    }

    [Fact]
    public void Too_many_cells_are_refused_before_loading()
    {
        var xml = new StringBuilder($"<worksheet xmlns=\"{Main}\"><sheetData><row>");
        xml.Insert(xml.Length, "<c/>", WorkbookPackage.MaxCellElements + 1).Append("</row></sheetData></worksheet>");

        Assert.Equal("tooManyCells", WorkbookPackage.Rejection(Workbook(("xl/worksheets/sheet1.xml", Encoding.UTF8.GetBytes(xml.ToString())))));
    }

    [Fact]
    public void Too_many_shared_strings_are_refused_before_loading()
    {
        var xml = new StringBuilder($"<sst xmlns=\"{Main}\">");
        xml.Insert(xml.Length, "<si><t>x</t></si>", WorkbookPackage.MaxSharedStrings + 1).Append("</sst>");

        Assert.Equal("tooManySharedStrings", WorkbookPackage.Rejection(Workbook(("xl/sharedStrings.xml", Encoding.UTF8.GetBytes(xml.ToString())))));
    }

    [Fact]
    public void Malformed_xml_is_refused() =>
        Assert.Equal("xml", WorkbookPackage.Rejection(Workbook(("xl/workbook.xml", "<workbook><sheets>"u8.ToArray()))));

    private static byte[] Workbook(params (string Name, byte[] Content)[] parts) =>
        Zip([("[Content_Types].xml", Encoding.UTF8.GetBytes(WorkbookContentTypes)), .. parts]);

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    /// <summary>Rewrites every declared uncompressed size, local and central, to 1 byte.</summary>
    private static byte[] UnderstateSizes(byte[] zip)
    {
        var bytes = (byte[])zip.Clone();
        for (var offset = 0; offset + 30 < bytes.Length; offset++)
        {
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
            if (signature == 0x04034b50)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 22), 1);
            }
            else if (signature == 0x02014b50)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24), 1);
            }
        }

        return bytes;
    }
}
