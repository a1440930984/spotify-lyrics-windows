using System.Security.Cryptography;

namespace SpotifyLyricsWin.Core;

/// <summary>ICO 文件里的一帧（目录项 + 图像数据位置）。宽高已把「字节为 0 表示 256」换算好。</summary>
public sealed record IcoFrame(int Width, int Height, int BitsPerPixel, int ImageSize, int ImageOffset);

/// <summary>
/// ICO 文件解析与单帧抽取（v0.8.3，纯逻辑、无头可测）。
/// 动机：托盘的 new Icon(path) 与窗口的 BitmapImage 加载多帧 ICO 时选哪一帧由框架
/// 决定，真机出现过「任务栏图标偏小」——不排除拿到小帧后只居中不放大。本类把
/// 「取最大帧」变成确定行为：解析目录 → 选面积最大（同面积取色深高）的一帧 →
/// 拼一份只含该帧的合法 ICO 字节，加载方统一吃这份字节，选帧不再听天由命。
/// ICO 格式：6 字节头（保留 0、类型 1=图标、帧数）+ 每帧 16 字节目录项
/// （宽/高字节 0 表示 256、色数、保留、平面数、位深、图像字节数、图像偏移）。
/// </summary>
public static class IcoFrames
{
    /// <summary>解析目录项；数据非法（截断/偏移越界/不是图标）返回空表，绝不抛异常。</summary>
    public static IReadOnlyList<IcoFrame> Parse(byte[] data)
    {
        var frames = new List<IcoFrame>();
        if (data.Length < 6) return frames;
        if (BitConverter.ToUInt16(data, 0) != 0) return frames;      // 保留字段必须为 0
        if (BitConverter.ToUInt16(data, 2) != 1) return frames;      // 类型 1 = 图标（2 是光标）
        int count = BitConverter.ToUInt16(data, 4);
        for (int i = 0; i < count; i++)
        {
            int e = 6 + i * 16;
            if (e + 16 > data.Length) break;
            int width = data[e] == 0 ? 256 : data[e];
            int height = data[e + 1] == 0 ? 256 : data[e + 1];
            int bpp = BitConverter.ToUInt16(data, e + 6);
            int size = BitConverter.ToInt32(data, e + 8);
            int offset = BitConverter.ToInt32(data, e + 12);
            if (size <= 0 || offset < 0 || offset + size > data.Length) continue;
            frames.Add(new IcoFrame(width, height, bpp, size, offset));
        }
        return frames;
    }

    /// <summary>最大帧：先比面积，同面积比色深，再同取先出现者。空表返回 null。</summary>
    public static IcoFrame? Largest(IReadOnlyList<IcoFrame> frames)
        => LargestIndex(frames) is int i ? frames[i] : null;

    /// <summary>最大帧的下标（规则同 Largest）；空表返回 null。</summary>
    public static int? LargestIndex(IReadOnlyList<IcoFrame> frames)
    {
        int best = -1;
        for (int i = 0; i < frames.Count; i++)
        {
            if (best < 0) { best = i; continue; }
            var f = frames[i];
            var b = frames[best];
            if ((long)f.Width * f.Height > (long)b.Width * b.Height
                || ((long)f.Width * f.Height == (long)b.Width * b.Height
                    && f.BitsPerPixel > b.BitsPerPixel))
                best = i;
        }
        return best >= 0 ? best : null;
    }

    /// <summary>
    /// 抽最大帧，拼一份只含该帧的合法 ICO（头 + 单个目录项 + 图像数据原样搬运）。
    /// 解析失败/无有效帧返回 null。
    /// </summary>
    public static byte[]? ExtractLargestFrameIco(byte[] data)
    {
        var frames = Parse(data);
        if (LargestIndex(frames) is not int largestIdx) return null;
        var largest = frames[largestIdx];
        var result = new byte[6 + 16 + largest.ImageSize];
        // 头：保留 0、类型 1、帧数 1
        BitConverter.GetBytes((ushort)0).CopyTo(result, 0);
        BitConverter.GetBytes((ushort)1).CopyTo(result, 2);
        BitConverter.GetBytes((ushort)1).CopyTo(result, 4);
        // 目录项：宽/高字节（256 回写为 0）、色数与保留抄原项、平面数 1、其余按帧信息
        int srcEntry = 6 + largestIdx * 16;
        result[6] = data[srcEntry];          // 宽度字节（256 时原值即 0）
        result[7] = data[srcEntry + 1];      // 高度字节
        result[8] = data[srcEntry + 2];      // 色数
        result[9] = 0;                        // 保留
        BitConverter.GetBytes((ushort)1).CopyTo(result, 10);                 // 平面数
        BitConverter.GetBytes((ushort)largest.BitsPerPixel).CopyTo(result, 12);
        BitConverter.GetBytes(largest.ImageSize).CopyTo(result, 14);
        BitConverter.GetBytes(6 + 16).CopyTo(result, 18);                    // 图像偏移 = 头+一项
        Array.Copy(data, largest.ImageOffset, result, 6 + 16, largest.ImageSize);
        return result;
    }

    /// <summary>
    /// 把一份 PNG 图像数据包成只含单帧的合法 ICO（v0.8.6，纯逻辑、无头可测）。
    /// 用途：托盘专用图是运行时渲染出来的 PNG，需要变成 Icon 才能喂给
    /// NotifyIcon——手写 ICO 容器（头 + 单目录项 + PNG 原样搬运）比走
    /// GetHicon 的句柄路线干净（无句柄所有权问题）。宽高 ≥256 时目录项
    /// 字节按规范写 0。PNG 为空/尺寸非法返回 null。
    /// </summary>
    public static byte[]? WrapPngAsIco(byte[] png, int width, int height)
    {
        if (png.Length == 0 || width <= 0 || height <= 0 || width > 256 || height > 256)
            return null;
        var result = new byte[6 + 16 + png.Length];
        BitConverter.GetBytes((ushort)0).CopyTo(result, 0);          // 保留
        BitConverter.GetBytes((ushort)1).CopyTo(result, 2);          // 类型 1 = 图标
        BitConverter.GetBytes((ushort)1).CopyTo(result, 4);          // 帧数 1
        result[6] = width >= 256 ? (byte)0 : (byte)width;            // 宽（256 写 0）
        result[7] = height >= 256 ? (byte)0 : (byte)height;          // 高
        result[8] = 0;                                                // 色数（0 = ≥256 色）
        result[9] = 0;                                                // 保留
        BitConverter.GetBytes((ushort)1).CopyTo(result, 10);         // 平面数
        BitConverter.GetBytes((ushort)32).CopyTo(result, 12);        // 位深 32（PNG 带 alpha）
        BitConverter.GetBytes(png.Length).CopyTo(result, 14);        // 图像字节数
        BitConverter.GetBytes(6 + 16).CopyTo(result, 18);            // 图像偏移
        Array.Copy(png, 0, result, 6 + 16, png.Length);
        return result;
    }

    /// <summary>帧尺寸列表（日志/诊断用）：方形帧只写边长（如 16,20,256），非方形写 WxH。</summary>
    public static string FrameListText(IReadOnlyList<IcoFrame> frames)
        => string.Join(",", frames.Select(f => f.Width == f.Height ? $"{f.Width}" : $"{f.Width}x{f.Height}"));

    /// <summary>文件 MD5 的前 8 位（小写十六进制）——启动日志自报装机文件指纹用。</summary>
    public static string Md5Prefix8(byte[] data)
        => Convert.ToHexString(MD5.HashData(data))[..8].ToLowerInvariant();
}
