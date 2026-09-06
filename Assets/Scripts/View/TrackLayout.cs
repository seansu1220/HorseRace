using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 賽道在世界座標中的配置。刻意與 Core 的「公尺」脫鉤：
    /// 模擬用真實尺度（450 公尺）算出好看的速度曲線，畫面用方便取鏡的尺度呈現，
    /// 兩者只透過 0~1 的完成度連接。改動視覺尺度不會影響任何賽果。
    /// </summary>
    public static class TrackLayout
    {
        /// <summary>賽道在世界座標的長度。</summary>
        public const float VisualLength = 160f;

        /// <summary>單一跑道寬度。</summary>
        public const float LaneWidth = 3.2f;

        /// <summary>賽道路面的厚度。</summary>
        public const float BedHeight = 0.2f;

        /// <summary>
        /// 路面上緣的 Y 座標，馬匹與所有路面標線都貼在這個高度。
        /// 注意路面是一個中心在 BedHeight/2、高度為 BedHeight 的方塊，
        /// 上緣因此是 BedHeight 而不是 BedHeight/2——用錯會讓標線埋進路面裡看不見。
        /// </summary>
        public const float GroundY = BedHeight;

        /// <summary>欄杆距離跑道邊緣的距離。</summary>
        public const float RailOffset = 2.5f;

        public const float StartX = 0f;

        public static float FinishX
        {
            get { return VisualLength; }
        }

        /// <summary>某個閘號在 Z 軸上的中心位置，整體以 z = 0 為對稱軸。</summary>
        public static float LaneZ(int lane, int laneCount)
        {
            return (lane - (laneCount - 1) * 0.5f) * LaneWidth;
        }

        /// <summary>所有跑道加起來的總寬度。</summary>
        public static float LaneSpan(int laneCount)
        {
            return laneCount * LaneWidth;
        }

        /// <summary>把 0~1 的賽程完成度換算成世界座標的 X。</summary>
        public static float ProgressToX(float progress01)
        {
            return Mathf.Lerp(StartX, FinishX, progress01);
        }
    }
}
