using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace DanceLab.Capture
{
    /// <summary>Заголовок записи (первая строка skeleton.jsonl), версия формата 1.</summary>
    public sealed class RunHeader
    {
        public const int FormatVersion = 1;

        public RunHeader(string trackerId, string trackerVersion, string? engine, SensorInfo sensor, DateTimeOffset recordedAtUtc, string scenario, string? note = null)
        {
            TrackerId = trackerId;
            TrackerVersion = trackerVersion;
            Engine = engine;
            Sensor = sensor;
            RecordedAtUtc = recordedAtUtc;
            Scenario = scenario;
            Note = note;
        }

        public string TrackerId { get; }
        public string TrackerVersion { get; }
        public string? Engine { get; }
        public SensorInfo Sensor { get; }
        public DateTimeOffset RecordedAtUtc { get; }
        public string Scenario { get; }
        public string? Note { get; }
    }

    /// <summary>Буфер записи переполнился: запись остановлена, кадры не пропускаются молча.</summary>
    public sealed class CaptureBufferOverflowException : Exception
    {
        public CaptureBufferOverflowException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Запись skeleton.jsonl. Write не блокирует поток трекера: кадр кладётся в ограниченную очередь,
    /// сериализация и запись на диск идут в отдельном потоке. При переполнении очереди запись
    /// останавливается с ошибкой, которую выбрасывает Complete.
    /// </summary>
    public sealed class SkeletonRecordWriter : IDisposable
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly Stream stream;
        private readonly bool leaveOpen;
        private readonly BlockingCollection<SkeletonFrame> queue;
        private readonly Thread worker;
        private readonly MemoryStream line = new MemoryStream();
        private readonly Utf8JsonWriter json;
        private Exception? failure;
        private int written;
        private bool completed;

        public SkeletonRecordWriter(Stream stream, RunHeader header, int capacity = 4096, bool leaveOpen = false)
        {
            this.stream = stream;
            this.leaveOpen = leaveOpen;
            queue = new BlockingCollection<SkeletonFrame>(capacity);
            json = new Utf8JsonWriter(line);
            WriteHeader(header);
            worker = new Thread(Run) { IsBackground = true, Name = "skeleton-record-writer" };
            worker.Start();
        }

        public int FramesWritten => Volatile.Read(ref written);

        /// <summary>Не блокирует. После переполнения или ошибки записи последующие кадры игнорируются, ошибка выбросится в Complete.</summary>
        public void Write(SkeletonFrame frame)
        {
            if (Volatile.Read(ref failure) != null)
            {
                return;
            }

            if (!queue.TryAdd(frame))
            {
                Interlocked.CompareExchange(ref failure, new CaptureBufferOverflowException("Очередь записи скелета переполнена, диск не успевает."), null);
            }
        }

        /// <summary>Дожидается записи всех принятых кадров. Выбрасывает ошибку записи или переполнения, если она была.</summary>
        public void Complete()
        {
            if (!completed)
            {
                completed = true;
                queue.CompleteAdding();
                worker.Join();
                stream.Flush();
            }

            var error = Volatile.Read(ref failure);
            if (error != null)
            {
                throw error;
            }
        }

        public void Dispose()
        {
            try
            {
                if (!completed)
                {
                    completed = true;
                    queue.CompleteAdding();
                    worker.Join();
                    stream.Flush();
                }
            }
            finally
            {
                json.Dispose();
                queue.Dispose();
                if (!leaveOpen)
                {
                    stream.Dispose();
                }
            }
        }

        private void Run()
        {
            try
            {
                var n = 0;
                foreach (var frame in queue.GetConsumingEnumerable())
                {
                    WriteFrame(frame, n++);
                    Interlocked.Increment(ref written);
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref failure, ex, null);
            }
        }

        private void FlushLine()
        {
            json.Flush();
            line.WriteByte((byte)'\n');
            line.Position = 0;
            line.CopyTo(stream);
            line.SetLength(0);
            json.Reset(line);
        }

        internal static void WriteFrameSize(Utf8JsonWriter w, string name, FrameSize? size)
        {
            if (size == null)
            {
                return;
            }

            w.WriteStartObject(name);
            w.WriteNumber("width", size.Width);
            w.WriteNumber("height", size.Height);
            w.WriteNumber("fps", size.Fps);
            if (size.Format != null)
            {
                w.WriteString("format", size.Format);
            }

            w.WriteEndObject();
        }

        private void WriteHeader(RunHeader h)
        {
            WriteHeaderObject(json, h, null);
            FlushLine();
        }

        /// <summary>Пишет объект заголовка по схеме. extra дописывает свои свойства в конец объекта (run.json).</summary>
        internal static void WriteHeaderObject(Utf8JsonWriter json, RunHeader h, Action<Utf8JsonWriter>? extra)
        {
            json.WriteStartObject();
            json.WriteString("kind", "header");
            json.WriteNumber("formatVersion", RunHeader.FormatVersion);
            json.WriteStartObject("tracker");
            json.WriteString("id", h.TrackerId);
            json.WriteString("version", h.TrackerVersion);
            if (h.Engine != null)
            {
                json.WriteString("engine", h.Engine);
            }

            json.WriteEndObject();
            json.WriteStartObject("sensor");
            json.WriteString("id", h.Sensor.Id);
            if (h.Sensor.Serial != null)
            {
                json.WriteString("serial", h.Sensor.Serial);
            }

            WriteFrameSize(json, "depth", h.Sensor.Depth);
            WriteFrameSize(json, "color", h.Sensor.Color);
            json.WriteEndObject();
            json.WriteString("recordedAtUtc", h.RecordedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            json.WriteString("scenario", h.Scenario);
            if (h.Note != null)
            {
                json.WriteString("note", h.Note);
            }

            extra?.Invoke(json);
            json.WriteEndObject();
        }

        private void WriteFrame(SkeletonFrame f, int n)
        {
            json.WriteStartObject();
            json.WriteString("kind", "frame");
            json.WriteNumber("n", n);
            json.WriteNumber("deviceTimeUs", f.DeviceTimeUs);
            json.WriteNumber("hostTimeUtcTicks", f.HostTimeUtcTicks);
            json.WriteStartArray("users");
            foreach (var u in f.Users)
            {
                json.WriteStartObject();
                json.WriteNumber("id", u.Id);
                json.WriteStartArray("joints");
                foreach (var j in u.Joints)
                {
                    json.WriteStartObject();
                    json.WriteString("name", j.Name);
                    json.WriteNumber("x", j.X);
                    json.WriteNumber("y", j.Y);
                    json.WriteNumber("z", j.Z);
                    if (j.Conf.HasValue)
                    {
                        json.WriteNumber("conf", j.Conf.Value);
                    }

                    if (j.State != null)
                    {
                        json.WriteString("state", j.State);
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            if (f.Scene != null)
            {
                json.WriteStartObject("scene");
                if (f.Scene.FloorPlane != null)
                {
                    json.WriteStartArray("floorPlane");
                    foreach (var v in f.Scene.FloorPlane)
                    {
                        json.WriteNumberValue(v);
                    }

                    json.WriteEndArray();
                }

                if (f.Scene.Other != null)
                {
                    json.WritePropertyName("other");
                    JsonSerializer.Serialize(json, f.Scene.Other);
                }

                json.WriteEndObject();
            }

            json.WriteEndObject();
            FlushLine();
        }
    }

    /// <summary>Чтение skeleton.jsonl.</summary>
    public sealed class SkeletonRecordReader : IDisposable
    {
        private readonly StreamReader reader;

        public SkeletonRecordReader(Stream stream)
        {
            reader = new StreamReader(stream, new UTF8Encoding(false));
            var first = reader.ReadLine() ?? throw new InvalidDataException("Пустой файл записи.");
            Header = ParseHeader(first);
        }

        public RunHeader Header { get; }

        public IEnumerable<SkeletonFrame> ReadFrames()
        {
            string? text;
            while ((text = reader.ReadLine()) != null)
            {
                if (text.Length == 0)
                {
                    continue;
                }

                yield return ParseFrame(text);
            }
        }

        public void Dispose() => reader.Dispose();

        private static FrameSize? ParseSize(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var s))
            {
                return null;
            }

            var fps = s.TryGetProperty("fps", out var f) ? f.GetDouble() : 0;
            var format = s.TryGetProperty("format", out var fm) ? fm.GetString()! : string.Empty;
            return new FrameSize(s.GetProperty("width").GetInt32(), s.GetProperty("height").GetInt32(), fps, format);
        }

        private static RunHeader ParseHeader(string text)
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.GetProperty("kind").GetString() != "header")
            {
                throw new InvalidDataException("Первая строка записи не заголовок.");
            }

            if (root.GetProperty("formatVersion").GetInt32() != RunHeader.FormatVersion)
            {
                throw new InvalidDataException("Неподдерживаемая версия формата записи.");
            }

            var tracker = root.GetProperty("tracker");
            var sensor = root.GetProperty("sensor");
            var info = new SensorInfo(
                sensor.GetProperty("id").GetString()!,
                sensor.TryGetProperty("serial", out var serial) ? serial.GetString() : null,
                ParseSize(sensor, "depth"),
                ParseSize(sensor, "color"));
            return new RunHeader(
                tracker.GetProperty("id").GetString()!,
                tracker.GetProperty("version").GetString()!,
                tracker.TryGetProperty("engine", out var engine) ? engine.GetString() : null,
                info,
                DateTimeOffset.Parse(root.GetProperty("recordedAtUtc").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                root.GetProperty("scenario").GetString()!,
                root.TryGetProperty("note", out var note) ? note.GetString() : null);
        }

        private static object ToObject(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                    return e.GetString()!;
                case JsonValueKind.Number:
                    return e.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return e.GetRawText();
            }
        }

        private static SkeletonFrame ParseFrame(string text)
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.GetProperty("kind").GetString() != "frame")
            {
                throw new InvalidDataException("Ожидалась строка frame.");
            }

            var users = new List<TrackedUser>();
            foreach (var u in root.GetProperty("users").EnumerateArray())
            {
                var joints = new List<RawJoint>();
                foreach (var j in u.GetProperty("joints").EnumerateArray())
                {
                    joints.Add(new RawJoint(
                        j.GetProperty("name").GetString()!,
                        j.GetProperty("x").GetDouble(),
                        j.GetProperty("y").GetDouble(),
                        j.GetProperty("z").GetDouble(),
                        j.TryGetProperty("conf", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : (double?)null,
                        j.TryGetProperty("state", out var s) ? s.GetString() : null));
                }

                users.Add(new TrackedUser(u.GetProperty("id").GetInt32(), joints));
            }

            SceneInfo? scene = null;
            if (root.TryGetProperty("scene", out var sc))
            {
                double[]? floor = null;
                if (sc.TryGetProperty("floorPlane", out var fp))
                {
                    var list = new List<double>();
                    foreach (var v in fp.EnumerateArray())
                    {
                        list.Add(v.GetDouble());
                    }

                    floor = list.ToArray();
                }

                Dictionary<string, object>? other = null;
                if (sc.TryGetProperty("other", out var o))
                {
                    other = new Dictionary<string, object>();
                    foreach (var p in o.EnumerateObject())
                    {
                        other[p.Name] = ToObject(p.Value);
                    }
                }

                scene = new SceneInfo(floor, other);
            }

            return new SkeletonFrame(
                root.GetProperty("deviceTimeUs").GetInt64(),
                root.GetProperty("hostTimeUtcTicks").GetInt64(),
                users,
                scene);
        }
    }
}
