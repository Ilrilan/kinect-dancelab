using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DanceLab.Capture
{
    public sealed class PreflightResult
    {
        public PreflightResult(bool ok, long needBytes, long freeBytes, long toFreeBytes)
        {
            Ok = ok;
            NeedBytes = needBytes;
            FreeBytes = freeBytes;
            ToFreeBytes = toFreeBytes;
        }

        public bool Ok { get; }
        public long NeedBytes { get; }
        public long FreeBytes { get; }

        /// <summary>Сколько памяти нужно освободить, если проверка не пройдена.</summary>
        public long ToFreeBytes { get; }
    }

    /// <summary>Проверка свободной памяти перед записью глубины и цвета в память.</summary>
    public static class MemoryPreflight
    {
        public const double DefaultFactor = 1.5;

        public static int BytesPerPixel(string format)
        {
            switch (format.ToLowerInvariant())
            {
                case "depth16":
                case "yuy2":
                    return 2;
                case "rgb24":
                    return 3;
                case "bgra":
                case "rgba":
                    return 4;
                default:
                    throw new ArgumentException("Неизвестный формат пикселя: " + format, nameof(format));
            }
        }

        public static long FrameBytes(FrameSize size) => (long)size.Width * size.Height * BytesPerPixel(size.Format);

        public static long EstimateBytes(int depthFrames, long depthFrameBytes, int colorFrames, long colorFrameBytes) =>
            (depthFrames * depthFrameBytes) + (colorFrames * colorFrameBytes);

        public static PreflightResult Check(long needBytes, long freeBytes, double factor = DefaultFactor)
        {
            var required = (long)Math.Ceiling(needBytes * factor);
            var ok = freeBytes >= required;
            return new PreflightResult(ok, needBytes, freeBytes, ok ? 0 : required - freeBytes);
        }

        public static long GetFreePhysicalBytes()
        {
            var s = new MemoryStatusEx();
            s.dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            if (!GlobalMemoryStatusEx(ref s))
            {
                throw new InvalidOperationException("GlobalMemoryStatusEx не сработала.");
            }

            return (long)s.ullAvailPhys;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }

    /// <summary>
    /// Накопление кадров глубины и цвета в памяти и сброс в папку прогона после остановки.
    /// Ёмкость задаётся заранее, кадры сверх ёмкости не принимаются (запись по длительности).
    /// </summary>
    public sealed class ImageRunRecorder
    {
        private readonly object sync = new object();
        private readonly List<ImageFrame> depth;
        private readonly List<ImageFrame> color;
        private readonly int depthCapacity;
        private readonly int colorCapacity;

        public ImageRunRecorder(int depthCapacity, int colorCapacity)
        {
            this.depthCapacity = depthCapacity;
            this.colorCapacity = colorCapacity;
            depth = new List<ImageFrame>(depthCapacity);
            color = new List<ImageFrame>(colorCapacity);
        }

        public int DepthCount
        {
            get
            {
                lock (sync)
                {
                    return depth.Count;
                }
            }
        }

        public int ColorCount
        {
            get
            {
                lock (sync)
                {
                    return color.Count;
                }
            }
        }

        public bool IsFull
        {
            get
            {
                lock (sync)
                {
                    return depth.Count >= depthCapacity && color.Count >= colorCapacity;
                }
            }
        }

        public void AddDepth(ImageFrame frame)
        {
            lock (sync)
            {
                if (depth.Count < depthCapacity)
                {
                    depth.Add(frame);
                }
            }
        }

        public void AddColor(ImageFrame frame)
        {
            lock (sync)
            {
                if (color.Count < colorCapacity)
                {
                    color.Add(frame);
                }
            }
        }

        /// <summary>Пишет run.json, depth.bin, color.bin и файлы времён в папку. Формат: specs/capture/index.md.</summary>
        public void Save(string directory, RunHeader header)
        {
            ImageFrame[] d;
            ImageFrame[] c;
            lock (sync)
            {
                d = depth.ToArray();
                c = color.ToArray();
            }

            Directory.CreateDirectory(directory);
            WriteBin(Path.Combine(directory, "depth.bin"), d);
            WriteBin(Path.Combine(directory, "color.bin"), c);
            WriteTimes(Path.Combine(directory, "depth.times.jsonl"), d);
            WriteTimes(Path.Combine(directory, "color.times.jsonl"), c);
            WriteRunJson(Path.Combine(directory, "run.json"), header, d, c);
        }

        private static void WriteBin(string path, ImageFrame[] frames)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                foreach (var f in frames)
                {
                    fs.Write(f.Data, 0, f.Data.Length);
                }
            }
        }

        private static void WriteTimes(string path, ImageFrame[] frames)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" })
            {
                for (var n = 0; n < frames.Length; n++)
                {
                    w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{{\"n\":{0},\"deviceTimeUs\":{1},\"hostTimeUtcTicks\":{2}}}", n, frames[n].DeviceTimeUs, frames[n].HostTimeUtcTicks));
                }
            }
        }

        private static void WriteStreamInfo(Utf8JsonWriter w, string name, ImageFrame[] frames)
        {
            w.WriteStartObject(name);
            w.WriteNumber("count", frames.Length);
            if (frames.Length > 0)
            {
                w.WriteNumber("width", frames[0].Width);
                w.WriteNumber("height", frames[0].Height);
                w.WriteNumber("frameBytes", frames[0].Data.Length);
                w.WriteString("format", frames[0].Format);
            }

            w.WriteEndObject();
        }

        private static void WriteRunJson(string path, RunHeader header, ImageFrame[] d, ImageFrame[] c)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
            {
                SkeletonRecordWriter.WriteHeaderObject(w, header, jw =>
                {
                    jw.WriteStartObject("frames");
                    WriteStreamInfo(jw, "depth", d);
                    WriteStreamInfo(jw, "color", c);
                    jw.WriteEndObject();
                });
            }
        }
    }
}
