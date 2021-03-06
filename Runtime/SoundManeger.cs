﻿using System;
using System.Collections;
using System.Collections.Generic;
using Mane.Extensions;
using UnityEngine;
using UnityEngine.Audio;

namespace Mane.SoundManeger
{
    public delegate void TrackChangeHandler();

    [AddComponentMenu("Sound Manager")]
    [DisallowMultipleComponent]
    public class SoundManeger : MonoBehaviour
    {
        public enum PlayingOrder
        {
            Default,
            Random,
            Shuffle
        }

        public enum DuckType
        {
            None = 0,
            MusicOnly = 1,
            AllSources = 2,
        }

        [Flags]
        private enum PlayMode : byte
        {
            Silence = 0,
            PlayingMusic = 1,
            PlaylistActive = 2,
            NeedMusicSwitch = PlayingMusic | PlaylistActive
        }
        

        [Header("Sources")]
        [SerializeField] private AudioMixer _mixer = null;

        [SerializeField] private AudioSource _musicSource1 = null;
        [SerializeField] private AudioSource _musicSource2 = null;
        [SerializeField] private AudioSource _sfxSource = null;
        [SerializeField] private AudioSource _duckBgmSource = null;
        [SerializeField] private AudioSource _duckAllSource = null;
        
        [Header("Settings")]
        [SerializeField] private float _transitionTime = 1.5f;
        [SerializeField] private float _lowpassTime = 0.4f;
        [SerializeField] private PlayingOrder _playlistPlayingOrder = PlayingOrder.Default;

        [Header("SFX count limitations")]
        [SerializeField] private List<string> _limitedSfxs = new List<string>();
        [SerializeField] private int _maxSameSoundsCount = 3;
        [SerializeField] private float _delayBetweenSameSounds = 0.3f;

        
        private AudioMixerSnapshot _music1Snapshot;
        private AudioMixerSnapshot _music2Snapshot;
        private AudioMixerSnapshot _music1LowSnapshot;
        private AudioMixerSnapshot _music2LowSnapshot;


        private static SoundManeger _instance = null;

        public static SoundManeger Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<SoundManeger>();
                }

                return _instance;
            }
        }


        public event TrackChangeHandler OnPlaylistTrackChange;


        private List<string> _playlist;
        private int _playlistPointer;
        private PlayMode _mode;

        private bool _activeFirstMusicSource;
        private bool _lowpass;

        private bool _muteMusic = false;
        private bool _muteSfx = false;
        private float _cachedBgmVolume = .8f;
        private float _cachedSfxVolume = .8f;

        // used to keep active clips and prevent unloading while playing
        private readonly List<AudioClip> _activeSfx = new List<AudioClip>();
        private readonly Dictionary<string, List<float>> _limitedSfxTimings = new Dictionary<string, List<float>>();


        private void Awake()
        {
            FillMixerStuff();

            if (transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
            }
        }


        private void Reset() => FillMixerStuff();


        private void FillMixerStuff()
        {
            if (_mixer == null)
            {
                _mixer = Resources.Load<AudioMixer>("AudioMixer");
            }

            if (_mixer == null)
            {
                Debug.LogWarning("Can't find AudioMixer in the project!");
                return;
            }

            _music1Snapshot = _mixer.FindSnapshot("Music1");
            _music1LowSnapshot = _mixer.FindSnapshot("Music1Lowpass");
            _music2Snapshot = _mixer.FindSnapshot("Music2");
            _music2LowSnapshot = _mixer.FindSnapshot("Music2Lowpass");
        }


        public float TransitionTime
        {
            get => _transitionTime;
            set => _transitionTime = value;
        }


        public bool MuteMusic
        {
            get => _muteMusic;
            set
            {
                if (value)
                {
                    _mode &= ~PlayMode.PlayingMusic;
                }
                else
                {
                    _mode |= PlayMode.PlayingMusic;
                }

                _muteMusic = value;
                SetVolume("BgmVolume", value ? 0 : _cachedBgmVolume);
            }
        }


        public bool MuteSfx
        {
            get => _muteSfx;
            set
            {
                _muteSfx = value;
                SetVolume("SfxVolume", value ? 0 : _cachedSfxVolume);
            }
        }


        /// <summary>
        /// Set the volume between 0f and 1f, what depends on range from -80 to 20 in mixer.
        /// </summary>
        public float MasterVolume
        {
            get => GetVolume("MasterVolume");
            set => SetVolume("MasterVolume", value);
        }


        /// <summary>
        /// Set the volume between 0f and 1f, what depends on range from -80 to 20 in mixer.
        /// </summary>
        public float BgmVolume
        {
            get => GetVolume("BgmVolume");
            set
            {
                _cachedBgmVolume = value;

                if (!MuteMusic)
                {
                    SetVolume("BgmVolume", value);
                }
            }
        }


        /// <summary>
        /// Set the volume between 0f and 1f, what depends on range from -80 to 20 in mixer.
        /// </summary>
        public float SfxVolume
        {
            get => GetVolume("SfxVolume");
            set
            {
                _cachedSfxVolume = value;

                if (!MuteSfx)
                {
                    SetVolume("SfxVolume", value);
                }
            }
        }


        private float GetVolume(string key)
        {
            _mixer.GetFloat(key, out float value);
            value = (value + 80.0f) / 100.0f;

            return value;
        }


        private void SetVolume(string key, float value)
        {
            if (value < 0.0f)
            {
                value = 0.0f;
            }
            else if (value > 1.0f)
            {
                value = 1.0f;
            }

            value = value * 100.0f - 80.0f;

            _mixer.SetFloat(key, value);
        }


        public void PlayMusic(AudioClip clip, float? newTransitionTime = null)
        {
            if ((_musicSource2.clip == clip && _musicSource2.isPlaying && !_activeFirstMusicSource) ||
                (_musicSource1.clip == clip && _musicSource1.isPlaying && _activeFirstMusicSource))
            {
                _activeFirstMusicSource = !_activeFirstMusicSource;
                return;
            }

            float musicTransitionTime = newTransitionTime ?? (_transitionTime);
            if (_activeFirstMusicSource)
            {
                _musicSource2.clip = clip;
                _musicSource2.loop = true;
                _musicSource2.Play();
                _music2Snapshot.TransitionTo(musicTransitionTime);
                _activeFirstMusicSource = false;
            }
            else
            {
                _musicSource1.clip = clip;
                _musicSource1.loop = true;
                _musicSource1.Play();
                _music1Snapshot.TransitionTo(musicTransitionTime);
                _activeFirstMusicSource = true;
            }
        }


        public bool IsMusicPlaying => _musicSource1.isPlaying || _musicSource2.isPlaying;


        public void Lowpass(bool enable)
        {
            if (_lowpass == enable)
            {
                return;
            }

            _lowpass = enable;
            if (_activeFirstMusicSource)
            {
                if (_lowpass)
                {
                    _music1LowSnapshot.TransitionTo(_lowpassTime);
                }
                else
                {
                    _music1Snapshot.TransitionTo(_lowpassTime);
                }
            }
            else
            {
                if (_lowpass)
                {
                    _music2LowSnapshot.TransitionTo(_lowpassTime);
                }
                else
                {
                    _music2Snapshot.TransitionTo(_lowpassTime);
                }
            }
        }


        /// <summary>
        /// Play a playlist.
        /// If you need to play it Shuffled set PlaylistPlayingOrder first.
        /// And you can't return order from Shuffle to Default without restarting PlayMusic().
        /// Random order, however, can be chose and returned back to Default 'on fly'.
        /// Random behavior guarantee NOT to play the same track twice on a row either!
        /// </summary>
        public void PlayMusic(string[] playlist)
        {
            // empty call
            if (playlist == null || playlist.Length == 0)
            {
                return;
            }

            // single track
            if (playlist.Length == 1)
            {
                PlayTrack(GetClip(playlist[0]));

                return;
            }

            // playlist
            StartPlaylist(new List<string>(playlist));
        }


        /// <summary>
        /// Play a track or a playlist.
        /// If you need to play it Shuffled set PlaylistPlayingOrder first.
        /// And you can't return order from Shuffle to Default without restarting PlayMusic().
        /// Random order, however, can be chose and returned back to Default 'on fly'.
        /// Random behavior guarantee NOT to play the same track twice on a row either!
        /// </summary>
        public void PlayMusic(string first, params string[] playlist)
        {
            if (string.IsNullOrEmpty(first))
            {
                return;
            }

            // single track
            if (playlist == null || playlist.Length == 0)
            {
                PlayTrack(GetClip(first));

                return;
            }

            // playlist
            List<string> pl = new List<string>(playlist.Length + 1) {first};
            pl.AddRange(playlist);
            StartPlaylist(pl);
        }


        private void PlayTrack(AudioClip clip)
        {
            _musicSource1.loop = true;
            _musicSource2.loop = true;
            _mode &= ~PlayMode.PlaylistActive;
            OnPlaylistTrackChange -= OnTrackChange;
            PlayMusic(clip);
        }


        private void StartPlaylist(List<string> playlist)
        {
            this._playlist = playlist;

            _musicSource1.loop = false;
            _musicSource2.loop = false;
            _mode |= PlayMode.PlaylistActive;
            OnPlaylistTrackChange += OnTrackChange;
            _playlistPointer = -1;
            if (_playlistPlayingOrder == PlayingOrder.Shuffle)
            {
                // Check out my neat extensions for Unity classes or just shuffle the array manually
                // https://github.com/ManeFunction/unity-mane
                _playlist.Shuffle();
            }

            OnTrackChange();
        }


        private void OnTrackChange()
        {
            if (_playlistPlayingOrder == PlayingOrder.Random)
            {
                if (_playlist.Count == 1)
                {
                    _playlistPointer = 0;
                }
                else
                {
                    int old = _playlistPointer;
                    do
                    {
                        _playlistPointer = UnityEngine.Random.Range(0, _playlist.Count);
                    } while (_playlistPointer == old);
                }
            }
            else
            {
                _playlistPointer++;
                if (_playlistPointer >= _playlist.Count)
                {
                    _playlistPointer = 0;
                }
            }

            PlayMusic(GetClip(_playlist[_playlistPointer]));
        }


        public void StopMusic()
        {
            _mode &= PlayMode.PlayingMusic;
            OnPlaylistTrackChange -= OnTrackChange;
            _musicSource1.Stop();
            _musicSource2.Stop();
        }


        public void PlaySfx(AudioClip clip, DuckType duck = DuckType.None)
        {
            if (clip == null)
            {
                return;
            }

            if (IsSoundPlayingAvailable(clip))
            {
                // Save link to SoundEffects asset to prevent unloading via UnloadUnusedAssets()
                StartCoroutine(SfxCache(clip));

                switch (duck)
                {
                    case DuckType.MusicOnly:
                        _duckBgmSource.PlayOneShot(clip);
                        break;

                    case DuckType.AllSources:
                        _duckAllSource.PlayOneShot(clip);
                        break;

                    default:
                        _sfxSource.PlayOneShot(clip);
                        break;
                }
            }
        }


        public void PlaySfx(string path, DuckType duck = DuckType.None) => PlaySfx(GetClip(path), duck);


        public void StopSfx()
        {
            _sfxSource.Stop();
            _duckBgmSource.Stop();
            _duckAllSource.Stop();
        }


        public void PlayVoice(AudioClip clip, float delay = 0)
        {
            _duckBgmSource.clip = clip;
            if (delay > 0)
            {
                _duckBgmSource.PlayDelayed(delay);
            }
            else
            {
                _duckBgmSource.Play();
            }
        }


        public void PlayVoice(string path) => PlayVoice(GetClip(path));


        public void StopVoice() => _duckBgmSource.Stop();


        public static AudioClip GetClip(string path) => Resources.Load<AudioClip>(path);


        private void Update()
        {
            if (_mode == PlayMode.NeedMusicSwitch && !IsMusicPlaying)
            {
                OnPlaylistTrackChange?.Invoke();
            }
        }


        private IEnumerator SfxCache(AudioClip clip)
        {
            _activeSfx.Add(clip);

            yield return new WaitForSeconds(clip.length);

            _activeSfx.Remove(clip);
        }


        private bool IsSoundPlayingAvailable(AudioClip audioClip)
        {
            string clipName = audioClip.name;
            float currentTime = Time.time;

            if (_limitedSfxs.Contains(clipName))
            {
                return true;
            }

            bool result = true;
            
            if (!_limitedSfxTimings.TryGetValue(clipName, out List<float> times))
            {
                _limitedSfxTimings.Add(clipName, new List<float> { currentTime });
            }
            else
            {
                int timesCount = times.Count;
                if (timesCount < _maxSameSoundsCount)
                {
                    times.Add(currentTime);
                }
                else
                {
                    int index = times.Count - _maxSameSoundsCount;
                    if (currentTime - times[index] < _delayBetweenSameSounds)
                    {
                        result = false;
                    }
                    else
                    {
                        times.Add(currentTime);
                    }
                }
            }

            return result;
        }
    }
}