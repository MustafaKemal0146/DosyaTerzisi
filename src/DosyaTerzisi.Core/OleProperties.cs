using System.Buffers.Binary;
using System.Text;

namespace DosyaTerzisi.Core;

/// <summary>
/// Reads the summary of the old binary Office files (.doc, .xls, .ppt and templates): they are OLE2 compound files that
/// keep the properties in the <c>\005SummaryInformation</c> and <c>\005DocumentSummaryInformation</c> streams.
/// The compound file format is parsed here, no outside package is used.
/// </summary>
internal static class OleProperties
{
    public const long MaxFileSize = 64L * 1024 * 1024;

    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "doc", "dot", "xls", "xlt", "ppt", "pot",
    };

    private static readonly byte[] Signature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FreeSector = 0xFFFFFFFF;
    private const uint MaxRegularSector = 0xFFFFFFFA;

    /// <exception cref="InvalidDataException">The file is not a readable compound file.</exception>
    public static void Read(string path, Dictionary<string, TagValue> values)
    {
        byte[] data;
        using (var stream = File.OpenRead(path))
        {
            if (stream.Length > MaxFileSize) return;
            data = new byte[stream.Length];
            stream.ReadExactly(data);
        }

        try
        {
            var file = new CompoundFile(data);

            if (file.ReadStream("\u0005SummaryInformation") is { } summary) ReadSummary(summary, values);
            if (file.ReadStream("\u0005DocumentSummaryInformation") is { } extra) ReadDocumentSummary(extra, values);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException)
        {
            throw new InvalidDataException("The Office file is damaged.", ex);
        }
    }

    // ---- Property sets -------------------------------------------------------------------------------------------

    private static void ReadSummary(byte[] stream, Dictionary<string, TagValue> values)
    {
        var properties = ParsePropertySet(stream);
        Text(values, "DOC.Title", properties, 2);
        Text(values, "DOC.Subject", properties, 3);
        Text(values, "DOC.Author", properties, 4);
        Text(values, "DOC.Keywords", properties, 5);
        Text(values, "DOC.Comments", properties, 6);
        Text(values, "DOC.LastModifiedBy", properties, 8);
        Date(values, "DOC.Created", properties, 12);
        Date(values, "DOC.Modified", properties, 13);
        Count(values, "DOC.Pages", properties, 14);
        Count(values, "DOC.Words", properties, 15);
    }

    private static void ReadDocumentSummary(byte[] stream, Dictionary<string, TagValue> values)
    {
        var properties = ParsePropertySet(stream);
        Text(values, "DOC.Category", properties, 2);
        Text(values, "DOC.Company", properties, 15);
    }

    private static void Text(Dictionary<string, TagValue> values, string name, Dictionary<uint, object> properties, uint id)
    {
        if (properties.TryGetValue(id, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
            values.TryAdd(name, new TagValue(text.Trim()));
    }

    private static void Count(Dictionary<string, TagValue> values, string name, Dictionary<uint, object> properties, uint id)
    {
        if (properties.TryGetValue(id, out var value) && value is int number && number > 0)
            values.TryAdd(name, new TagValue(number.ToString(System.Globalization.CultureInfo.InvariantCulture), Number: number));
    }

    private static void Date(Dictionary<string, TagValue> values, string name, Dictionary<uint, object> properties, uint id)
    {
        if (properties.TryGetValue(id, out var value) && value is DateTime utc)
        {
            var local = utc.ToLocalTime();
            values.TryAdd(name, new TagValue(local.ToString("yyyy-MM-dd HH:mm:ss"), Date: local));
        }
    }

    /// <summary>Parses the first section of a property set stream into id → string, int or UTC date.</summary>
    private static Dictionary<uint, object> ParsePropertySet(byte[] stream)
    {
        var result = new Dictionary<uint, object>();
        if (stream.Length < 48) return result;

        uint sets = BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(24));
        if (sets == 0 || sets > 16) return result;

        long section = BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(44));
        if (section + 8 > stream.Length) return result;

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan((int)section + 4));
        if (count > 4096 || section + 8 + count * 8L > stream.Length) return result;

        var offsets = new List<(uint Id, long Offset)>();
        for (uint i = 0; i < count; i++)
        {
            int at = (int)(section + 8 + i * 8);
            offsets.Add((BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(at)), section + BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(at + 4))));
        }

        // Property 1 is the code page that the narrow strings use.
        int codePage = 1252;
        foreach (var (id, offset) in offsets)
        {
            if (id == 1 && offset + 8 <= stream.Length && stream[offset] is 2 or 18)
                codePage = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan((int)offset + 4));
        }

        foreach (var (id, offset) in offsets)
        {
            if (id == 0 || id == 1 || offset + 8 > stream.Length) continue;
            if (ReadValue(stream, (int)offset, codePage) is { } value) result.TryAdd(id, value);
        }

        return result;
    }

    private static object? ReadValue(byte[] stream, int offset, int codePage)
    {
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(offset));
        int body = offset + 4;

        switch (type)
        {
            case 2 when body + 2 <= stream.Length: // VT_I2
                return (int)BinaryPrimitives.ReadInt16LittleEndian(stream.AsSpan(body));
            case 3 when body + 4 <= stream.Length: // VT_I4
            case 19 when body + 4 <= stream.Length: // VT_UI4
                return BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(body));
            case 0x1E when body + 4 <= stream.Length: // VT_LPSTR
            {
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(body));
                if (length == 0 || body + 4L + length > stream.Length) return null;
                return Narrow(stream.AsSpan(body + 4, (int)length), codePage);
            }
            case 0x1F when body + 4 <= stream.Length: // VT_LPWSTR
            {
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(body));
                if (length == 0 || body + 4L + length * 2 > stream.Length) return null;
                return Encoding.Unicode.GetString(stream, body + 4, (int)length * 2).TrimEnd('\0');
            }
            case 0x40 when body + 8 <= stream.Length: // VT_FILETIME
            {
                long ticks = BinaryPrimitives.ReadInt64LittleEndian(stream.AsSpan(body));
                if (ticks <= 0 || ticks > 2650467743999999999L) return null; // before 1601 or after year 9999
                return DateTime.FromFileTimeUtc(ticks);
            }
            default:
                return null;
        }
    }

    private static string Narrow(ReadOnlySpan<byte> bytes, int codePage)
    {
        int end = bytes.IndexOf((byte)0);
        if (end >= 0) bytes = bytes[..end];

        Encoding encoding;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            encoding = codePage switch
            {
                1200 => Encoding.Unicode,
                65001 => Encoding.UTF8,
                0 or 1 => Encoding.Latin1,
                _ => Encoding.GetEncoding(codePage),
            };
        }
        catch (ArgumentException)
        {
            encoding = Encoding.Latin1;
        }

        return encoding.GetString(bytes);
    }

    // ---- Compound file -------------------------------------------------------------------------------------------

    private sealed class CompoundFile
    {
        private readonly byte[] _data;
        private readonly int _sectorSize;
        private readonly int _miniSectorSize;
        private readonly uint _miniCutoff;
        private readonly uint[] _fat;
        private uint[] _miniFat = Array.Empty<uint>();
        private byte[] _miniStream = Array.Empty<byte>();
        private readonly List<(string Name, byte Type, uint Start, long Size)> _entries = new();

        public CompoundFile(byte[] data)
        {
            _data = data;
            if (data.Length < 512 || !data.AsSpan(0, 8).SequenceEqual(Signature))
                throw new InvalidDataException("Not an OLE2 compound file.");

            int sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x1E));
            int miniShift = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x20));
            if (sectorShift is not (9 or 12) || miniShift is < 4 or > 9)
                throw new InvalidDataException("Unsupported compound file sector size.");

            _sectorSize = 1 << sectorShift;
            _miniSectorSize = 1 << miniShift;
            _miniCutoff = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x38));

            uint fatCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x2C));
            uint firstDirectory = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x30));
            uint firstMiniFat = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x3C));
            uint miniFatCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x40));
            uint firstDifat = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x44));
            uint difatCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x48));

            long sectors = (data.Length - 512L + _sectorSize - 1) / _sectorSize;
            if (fatCount == 0 || fatCount > sectors || miniFatCount > sectors || difatCount > sectors)
                throw new InvalidDataException("The compound file header is damaged.");

            // FAT sector list: 109 entries in the header, then the DIFAT chain.
            var fatSectors = new List<uint>();
            for (int i = 0; i < 109 && fatSectors.Count < fatCount; i++)
            {
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x4C + i * 4));
                if (id <= MaxRegularSector) fatSectors.Add(id);
            }

            uint difat = firstDifat;
            for (uint n = 0; n < difatCount && difat <= MaxRegularSector && fatSectors.Count < fatCount; n++)
            {
                int offset = SectorOffset(difat);
                int perSector = _sectorSize / 4 - 1;
                for (int i = 0; i < perSector && fatSectors.Count < fatCount; i++)
                {
                    uint id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + i * 4));
                    if (id <= MaxRegularSector) fatSectors.Add(id);
                }

                difat = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + perSector * 4));
            }

            int perFat = _sectorSize / 4;
            _fat = new uint[fatSectors.Count * perFat];
            for (int s = 0; s < fatSectors.Count; s++)
            {
                int offset = SectorOffset(fatSectors[s]);
                for (int i = 0; i < perFat; i++)
                    _fat[s * perFat + i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + i * 4));
            }

            ReadDirectory(firstDirectory);

            if (miniFatCount > 0 && firstMiniFat <= MaxRegularSector)
            {
                byte[] raw = ReadChain(firstMiniFat, -1);
                _miniFat = new uint[raw.Length / 4];
                for (int i = 0; i < _miniFat.Length; i++) _miniFat[i] = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * 4));
            }

            var root = _entries.FirstOrDefault(e => e.Type == 5);
            if (root.Name is not null && root.Start <= MaxRegularSector && root.Size > 0)
                _miniStream = ReadChain(root.Start, root.Size);
        }

        public byte[]? ReadStream(string name)
        {
            foreach (var entry in _entries)
            {
                if (entry.Type != 2 || !string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Size > MaxFileSize) throw new InvalidDataException("A stream is too large.");
                if (entry.Size == 0) return Array.Empty<byte>();

                return entry.Size < _miniCutoff ? ReadMiniChain(entry.Start, entry.Size) : ReadChain(entry.Start, entry.Size);
            }

            return null;
        }

        private int SectorOffset(uint id)
        {
            long offset = 512L + (long)id * _sectorSize;
            if (offset + _sectorSize > _data.Length)
                throw new InvalidDataException("A sector lies beyond the end of the file.");
            return (int)offset;
        }

        /// <summary>Reads a FAT chain; <paramref name="size"/> -1 reads whole sectors, otherwise the result is cut to it.</summary>
        private byte[] ReadChain(uint start, long size)
        {
            using var output = new MemoryStream();
            uint sector = start;
            int guard = 0;
            while (sector <= MaxRegularSector)
            {
                if (sector >= _fat.Length || ++guard > _fat.Length) throw new InvalidDataException("The sector chain is damaged.");
                output.Write(_data, SectorOffset(sector), _sectorSize);
                if (size >= 0 && output.Length >= size) break;
                if (output.Length > MaxFileSize) throw new InvalidDataException("A stream is too large.");
                sector = _fat[sector];
            }

            byte[] bytes = output.ToArray();
            if (size >= 0 && bytes.Length > size) Array.Resize(ref bytes, (int)size);
            if (size >= 0 && bytes.Length < size) throw new InvalidDataException("A stream is shorter than announced.");
            return bytes;
        }

        private byte[] ReadMiniChain(uint start, long size)
        {
            using var output = new MemoryStream();
            uint sector = start;
            int guard = 0;
            while (sector <= MaxRegularSector && output.Length < size)
            {
                if (sector >= _miniFat.Length || ++guard > _miniFat.Length) throw new InvalidDataException("The mini sector chain is damaged.");
                long offset = (long)sector * _miniSectorSize;
                if (offset + _miniSectorSize > _miniStream.Length) throw new InvalidDataException("A mini sector lies beyond the mini stream.");
                output.Write(_miniStream, (int)offset, _miniSectorSize);
                sector = _miniFat[sector];
            }

            byte[] bytes = output.ToArray();
            if (bytes.Length < size) throw new InvalidDataException("A stream is shorter than announced.");
            Array.Resize(ref bytes, (int)size);
            return bytes;
        }

        private void ReadDirectory(uint first)
        {
            byte[] raw = ReadChain(first, -1);
            for (int at = 0; at + 128 <= raw.Length && _entries.Count < 100_000; at += 128)
            {
                byte type = raw[at + 66];
                if (type == 0) continue;

                int nameBytes = Math.Min((int)BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(at + 64)), 64);
                string name = Encoding.Unicode.GetString(raw, at, Math.Max(nameBytes - 2, 0));
                uint start = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(at + 116));
                long size = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(at + 120)); // version 3 files use 32 bits only
                _entries.Add((name, type, start, size));
            }

            if (_entries.Count == 0) throw new InvalidDataException("The compound file has no directory.");
        }
    }
}
