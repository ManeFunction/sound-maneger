using NUnit.Framework;
using UnityEngine;

namespace Mane.Unity.SoundManeger.Tests
{
    public class MusicCrossfadeTests
    {
        // The default curves are fitted to sin/cos, so the sums are close to exact, not exact
        private const float Tolerance = 0.02f;
        private const int Steps = 50;

        private static MusicCrossfade Default(bool sameMusic) =>
            new(0f, 1f, Easing.SinOut, Easing.SinIn, sameMusic);

        [Test]
        public void DefaultCurves_KeepPowerConstant()
        {
            MusicCrossfade crossfade = Default(false);

            for (int i = 0; i <= Steps; i++)
            {
                (float incoming, float outgoing) = crossfade.At(i / (float)Steps);
                Assert.AreEqual(1f, incoming * incoming + outgoing * outgoing, Tolerance, $"step {i}");
            }
        }

        [Test]
        public void DifferentTracks_PowersNeverAddAboveOne()
        {
            // Linear curves alone would add up to +3 dB in the middle
            MusicCrossfade crossfade = new(0f, 1f, Easing.Linear, Easing.Linear, false);

            for (int i = 0; i <= Steps; i++)
            {
                (float incoming, float outgoing) = crossfade.At(i / (float)Steps);
                Assert.LessOrEqual(incoming * incoming + outgoing * outgoing, 1f + 1e-5f, $"step {i}");
            }
        }

        [Test]
        public void SameMusic_AmplitudesNeverAddAboveOne()
        {
            MusicCrossfade crossfade = Default(true);

            for (int i = 0; i <= Steps; i++)
            {
                (float incoming, float outgoing) = crossfade.At(i / (float)Steps);
                Assert.LessOrEqual(incoming + outgoing, 1f + 1e-5f, $"step {i}");
            }
        }

        [Test]
        public void OvershootingCurves_StayWithinZeroAndOne()
        {
            MusicCrossfade crossfade = new(0f, 1f, Easing.ElasticOut, Easing.BackIn, false);

            for (int i = 0; i <= Steps; i++)
            {
                (float incoming, float outgoing) = crossfade.At(i / (float)Steps);
                Assert.That(incoming, Is.InRange(0f, 1f), $"incoming, step {i}");
                Assert.That(outgoing, Is.InRange(0f, 1f), $"outgoing, step {i}");
            }
        }

        [Test]
        public void EmptyCurve_IsLinear()
        {
            MusicCrossfade crossfade = new(0f, 1f, new AnimationCurve(), new AnimationCurve(), true);

            (float incoming, float outgoing) = crossfade.At(0.25f);

            Assert.AreEqual(0.25f, incoming, 1e-5f);
            Assert.AreEqual(0.75f, outgoing, 1e-5f);
        }

        [Test]
        public void NullCurve_IsLinear()
        {
            MusicCrossfade crossfade = new(0f, 1f, null, null, true);

            Assert.AreEqual(0.5f, crossfade.At(0.5f).incoming, 1e-5f);
        }

        [Test]
        public void End_IsIncomingOnly()
        {
            MusicCrossfade crossfade = new(0.3f, 0.6f, Easing.QuadIn, Easing.QuadOut, false);

            Assert.AreEqual((1f, 0f), crossfade.At(1f));
            Assert.AreEqual((1f, 0f), crossfade.At(1.5f));
        }

        [Test]
        public void Interrupted_StartsFromCurrentGains()
        {
            // A crossfade that cuts into another starts where the tracks are
            MusicCrossfade crossfade = new(0.4f, 0.7f, Easing.SinOut, Easing.SinIn, false);

            (float incoming, float outgoing) = crossfade.At(0f);

            Assert.AreEqual(0.4f, incoming, 1e-5f);
            Assert.AreEqual(0.7f, outgoing, 1e-5f);
        }

        [Test]
        public void Interrupted_LoudOutgoing_YieldsToIncoming()
        {
            MusicCrossfade crossfade = new(0.8f, 1f, Easing.SinOut, Easing.SinIn, false);

            Assert.AreEqual(0.6f, crossfade.At(0f).outgoing, 1e-5f);
        }

        [Test]
        public void Headroom_DifferentTracks_IsPowerComplement()
        {
            Assert.AreEqual(0.6f, MusicCrossfade.Headroom(0.8f, false), 1e-5f);
            Assert.AreEqual(1f, MusicCrossfade.Headroom(0f, false), 1e-5f);
            Assert.AreEqual(0f, MusicCrossfade.Headroom(1f, false), 1e-5f);
        }

        [Test]
        public void Headroom_SameMusic_IsAmplitudeComplement()
        {
            Assert.AreEqual(0.2f, MusicCrossfade.Headroom(0.8f, true), 1e-5f);
        }

        [Test]
        public void SyncedSample_SameFrequency_KeepsPosition()
        {
            Assert.AreEqual(1000, MusicCrossfade.SyncedSample(1000, 44100, 44100, 441000));
        }

        [Test]
        public void SyncedSample_ConvertsFrequency()
        {
            // One second in
            Assert.AreEqual(48000, MusicCrossfade.SyncedSample(44100, 44100, 48000, 480000));
        }

        [Test]
        public void SyncedSample_WrapsOnShorterClip()
        {
            Assert.AreEqual(500, MusicCrossfade.SyncedSample(1500, 44100, 44100, 1000));
        }

        [Test]
        public void SyncedSample_LongPosition_DoesNotOverflow()
        {
            // Ten minutes at 96 kHz into a 48 kHz clip: the product overflows int
            int playing = 96000 * 600;
            Assert.AreEqual(48000 * 600, MusicCrossfade.SyncedSample(playing, 96000, 48000, int.MaxValue));
        }

        [Test]
        public void SyncedSample_NoFrequencyOrSamples_IsZero()
        {
            Assert.AreEqual(0, MusicCrossfade.SyncedSample(1000, 0, 44100, 1000));
            Assert.AreEqual(0, MusicCrossfade.SyncedSample(1000, 44100, 0, 1000));
            Assert.AreEqual(0, MusicCrossfade.SyncedSample(1000, 44100, 44100, 0));
        }
    }
}
