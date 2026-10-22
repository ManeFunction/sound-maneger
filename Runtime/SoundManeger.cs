using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mane.DotNet;
using UnityEngine;
using UnityEngine.Audio;

namespace Mane.Unity.SoundManeger
{
    [ManeStyle]
    [AddComponentMenu("Mane Tools/Audio/Sound Maneger")]
    [DisallowMultipleComponent]
    public class SoundManeger : UnitySingleton<SoundManeger>, IDisposable
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
        

        [SerializeField] private AudioMixer _mixer;
        [SerializeField] private AudioListener _listener;

        [Header("Sources")]
        [SerializeField] private AudioSource _musicSource1;
        [SerializeField] private AudioSource _musicSource2;
        [SerializeField] private AudioSource _sfxSource;
        [SerializeField] private AudioSource _duckBgmSource;
        [SerializeField] private AudioSource _duckAllSource;
        
        [Header("Settings")]
        [SerializeField] private float _transitionTime = 1.5f;
        [SerializeField] private float _lowpassTime = 0.4f;
        [SerializeField] private PlayingOrder _playlistPlayingOrder = PlayingOrder.Default;
        [SerializeField] private int _maxSameSoundsCount = 3;
        [SerializeField] private bool _continueAudioOnPause = true;

        [Header("Optimization")]
        [Tooltip("How often, in seconds, the manager forgets sound effects that have finished playing.")]
        [SerializeField] private float _sfxTimingsCleanupThreshold = 60f;
        [Tooltip("How many different sound effect clips are protected from unloading while they play.")]
        [SerializeField] private int _maxConcurrentSfx = 100;
        [SerializeField] private float _loadingTimeout = 30f;
        [Tooltip("Delay in seconds before retrying music loading if it failed. Only applies to loaders that support retrying.")]
        [SerializeField] private float _musicRetryDelay = 5f;

        
        private AudioMixerSnapshot _music1Snapshot;
        private AudioMixerSnapshot _music2Snapshot;
        private AudioMixerSnapshot _music1LowSnapshot;
        private AudioMixerSnapshot _music2LowSnapshot;

        private IMusicLoader _musicLoader;
        // Own random source, so playlists do not change UnityEngine.Random or a seeded global random.
        private readonly IRandom _random = new ManeRandom();
        private readonly SemaphoreSlim _musicLoadingSemaphore = new(1, 1);
        private readonly object _playlistLock = new();
        // Music loads in progress, the playlist waits for them before it changes the track
        private int _musicLoads;
        // Bumped by every PlayMusic and StopMusic call, see StartMusicRequest()
        private int _musicRequest;
        private UnityCancellationTokenSource _sfxCancellationSource;
        private UnityCancellationTokenSource _musicCancellationSource;

        private Coroutine _transitionAwaiter;

        public event Action PlaylistTrackChange;


        private List<string> _playlist;
        private int _playlistPointer;
        private MonoBehaviour _playlistOwner;
        private AudioClip _nextPlaylistTrack;
        // Playlist tracks in a row that failed to load
        private int _failedPlaylistTracks;
        private volatile int _modeFlags;

        private bool _activeFirstMusicSource;
        private bool _lowpass;

        private bool _muteMusic;
        private bool _muteSfx;
        private float _cachedBgmVolume = .8f;
        private float _cachedSfxVolume = .8f;

        private readonly object _sfxTokenLock = new();
        // Clips of playing SFX with the time they finish, so UnloadUnusedAssets() cannot unload them mid-play
        private readonly ConcurrentDictionary<AudioClip, float> _playingSfx = new();
        private readonly ConcurrentDictionary<AudioClip, ConcurrentQueue<float>> _limitedSfxTimings = new(1, 100);

        private float _lastCleanupTime;

        private bool _isDisposed;

        /// <summary>
        /// Set whether audio should continue playing when the game is paused.
        /// This affects both Time.timeScale = 0 and AudioListener.pause states.
        /// </summary>
        public bool ContinueAudioOnPause
        {
            get => _continueAudioOnPause;
            set
            {
                if (_continueAudioOnPause == value) return;
                _continueAudioOnPause = value;
                ConfigureAudioSources();
            }
        }

        /// <summary>
        /// Pause or unpause all audio. This is separate from Unity's Time.timeScale.
        /// </summary>
        public bool AudioPaused
        {
            get => AudioListener.pause;
            set => AudioListener.pause = value;
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this)
                return;

            _musicLoader = new ResourcesMusicLoader();

            FillMixerStuff();

            // The volumes restored on unmute start from the mixer
            if (TryGetVolume("BgmVolume", out float bgmVolume))
                _cachedBgmVolume = bgmVolume;
            if (TryGetVolume("SfxVolume", out float sfxVolume))
                _cachedSfxVolume = sfxVolume;

            if (!ValidateAudioSources())
            {
                Debug.LogError("[SoundManeger] Critical components are missing! Check the prefab setup.");
                enabled = false;
                return;
            }

            ConfigureAudioSources();
        }

        private void Reset() => FillMixerStuff();


        private void ConfigureAudioSources()
        {
            ConfigureAudioSource(_musicSource1);
            ConfigureAudioSource(_musicSource2);
            ConfigureAudioSource(_sfxSource);
            ConfigureAudioSource(_duckBgmSource);
            ConfigureAudioSource(_duckAllSource);
        }

        private void ConfigureAudioSource(AudioSource source)
        {
            if (source != null)
                source.ignoreListenerPause = _continueAudioOnPause;
        }
        
        private void FillMixerStuff()
        {
            if (_mixer == null)
                _mixer = Resources.Load<AudioMixer>("AudioMixer");

            if (_mixer == null)
            {
                Debug.LogError("[SoundManeger] Can't find AudioMixer in the project! Audio features will be disabled.");
                return;
            }

            _music1Snapshot = _mixer.FindSnapshot("Music1");
            _music1LowSnapshot = _mixer.FindSnapshot("Music1Lowpass");
            _music2Snapshot = _mixer.FindSnapshot("Music2");
            _music2LowSnapshot = _mixer.FindSnapshot("Music2Lowpass");

            if (_music1Snapshot == null || _music2Snapshot == null)
            {
                Debug.LogError("[SoundManeger] Missing required snapshots Music1/Music2! Music features will be disabled.");
                _mixer = null;
                return;
            }

            if (_music1LowSnapshot == null || _music2LowSnapshot == null)
            {
                Debug.LogWarning("[SoundManeger] Missing lowpass snapshots! Lowpass feature will be disabled.");
                _lowpass = false;
            }
        }

        private bool ValidateAudioSources()
        {
            if (_musicSource1 == null || _musicSource2 == null)
            {
                Debug.LogError("[SoundManeger] Missing music sources! Music features will be disabled.");
                return false;
            }

            if (_sfxSource == null)
            {
                Debug.LogError("[SoundManeger] Missing SFX source! SFX features will be disabled.");
                return false;
            }

            if (_duckBgmSource == null)
                Debug.LogWarning("[SoundManeger] Missing duck BGM source! Duck BGM and voice features will be disabled.");

            if (_duckAllSource == null)
                Debug.LogWarning("[SoundManeger] Missing duck all source! Duck all feature will be disabled.");

            return true;
        }

        public AudioMixer Mixer => _mixer;

        public AudioListener Listener => _listener;

        public AudioMixerGroup MusicGroup => FindGroup("Master/BGM/MusicAdd");

        public AudioMixerGroup SfxGroup => FindGroup("Master/SFX/Effects");

        public AudioMixerGroup SfxDuckMusicGroup => FindGroup("Master/SFX/FadeBgm");

        public AudioMixerGroup SfxDuckAllGroup => FindGroup("Master/SFX/FadeAll");

        private AudioMixerGroup FindGroup(string path) =>
            _mixer != null ? _mixer.FindMatchingGroups(path).FirstOrDefault() : null;


        /// <summary>
        /// Transition time between music tracks (applied only for manual track switching, not for a playlist).
        /// </summary>
        public float TransitionTime
        {
            get => _transitionTime;
            set => _transitionTime = value;
        }

        /// <summary>
        /// Indicates that music is playing.
        /// </summary>
        public bool IsMusicPlaying => _musicSource1.isPlaying || _musicSource2.isPlaying;
        
        /// <summary>
        /// Mute all music.
        /// </summary>
        public bool MuteMusic
        {
            get => _muteMusic;
            set
            {
                // Playlist tracks do not change while muted, see Update()
                _muteMusic = value;
                SetVolume("BgmVolume", value ? 0 : _cachedBgmVolume);
            }
        }
        
        /// <summary>
        /// Mute all sound effects.
        /// </summary>
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
            get => _cachedBgmVolume;
            set
            {
                _cachedBgmVolume = Mathf.Clamp01(value);

                if (!MuteMusic)
                    SetVolume("BgmVolume", _cachedBgmVolume);
            }
        }
        
        /// <summary>
        /// Set the volume between 0f and 1f, what depends on range from -80 to 20 in mixer.
        /// </summary>
        public float SfxVolume
        {
            get => _cachedSfxVolume;
            set
            {
                _cachedSfxVolume = Mathf.Clamp01(value);

                if (!MuteSfx)
                    SetVolume("SfxVolume", _cachedSfxVolume);
            }
        }

        private float GetVolume(string key) => TryGetVolume(key, out float volume) ? volume : 0.0f;

        private bool TryGetVolume(string key, out float volume)
        {
            volume = 0.0f;
            if (_mixer == null || !_mixer.GetFloat(key, out float value))
                return false;

            volume = (value + 80.0f) / 100.0f;

            return true;
        }

        private void SetVolume(string key, float value)
        {
            if (_mixer == null) return;

            if (value < 0.0f)
                value = 0.0f;
            else if (value > 1.0f)
                value = 1.0f;

            value = value * 100.0f - 80.0f;

            _mixer.SetFloat(key, value);
        }
        
        
        /// <summary>
        /// Set music loader. Use for your own implementation or create a standard AddressableMusicLoader.
        /// ResourcesMusicLoader is used by default.
        /// </summary>
        /// <param name="musicLoader">Music loader to use.</param>
        public void SetMusicLoader(IMusicLoader musicLoader)
        {
            if (musicLoader == null) return;
            
            _musicLoader = musicLoader;
        }


        /// <summary>
        /// Enable or disable lowpass filter on music.
        /// </summary>
        /// <param name="enable">Enable or disable.</param>
        public void Lowpass(bool enable)
        {
            if (_lowpass == enable || !HasLowpass) return;

            _lowpass = enable;
            GetMusicSnapshot(_activeFirstMusicSource).TransitionTo(_lowpassTime);
        }

        // Missing lowpass snapshots are reported by FillMixerStuff()
        private bool HasLowpass => _mixer != null && _music1LowSnapshot != null && _music2LowSnapshot != null;

        // Snapshot that makes the given music source audible, with the lowpass while it is on
        private AudioMixerSnapshot GetMusicSnapshot(bool firstSource)
        {
            if (firstSource)
                return _lowpass ? _music1LowSnapshot : _music1Snapshot;

            return _lowpass ? _music2LowSnapshot : _music2Snapshot;
        }

        
        /// <summary>
        /// Play a single music track.
        /// </summary>
        /// <param name="clip">Track to play.</param>
        public void PlayMusic(AudioClip clip)
        {
            if (clip == null) return;

            StartMusicRequest();
            PlaySingleTrack(clip);
        }

        /// <summary>
        /// Load and play a single music track.
        /// </summary>
        /// <param name="owner">Track loading owner. If null, SoundManeger.Instance will be used.</param>
        /// <param name="path">Path to the track.</param>
        public async void PlayMusic(MonoBehaviour owner, string path) =>
            await LoadSingleTrack(StartMusicRequest(), owner, path);

        /// <summary>
        /// Play a playlist.
        /// </summary>
        /// <param name="owner">Playlist loading owner. If null, SoundManeger.Instance will be used.</param>
        /// <param name="firstTrackIsIntro">If true, first track will be played only once.</param>
        /// <param name="first">First track to play.</param>
        /// <param name="playlist">Playlist to play.</param>
        public async void PlayMusic(MonoBehaviour owner, bool firstTrackIsIntro, string first, params string[] playlist)
        {
            if (string.IsNullOrEmpty(first)) return;

            int request = StartMusicRequest();

            // single track
            if (playlist.IsNullOrEmpty())
            {
                await LoadSingleTrack(request, owner, first);

                return;
            }

            // playlist
            List<string> pl = new(playlist);
            if (firstTrackIsIntro)
            {
                await StartPlaylistAfterIntro(request, owner, first, pl);

                return;
            }

            pl.Insert(0, first);
            StartPlaylist(owner, pl, false);
        }

        /// <summary>
        /// Play a playlist.
        /// </summary>
        /// <param name="owner">Playlist loading owner. If null, SoundManeger.Instance will be used.</param>
        /// <param name="firstTrackIsIntro">If true, first track will be played only once.</param>
        /// <param name="playlist">Playlist to play.</param>
        public async void PlayMusic(MonoBehaviour owner, bool firstTrackIsIntro, string[] playlist)
        {
            // empty call
            if (playlist.IsNullOrEmpty()) return;

            int request = StartMusicRequest();

            // single track
            if (playlist.Length == 1)
            {
                await LoadSingleTrack(request, owner, playlist[0]);

                return;
            }

            // playlist
            List<string> pl = new(playlist);
            if (firstTrackIsIntro)
            {
                string intro = pl[0];
                pl.RemoveAt(0);
                await StartPlaylistAfterIntro(request, owner, intro, pl);

                return;
            }

            StartPlaylist(owner, pl, false);
        }
        
        /// <summary>
        /// Play a playlist.
        /// </summary>
        /// <param name="owner">Playlist loading owner. If null, SoundManeger.Instance will be used.</param>
        /// <param name="playingOrder">Order of playing tracks. Default order keeps the order as is. Random order is completely randomized. Shuffle shuffles the list after every cycle. Both shuffle and random are guarantee NOT to play the same track twice in a row.</param>
        /// <param name="firstTrackIsIntro">If true, first track will be played only once.</param>
        /// <param name="first">First track to play.</param>
        /// <param name="playlist">Playlist to play.</param>
        public void PlayMusic(MonoBehaviour owner, PlayingOrder playingOrder,
            bool firstTrackIsIntro, string first, params string[] playlist)
        {
            _playlistPlayingOrder = playingOrder;
            
            PlayMusic(owner, firstTrackIsIntro, first, playlist);
        }

        /// <summary>
        /// Start a playlist.
        /// </summary>
        /// <param name="owner">Playlist loading owner. If null, SoundManeger.Instance will be used.</param>
        /// <param name="playingOrder">Order of playing tracks. Default order keeps the order as is. Random order is completely randomized. Shuffle shuffles the list after every cycle. Both shuffle and random are guarantee NOT to play the same track twice in a row.</param>
        /// <param name="firstTrackIsIntro">If true, first track will be played only once.</param>
        /// <param name="playlist">Playlist to play.</param>
        public void PlayMusic(MonoBehaviour owner, PlayingOrder playingOrder, bool firstTrackIsIntro, string[] playlist)
        {
            _playlistPlayingOrder = playingOrder;
            
            PlayMusic(owner, firstTrackIsIntro, playlist);
        }
        
        /// <summary>
        /// Stop playing music.
        /// </summary>
        public void StopMusic()
        {
            StartMusicRequest();
            SetMode(PlayMode.Silence);
            PlaylistTrackChange -= OnTrackChange;
            _musicSource1.Stop();
            _musicSource2.Stop();
            ClearPlaylist();

            // Clear references to allow GC
            _musicSource1.clip = null;
            _musicSource2.clip = null;
        }

        // Every PlayMusic and StopMusic call starts a new request and cancels the music loads of the older ones.
        // A load that finishes after a newer request is dropped, so music calls take effect in the order they are made.
        private int StartMusicRequest()
        {
            CancelMusicLoading();
            return ++_musicRequest;
        }

        private void CancelMusicLoading()
        {
            if (_musicCancellationSource == null) return;

            _musicCancellationSource.Cancel();
            _musicCancellationSource.Dispose();
            _musicCancellationSource = null;
        }

        private void PlaySingleTrack(AudioClip clip)
        {
            _musicSource1.loop = true;
            _musicSource2.loop = true;
            RemoveMode(PlayMode.PlaylistActive);
            PlaylistTrackChange -= OnTrackChange;
            ClearPlaylist();

            PlayMusicClip(clip);
        }

        private async Task LoadSingleTrack(int request, MonoBehaviour owner, string path)
        {
            AudioClip clip = await GetClip(owner, path, true);
            if (request == _musicRequest && clip != null)
                PlaySingleTrack(clip);
        }

        // The intro plays once and the playlist starts after it. If the intro does not load, the playlist starts at once.
        private async Task StartPlaylistAfterIntro(int request, MonoBehaviour owner, string intro, List<string> playlist)
        {
            AudioClip clip = await GetClip(owner, intro, true);
            if (request != _musicRequest)
                return;

            if (clip != null)
                PlaySingleTrack(clip);

            StartPlaylist(owner, playlist, clip != null);
        }


        private void StartPlaylist(MonoBehaviour owner, List<string> playlist, bool startAfterCurrentTrack)
        {
            // Music is disabled without a mixer, see FillMixerStuff()
            if (_mixer == null) return;

            lock (_playlistLock)
            {
                _playlist = new List<string>(playlist); // Create defensive copy
                _playlistOwner = owner != null ? owner : this;
                _nextPlaylistTrack = null;
                _failedPlaylistTracks = 0;

                _musicSource1.loop = false;
                _musicSource2.loop = false;
                // Music is on from here, so Update() moves on even if the first track fails to load
                AddMode(PlayMode.NeedMusicSwitch);

                // safe way to exclude double subscription
                PlaylistTrackChange -= OnTrackChange;
                PlaylistTrackChange += OnTrackChange;
            
                switch (_playlistPlayingOrder)
                {
                    case PlayingOrder.Default:
                        _playlistPointer = 0;
                        break;
                    
                    case PlayingOrder.Shuffle:
                        _playlistPointer = 0;
                        _playlist.Shuffle(_random);
                        break;

                    case PlayingOrder.Random:
                        _playlistPointer = _random.Next(0, _playlist.Count);
                        break;
                }
            }

            if (startAfterCurrentTrack)
                CacheNextTrack();
            else
                OnTrackChange();
        }

        private void PrepareNextTrack()
        {
            string trackPath = null;
            lock (_playlistLock)
            {
                if (_playlist == null || _playlist.Count == 0) return;

                MovePlaylistPointer();

                // Get the track path while still in the lock
                trackPath = _playlist[_playlistPointer];
            }

            // Only proceed if we got a valid track path
            if (!string.IsNullOrEmpty(trackPath))
                CacheNextTrack();
        }

        // Call under _playlistLock
        private void MovePlaylistPointer()
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
                        _playlistPointer = _random.Next(0, _playlist.Count);
                    } while (_playlistPointer == old);
                }
            }
            else
            {
                _playlistPointer++;
                if (_playlistPointer >= _playlist.Count)
                {
                    _playlistPointer = 0;
                    if (_playlistPlayingOrder == PlayingOrder.Shuffle && _playlist.Count > 1)
                    {
                        var lastTrack = _playlist.Last();
                        do
                        {
                            _playlist.Shuffle(_random);
                        } while (_playlist.First() == lastTrack);
                    }
                }
            }
        }

        // A track that does not load is skipped, the next Update() tries the one after it.
        // When none of the tracks loads, the playlist stops.
        private void SkipFailedTrack()
        {
            lock (_playlistLock)
            {
                if (_playlist == null || _playlist.Count == 0) return;

                if (++_failedPlaylistTracks < _playlist.Count)
                {
                    MovePlaylistPointer();
                    return;
                }
            }

            Debug.LogError("[SoundManeger] None of the playlist tracks could be loaded, the playlist is stopped.");
            RemoveMode(PlayMode.PlaylistActive);
            PlaylistTrackChange -= OnTrackChange;
            ClearPlaylist();
        }

        private async void CacheNextTrack()
        {
            int request = _musicRequest;
            MonoBehaviour owner;
            string trackPath;

            // Get all needed data under lock
            lock (_playlistLock)
            {
                if (_playlist == null || _playlist.Count == 0) return;
                trackPath = _playlist[_playlistPointer];
                owner = _playlistOwner;
            }

            // Only proceed if we have valid data (GetClip() replaces a destroyed owner with the manager)
            if (!string.IsNullOrEmpty(trackPath))
            {
                AudioClip clip = await GetClip(owner, trackPath, true);
                if (request == _musicRequest)
                    _nextPlaylistTrack = clip;
            }
        }

        private async void OnTrackChange()
        {
            if (_nextPlaylistTrack)
            {
                _failedPlaylistTracks = 0;
                PlayMusicClip(_nextPlaylistTrack);
                _nextPlaylistTrack = null;
                PrepareNextTrack();
                return;
            }

            int request = _musicRequest;
            MonoBehaviour owner;
            string trackPath;
            lock (_playlistLock)
            {
                if (_playlist == null || _playlist.Count == 0) return;
                owner = _playlistOwner;
                trackPath = _playlist[_playlistPointer];
            }

            var clip = await GetClip(owner, trackPath, true);
            if (request != _musicRequest)
                return;

            if (clip)
            {
                _failedPlaylistTracks = 0;
                PlayMusicClip(clip);
                PrepareNextTrack();
            }
            else
            {
                SkipFailedTrack();
            }
        }

        private void ClearPlaylist()
        {
            lock (_playlistLock)
            {
                _playlist = null;
                _playlistOwner = null;
                _nextPlaylistTrack = null;
                _playlistPointer = 0;
                _failedPlaylistTracks = 0;
            }
        }


        /// <summary>
        /// Play a sound effect.
        /// </summary>
        /// <param name="clip">Sound effect to play.</param>
        /// <param name="duck">Ducking type.</param>
        public void PlaySfx(AudioClip clip, DuckType duck = DuckType.None)
        {
            if (_mixer == null || clip == null || _sfxSource == null || !IsSoundPlayingAvailable(clip))
                return;

            // Save link to SoundEffects asset to prevent unloading via UnloadUnusedAssets()
            KeepAlive(clip);

            // Play sound based on duck type with null checks
            switch (duck)
            {
                case DuckType.MusicOnly:
                    if (_duckBgmSource != null)
                        _duckBgmSource.PlayOneShot(clip);
                    else
                        _sfxSource.PlayOneShot(clip);
                    break;

                case DuckType.AllSources:
                    if (_duckAllSource != null)
                        _duckAllSource.PlayOneShot(clip);
                    else
                        _sfxSource.PlayOneShot(clip);
                    break;

                default:
                    _sfxSource.PlayOneShot(clip);
                    break;
            }
        }
        
        /// <summary>
        /// Load and play a sound effect.
        /// </summary>
        /// <param name="owner">Sound effect loading owner. If null, SoundManeger.Instance will be used.</param>
        /// <param name="path">Path to the sound effect.</param>
        /// <param name="duck">Ducking type.</param>
        public async void PlaySfx(MonoBehaviour owner, string path, DuckType duck = DuckType.None) => 
            PlaySfx(await GetClip(owner, path, false), duck);

        /// <summary>
        /// Stop all sound effects.
        /// </summary>
        public void StopSfx()
        {
            if (_sfxSource != null)
                _sfxSource.Stop();
            if (_duckBgmSource != null)
                _duckBgmSource.Stop();
            if (_duckAllSource != null)
                _duckAllSource.Stop();
        }


        /// <summary>
        /// Play a voice. Voice is played on the same source as sfx, but fades out music automatically, based on voice volume.
        /// </summary>
        /// <param name="clip">Voice clip to play.</param>
        /// <param name="delay">Delay before playing.</param>
        public void PlayVoice(AudioClip clip, float delay = 0)
        {
            if (clip == null || _duckBgmSource == null)
                return;

            _duckBgmSource.clip = clip;
            if (delay > 0)
                _duckBgmSource.PlayDelayed(delay);
            else
                _duckBgmSource.Play();
        }
        
        /// <summary>
        /// Load and play a voice. Voice is played on the same source as sfx, but fades out music automatically, based on voice volume.
        /// </summary>
        /// <param name="owner">Voice loading owner. If null, SoundManeger.Instance will be used.</param>
        /// <param name="path">Path to the voice clip.</param>
        public async void PlayVoice(MonoBehaviour owner, string path) => PlayVoice(await GetClip(owner, path, false));
        
        /// <summary>
        /// Stop playing voice.
        /// </summary>
        public void StopVoice()
        {
            if (_duckBgmSource != null)
                _duckBgmSource.Stop();
        }

        
        private void PlayMusicClip(AudioClip clip)
        {
            if (_mixer == null || clip == null) return;

            // The clip already plays on the active source: keep it playing
            if (_musicSource2.clip == clip && _musicSource2.isPlaying && !_activeFirstMusicSource ||
                _musicSource1.clip == clip && _musicSource1.isPlaying && _activeFirstMusicSource)
                return;

            try
            {
                if (_activeFirstMusicSource)
                {
                    _musicSource2.clip = clip;
                    if (!GetMode().HasFlag(PlayMode.PlaylistActive))
                        _musicSource2.loop = true;
                    _musicSource2.Play();
                    GetMusicSnapshot(false).TransitionTo(_transitionTime);
                    _activeFirstMusicSource = false;
                    this.TryKillCoroutine(ref _transitionAwaiter);
                    _transitionAwaiter = StartCoroutine(StopMusicSource(_musicSource1, _transitionTime));
                }
                else
                {
                    _musicSource1.clip = clip;
                    if (!GetMode().HasFlag(PlayMode.PlaylistActive))
                        _musicSource1.loop = true;
                    _musicSource1.Play();
                    GetMusicSnapshot(true).TransitionTo(_transitionTime);
                    _activeFirstMusicSource = true;
                    this.TryKillCoroutine(ref _transitionAwaiter);
                    _transitionAwaiter = StartCoroutine(StopMusicSource(_musicSource2, _transitionTime));
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SoundManeger] Failed to play music clip {clip.name}: {e.Message}");
                return;
            }
            
            AddMode(PlayMode.PlayingMusic);
        }

        private IEnumerator StopMusicSource(AudioSource source, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            
            source.Stop();
            _transitionAwaiter = null;
        }
        
        private async Task<AudioClip> GetClip(MonoBehaviour owner, string path, bool isMusic)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[SoundManeger] Attempted to load clip with null or empty path!");
                return null;
            }

            if (_isDisposed)
            {
                Debug.LogWarning("[SoundManeger] Attempted to load clip after disposal!");
                return null;
            }

            if (owner == null)
                owner = this;

            try
            {
                if (isMusic)
                {
                    // Taken once, so a newer music request cancels the waiting and the retries of this load too
                    CancellationToken musicToken = GetCancellationToken(true);
                    Interlocked.Increment(ref _musicLoads);
                    try
                    {
                        if (!await _musicLoadingSemaphore.WaitAsync(TimeSpan.FromSeconds(_loadingTimeout), musicToken))
                        {
                            Debug.LogError("[SoundManeger] Music loading semaphore timeout!");
                            return null;
                        }

                        try
                        {
                            AudioClip clip = null;
                            bool shouldRetry = _musicLoader.ShouldRetry;

                            do
                            {
                                clip = await _musicLoader.GetMusicAsync(owner, path, musicToken);

                                if (clip != null || !shouldRetry || musicToken.IsCancellationRequested)
                                    break;

                                Debug.LogWarning($"[SoundManeger] Failed to load music, retrying after {_musicRetryDelay} seconds: {path}");
                                await Task.Delay(TimeSpan.FromSeconds(_musicRetryDelay), musicToken);

                            } while (!_isDisposed);

                            if (clip == null && !musicToken.IsCancellationRequested)
                                Debug.LogError($"[SoundManeger] Failed to load music clip at path: {path}");

                            return clip;
                        }
                        finally
                        {
                            _musicLoadingSemaphore.Release();
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _musicLoads);
                    }
                }
                
                // For SFX, we only need to protect token creation/disposal
                CancellationToken token;
                lock (_sfxTokenLock)
                {
                    token = GetCancellationToken(false);
                }
                
                // No retries: a sound effect or voice line played seconds late is worse than none
                AudioClip sfxClip = await _musicLoader.GetMusicAsync(owner, path, token);

                if (sfxClip == null && !token.IsCancellationRequested)
                    Debug.LogError($"[SoundManeger] Failed to load SFX clip at path: {path}");
                
                return sfxClip;
            }
            catch (OperationCanceledException)
            {
                EditorDebug.LogWarning($"[SoundManeger] Loading was canceled: {path}");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SoundManeger] Error loading clip at {path}: {e.Message}");
                return null;
            }
        }
        
        private CancellationToken GetCancellationToken(bool isMusic)
        {
            if (!isMusic)
            {
                // Note: This method is called within a lock for SFX
                if (_sfxCancellationSource == null || _sfxCancellationSource.IsCancellationRequested)
                    CreateNewSfxCancellationSource();
                
                return _sfxCancellationSource.Token;
            }
            
            // Music token operations are protected by semaphore in GetClip
            if (_musicCancellationSource == null || _musicCancellationSource.IsCancellationRequested)
                CreateNewMusicCancellationSource();

            return _musicCancellationSource.Token;
        }

        private void Update()
        {
            float currentTime = Time.unscaledTime;
            
            if (currentTime - _lastCleanupTime >= _sfxTimingsCleanupThreshold)
            {
                CleanupSfxTimings();
                CleanupActiveSfx();
                _lastCleanupTime = currentTime;
            }

            if (!_muteMusic && GetMode() == PlayMode.NeedMusicSwitch && !IsMusicPlaying && _musicLoads == 0)
                PlaylistTrackChange?.Invoke();
        }
        
        private void KeepAlive(AudioClip clip)
        {
            if (!_playingSfx.ContainsKey(clip) && _playingSfx.Count >= _maxConcurrentSfx)
            {
                CleanupActiveSfx();

                // Still full: the sound plays, but is not protected from unloading
                if (_playingSfx.Count >= _maxConcurrentSfx)
                    return;
            }

            _playingSfx[clip] = Time.unscaledTime + clip.length;
        }

        private bool IsSoundPlayingAvailable(AudioClip audioClip)
        {
            float currentTime = Time.unscaledTime;

            // Try to get existing queue or create new one
            ConcurrentQueue<float> times = _limitedSfxTimings.GetOrAdd(audioClip, _ => new ConcurrentQueue<float>());

            // Cleanup old timings that are beyond the clip length
            while (times.TryPeek(out float oldestTime) && currentTime - oldestTime >= audioClip.length)
                times.TryDequeue(out _);

            // If we have less sounds than maximum allowed (one is always allowed), add new timing
            if (times.IsEmpty || times.Count < _maxSameSoundsCount)
            {
                times.Enqueue(currentTime);
                return true;
            }

            // Every timing left is younger than the clip, so the limit is reached
            return false;
        }

        private void CleanupSfxTimings()
        {
            float currentTime = Time.unscaledTime;

            foreach (var kvp in _limitedSfxTimings)
            {
                var times = kvp.Value;

                // Remove the timings of finished plays (all of them for a destroyed clip)
                float length = kvp.Key ? kvp.Key.length : 0f;
                while (times.TryPeek(out float oldestTime) && currentTime - oldestTime >= length)
                    times.TryDequeue(out _);
                
                // Remove empty queues
                if (times.IsEmpty)
                    _limitedSfxTimings.TryRemove(kvp.Key, out _);
            }
        }

        private void CleanupActiveSfx()
        {
            // Forget finished and destroyed clips
            float currentTime = Time.unscaledTime;
            foreach (var kvp in _playingSfx)
            {
                if (!kvp.Key || kvp.Value <= currentTime)
                    _playingSfx.TryRemove(kvp.Key, out _);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Dispose();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            
            _isDisposed = true;

            _playingSfx.Clear();
            _limitedSfxTimings.Clear();

            _sfxCancellationSource?.Dispose();
            _musicCancellationSource?.Dispose();
            _sfxCancellationSource = null;
            _musicCancellationSource = null;
            _musicLoadingSemaphore.Dispose();

            ClearPlaylist();
        }

        private void CreateNewMusicCancellationSource()
        {
            if (_musicCancellationSource != null)
            {
                _musicCancellationSource.Cancel();
                _musicCancellationSource.Dispose();
            }
            _musicCancellationSource = new UnityCancellationTokenSource();
        }

        private void CreateNewSfxCancellationSource()
        {
            // Note: This method is called within a lock
            if (_sfxCancellationSource != null)
            {
                _sfxCancellationSource.Cancel();
                _sfxCancellationSource.Dispose();
            }
            _sfxCancellationSource = new UnityCancellationTokenSource();
        }

        private PlayMode GetMode()
        {
            return (PlayMode)Interlocked.CompareExchange(ref _modeFlags, 0, 0);
        }

        private void SetMode(PlayMode value)
        {
            Interlocked.Exchange(ref _modeFlags, (int)value);
        }

        private void AddMode(PlayMode flag)
        {
            var current = Interlocked.CompareExchange(ref _modeFlags, 0, 0);
            Interlocked.Exchange(ref _modeFlags, current | (int)flag);
        }

        private void RemoveMode(PlayMode flag)
        {
            var current = Interlocked.CompareExchange(ref _modeFlags, 0, 0);
            Interlocked.Exchange(ref _modeFlags, current & ~(int)flag);
        }
    }
}