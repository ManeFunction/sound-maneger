using UnityEngine;

namespace Mane.Unity.SoundManeger
{
    // What the manager needs from its AudioMixer, by name (see "Your own mixer" in the README),
    // and the two ways it maps volumes to the mixer's decibels.
    internal static class MixerContract
    {
        // Snapshots: the lowpass switches between them. MusicLowpass is optional
        public const string MusicSnapshot = "Music";
        public const string MusicLowpassSnapshot = "MusicLowpass";

        // Exposed parameters. The music group volumes are required, the crossfade sets them
        public const string Music1Volume = "Music1Volume";
        public const string Music2Volume = "Music2Volume";
        public const string BgmVolume = "BgmVolume";
        public const string SfxVolume = "SfxVolume";
        public const string MasterVolume = "MasterVolume";

        // Groups of the manager's own sources and of the group properties
        public const string Music1Group = "Master/BGM/Music1";
        public const string Music2Group = "Master/BGM/Music2";
        public const string MusicAddGroup = "Master/BGM/MusicAdd";
        public const string EffectsGroup = "Master/SFX/Effects";
        public const string FadeBgmGroup = "Master/SFX/FadeBgm";
        public const string FadeAllGroup = "Master/SFX/FadeAll";

        private const float MinDecibels = -80f;
        private const float LevelRange = 100f;
        // Below it a gain is silent: 20 * log10(0.0001) is -80 dB
        private const float SilentGain = 0.0001f;

        // A user volume (BgmVolume, SfxVolume, MasterVolume) from 0 to 1, mapped linearly to -80..+20 dB (0.8 is 0 dB)
        public static float LevelToDecibels(float level) => Mathf.Clamp01(level) * LevelRange + MinDecibels;

        public static float DecibelsToLevel(float decibels) => (decibels - MinDecibels) / LevelRange;

        // A music gain (amplitude from 0 to 1) to -80..0 dB. Logarithmic, so the crossfade curves are heard as drawn
        public static float GainToDecibels(float gain) =>
            gain > SilentGain ? 20f * Mathf.Log10(Mathf.Min(gain, 1f)) : MinDecibels;
    }
}
