namespace HorseRace.Core
{
    /// <summary>設定值夾取的共用小工具。Core 不依賴 UnityEngine.Mathf，所以自己備一份。</summary>
    public static class ConfigMath
    {
        public static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value))
            {
                return min;
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
