using System;

namespace HorseRace.Core
{
    /// <summary>
    /// 馬匹 3D 模型的設定。沒有指定模型（或載入失敗）時，大螢幕退回程式生成的方塊馬。
    /// 純視覺，不影響任何賽果。
    ///
    /// 模型放在 Assets/Resources/ 底下，路徑寫「相對 Resources、不含副檔名」，
    /// 例如檔案是 Assets/Resources/Horses/horse.fbx 就寫 "Horses/horse"。
    /// 個別馬匹可用 <see cref="HorseConfig.Model"/> 覆蓋。
    /// </summary>
    [Serializable]
    public sealed class HorseModelConfig
    {
        /// <summary>所有馬共用的模型路徑。空字串＝用方塊馬。</summary>
        public string DefaultModel = "";

        /// <summary>
        /// 模型繞垂直軸轉幾度才會面向跑的方向（+X）。多數模型面向 +Z，所以預設 90。
        /// 馬若倒著跑或橫著跑就調這裡（常見值 0、90、180、−90）。
        /// </summary>
        public double YawDegrees = 90.0;

        /// <summary>
        /// 自動縮放：把模型的身長（沿跑的方向）縮放到這個長度（世界單位，方塊馬約 3.2 含頭尾）。
        /// 0 代表不自動縮放，改用 <see cref="Scale"/>。
        /// </summary>
        public double FitLength = 3.2;

        /// <summary>FitLength 為 0 時使用的固定縮放倍率。</summary>
        public double Scale = 1.0;

        /// <summary>
        /// 材質名稱含有這段文字（不分大小寫）的部位會染成該馬的代表色，例如騎師服或鞍墊的材質名 "Silk"、"Saddle"。
        /// 空字串＝不染色（這時靠名牌與光環辨識）。
        /// </summary>
        public string TintMaterialKeyword = "";

        /// <summary>
        /// 跑步動畫片段名稱（模型 FBX 裡的 clip 名）。空字串＝自動挑第一個片段；模型沒有動畫時只做上下起伏。
        /// </summary>
        public string RunClip = "";

        /// <summary>待機動畫片段名稱（站在閘門裡時播）。空字串＝用跑步動畫放慢代替。</summary>
        public string IdleClip = "";

        /// <summary>跑步動畫在最慢／最快時的播放速度倍率，依馬的即時速度在兩者之間變化。</summary>
        public double MinAnimationSpeed = 0.6;

        public double MaxAnimationSpeed = 1.6;

        public void Validate()
        {
            DefaultModel = DefaultModel ?? "";
            TintMaterialKeyword = TintMaterialKeyword ?? "";
            RunClip = RunClip ?? "";
            IdleClip = IdleClip ?? "";
            YawDegrees = ConfigMath.Clamp(YawDegrees, -360.0, 360.0);
            FitLength = ConfigMath.Clamp(FitLength, 0.0, 50.0);
            Scale = ConfigMath.Clamp(Scale, 0.001, 1000.0);
            MinAnimationSpeed = ConfigMath.Clamp(MinAnimationSpeed, 0.0, 10.0);
            MaxAnimationSpeed = ConfigMath.Clamp(MaxAnimationSpeed, MinAnimationSpeed, 10.0);
        }

        /// <summary>這匹馬實際要用的模型路徑：個別設定優先，其次共用設定；都空則回傳空字串（用方塊馬）。</summary>
        public string ResolveModelPath(HorseConfig horse)
        {
            if (horse != null && !string.IsNullOrEmpty(horse.Model))
            {
                return horse.Model.Trim();
            }

            return (DefaultModel ?? "").Trim();
        }
    }
}
