using System.Collections.Generic;
using DanceLab.Capture;

namespace DanceLab.Core.Reconstruction
{
    public static class Conversion
    {
        public const string StateReconstructed = "reconstructed";
        public const string StateExtrapolated = "extrapolated";

        /// <summary>
        /// Достроенная поза обратно в кадр слоя захвата (для просмотрщика и записи): Measured: tracked, Reconstructed: reconstructed, Extrapolated: extrapolated,
        /// Invalid: нули, уверенность 0, not-tracked. Время, сцена и идентификатор человека берутся из исходного кадра.
        /// </summary>
        public static SkeletonFrame ToFrame(ReconstructedPose pose, SkeletonFrame source)
        {
            if (pose.Joints.Count == 0 || source.Users.Count == 0)
            {
                return new SkeletonFrame(source.DeviceTimeUs, source.HostTimeUtcTicks, new List<TrackedUser>(), source.Scene);
            }

            var joints = new List<RawJoint>(pose.Joints.Count);
            foreach (var j in pose.Joints)
            {
                switch (j.Origin)
                {
                    case JointOrigin.Measured:
                        joints.Add(new RawJoint(j.Name, j.X, j.Y, j.Z, j.Confidence, "tracked"));
                        break;
                    case JointOrigin.Reconstructed:
                        joints.Add(new RawJoint(j.Name, j.X, j.Y, j.Z, j.Confidence, StateReconstructed));
                        break;
                    case JointOrigin.Extrapolated:
                        joints.Add(new RawJoint(j.Name, j.X, j.Y, j.Z, j.Confidence, StateExtrapolated));
                        break;
                    default:
                        joints.Add(new RawJoint(j.Name, 0, 0, 0, 0, "not-tracked"));
                        break;
                }
            }

            return new SkeletonFrame(source.DeviceTimeUs, source.HostTimeUtcTicks, new List<TrackedUser> { new TrackedUser(source.Users[0].Id, joints) }, source.Scene);
        }
    }
}
