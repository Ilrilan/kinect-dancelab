using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DanceLab.Core.Canonical
{
    /// <summary>
    /// Эталонная последовательность канонических поз (player/trial Contract). Кадры идут по неубывающему
    /// времени, Sample выдаёт линейную интерполяцию на любой момент. Загрузка из canonical.json formatVersion 2.
    /// </summary>
    public sealed class CanonicalTrack
    {
        public const int FormatVersion = 2;

        public CanonicalTrack(double fps, double legLengthM, IReadOnlyList<CanonicalFrame> frames, string? sequence = null, string? video = null)
        {
            if (frames == null || frames.Count == 0)
            {
                throw new ArgumentException("Трек без кадров.", nameof(frames));
            }

            var sorted = new List<CanonicalFrame>(frames);
            sorted.Sort((a, b) => a.TSec.CompareTo(b.TSec));
            Fps = fps;
            LegLengthM = legLengthM;
            Frames = sorted;
            Sequence = sequence;
            Video = video;
        }

        /// <summary>Частота эталонной сетки, кадров в секунду (информационная: Sample работает по временным меткам).</summary>
        public double Fps { get; }

        /// <summary>Длина ноги эталонного танцора (бедро плюс голень), метры.</summary>
        public double LegLengthM { get; }

        public IReadOnlyList<CanonicalFrame> Frames { get; }

        public string? Sequence { get; }

        public string? Video { get; }

        /// <summary>Читает canonical.json formatVersion 2. Кадры с невалидной костью (короткий или NaN вектор) получают кость с пометкой невалидной; torso.roll и torso.basis (девять чисел) обязательны.</summary>
        public static CanonicalTrack Load(string path)
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("formatVersion", out var version) || version.GetInt32() != FormatVersion)
            {
                throw new InvalidDataException("Неподдерживаемая версия формата canonical.json: " + path);
            }

            if (!root.TryGetProperty("frames", out var framesEl) || framesEl.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("В canonical.json нет массива frames: " + path);
            }

            var frames = new List<CanonicalFrame>();
            foreach (var f in framesEl.EnumerateArray())
            {
                frames.Add(ParseFrame(f, path));
            }

            if (frames.Count == 0)
            {
                throw new InvalidDataException("canonical.json без кадров: " + path);
            }

            return new CanonicalTrack(
                root.TryGetProperty("fps", out var fpsEl) && fpsEl.ValueKind == JsonValueKind.Number ? fpsEl.GetDouble() : 0,
                root.TryGetProperty("legLengthCm", out var legEl) && legEl.ValueKind == JsonValueKind.Number ? legEl.GetDouble() / 100.0 : 1.0,
                frames,
                root.TryGetProperty("sequence", out var seqEl) ? seqEl.GetString() : null,
                root.TryGetProperty("video", out var videoEl) ? videoEl.GetString() : null);
        }

        /// <summary>Линейная интерполяция кадров на момент tSec; вне диапазона трека зажим на крайний кадр.</summary>
        public CanonicalFrame Sample(double tSec)
        {
            var frames = Frames;
            if (frames.Count == 1 || tSec <= frames[0].TSec)
            {
                return Copy(frames[0], tSec);
            }

            if (tSec >= frames[frames.Count - 1].TSec)
            {
                return Copy(frames[frames.Count - 1], tSec);
            }

            var lo = 0;
            var hi = frames.Count - 1;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) / 2;
                if (frames[mid].TSec <= tSec)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            var a = frames[lo];
            var b = frames[hi];
            var span = b.TSec - a.TSec;
            var alpha = span > 0 ? (tSec - a.TSec) / span : 0;
            return Lerp(a, b, alpha, tSec);
        }

        private static CanonicalFrame ParseFrame(JsonElement f, string path)
        {
            var bones = new double[CanonicalFrame.BoneCount * 3];
            var valid = new bool[CanonicalFrame.BoneCount];
            if (f.TryGetProperty("bones", out var bonesEl) && bonesEl.ValueKind == JsonValueKind.Object)
            {
                for (var i = 0; i < CanonicalFrame.BoneCount; i++)
                {
                    if (TryReadVec(bonesEl, CanonicalFrame.BoneNames[i], out var v) && v.Length > 1e-9)
                    {
                        var n = v.Normalized();
                        bones[i * 3] = n.X;
                        bones[(i * 3) + 1] = n.Y;
                        bones[(i * 3) + 2] = n.Z;
                        valid[i] = true;
                    }
                }
            }

            double GetNum(JsonElement parent, string name, string? container = null)
            {
                if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number)
                {
                    return el.GetDouble();
                }

                if (container != null)
                {
                    throw new InvalidDataException("В canonical.json нет числа " + container + "." + name + ".");
                }

                return 0;
            }

            double GetIn(string container, string name)
            {
                return f.TryGetProperty(container, out var sub) ? GetNum(sub, name, container) : throw new InvalidDataException("В canonical.json нет числа " + container + "." + name + ".");
            }

            var t = GetNum(f, "t", "frame");
            var lean = GetIn("torso", "lean");
            var turn = GetIn("torso", "turn");
            var tilt = GetIn("head", "tilt");
            double forward = 0, side = 0, height = 0, heading = 0;
            if (f.TryGetProperty("root", out var rootEl))
            {
                forward = GetNum(rootEl, "forward");
                side = GetNum(rootEl, "side");
                height = GetNum(rootEl, "height");
                heading = GetNum(rootEl, "heading");
            }

            // Формат версии 2 (S39): боковой наклон и оси торса обязательны в каждом кадре.
            if (!f.TryGetProperty("torso", out var torsoEl) || torsoEl.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(FrameError(path, t, "нет объекта torso."));
            }

            if (!torsoEl.TryGetProperty("roll", out var rollEl) || rollEl.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidDataException(FrameError(path, t, "нет числа torso.roll."));
            }

            if (!torsoEl.TryGetProperty("basis", out var basisEl) || basisEl.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(FrameError(path, t, "нет массива torso.basis."));
            }

            var basis = new double[9];
            var bi = 0;
            foreach (var item in basisEl.EnumerateArray())
            {
                if (bi >= 9 || item.ValueKind != JsonValueKind.Number)
                {
                    throw new InvalidDataException(FrameError(path, t, "массив torso.basis должен содержать девять чисел."));
                }

                basis[bi++] = item.GetDouble();
            }

            if (bi != 9)
            {
                throw new InvalidDataException(FrameError(path, t, "массив torso.basis должен содержать девять чисел."));
            }

            return new CanonicalFrame(t, bones, valid, lean, turn, tilt, forward, side, height, heading, rollEl.GetDouble(), basis);
        }

        private static string FrameError(string path, double tSec, string what)
        {
            return path + ": кадр t=" + tSec.ToString("0.######", CultureInfo.InvariantCulture) + ": " + what;
        }

        private static bool TryReadVec(JsonElement parent, string name, out Vec3 v)
        {
            v = new Vec3(0, 0, 0);
            if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var c = new double[3];
            var i = 0;
            foreach (var item in arr.EnumerateArray())
            {
                if (i >= 3 || item.ValueKind != JsonValueKind.Number || double.IsNaN(item.GetDouble()))
                {
                    return false;
                }

                c[i++] = item.GetDouble();
            }

            if (i != 3)
            {
                return false;
            }

            v = new Vec3(c[0], c[1], c[2]);
            return true;
        }

        private static CanonicalFrame Copy(CanonicalFrame f, double tSec) =>
            new CanonicalFrame(tSec, f.Bones, f.BoneValid, f.TorsoLeanRad, f.TorsoTurnRad, f.HeadTiltRad, f.RootForward, f.RootSide, f.RootHeight, f.HeadingRad, f.TorsoRollRad, f.TorsoBasis);

        private static CanonicalFrame Lerp(CanonicalFrame a, CanonicalFrame b, double alpha, double tSec)
        {
            var bones = new double[CanonicalFrame.BoneCount * 3];
            var valid = new bool[CanonicalFrame.BoneCount];
            for (var i = 0; i < CanonicalFrame.BoneCount; i++)
            {
                valid[i] = a.BoneValid[i] && b.BoneValid[i];
                if (!valid[i])
                {
                    continue;
                }

                var x = a.Bones[i * 3] + ((b.Bones[i * 3] - a.Bones[i * 3]) * alpha);
                var y = a.Bones[(i * 3) + 1] + ((b.Bones[(i * 3) + 1] - a.Bones[(i * 3) + 1]) * alpha);
                var z = a.Bones[(i * 3) + 2] + ((b.Bones[(i * 3) + 2] - a.Bones[(i * 3) + 2]) * alpha);
                var v = new Vec3(x, y, z);
                if (v.Length > 1e-9)
                {
                    var n = v.Normalized();
                    bones[i * 3] = n.X;
                    bones[(i * 3) + 1] = n.Y;
                    bones[(i * 3) + 2] = n.Z;
                }
                else
                {
                    valid[i] = false;
                }
            }

            double L(double x, double y) => x + ((y - x) * alpha);

            // Оси торса (S39): у обоих кадров — покомпонентная линейная интерполяция,
            // только у одного — берётся имеющийся, ни у одного — null.
            double[]? basis = null;
            if (a.TorsoBasis != null && b.TorsoBasis != null)
            {
                basis = new double[9];
                for (var i = 0; i < 9; i++)
                {
                    basis[i] = L(a.TorsoBasis[i], b.TorsoBasis[i]);
                }
            }
            else if (a.TorsoBasis != null)
            {
                basis = a.TorsoBasis;
            }
            else if (b.TorsoBasis != null)
            {
                basis = b.TorsoBasis;
            }

            return new CanonicalFrame(
                tSec,
                bones,
                valid,
                L(a.TorsoLeanRad, b.TorsoLeanRad),
                L(a.TorsoTurnRad, b.TorsoTurnRad),
                L(a.HeadTiltRad, b.HeadTiltRad),
                L(a.RootForward, b.RootForward),
                L(a.RootSide, b.RootSide),
                L(a.RootHeight, b.RootHeight),
                L(a.HeadingRad, b.HeadingRad),
                L(a.TorsoRollRad, b.TorsoRollRad),
                basis);
        }
    }
}
