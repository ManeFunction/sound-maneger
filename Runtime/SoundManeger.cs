using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mane.Extensions;
using UnityEngine;
using UnityEngine.Audio;

namespace Mane.SoundManeger
{
    [AddComponentMenu("Audio/Sound Manager")]
    [DisallowMultipleComponent]
    public class SoundManeger : MonoSingleton<SoundManeger>, IDisposable
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

        
        private AudioMixerSnapshot _music1Snapshot;
        private AudioMixerSnapshot _music2Snapshot;
        private AudioMixerSnapshot _music1LowSnapshot;
        private AudioMixerSnapshot _music2LowSnapshot;

        private IMusicLoader _musicLoader;
        private readonly SemaphoreSlim _musicLoadingSemaphore = new SemaphoreSlim(1, 1);
        private readonly object _playlistLock = new object();
        private volatile bool _isMusicLoading;
        private UnityCancellationTokenSource _sfxCancellationSource;
        private UnityCancellationTokenSource _musicCancellationSource;

        private Coroutine _transitionAwaiter;

        public event Action PlaylistTrackChange;


        private List<string> _playlist;
        private int _playlistPointer;
        private MonoBehaviour _playlistOwner; 
        private PlayMode _mode;
        private AudioClip _nextPlaylistTrack;

        private bool _activeFirstMusicSource;
        private bool _lowpass;
        private bool _manualPlaylistMode;

        private bool _muteMusic;
        private bool _muteSfx;
        private float _cachedBgmVolume = .8f;
        private float _cachedSfxVolume = .8f;

        // used to keep active clips and prevent unloading while playing
        private readonly object _sfxTokenLock = new object();
        private readonly ConcurrentBag<AudioClip> _activeSfx = new ConcurrentBag<AudioClip>();
        private readonly ConcurrentDictionary<AudioClip, Coroutine> _activeSfxCoroutines = new ConcurrentDictionary<AudioClip, Coroutine>();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<float>> _limitedSfxTimings = new ConcurrentDictionary<string, ConcurrentQueue<float>>();

        private bool _isDisposed;

        protected override void Awake()
        {
            base.Awake();

            _musicLoader = new ResourcesMusicLoader();
            
            FillMixerStuff();

            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }


        private void Reset() => FillMixerStuff();
        
        
        private void FillMixerStuff()
        {
            if (_mixer == null)
                _mixer = Resources.Load<AudioMixer>("AudioMixer");

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


        public AudioMixer Mixer => _mixer;

        public AudioListener Listener => _listener;

        public AudioMixerGroup MusicGroup =>
            _mixer.FindMatchingGroups("Master/BGM/MusicAdd").First();
        
        public AudioMixerGroup SfxGroup =>
            _mixer.FindMatchingGroups("Master/SFX/Effects").First();
        
        public AudioMixerGroup SfxDuckMusicGroup =>
            _mixer.FindMatchingGroups("Master/SFX/FadeBgm").First();
        
        public AudioMixerGroup SfxDuckAllGroup =>
            _mixer.FindMatchingGroups("Master/SFX/FadeAll").First();


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
                if (value)
                    _mode &= ~PlayMode.PlayingMusic;
                else
                    _mode |= PlayMode.PlayingMusic;

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
            get => GetVolume("BgmVolume");
            set
            {
                _cachedBgmVolume = value;

                if (!MuteMusic)
                    SetVolume("BgmVolume", value);
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
                    SetVolume("SfxVolume", value);
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
            if (_lowpass == enable) return;

            _lowpass = enable;
            if (_activeFirstMusicSource)
            {
                if (_lowpass)
                    _music1LowSnapshot.TransitionTo(_lowpassTime);
                else
                    _music1Snapshot.TransitionTo(_lowpassTime);
            }
            else
            {
                if (_lowpass)
                    _music2LowSnapshot.TransitionTo(_lowpassTime);
                else
                    _music2Snapshot.TransitionTo(_lowpassTime);
            }
        }

        
        /// <summary>
        /// Play a single music track.
        /// </summary>
        /// <param name="clip">Track to play.</param>
        public void PlayMusic(AudioClip clip)
        {
            _musicSource1.loop = true;
            _musicSource2.loop = true;
            _mode &= ~PlayMode.PlaylistActive;
            PlaylistTrackChange -= OnTrackChange;
            ClearPlaylist();
            
            PlayMusicClip(clip);
        }
        
        /// <summary>
        /// Load and play a single music track.
        /// </summary>
        /// <param name="owner">Track loading owner. If null, SoundManeger.Instance will be used.</param>
        /// <param name="path">Path to the track.</param>
        public async void PlayMusic(MonoBehaviour owner, string path) => 
            PlayMusic(await GetClip(owner, path, true));
        
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

            // single track
            if (playlist == null || playlist.Length == 0)
            {
                PlayMusic(await GetClip(owner, first, true));

                return;
            }

            // playlist
            List<string> pl = new List<string>(playlist);
            if (firstTrackIsIntro)
                PlayMusic(await GetClip(owner, first, true));
            else
                pl.Insert(0, first);

            StartPlaylist(owner, pl, firstTrackIsIntro);
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
            if (playlist == null || playlist.Length == 0) return;

            // single track
            if (playlist.Length == 1)
            {
                PlayMusic(await GetClip(owner, playlist[0], true));

                return;
            }
            
            // playlist
            List<string> pl = new List<string>(playlist);
            if (firstTrackIsIntro)
            {
                PlayMusic(await GetClip(owner, pl[0], true));
                pl.RemoveAt(0);
            }

            StartPlaylist(owner, pl, firstTrackIsIntro);
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
            _mode &= PlayMode.PlayingMusic;
            PlaylistTrackChange -= OnTrackChange;
            _musicSource1.Stop();
            _musicSource2.Stop();
            ClearPlaylist();
            
            if (_musicCancellationSource != null)
            {
                _musicCancellationSource.Cancel();
                _musicCancellationSource.Dispose();
                _musicCancellationSource = null;
            }
        }


        private void StartPlaylist(MonoBehaviour owner, List<string> playlist, bool startAfterCurrentTrack)
        {
            lock (_playlistLock)
            {
                _playlist = new List<string>(playlist); // Create defensive copy
                _playlistOwner = owner;
                _nextPlaylistTrack = null;

                _musicSource1.loop = false;
                _musicSource2.loop = false;
                _mode |= PlayMode.PlaylistActive;

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
                        _playlist.Shuffle();
                        break;
                    
                    case PlayingOrder.Random:
                        _playlistPointer = UnityEngine.Random.Range(0, _playlist.Count);
                        break;
                }
            }

            if (startAfterCurrentTrack)
                CacheNextTrack(GetCancellationToken(true));
            else
                OnTrackChange();
        }

        private async void CacheNextTrack(CancellationToken token)
        {
            string trackPath;
            lock (_playlistLock)
            {
                if (_playlist == null || _playlist.Count == 0) return;
                trackPath = _playlist[_playlistPointer];
            }

            _nextPlaylistTrack = await GetClip(_playlistOwner, trackPath, true);
        }

        private async void OnTrackChange()
        {
            if (_nextPlaylistTrack)
            {
                PlayMusicClip(_nextPlaylistTrack);
                _nextPlaylistTrack = null;
                PrepareNextTrack();
                return;
            }

            var clip = await GetClip(_playlistOwner, _playlist[_playlistPointer], true);
            if (clip)
            {
                PlayMusicClip(clip);
                PrepareNextTrack();
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
            }
        }


        /// <summary>
        /// Play a sound effect.
        /// </summary>
        /// <param name="clip">Sound effect to play.</param>
        /// <param name="duck">Ducking type.</param>
        public void PlaySfx(AudioClip clip, DuckType duck = DuckType.None)
        {
            if (clip == null) return;

            if (!IsSoundPlayingAvailable(clip)) return;
            
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
            _sfxSource.Stop();
            _duckBgmSource.Stop();
            _duckAllSource.Stop();
        }


        /// <summary>
        /// Play a voice. Voice is played on the same source as sfx, but fades out music automatically, based on voice volume.
        /// </summary>
        /// <param name="clip">Voice clip to play.</param>
        /// <param name="delay">Delay before playing.</param>
        public void PlayVoice(AudioClip clip, float delay = 0)
        {
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
        public void StopVoice() => _duckBgmSource.Stop();

        
        private void PlayMusicClip(AudioClip clip)
        {
            if (_musicSource2.clip == clip && _musicSource2.isPlaying && !_activeFirstMusicSource ||
                _musicSource1.clip == clip && _musicSource1.isPlaying && _activeFirstMusicSource)
            {
                _activeFirstMusicSource = !_activeFirstMusicSource;
                return;
            }

            if (_activeFirstMusicSource)
            {
                _musicSource2.clip = clip;
                if (!_mode.HasFlag(PlayMode.PlaylistActive))
                    _musicSource2.loop = true;
                _musicSource2.Play();
                _music2Snapshot.TransitionTo(_transitionTime);
                _activeFirstMusicSource = false;
                if (_transitionAwaiter != null)
                    StopCoroutine(_transitionAwaiter);
                _transitionAwaiter = StartCoroutine(StopMusicSource(_musicSource1, _transitionTime));
            }
            else
            {
                _musicSource1.clip = clip;
                if (!_mode.HasFlag(PlayMode.PlaylistActive))
                    _musicSource1.loop = true;
                _musicSource1.Play();
                _music1Snapshot.TransitionTo(_transitionTime);
                _activeFirstMusicSource = true;
                if (_transitionAwaiter != null)
                    StopCoroutine(_transitionAwaiter);
                _transitionAwaiter = StartCoroutine(StopMusicSource(_musicSource2, _transitionTime));
            }
            
            _mode |= PlayMode.PlayingMusic;
        }

        private IEnumerator StopMusicSource(AudioSource source, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            
            source.Stop();
            _transitionAwaiter = null;
        }
        
        private async Task<AudioClip> GetClip(MonoBehaviour requester, string path, bool isMusic)
        {
            if (isMusic)
            {
                _isMusicLoading = true;
                try
                {
                    await _musicLoadingSemaphore.WaitAsync();
                    try
                    {
                        if (_musicCancellationSource != null)
                        {
                            _musicCancellationSource.Cancel();
                            _musicCancellationSource.Dispose();
                            _musicCancellationSource = null;
                        }

                        var task = _musicLoader.GetMusicAsync(requester, path, GetCancellationToken(true));
                        return await task;
                    }
                    catch (OperationCanceledException)
                    {
#if UNITY_EDITOR
                        Debug.LogWarning($"<b>Sound Maneger:</b> Music loading was canceled: {path}");
#endif
                        return null;
                    }
                    finally
                    {
                        _musicLoadingSemaphore.Release();
                    }
                }
                finally
                {
                    _isMusicLoading = false;
                }
            }
            
            // For SFX, we only need to protect token creation/disposal
            CancellationToken token;
            lock (_sfxTokenLock)
            {
                token = GetCancellationToken(false);
            }
            
            try
            {
                return await _musicLoader.GetMusicAsync(requester, path, token);
            }
            catch (OperationCanceledException)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"<b>Sound Maneger:</b> SFX loading was canceled: {path}");
#endif
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
            if (_mode == PlayMode.NeedMusicSwitch && !IsMusicPlaying && !_isMusicLoading)
            {
                PlaylistTrackChange?.Invoke();
            }
        }
        
        private IEnumerator SfxCache(AudioClip clip)
        {
            if (_activeSfxCoroutines.TryGetValue(clip, out var existingCoroutine))
            {
                StopCoroutine(existingCoroutine);
                _activeSfxCoroutines.TryRemove(clip, out _);
            }

            _activeSfx.Add(clip);
            _activeSfxCoroutines[clip] = StartCoroutine(SfxCacheInternal(clip));
            
            yield break;
        }

        private IEnumerator SfxCacheInternal(AudioClip clip)
        {
            try
            {
                yield return new WaitForSecondsRealtime(clip.length);
            }
            finally
            {
                _activeSfx.TryTake(out _);
                _activeSfxCoroutines.TryRemove(clip, out _);
            }
        }

        private bool IsSoundPlayingAvailable(AudioClip audioClip)
        {
            string clipName = audioClip.name;
            float currentTime = Time.unscaledTime;

            var times = _limitedSfxTimings.GetOrAdd(clipName, _ => new ConcurrentQueue<float>(new[] { currentTime }));
            
            // If we have less sounds than maximum allowed, add new timing
            if (times.Count < _maxSameSoundsCount + 1)
            {
                times.Enqueue(currentTime);
                return true;
            }

            // Check if oldest sound has finished playing
            if (times.TryPeek(out float oldestTime))
            {
                if (currentTime - oldestTime < audioClip.length)
                    return false;
                
                // Remove oldest time and add new one
                times.TryDequeue(out _);
                times.Enqueue(currentTime);
                return true;
            }

            return false;
        }

        protected virtual void OnDestroy() => Dispose();

        public void Dispose()
        {
            if (_isDisposed) return;
            
            _isDisposed = true;

            foreach (var coroutine in _activeSfxCoroutines.Values)
            {
                if (coroutine != null)
                    StopCoroutine(coroutine);
            }
            _activeSfxCoroutines.Clear();
            
            while (_activeSfx.TryTake(out _)) { }
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

        private void PrepareNextTrack()
        {
            lock (_playlistLock)
            {
                if (_playlist == null || _playlist.Count == 0) return;

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
                        if (_playlistPlayingOrder == PlayingOrder.Shuffle && _playlist.Count > 1)
                        {
                            var lastTrack = _playlist.Last();
                            do
                            {
                                _playlist.Shuffle();
                            } while (_playlist.First() == lastTrack);
                        }
                    }
                }
            }

            CacheNextTrack(GetCancellationToken(true));
        }
    }
}