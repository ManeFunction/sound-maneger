using System;
using System.Collections.Generic;
using System.Linq;
using Mane.DotNet;
using NUnit.Framework;
using PlayingOrder = Mane.Unity.SoundManeger.SoundManeger.PlayingOrder;

namespace Mane.Unity.SoundManeger.Tests
{
    public class PlaylistTests
    {
        private static readonly string[] Tracks = { "a", "b", "c" };

        [Test]
        public void Default_PlaysInOrderAndWraps()
        {
            Playlist playlist = new(Tracks, PlayingOrder.Default, new ScriptedRandom());

            Assert.AreEqual("a", playlist.Current);
            playlist.MoveNext();
            Assert.AreEqual("b", playlist.Current);
            playlist.MoveNext();
            Assert.AreEqual("c", playlist.Current);
            playlist.MoveNext();
            Assert.AreEqual("a", playlist.Current);
        }

        [Test]
        public void Constructor_CopiesTracks()
        {
            List<string> tracks = new(Tracks);
            Playlist playlist = new(tracks, PlayingOrder.Default, new ScriptedRandom());
            tracks[0] = "changed";

            Assert.AreEqual("a", playlist.Current);
        }

        [Test]
        public void Constructor_EmptyTracks_Throws() =>
            Assert.Throws<ArgumentException>(() =>
                new Playlist(Array.Empty<string>(), PlayingOrder.Default, new ScriptedRandom()));

        [Test]
        public void Random_StartsAtRandomTrack()
        {
            Playlist playlist = new(Tracks, PlayingOrder.Random, new ScriptedRandom(2));

            Assert.AreEqual("c", playlist.Current);
        }

        [Test]
        public void Random_NeverRepeatsTrack()
        {
            // Starts at "a", then rolls "a" twice more before "b"
            Playlist playlist = new(Tracks, PlayingOrder.Random, new ScriptedRandom(0, 0, 0, 1));

            playlist.MoveNext();
            Assert.AreEqual("b", playlist.Current);
        }

        [Test]
        public void Random_SingleTrack_StaysOnIt()
        {
            // No value is scripted for MoveNext(), so a roll would throw
            Playlist playlist = new(new[] { "a" }, PlayingOrder.Random, new ScriptedRandom(0));

            playlist.MoveNext();
            Assert.AreEqual("a", playlist.Current);
        }

        [Test]
        public void Shuffle_EveryCycleHasEveryTrack()
        {
            string[] tracks = { "a", "b", "c", "d", "e" };
            Playlist playlist = new(tracks, PlayingOrder.Shuffle, new ManeRandom(42));

            for (int cycle = 0; cycle < 100; cycle++)
                CollectionAssert.AreEquivalent(tracks, PlayCycle(playlist, tracks.Length));
        }

        [Test]
        public void Shuffle_CycleNeverStartsWithLastTrack()
        {
            Playlist playlist = new(Tracks, PlayingOrder.Shuffle, new ManeRandom(7));

            List<string> previous = PlayCycle(playlist, Tracks.Length);
            for (int cycle = 0; cycle < 200; cycle++)
            {
                List<string> next = PlayCycle(playlist, Tracks.Length);
                Assert.AreNotEqual(previous.Last(), next.First());
                previous = next;
            }
        }

        [Test]
        public void Shuffle_OneRepeatedTrack_DoesNotHang()
        {
            Playlist playlist = new(new[] { "a", "a" }, PlayingOrder.Shuffle, new ManeRandom(1));

            for (int i = 0; i < 4; i++)
                playlist.MoveNext();

            Assert.AreEqual("a", playlist.Current);
        }

        [Test]
        public void SkipFailedTrack_MovesToNextTrack()
        {
            Playlist playlist = new(Tracks, PlayingOrder.Default, new ScriptedRandom());

            Assert.IsTrue(playlist.SkipFailedTrack());
            Assert.AreEqual("b", playlist.Current);
        }

        [Test]
        public void SkipFailedTrack_FailsOnceEveryTrackFailedInRow()
        {
            Playlist playlist = new(Tracks, PlayingOrder.Default, new ScriptedRandom());

            Assert.IsTrue(playlist.SkipFailedTrack());
            Assert.IsTrue(playlist.SkipFailedTrack());
            Assert.IsFalse(playlist.SkipFailedTrack());
        }

        [Test]
        public void TrackLoaded_StartsFailureCountAgain()
        {
            Playlist playlist = new(Tracks, PlayingOrder.Default, new ScriptedRandom());

            playlist.SkipFailedTrack();
            playlist.SkipFailedTrack();
            playlist.TrackLoaded();

            Assert.IsTrue(playlist.SkipFailedTrack());
            Assert.IsTrue(playlist.SkipFailedTrack());
            Assert.IsFalse(playlist.SkipFailedTrack());
        }

        private static List<string> PlayCycle(Playlist playlist, int count)
        {
            List<string> played = new();
            for (int i = 0; i < count; i++)
            {
                played.Add(playlist.Current);
                playlist.MoveNext();
            }

            return played;
        }

        // Returns the given values from Next(), in order
        private class ScriptedRandom : IRandom
        {
            private readonly Queue<int> _values;

            public ScriptedRandom(params int[] values) => _values = new Queue<int>(values);

            public int Seed => 0;

            public int Next(int min, int max)
            {
                Assert.IsNotEmpty(_values, "Unexpected random roll.");
                int value = _values.Dequeue();
                Assert.That(value, Is.InRange(min, max - 1));
                return value;
            }

            public double Range01Double() => throw new NotSupportedException();
            public float Range01() => throw new NotSupportedException();
            public float Range01Inc() => throw new NotSupportedException();
        }
    }
}
