using NUnit.Framework;
using UnityEngine;

namespace Mane.Unity.SoundManeger.Tests
{
    public class SfxLimiterTests
    {
        private const int KeptAlive = 100;

        private AudioClip _clip;
        private AudioClip _otherClip;

        [SetUp]
        public void SetUp()
        {
            // One second long
            _clip = AudioClip.Create("Sfx", 44100, 1, 44100, false);
            _otherClip = AudioClip.Create("OtherSfx", 44100, 1, 44100, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_clip);
            Object.DestroyImmediate(_otherClip);
        }

        [Test]
        public void SameClip_PlaysUpToLimit()
        {
            SfxLimiter limiter = new();

            Assert.IsTrue(limiter.TryPlay(_clip, 0f, 2, KeptAlive));
            Assert.IsTrue(limiter.TryPlay(_clip, 0.1f, 2, KeptAlive));
            Assert.IsFalse(limiter.TryPlay(_clip, 0.2f, 2, KeptAlive));
        }

        [Test]
        public void FinishedPlay_FreesItsSlot()
        {
            SfxLimiter limiter = new();

            Assert.IsTrue(limiter.TryPlay(_clip, 0f, 1, KeptAlive));
            Assert.IsFalse(limiter.TryPlay(_clip, 0.5f, 1, KeptAlive));
            Assert.IsTrue(limiter.TryPlay(_clip, 1f, 1, KeptAlive));
        }

        [Test]
        public void ZeroLimit_StillPlaysOneCopy()
        {
            SfxLimiter limiter = new();

            Assert.IsTrue(limiter.TryPlay(_clip, 0f, 0, KeptAlive));
            Assert.IsFalse(limiter.TryPlay(_clip, 0.1f, 0, KeptAlive));
        }

        [Test]
        public void DifferentClips_HaveSeparateLimits()
        {
            SfxLimiter limiter = new();

            Assert.IsTrue(limiter.TryPlay(_clip, 0f, 1, KeptAlive));
            Assert.IsTrue(limiter.TryPlay(_otherClip, 0f, 1, KeptAlive));
        }

        [Test]
        public void Cleanup_KeepsLimitOfPlayingClips()
        {
            SfxLimiter limiter = new();

            limiter.TryPlay(_clip, 0f, 1, KeptAlive);
            limiter.Cleanup(0.5f);

            Assert.IsFalse(limiter.TryPlay(_clip, 0.6f, 1, KeptAlive));
        }

        [Test]
        public void KeptAliveLimit_DoesNotStopPlaying()
        {
            SfxLimiter limiter = new();

            Assert.IsTrue(limiter.TryPlay(_clip, 0f, 1, 1));
            Assert.IsTrue(limiter.TryPlay(_otherClip, 0f, 1, 1));
        }
    }
}
