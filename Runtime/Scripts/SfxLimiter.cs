using System.Collections.Generic;
using UnityEngine;

namespace Mane.Unity.SoundManeger
{
    // Bookkeeping of the sound effects that play: the limit of copies of the same clip at once, and the references
    // that keep playing clips from Resources.UnloadUnusedAssets(). Times are unscaled seconds, passed in by the caller.
    internal class SfxLimiter
    {
        // Start times of the plays of each clip, oldest first
        private readonly Dictionary<AudioClip, Queue<float>> _starts = new();
        // Clips of playing sound effects with the time they finish
        private readonly Dictionary<AudioClip, float> _keptAlive = new();
        private readonly List<AudioClip> _toForget = new();

        // Records a play of the clip, unless it already plays maxSameSounds times (one play is always allowed).
        // Up to maxKeptAlive different clips are kept referenced until they end. True when the clip may play
        public bool TryPlay(AudioClip clip, float now, int maxSameSounds, int maxKeptAlive)
        {
            if (!_starts.TryGetValue(clip, out Queue<float> starts))
                _starts[clip] = starts = new Queue<float>();

            DropFinished(starts, clip.length, now);
            if (starts.Count > 0 && starts.Count >= maxSameSounds)
                return false;

            starts.Enqueue(now);
            KeepAlive(clip, now, maxKeptAlive);
            return true;
        }

        // Forgets the finished plays, and the clips that have finished or were destroyed
        public void Cleanup(float now)
        {
            foreach (var kvp in _starts)
            {
                // Every play of a destroyed clip is over
                DropFinished(kvp.Value, kvp.Key ? kvp.Key.length : 0f, now);
                if (kvp.Value.Count == 0)
                    _toForget.Add(kvp.Key);
            }

            foreach (AudioClip clip in _toForget)
                _starts.Remove(clip);
            _toForget.Clear();

            ForgetFinishedClips(now);
        }

        public void Clear()
        {
            _starts.Clear();
            _keptAlive.Clear();
        }

        private static void DropFinished(Queue<float> starts, float length, float now)
        {
            while (starts.TryPeek(out float oldest) && now - oldest >= length)
                starts.Dequeue();
        }

        private void KeepAlive(AudioClip clip, float now, int maxKeptAlive)
        {
            if (!_keptAlive.ContainsKey(clip) && _keptAlive.Count >= maxKeptAlive)
            {
                ForgetFinishedClips(now);

                // Still full: the sound plays, but is not protected from unloading
                if (_keptAlive.Count >= maxKeptAlive)
                    return;
            }

            _keptAlive[clip] = now + clip.length;
        }

        private void ForgetFinishedClips(float now)
        {
            foreach (var kvp in _keptAlive)
            {
                if (!kvp.Key || kvp.Value <= now)
                    _toForget.Add(kvp.Key);
            }

            foreach (AudioClip clip in _toForget)
                _keptAlive.Remove(clip);
            _toForget.Clear();
        }
    }
}
