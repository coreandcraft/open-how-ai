using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;

namespace Journey.Service.Features.Imports;

// How a supplied file may be shown. Executables, archives, and active content stay downloads.
public static class SuppliedFileTypes
{
    public const string Image = "image";
    public const string Pdf = "pdf";
    public const string Video = "video";
    public const string Audio = "audio";
    public const string Text = "text";
    public const string Document = "document";
    public const string Download = "download";

    public static string Kind(string path)
    {
        var extension = Extension(path);
        if (extension is "png" or "jpg" or "jpeg" or "gif" or "webp" or "bmp")
            return Image;
        if (extension == "pdf")
            return Pdf;
        if (extension is "mp4" or "m4v" or "webm" or "mov" or "ogv" or "mkv" or "avi")
            return Video;
        if (extension is "mp3" or "wav" or "ogg" or "m4a" or "aac" or "flac" or "opus")
            return Audio;
        if (extension is "txt" or "md" or "markdown" or "csv" or "json" or "log")
            return Text;
        if (extension is "doc" or "docx" or "rtf" or "odt" or "ppt" or "pptx" or "xls" or "xlsx")
            return Document;
        return Download;
    }

    public static bool ServesInline(string path) =>
        Kind(path) is Image or Pdf or Video or Audio or Text;

    public static string MediaType(string path) =>
        Extension(path) switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            "pdf" => "application/pdf",
            "mp4" or "m4v" => "video/mp4",
            "webm" => "video/webm",
            "mov" => "video/quicktime",
            "ogv" => "video/ogg",
            "mkv" => "video/x-matroska",
            "avi" => "video/x-msvideo",
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "ogg" or "opus" => "audio/ogg",
            "m4a" => "audio/mp4",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "txt" or "md" or "markdown" or "csv" or "log" => "text/plain; charset=utf-8",
            "json" => "application/json",
            _ => "application/octet-stream",
        };

    public static string? MediaTypeFromSignature(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return "image/png";
        if (bytes.StartsWith(new byte[] { 255, 216, 255 }))
            return "image/jpeg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
            return "image/gif";
        if (
            bytes.Length >= 12
            && bytes.StartsWith("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
        )
            return "image/webp";
        if (bytes.StartsWith("%PDF-"u8))
            return "application/pdf";
        if (
            bytes.Length >= 12
            && bytes.StartsWith("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WAVE"u8)
        )
            return "audio/wav";
        if (bytes.StartsWith("ID3"u8))
            return "audio/mpeg";
        if (bytes.StartsWith("OggS"u8))
            return "audio/ogg";
        if (bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8))
            return "video/mp4";
        if (bytes.StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }))
            return "video/webm";
        return null;
    }

    public static string ContentDisposition(string path, bool download, bool forceInline = false)
    {
        var inline = !download && (forceInline || ServesInline(path));
        var header = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        var name = FileName(path);
        header.FileNameStar = name;
        if (name.All(c => c > ' ' && c < 128 && c != '"' && c != '\\'))
            header.FileName = name;
        return header.ToString();
    }

    public static string? ReadText(Stream stream, string path, int maxChars = 100_000)
    {
        var extension = Extension(path);
        if (extension is "txt" or "md" or "markdown" or "csv" or "json" or "log")
            return ReadPlain(stream, maxChars);
        if (extension is "docx" or "pptx" or "xlsx")
            return ReadOffice(stream, extension, maxChars);
        return null;
    }

    static string Extension(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    public static string FileName(string path)
    {
        var name = Path.GetFileName(path.Replace('\\', '/'));
        var cleaned = new string(name.Where(c => c >= ' ' && c != '"' && c != '\\').ToArray());
        return cleaned.Length == 0 ? "file" : cleaned;
    }

    static string ReadPlain(Stream stream, int maxChars)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true
        );
        var buffer = new char[maxChars];
        var read = reader.Read(buffer, 0, maxChars);
        var text = new string(buffer, 0, read).Trim();
        return text.Length == 0 ? "" : text;
    }

    static string? ReadOffice(Stream stream, string extension, int maxChars)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > 20_000_000)
                return null;
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        try
        {
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            var entries = extension switch
            {
                "docx" => zip.Entries.Where(e =>
                    e.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase)
                ),
                "pptx" => zip
                    .Entries.Where(e =>
                        e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    )
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase),
                _ => zip.Entries.Where(e =>
                    e.FullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase)
                ),
            };
            var text = new StringBuilder();
            foreach (var entry in entries)
            {
                if (entry.Length > 5_000_000)
                    continue;
                using var xml = entry.Open();
                using var reader = XmlReader.Create(
                    xml,
                    new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        MaxCharactersInDocument = 2_000_000,
                    }
                );
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
                    {
                        text.Append(reader.ReadElementContentAsString());
                        if (text.Length >= maxChars)
                            return text.ToString(0, maxChars);
                    }
                    else if (
                        reader.NodeType == XmlNodeType.EndElement && reader.LocalName is "p" or "br"
                    )
                        text.Append('\n');
                }
            }
            var result = text.ToString().Trim();
            return result.Length == 0 ? null : result;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException)
        {
            return null;
        }
    }
}
