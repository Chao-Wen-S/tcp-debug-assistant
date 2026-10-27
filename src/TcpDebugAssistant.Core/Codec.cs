using System.Text;

namespace TcpDebugAssistant.Core;

/// <summary>
/// 带状态的 UTF-8 解码器：TCP 是字节流，一个中文字符可能被拆到两个数据包里，
/// 用普通 Encoding.GetString 会把它们解成乱码，所以这里保持跨包状态。
/// </summary>
public sealed class Utf8StreamDecoder
{
    private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetDecoder();

    public string Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        // 注意：GetCharCount 也会推进解码器状态，必须先算容量再一次 GetChars，
        // 否则跨包残留的半截字符会被消费两次。
        var chars = new char[data.Length];
        var charCount = _decoder.GetChars(data, chars, flush: false);
        return charCount == 0 ? string.Empty : new string(chars, 0, charCount);
    }

    public void Reset() => _decoder.Reset();
}

/// <summary>发送时使用的行尾追加方式。</summary>
public enum LineEnding
{
    None,
    CrLf,
    Lf,
    Cr
}

public static class LineEndingExtensions
{
    public static byte[] GetBytes(this LineEnding ending) => ending switch
    {
        LineEnding.CrLf => new byte[] { 0x0D, 0x0A },
        LineEnding.Lf => new byte[] { 0x0A },
        LineEnding.Cr => new byte[] { 0x0D },
        _ => Array.Empty<byte>()
    };

    public static string DisplayName(this LineEnding ending) => ending switch
    {
        LineEnding.CrLf => "CRLF (\\r\\n)",
        LineEnding.Lf => "LF (\\n)",
        LineEnding.Cr => "CR (\\r)",
        _ => "无"
    };
}

/// <summary>字节数格式化，用于状态栏显示。</summary>
public static class SizeFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}

