using DnWModLoader.Config;

namespace PartyTime
{
    internal sealed class PartyTimeSettings
    {
        public readonly ConfigEntry<float> Volume;
        public readonly ConfigEntry<bool> IncludeGameMusic;

        public PartyTimeSettings(ModConfig config)
        {
            config.DescribeSection("Music", "Music");
            Volume = config.Bind("Music", "Volume", 1f, "Volume. Yeah.", ConfigMeta.Range(0.1, 2, 0.05));
            IncludeGameMusic = config.Bind("Music", "IncludeGameMusic", false, "Whether default songs are part of the playlist.");
        }
    }
}
