using System.Text;

namespace Ps3IsoTools;

public class ParamSfo
{
    public string Title { get; init; } = "";
    public string TitleId { get; init; } = "";

    public static ParamSfo? Parse(string path)
    {
        var data = File.ReadAllBytes(path);

        // Validate magic: 00 50 53 46 ("\0PSF")
        if (data.Length < 0x14 || data[0] != 0x00 || data[1] != 0x50 || data[2] != 0x53 || data[3] != 0x46)
            return null;

        var keyTableStart = BitConverter.ToInt32(data, 8);
        var dataTableStart = BitConverter.ToInt32(data, 12);
        var numEntries = BitConverter.ToInt32(data, 16);

        string? title = null;
        string? titleId = null;

        for (int i = 0; i < numEntries; i++)
        {
            int indexOffset = 0x14 + i * 16;
            if (indexOffset + 16 > data.Length) break;

            var keyOffset = BitConverter.ToUInt16(data, indexOffset);
            var dataLen = BitConverter.ToInt32(data, indexOffset + 8);
            var dataOffset = BitConverter.ToInt32(data, indexOffset + 12);

            var keyStart = keyTableStart + keyOffset;
            var key = ReadNullTerminated(data, keyStart);

            var valStart = dataTableStart + dataOffset;
            var value = Encoding.UTF8.GetString(data, valStart, Math.Max(0, dataLen - 1)).TrimEnd('\0', ' ');

            if (key == "TITLE" && title == null)
                title = value;
            else if (key == "TITLE_ID")
            {
                if (value.Length >= 9 && value[4] != '-')
                    titleId = value[..4] + "-" + value[4..];
                else
                    titleId = value;
            }

            if (title != null && titleId != null)
                break;
        }

        if (title == null || titleId == null)
            return null;

        return new ParamSfo { Title = title.Trim(), TitleId = titleId.Trim() };
    }

    static string ReadNullTerminated(byte[] data, int offset)
    {
        int end = offset;
        while (end < data.Length && data[end] != 0) end++;
        return Encoding.UTF8.GetString(data, offset, end - offset);
    }
}
