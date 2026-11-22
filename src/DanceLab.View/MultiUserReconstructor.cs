using System.Collections.Generic;
using System.Linq;
using DanceLab.Capture;
using DanceLab.Core.Reconstruction;

namespace DanceLab.View
{
    /// <summary>
    /// Достройка рассчитана на одного человека: свой реконструктор на каждый id, кадр собирается из всех людей.
    /// Общий код tools/sensor_view и tools/player_trial (аудит P2.11, specs/player/trial.md T21).
    /// Сохраняет последнюю достроенную позу и снимок длин костей каждого человека (TryGetUser:
    /// нужно Canonicalizer.ToCanonical и калибровке в player_trial; остальным потребителям можно игнорировать).
    /// </summary>
    public sealed class MultiUserReconstructor
    {
        private readonly string trackerId;
        private readonly Dictionary<int, PoseReconstructor> perUser = new Dictionary<int, PoseReconstructor>();
        private readonly Dictionary<int, ReconstructedPose> lastPose = new Dictionary<int, ReconstructedPose>();
        private readonly Dictionary<int, IReadOnlyDictionary<string, LimbLengths>> lastLengths = new Dictionary<int, IReadOnlyDictionary<string, LimbLengths>>();

        public MultiUserReconstructor(string trackerId)
        {
            this.trackerId = trackerId;
        }

        public SkeletonFrame Process(SkeletonFrame f)
        {
            var users = new List<TrackedUser>();
            foreach (var u in f.Users)
            {
                if (!perUser.TryGetValue(u.Id, out var rc))
                {
                    rc = new PoseReconstructor(trackerId);
                    perUser[u.Id] = rc;
                }

                var single = new SkeletonFrame(f.DeviceTimeUs, f.HostTimeUtcTicks, new[] { u }, f.Scene);
                var pose = rc.Process(single);
                lastPose[u.Id] = pose;
                var lengths = rc.BoneLengths;
                if (lengths.Count > 0)
                {
                    lastLengths[u.Id] = lengths;
                }

                var one = Conversion.ToFrame(pose, single);
                users.AddRange(one.Users.Count > 0 ? one.Users : new[] { u });
            }

            foreach (var id in perUser.Keys.Where(id => f.Users.All(u => u.Id != id)).ToList())
            {
                perUser.Remove(id);
                lastPose.Remove(id);
                lastLengths.Remove(id);
            }

            return new SkeletonFrame(f.DeviceTimeUs, f.HostTimeUtcTicks, users, f.Scene);
        }

        /// <summary>Последняя достроенная поза человека и последний снимок длин костей (может быть пустым словарём).</summary>
        public bool TryGetUser(int id, out ReconstructedPose pose, out IReadOnlyDictionary<string, LimbLengths> lengths)
        {
            if (!lastPose.TryGetValue(id, out var p))
            {
                pose = p!;
                lengths = new Dictionary<string, LimbLengths>();
                return false;
            }

            pose = p;
            lengths = lastLengths.TryGetValue(id, out var l) ? l : new Dictionary<string, LimbLengths>();
            return true;
        }

        public void Reset()
        {
            perUser.Clear();
            lastPose.Clear();
            lastLengths.Clear();
        }
    }
}
