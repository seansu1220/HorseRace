using System;

namespace HorseRace.View
{
    /// <summary>
    /// 馬匹的外觀與動作（方塊馬或 3D 模型）。<see cref="HorseView"/> 只管位置、光環與名牌，
    /// 長相與跑步動作全部交給實作，換模型不必動 HorseView 以外的程式。
    /// </summary>
    public interface IHorseBody : IDisposable
    {
        /// <summary>名牌的基準高度（相對馬腳底）。</summary>
        float LabelHeight { get; }

        /// <summary>身長（世界單位），用來決定腳邊光環的大小。</summary>
        float Length { get; }

        /// <summary>每幀更新動作。<paramref name="speedRatio"/> 是目前速度相對基礎速度的比值（閘門待機時很小）。</summary>
        void Animate(float speedRatio, float deltaTime);

        /// <summary>回到靜止姿勢。換場時呼叫。</summary>
        void ResetPose();
    }
}
