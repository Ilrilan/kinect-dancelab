using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DanceLab.Capture;
using DanceLab.Core.Reconstruction;

namespace PoseEval
{
    /// <summary>
    /// Прогоняет запись skeleton.jsonl через достройку суставов и пишет результат в том же формате (Reconstructed помечается state reconstructed,
    /// Extrapolated: extrapolated, Invalid: нули с уверенностью 0). Сводку до и после даёт tools/capture_report/arms.ts.
    /// Запуск: pose_eval вход.jsonl выход.jsonl [--jitter метры] [--hold секунды] [--bone-dev метры] [--low-conf число]
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Использование: pose_eval вход.jsonl выход.jsonl [--jitter м] [--hold с] [--bone-dev м] [--low-conf число]");
                return 1;
            }

            string? Opt(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            double? Num(string name) => double.TryParse(Opt(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;

            using var input = File.OpenRead(args[0]);
            using var reader = new SkeletonRecordReader(input);
            var header = reader.Header;
            var parameters = header.TrackerId == "nuitrack" ? ReconstructionParameters.ForNuitrack() : ReconstructionParameters.ForKinect();
            if (Num("--jitter") is double jitter)
            {
                parameters.JitterSecondDifferenceM = jitter;
            }

            if (Num("--hold") is double hold)
            {
                parameters.HoldSeconds = hold;
            }

            if (Num("--bone-dev") is double boneDev)
            {
                parameters.BoneDeviationM = boneDev;
            }

            if (Num("--low-conf") is double lowConf)
            {
                parameters.LowConfidence = lowConf;
            }

            var reconstructor = new PoseReconstructor(header.TrackerId, parameters);
            var counts = new Dictionary<string, int[]>();
            var frames = 0;
            using (var output = new FileStream(args[1], FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16))
            using (var writer = new SkeletonRecordWriter(output, header, 1 << 16, true))
            {
                foreach (var frame in reader.ReadFrames())
                {
                    var pose = reconstructor.Process(frame);
                    frames++;
                    var joints = new List<RawJoint>(pose.Joints.Count);
                    foreach (var j in pose.Joints)
                    {
                        if (!counts.TryGetValue(j.Name, out var c))
                        {
                            c = new int[4];
                            counts[j.Name] = c;
                        }

                        c[(int)j.Origin]++;
                        switch (j.Origin)
                        {
                            case JointOrigin.Measured:
                                joints.Add(new RawJoint(j.Name, j.X, j.Y, j.Z, j.Confidence, "tracked"));
                                break;
                            case JointOrigin.Reconstructed:
                                joints.Add(new RawJoint(j.Name, j.X, j.Y, j.Z, j.Confidence, "reconstructed"));
                                break;
                            case JointOrigin.Extrapolated:
                                joints.Add(new RawJoint(j.Name, j.X, j.Y, j.Z, j.Confidence, "extrapolated"));
                                break;
                            default:
                                joints.Add(new RawJoint(j.Name, 0, 0, 0, 0, "not-tracked"));
                                break;
                        }
                    }

                    var users = pose.Joints.Count == 0 ? new List<TrackedUser>() : new List<TrackedUser> { new TrackedUser(frame.Users[0].Id, joints) };
                    writer.Write(new SkeletonFrame(frame.DeviceTimeUs, frame.HostTimeUtcTicks, users, frame.Scene));
                }

                writer.Complete();
            }

            Console.WriteLine("Кадров: " + frames);
            Console.WriteLine("Длины костей (м): " + string.Join(", ", System.Linq.Enumerable.Select(reconstructor.BoneLengths, kv => kv.Key + " " + kv.Value.Upper.ToString("0.000", CultureInfo.InvariantCulture) + "/" + kv.Value.Lower.ToString("0.000", CultureInfo.InvariantCulture))));
            Console.WriteLine("Сустав: измерен, достроен, продолжен, нет данных (доли кадров)");
            foreach (var name in new[] { "ElbowLeft", "WristLeft", "ElbowRight", "WristRight", "LeftElbow", "LeftWrist", "RightElbow", "RightWrist" })
            {
                if (counts.TryGetValue(name, out var c))
                {
                    var total = Math.Max(1, c[0] + c[1] + c[2] + c[3]);
                    Console.WriteLine("  " + name.PadRight(12) + string.Join(" ", System.Linq.Enumerable.Select(c, v => (100.0 * v / total).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(6) + "%")));
                }
            }

            return 0;
        }
    }
}
