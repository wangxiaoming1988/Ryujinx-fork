using System.Collections.Generic;
using Ryujinx.Common.Logging;
using Gommon;
using Ryujinx.Ava.Systems.Configuration;
using System;
using System.Text.Json;

namespace Ryujinx.Common
{
    public class SplashTextHelper
    {
        public static void PrintSplash()
        {
            Logger.Notice.Print(LogClass.Application,  "   ___                 __    _              ");
            Logger.Notice.Print(LogClass.Application, @"  / _ \  __ __ __ __  / /   (_)  ___   ___ _");
            Logger.Notice.Print(LogClass.Application, @" / , _/ / // // // / / _ \ / /  / _ \ / _ `/");
            Logger.Notice.Print(LogClass.Application, @"/_/|_|  \_, / \_,_/ /_.__//_/  /_//_/ \_, / ");
            Logger.Notice.Print(LogClass.Application,  "       /___/                         /___/  ");
            Logger.Notice.Print(LogClass.Application, "");
            Logger.Notice.Print(LogClass.Application, GetSplash());
            Logger.Notice.Print(LogClass.Application, "");
        }

        private static string s_finalSplash = "";

        public static string GetSplash()
        {
            if (string.IsNullOrEmpty(s_finalSplash))
            {
                s_finalSplash = GetLangJson();
                if (string.IsNullOrEmpty(s_finalSplash))
                {
                    s_finalSplash = "Splash Text";
                }
            }

            return $"{s_finalSplash}";
        }
        
        private static SplashLocales s_splashJson;

        private static string GetLangJson()
        {
            try
            {
                string data;
                data = EmbeddedResources.ReadAllText("Ryujinx/Assets/Splashes.json");
                s_splashJson = JsonSerializer.Deserialize<SplashLocales>(data);
                return s_splashJson.Locales[ConfigurationState.Instance.UI.LanguageCode.Value].GetRandomElement();
            }
            catch
            {
                return "";
            }
        }

        private struct SplashLocales
        {
            public Dictionary<string, List<string>> Locales { get; set; }
        }

    }

}
