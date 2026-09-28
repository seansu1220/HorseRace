using System;

namespace HorseRace.Core
{
    /// <summary>
    /// 大螢幕場景的外觀設定（城市街道或傳統賽馬場）。純視覺，不影響任何賽果；
    /// 放在設定檔是為了現場能切換或微調，不必重新編譯。
    /// </summary>
    [Serializable]
    public sealed class SceneryConfig
    {
        /// <summary>true：城市街道賽（兩側大樓、路燈、行道樹）；false：原本的草地賽馬場。</summary>
        public bool CityStreet = true;

        /// <summary>
        /// 城市素材（Kenney City Kit）的放大倍率。套件以「1 單位＝一格街區」製作，
        /// 放大 10 倍時路燈約 7 公尺、一般大樓 9～17 公尺高，與賽道比例相稱。
        /// </summary>
        public double KitScale = 10.0;

        /// <summary>大樓繞垂直軸旋轉幾度才會正面朝向賽道。換了模型方向不對時調這裡。</summary>
        public double BuildingYawDegrees = 180.0;

        /// <summary>路燈與行道樹的間距（世界單位）。</summary>
        public double LampSpacing = 18.0;

        /// <summary>每隔多遠留一個十字路口（世界單位），0 代表沒有路口、整排都是大樓。</summary>
        public double CrossStreetSpacing = 70.0;

        /// <summary>大樓排列的亂數種子。同一個種子每次開啟都是同一條街。</summary>
        public int Seed = 20260929;

        public void Validate()
        {
            KitScale = ConfigMath.Clamp(KitScale, 1.0, 50.0);
            BuildingYawDegrees = ConfigMath.Clamp(BuildingYawDegrees, -360.0, 360.0);
            LampSpacing = ConfigMath.Clamp(LampSpacing, 4.0, 200.0);
            CrossStreetSpacing = CrossStreetSpacing <= 0.0 ? 0.0 : ConfigMath.Clamp(CrossStreetSpacing, 30.0, 1000.0);
        }
    }
}
