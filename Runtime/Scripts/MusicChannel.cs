using UnityEngine;
using UnityEngine.Audio;

namespace Mane.Unity.SoundManeger
{
    // One of the two music sources, with the exposed mixer volume of its group and the gain the crossfade sets to it
    internal sealed class MusicChannel
    {
        public readonly AudioSource Source;
        // Exposed parameter of the source's group volume
        public readonly string VolumeParameter;
        // Amplitude from 0 to 1, not the linear-dB 0..1 of BgmVolume
        public float Gain;

        public MusicChannel(AudioSource source, string volumeParameter, float gain)
        {
            Source = source;
            VolumeParameter = volumeParameter;
            Gain = gain;
        }

        // Sets the exposed group volume from the gain. Once set, it no longer follows the snapshots
        public void Apply(AudioMixer mixer) => mixer.SetFloat(VolumeParameter, ToDecibels(Gain));

        // Gain (amplitude from 0 to 1) to the mixer's -80..0 dB
        private static float ToDecibels(float gain) =>
            gain > 0.0001f ? 20f * Mathf.Log10(gain) : -80f;
    }
}
