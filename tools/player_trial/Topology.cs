namespace PlayerTrial
{
    /// <summary>Копия соглашений tools/sensor_view/Topology.cs: имена суставов трекеров и знак экранного x.</summary>
    internal static class Topology
    {
        public const string Kinect = "kinect-sdk-2";
        public const string Nuitrack = "nuitrack";

        public static string TrackerId(string key) => key == "nuitrack-ai" || key == "nuitrack-native" ? Nuitrack : Kinect;

        /// <summary>Корневой сустав (таз) для выбора ближайшего к сенсору человека.</summary>
        public static string Root(string trackerId) => trackerId == Nuitrack ? "Waist" : "SpineBase";

        public static string LeftShoulder(string trackerId) => trackerId == Nuitrack ? "LeftShoulder" : "ShoulderLeft";

        public static string RightShoulder(string trackerId) => trackerId == Nuitrack ? "RightShoulder" : "ShoulderRight";

        /// <summary>Знак экранного x (вид от сенсора без зеркала): у Kinect SDK ось x влево, у Nuitrack вправо.</summary>
        public static double ScreenXSign(string trackerId) => trackerId == Kinect ? -1.0 : 1.0;
    }
}
