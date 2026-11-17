using System;
using System.IO;

namespace DanceLab.Capture
{
    /// <summary>
    /// Запись скелета с уже запущенного источника по требованию (для просмотрщика): тот же формат skeleton.jsonl и тот же писатель, что в CaptureSession.
    /// Источник не запускается и не останавливается, подписка на кадры живёт только пока идёт запись.
    /// </summary>
    public sealed class LiveRecorder : IDisposable
    {
        private readonly IPoseSource pose;
        private readonly SkeletonRecordWriter writer;
        private bool stopped;
        private bool disposed;
        private int frames;

        private LiveRecorder(IPoseSource pose, SkeletonRecordWriter writer, string path)
        {
            this.pose = pose;
            this.writer = writer;
            Path = path;
            pose.FrameReceived += writer.Write;
        }

        /// <summary>Файл skeleton.jsonl, в который идёт запись.</summary>
        public string Path { get; }

        public int FramesWritten => stopped ? frames : writer.FramesWritten;

        /// <summary>Создаёт папку и файл, пишет заголовок, подписывается на кадры.</summary>
        public static LiveRecorder Start(IPoseSource pose, string directory, string scenario, string trackerVersion, string? engine)
        {
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "skeleton.jsonl");
            var header = new RunHeader(pose.TrackerId, trackerVersion, engine, pose.Sensor, DateTimeOffset.UtcNow, scenario);
            var writer = new SkeletonRecordWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16), header, 8192);
            return new LiveRecorder(pose, writer, path);
        }

        /// <summary>Отписывается, дописывает очередь на диск и закрывает файл. Возвращает число записанных кадров. При переполнении очереди выбрасывает CaptureBufferOverflowException.</summary>
        public int Stop()
        {
            if (stopped)
            {
                return frames;
            }

            stopped = true;
            pose.FrameReceived -= writer.Write;
            try
            {
                writer.Complete();
                frames = writer.FramesWritten;
            }
            finally
            {
                Release();
            }

            return frames;
        }

        public void Dispose()
        {
            if (!stopped)
            {
                stopped = true;
                pose.FrameReceived -= writer.Write;
            }

            Release();
        }

        private void Release()
        {
            if (!disposed)
            {
                disposed = true;
                writer.Dispose();
            }
        }
    }
}
