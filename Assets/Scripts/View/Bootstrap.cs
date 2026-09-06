using UnityEngine;

namespace HorseRace.View
{
    /// <summary>
    /// 程式進入點。場景裡什麼都不用放——執行時自動生出總控、賽道、馬匹與介面。
    /// 這樣做的好處是不必在 Unity 編輯器裡拖拉任何引用，也不會有「場景忘了存」的問題。
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            // 場景中已經有總控就不重複建立（例如開發時手動放了一個）
            if (Object.FindObjectOfType<RaceDirector>() != null)
            {
                return;
            }

            GameObject director = new GameObject("RaceDirector");
            director.AddComponent<RaceDirector>();
        }
    }
}
