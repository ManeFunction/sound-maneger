using UnityEngine;

namespace Mane.Unity.SoundManeger
{
    // The gains (amplitudes from 0 to 1) of the two tracks of a music crossfade along the fade curves,
    // and the start sample of a track that joins the playing one in sync. Pure math, the manager applies it.
    internal readonly struct MusicCrossfade
    {
        private readonly float _incomingFrom;
        private readonly float _outgoingFrom;
        // Null for an empty curve, which counts as linear
        private readonly AnimationCurve _fadeInCurve;
        private readonly AnimationCurve _fadeOutCurve;
        private readonly bool _inSync;

        // Each track starts from the gain it has, so a crossfade that cuts into another does not jump.
        // inSync: the tracks are mixes of one piece, playing in sync
        public MusicCrossfade(float incomingFrom, float outgoingFrom,
            AnimationCurve fadeInCurve, AnimationCurve fadeOutCurve, bool inSync)
        {
            _incomingFrom = Mathf.Clamp01(incomingFrom);
            _outgoingFrom = Mathf.Clamp01(outgoingFrom);
            _fadeInCurve = HasKeys(fadeInCurve) ? fadeInCurve : null;
            _fadeOutCurve = HasKeys(fadeOutCurve) ? fadeOutCurve : null;
            _inSync = inSync;
        }

        // The gains at the progress from 0 to 1 of the crossfade. Once it is over, they are 1 and 0
        public (float incoming, float outgoing) At(float progress)
        {
            if (progress >= 1f)
                return (1f, 0f);

            // Overshooting curves (Back, Elastic) stay within 0..1, the music never goes above 0 dB
            float incoming = Mathf.Clamp01(_incomingFrom + (1f - _incomingFrom) * Evaluate(_fadeInCurve, progress));
            float outgoing = Mathf.Clamp01(_outgoingFrom * (1f - Evaluate(_fadeOutCurve, progress)));

            return (incoming, Mathf.Min(outgoing, Headroom(incoming, _inSync)));
        }

        // The loudest the outgoing track can be next to the incoming one, so together they are as loud as one track.
        // Different tracks are uncorrelated, so their powers add (+3 dB for two equal ones): the outgoing amplitude
        // is at most sqrt(1 - incoming²). Mixes of one piece in sync add like one signal (+6 dB): their amplitudes add.
        public static float Headroom(float incoming, bool inSync) =>
            inSync ? 1f - incoming : Mathf.Sqrt(1f - incoming * incoming);

        // Where a playing clip is, in samples of another clip with its own frequency, wrapped to that clip's length.
        // 0 when either clip has no frequency or samples
        public static int SyncedSample(int playingSample, int playingFrequency, int clipFrequency, int clipSamples)
        {
            if (playingSample <= 0 || playingFrequency <= 0 || clipFrequency <= 0 || clipSamples <= 0)
                return 0;

            return (int)((long)playingSample * clipFrequency / playingFrequency % clipSamples);
        }

        // An empty curve evaluates to 0, which would never fade
        private static bool HasKeys(AnimationCurve curve) => curve != null && curve.length > 0;

        private static float Evaluate(AnimationCurve curve, float time) =>
            curve != null ? curve.Evaluate(time) : time;
    }
}
