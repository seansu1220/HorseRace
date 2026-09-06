using System;
using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 提供支援繁體中文的字型。
    ///
    /// 刻意不使用 TextMeshPro：TMP 需要先在編輯器裡匯入 Essentials 並烘焙字型圖集，
    /// 違反「不要求使用者到 Unity 編輯器操作」的專案規則。改用 uGUI 的動態字型，
    /// 直接向作業系統要一支中文字型即可。目標平台是 Windows，可靠度足夠。
    /// </summary>
    public static class FontProvider
    {
        private static readonly string[] PreferredFonts =
        {
            "Microsoft JhengHei UI",
            "Microsoft JhengHei",
            "微軟正黑體",
            "Noto Sans CJK TC",
            "Noto Sans TC",
            "PingFang TC",
            "Microsoft YaHei",
            "SimHei"
        };

        private static Font _cached;

        public static Font Get()
        {
            if (_cached != null)
            {
                return _cached;
            }

            _cached = ResolveFont();
            return _cached;
        }

        private static Font ResolveFont()
        {
            try
            {
                string[] installed = Font.GetOSInstalledFontNames();
                for (int i = 0; i < PreferredFonts.Length; i++)
                {
                    if (!IsInstalled(installed, PreferredFonts[i]))
                    {
                        continue;
                    }

                    Font font = Font.CreateDynamicFontFromOSFont(PreferredFonts[i], 32);
                    if (font != null)
                    {
                        return font;
                    }
                }

                Debug.LogWarning("[FontProvider] 系統沒有預期的中文字型，中文可能顯示為方框。"
                                 + "請安裝微軟正黑體或 Noto Sans TC。");
            }
            catch (Exception error)
            {
                Debug.LogWarning("[FontProvider] 讀取系統字型失敗，改用內建字型：" + error.Message);
            }

            Font builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtin == null)
            {
                builtin = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return builtin;
        }

        private static bool IsInstalled(string[] installed, string name)
        {
            if (installed == null)
            {
                return false;
            }

            for (int i = 0; i < installed.Length; i++)
            {
                if (string.Equals(installed[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
