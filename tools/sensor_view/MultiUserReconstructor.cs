using System.Collections.Generic;
using System.Linq;
using DanceLab.Capture;
using DanceLab.Core.Reconstruction;

namespace SensorView
{
    /// <summary>Достройка рассчитана на одного человека: здесь свой реконструктор на каждый id, кадр собирается из всех людей.</summary>
    internal sealed class MultiUserReconstructor
    {
        private readonly string trackerId;
        private readonly Dictionary<int, PoseReconstructor> perUser = new Dictionary<int, PoseReconstructor>();

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
                var one = Conversion.ToFrame(rc.Process(single), single);
                users.AddRange(one.Users.Count > 0 ? one.Users : new[] { u });
            }

            foreach (var id in perUser.Keys.Where(id => f.Users.All(u => u.Id != id)).ToList())
            {
                perUser.Remove(id);
            }

            return new SkeletonFrame(f.DeviceTimeUs, f.HostTimeUtcTicks, users, f.Scene);
        }

        public void Reset() => perUser.Clear();
    }
}
