using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SpotifyLyricsWin.Core;

/// <summary>
/// 酷狗 KRC 逐字歌词解码器（逐行移植自安卓 KrcDecoder.kt V1.7）。
/// lyrics.kugou.com/download?fmt=krc 返回 base64("krc1" + XOR(zlib(KRC 明文)))。
/// XOR 密钥最后两字节必须是 6E 69（写成 E9 28 会 inflate 失败）。
/// 解密明文每行：[行起始ms,行时长ms]&lt;字偏移,字时长,0&gt;字...
/// 统一转成「每字一个绝对时间标签」的增强 LRC，整条链路与安卓版一致。
/// </summary>
public static class KrcDecoder
{
    private static readonly int[] XorKey =
    {
        0x40, 0x47, 0x61, 0x77, 0x5E, 0x32, 0x74, 0x47,
        0x51, 0x36, 0x31, 0x2D, 0xCE, 0xD2, 0x6E, 0x69
    };

    private static readonly Regex LineHead = new(@"^\[(\d+),(\d+)](.*)$");
    private static readonly Regex WordHead = new(@"<(\d+),(\d+),\d+>");
    private static readonly Regex MetaLine = new(
        @"^(词|曲|作词|作曲|编曲|制作人|和声|合声|录音|混音|母带|监制|出品|OP|SP)\s*[:：]",
        RegexOptions.IgnoreCase);

    /// <summary>download 接口的 content 字段 → KRC 明文；任何异常返回 null 由上层换源。</summary>
    public static string? Decode(string contentBase64)
    {
        try
        {
            byte[] raw = Convert.FromBase64String(contentBase64);
            if (raw.Length <= 4) return null;
            var enc = new byte[raw.Length - 4];
            for (int i = 0; i < enc.Length; i++)
                enc[i] = (byte)((raw[i + 4] & 0xFF) ^ XorKey[i % XorKey.Length]);
            using var ms = new MemoryStream(enc);
            using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(zlib, Encoding.UTF8);
            return reader.ReadToEnd().Replace("\r", "");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>KRC 明文 → 带绝对字级时间戳的增强 LRC 文本</summary>
    public static string ToEnhancedLrc(string krc)
    {
        var sb = new StringBuilder();
        int count = 0;
        foreach (string rawLine in krc.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;
            var head = LineHead.Match(line);
            if (!head.Success) continue;
            if (!long.TryParse(head.Groups[1].Value, out long start)) continue;
            long.TryParse(head.Groups[2].Value, out long duration);
            string body = head.Groups[3].Value;
            if (body.Length == 0) continue;

            var (text, offsets) = SplitWords(body);
            if (text.Length == 0 || MetaLine.IsMatch(text)) continue;

            sb.Append('[').Append(Stamp(start)).Append(']');
            if (offsets != null && offsets.Length == text.Length)
            {
                for (int i = 0; i < text.Length; i++)
                    sb.Append('<').Append(Stamp(start + offsets[i])).Append('>').Append(text[i]);
                sb.Append('<').Append(Stamp(start + Math.Max(1, duration))).Append('>');
            }
            else
            {
                sb.Append(text);
            }
            sb.Append('\n');
            count++;
        }
        return count > 0 ? sb.ToString() : "";
    }

    /// <summary>把 &lt;偏移,时长,0&gt;字 拆成「纯文本」+「每个字相对行起点的毫秒偏移」</summary>
    private static (string, long[]?) SplitWords(string body)
    {
        var tags = WordHead.Matches(body);
        if (tags.Count == 0) return (body, null);
        if (tags[0].Index != 0) return (WordHead.Replace(body, ""), null);

        var sb = new StringBuilder();
        var times = new List<long>(body.Length);
        for (int i = 0; i < tags.Count; i++)
        {
            int from = tags[i].Index + tags[i].Length;
            int to = i + 1 < tags.Count ? tags[i + 1].Index : body.Length;
            if (from >= to) continue;
            string seg = body.Substring(from, to - from);
            long.TryParse(tags[i].Groups[1].Value, out long offset);
            long.TryParse(tags[i].Groups[2].Value, out long dur);
            for (int j = 0; j < seg.Length; j++)
            {
                sb.Append(seg[j]);
                // 酷狗通常一块一个字；万一一块多个字，就在块内均分
                times.Add(offset + dur * j / seg.Length);
            }
        }
        string text = DecodeEntities(sb.ToString());
        long[] arr = times.ToArray();
        return (text, arr.Length == text.Length && text.Length > 0 ? arr : null);
    }

    private static string DecodeEntities(string s) => s
        .Replace("&apos;", "'")
        .Replace("&quot;", "\"")
        .Replace("&lt;", "<")
        .Replace("&gt;", ">")
        .Replace("&nbsp;", " ")
        .Replace("&amp;", "&");

    /// <summary>毫秒 → mm:ss.mmm（InvariantCulture，避免本地数字字形）</summary>
    private static string Stamp(long ms)
    {
        long v = Math.Max(0, ms);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{v / 60000L:00}:{(v % 60000L) / 1000L:00}.{v % 1000L:000}");
    }
}
