using System;
using System.Collections.Generic;

namespace HorseRace.Core
{
    /// <summary>
    /// 整份設定的根節點，對應 StreamingAssets/config/race.json。
    /// 欄位全部是 public field 而非 property，因為 Unity 的 JsonUtility 只認得 field。
    /// </summary>
    [Serializable]
    public sealed class GameConfig
    {
        /// <summary>本專案支援的最少／最多出賽馬匹數。</summary>
        public const int MinHorses = 2;
        public const int MaxHorses = 8;

        public RaceConfig Race = new RaceConfig();

        public ItemConfig Items = new ItemConfig();

        public NetworkConfig Network = new NetworkConfig();

        /// <summary>馬房名冊。每場從這裡取出全部馬匹出賽，並對能力值做隨機微調。</summary>
        public List<HorseConfig> Roster = new List<HorseConfig>();

        /// <summary>內建的預設四匹馬。使用者提供正式素材前，就用這組顏色與名字。</summary>
        public static GameConfig CreateDefault()
        {
            GameConfig config = new GameConfig();
            config.Roster.Add(new HorseConfig
            {
                Name = "赤焰",
                ColorHex = "#E74C3C",
                BaseSpeed = 17.4,
                Stamina = 0.45,
                Volatility = 0.70
            });
            config.Roster.Add(new HorseConfig
            {
                Name = "蒼影",
                ColorHex = "#3498DB",
                BaseSpeed = 17.0,
                Stamina = 0.80,
                Volatility = 0.35
            });
            config.Roster.Add(new HorseConfig
            {
                Name = "金鬃",
                ColorHex = "#F1C40F",
                BaseSpeed = 17.2,
                Stamina = 0.60,
                Volatility = 0.50
            });
            config.Roster.Add(new HorseConfig
            {
                Name = "墨風",
                ColorHex = "#2ECC71",
                BaseSpeed = 16.8,
                Stamina = 0.55,
                Volatility = 0.90
            });
            return config;
        }

        /// <summary>
        /// 夾取所有數值並確保名冊可用。設定壞掉時採「退回預設」而非拋例外——
        /// 現場活動中程式不能因為一個手殘的 JSON 就開不起來。
        /// </summary>
        public void Validate()
        {
            if (Race == null)
            {
                Race = new RaceConfig();
            }

            Race.Validate();

            if (Items == null)
            {
                Items = new ItemConfig();
            }

            Items.Validate();

            if (Network == null)
            {
                Network = new NetworkConfig();
            }

            Network.Validate();

            if (Roster == null)
            {
                Roster = new List<HorseConfig>();
            }

            for (int i = Roster.Count - 1; i >= 0; i--)
            {
                if (Roster[i] == null)
                {
                    Roster.RemoveAt(i);
                }
                else
                {
                    Roster[i].Validate();
                }
            }

            if (Roster.Count < MinHorses)
            {
                Roster = CreateDefault().Roster;
            }
            else if (Roster.Count > MaxHorses)
            {
                Roster.RemoveRange(MaxHorses, Roster.Count - MaxHorses);
            }
        }
    }
}
