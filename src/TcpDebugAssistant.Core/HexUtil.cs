namespace TcpDebugAssistant.Core;

/// <summary>
/// 十六进制与文本之间的转换工具。
/// </summary>
public static class HexUtil
{
    /// <summary>
    /// 把 "AA BB 0D 0A"、"aa,bb,0d,0a"、"0xAA 0xBB" 之类的字符串解析成字节数组。
    /// 允许空格、逗号、分号、换行和 0x 前缀；奇数个十六进制字符返回失败。
    /// </summary>
    public static bool TryParse(string? text, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "内容为空";
            return false;
        }

        // 先去掉 0x / 0X 前缀，避免把前缀里的 x 当成非法字符。
        var normalized = System.Text.RegularExpressions.Regex.Replace(text, "(?i)0x", " ");
        var nibbles = new List<byte>(normalized.Length);
        var cleaned = new System.Text.StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (ch is ' ' or '\t' or '\r' or '\n' or ',' or ';' or '|' or '-')
            {
                cleaned.Append(' ');
                continue;
            }

            if (!Uri.IsHexDigit(ch))
            {
                error = $"包含非法字符 '{ch}'（只允许 0-9 A-F，以及空格/逗号分隔符）";
                return false;
            }

            cleaned.Append(char.ToUpperInvariant(ch));
        }

        var tokens = cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in tokens)
        {
            var token = raw;
            if (token.Length == 0)
            {
                error = "存在空的字节片段，请检查分隔符";
                return false;
            }

            if (token.Length % 2 != 0)
            {
                // 允许 "A" 这种单个半字节，按 0A 处理（常见于手动输入）。
                token = "0" + token;
            }

            for (var i = 0; i < token.Length; i += 2)
            {
                nibbles.Add(Convert.ToByte(token.Substring(i, 2), 16));
            }
        }

        if (nibbles.Count == 0)
        {
            error = "没有解析到任何字节";
            return false;
        }

        bytes = nibbles.ToArray();
        return true;
    }

    /// <summary>字节数组格式化成大写十六进制字符串，默认空格分隔。</summary>
    public static string ToHexString(ReadOnlySpan<byte> data, string separator = " ", bool withPrefix = false)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(data.Length * (2 + separator.Length));
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(separator);
            }

            if (withPrefix)
            {
                sb.Append("0x");
            }

            sb.Append(data[i].ToString("X2"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 把字节数组渲染成可读文本：可打印 ASCII 原样输出，其余用 '.' 占位。
    /// 用于接收区在“文本模式”下显示二进制数据。
    /// </summary>
    public static string ToPrintable(ReadOnlySpan<byte> data)
    {
        var chars = new char[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            chars[i] = b is 0x09 or 0x0A or 0x0D ? (char)b : b is >= 0x20 and <= 0x7E ? (char)b : '\u00B7';
        }

        return new string(chars);
    }

    /// <summary>统计一段数据里有多少不可打印字节，用来判断“其实是二进制”的场景。</summary>
    public static int CountNonPrintable(ReadOnlySpan<byte> data)
    {
        var count = 0;
        foreach (var b in data)
        {
            if (b is not (0x09 or 0x0A or 0x0D) && (b < 0x20 || b > 0x7E))
            {
                count++;
            }
        }

        return count;
    }
}

