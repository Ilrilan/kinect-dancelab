using System;
using System.Collections.Generic;

namespace DanceLab.View
{
    /// <summary>Какая сторона тела у сустава по его имени у трекера.</summary>
    public enum Side
    {
        Center,
        Left,
        Right,
    }

    /// <summary>
    /// Кости и соглашения о координатах каждого трекера. Общий код tools/sensor_view и tools/player_trial:
    /// сведение двух копий (аудит P2.11, specs/player/trial.md T21).
    /// </summary>
    public static class Topology
    {
        public const string Kinect = "kinect-sdk-2";
        public const string Nuitrack = "nuitrack";

        /// <summary>Идентификатор трекера по ключу режима инструмента: nuitrack-ai/nuitrack-native → Nuitrack, иначе Kinect.</summary>
        public static string TrackerId(string key) => key == "nuitrack-ai" || key == "nuitrack-native" ? Nuitrack : Kinect;

        private static readonly string[][] KinectBones =
        {
            new[] { "Head", "Neck" }, new[] { "Neck", "SpineShoulder" }, new[] { "SpineShoulder", "SpineMid" }, new[] { "SpineMid", "SpineBase" },
            new[] { "SpineShoulder", "ShoulderLeft" }, new[] { "ShoulderLeft", "ElbowLeft" }, new[] { "ElbowLeft", "WristLeft" }, new[] { "WristLeft", "HandLeft" }, new[] { "HandLeft", "HandTipLeft" },
            new[] { "SpineShoulder", "ShoulderRight" }, new[] { "ShoulderRight", "ElbowRight" }, new[] { "ElbowRight", "WristRight" }, new[] { "WristRight", "HandRight" }, new[] { "HandRight", "HandTipRight" },
            new[] { "SpineBase", "HipLeft" }, new[] { "HipLeft", "KneeLeft" }, new[] { "KneeLeft", "AnkleLeft" }, new[] { "AnkleLeft", "FootLeft" },
            new[] { "SpineBase", "HipRight" }, new[] { "HipRight", "KneeRight" }, new[] { "KneeRight", "AnkleRight" }, new[] { "AnkleRight", "FootRight" },
        };

        private static readonly string[][] NuitrackBones =
        {
            new[] { "Head", "Neck" }, new[] { "Neck", "Torso" }, new[] { "Torso", "Waist" },
            new[] { "Neck", "LeftCollar" }, new[] { "LeftCollar", "LeftShoulder" }, new[] { "LeftShoulder", "LeftElbow" }, new[] { "LeftElbow", "LeftWrist" }, new[] { "LeftWrist", "LeftHand" }, new[] { "LeftHand", "LeftFingertip" },
            new[] { "Neck", "RightCollar" }, new[] { "RightCollar", "RightShoulder" }, new[] { "RightShoulder", "RightElbow" }, new[] { "RightElbow", "RightWrist" }, new[] { "RightWrist", "RightHand" }, new[] { "RightHand", "RightFingertip" },
            new[] { "Waist", "LeftHip" }, new[] { "LeftHip", "LeftKnee" }, new[] { "LeftKnee", "LeftAnkle" }, new[] { "LeftAnkle", "LeftFoot" },
            new[] { "Waist", "RightHip" }, new[] { "RightHip", "RightKnee" }, new[] { "RightKnee", "RightAnkle" }, new[] { "RightAnkle", "RightFoot" },
        };

        public static IReadOnlyList<string[]> Bones(string trackerId) => trackerId == Nuitrack ? NuitrackBones : KinectBones;

        public static Side SideOf(string jointName)
        {
            if (jointName.StartsWith("Left", StringComparison.Ordinal) || jointName.EndsWith("Left", StringComparison.Ordinal))
            {
                return Side.Left;
            }

            if (jointName.StartsWith("Right", StringComparison.Ordinal) || jointName.EndsWith("Right", StringComparison.Ordinal))
            {
                return Side.Right;
            }

            return Side.Center;
        }

        /// <summary>
        /// Знак, на который умножается x сустава, чтобы справа на экране был правый край кадра сенсора (вид от сенсора без зеркала).
        /// У Kinect SDK ось x направлена влево от сенсора, у Nuitrack вправо.
        /// </summary>
        public static double ScreenXSign(string trackerId) => trackerId == Kinect ? -1.0 : 1.0;

        public static string LeftShoulder(string trackerId) => trackerId == Nuitrack ? "LeftShoulder" : "ShoulderLeft";

        public static string RightShoulder(string trackerId) => trackerId == Nuitrack ? "RightShoulder" : "ShoulderRight";

        public static string Root(string trackerId) => trackerId == Nuitrack ? "Waist" : "SpineBase";

        public static string Head(string trackerId) => "Head";

        /// <summary>Лицом ли к сенсору по меткам плеч: у Kinect левое плечо имеет меньший x, у Nuitrack больший.</summary>
        public static bool FacesSensorByLabels(string trackerId, double leftShoulderX, double rightShoulderX) =>
            trackerId == Kinect ? leftShoulderX < rightShoulderX : leftShoulderX > rightShoulderX;
    }
}
