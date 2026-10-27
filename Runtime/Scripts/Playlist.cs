using System;
using System.Collections.Generic;
using Mane.DotNet;

namespace Mane.Unity.SoundManeger
{
    // Track order of a running playlist: the track to play, the move to the next one by the playing order,
    // and the tracks in a row that failed to load. It holds paths only, loading the clips is the manager's job.
    internal class Playlist
    {
        private readonly List<string> _tracks;
        private readonly SoundManeger.PlayingOrder _order;
        private readonly IRandom _random;
        private int _index;
        private int _failedTracks;

        public Playlist(IEnumerable<string> tracks, SoundManeger.PlayingOrder order, IRandom random)
        {
            _tracks = new List<string>(tracks ?? throw new ArgumentNullException(nameof(tracks)));
            if (_tracks.Count == 0)
                throw new ArgumentException("A playlist needs at least one track.", nameof(tracks));

            _order = order;
            _random = random ?? throw new ArgumentNullException(nameof(random));

            if (order == SoundManeger.PlayingOrder.Shuffle)
                _tracks.Shuffle(random);
            else if (order == SoundManeger.PlayingOrder.Random)
                _index = random.Next(0, _tracks.Count);
        }

        public int Count => _tracks.Count;

        // Path of the track to play
        public string Current => _tracks[_index];

        // Random order picks any other track. Default and Shuffle go down the list, and Shuffle shuffles it again
        // after every cycle, so that a cycle never starts with the track that ended the last one
        public void MoveNext()
        {
            if (_order == SoundManeger.PlayingOrder.Random)
            {
                if (_tracks.Count == 1) return;

                int previous = _index;
                do
                {
                    _index = _random.Next(0, _tracks.Count);
                } while (_index == previous);

                return;
            }

            if (++_index < _tracks.Count) return;

            _index = 0;
            string lastTrack = _tracks[^1];
            // A list of one repeated track cannot start with another one
            if (_order == SoundManeger.PlayingOrder.Shuffle && _tracks.Exists(track => track != lastTrack))
            {
                do
                {
                    _tracks.Shuffle(_random);
                } while (_tracks[0] == lastTrack);
            }
        }

        // The current track has loaded, so failed tracks are counted from zero again
        public void TrackLoaded() => _failedTracks = 0;

        // The current track failed to load: moves on to the next one.
        // False when as many tracks as the playlist has failed in a row, so it cannot play
        public bool SkipFailedTrack()
        {
            if (++_failedTracks >= _tracks.Count) return false;

            MoveNext();
            return true;
        }
    }
}
