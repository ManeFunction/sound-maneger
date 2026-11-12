using NUnit.Framework;

namespace Mane.Unity.SoundManeger.Tests
{
    public class MixerContractTests
    {
        private const float Tolerance = 1e-4f;

        [TestCase(0f, -80f)]
        [TestCase(0.8f, 0f)]
        [TestCase(1f, 20f)]
        [TestCase(-1f, -80f)]
        [TestCase(2f, 20f)]
        public void LevelToDecibels_IsLinearAndClamped(float level, float decibels)
        {
            Assert.AreEqual(decibels, MixerContract.LevelToDecibels(level), Tolerance);
        }

        [TestCase(0f)]
        [TestCase(0.35f)]
        [TestCase(0.8f)]
        [TestCase(1f)]
        public void DecibelsToLevel_IsInverse(float level)
        {
            Assert.AreEqual(level, MixerContract.DecibelsToLevel(MixerContract.LevelToDecibels(level)), Tolerance);
        }

        [TestCase(1f, 0f)]
        [TestCase(0.5f, -6.0206f)]
        [TestCase(0.1f, -20f)]
        [TestCase(0f, -80f)]
        [TestCase(0.00001f, -80f)]
        [TestCase(2f, 0f)]
        public void GainToDecibels_IsLogarithmicAndClamped(float gain, float decibels)
        {
            Assert.AreEqual(decibels, MixerContract.GainToDecibels(gain), Tolerance);
        }
    }
}
