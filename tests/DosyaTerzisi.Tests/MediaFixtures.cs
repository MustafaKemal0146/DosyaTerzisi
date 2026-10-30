using System.Windows.Media;
using System.Windows.Media.Imaging;
using DosyaTerzisi.Core;

namespace DosyaTerzisi.Tests;

/// <summary>Builds small real files with tags, so the readers are tested against what they will meet.</summary>
internal static class MediaFixtures
{
    /// <summary>A short silent MP3 (MPEG-1 Layer 3, 128 kbit/s) with ID3 tags.</summary>
    public static string WriteMp3(string path, Action<TagLib.Tag>? tag = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var frame = new byte[417];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = 0x90;
        frame[3] = 0x64;
        using (var stream = File.Create(path))
        {
            for (int i = 0; i < 30; i++) stream.Write(frame);
        }

        if (tag is not null)
        {
            using var file = TagLib.File.Create(path);
            tag(file.Tag);
            file.Save();
        }
        return path;
    }

    /// <summary>A tiny JPEG with the given EXIF/IPTC values (query path → value).</summary>
    public static string WriteJpeg(string path, IReadOnlyDictionary<string, object> metadata)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr24, null, new byte[2 * 6], 6);
        var meta = new BitmapMetadata("jpg");
        foreach (var (query, value) in metadata) meta.SetQuery(query, value);

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, meta, null));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    public static IReadOnlyDictionary<string, object> PhotoMetadata => new Dictionary<string, object>
    {
        ["/app1/ifd/{ushort=271}"] = "Canon",
        ["/app1/ifd/{ushort=272}"] = "EOS 5D",
        ["/app1/ifd/{ushort=270}"] = "Aile piknigi",
        ["/app1/ifd/{ushort=315}"] = "Ayse Yilmaz",
        ["/app1/ifd/exif/{ushort=36867}"] = "2019:08:17 14:30:05",
    };

    /// <summary>A reader that returns fixed values, for tests that are about the names, not the files.</summary>
    public sealed class FakeTags(params (string Name, TagValue Value)[] values) : ITagReader
    {
        public int Reads { get; private set; }

        public TagReadResult Read(FileEntry entry)
        {
            Reads++;
            return new TagReadResult(values.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase));
        }
    }

    public static (string, TagValue) Text(string name, string text) => (name, new TagValue(text));

    public static (string, TagValue) Number(string name, double number) => (name, new TagValue(number.ToString(), Number: number));

    public static (string, TagValue) Date(string name, DateTime date) => (name, new TagValue(date.ToString("s"), Date: date));
}
