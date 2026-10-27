using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mane.DotNet;
using UnityEngine;
using UnityEngine.Audio;

namespace Mane.Unity.SoundManeger
{
    /// <summary>
    /// Audio manager singleton. Music plays on two sources crossfaded by <see cref="AudioMixer"/> snapshots,
    /// as a single looped track or a playlist. Sound effects can duck the music or all other sounds,
    /// with a limit of simultaneous copies of the same clip. Clips are passed directly or loaded by path
    /// through an <see cref="IClipLoader"/> (<see cref="ResourcesClipLoader"/> by default).
    /// Put the package prefab into your first scene, or let <see cref="UnitySingleton{T}.Instance"/>
    /// create a manager with default sources.
    /// </summary>
    [ManeStyle]
    [AddComponentMenu("Mane Tools/Audio/Sound Maneger")]
    [DisallowMultipleComponent]
    // Set up before other scripts, so they can play audio from their own Awake()
    [DefaultExecutionOrder(-1000)]
    public class SoundManeger : UnitySingleton<SoundManeger>
    {
        /// <summary>
        /// Order of the tracks in a playlist.
        /// </summary>
        public enum PlayingOrder
        {
            /// <summary>
            /// Tracks play in the given order.
            /// </summary>
            Default,

            /// <summary>
            /// Every next track is picked at random, never the same one twice in a row.
            /// </summary>
            Random,

            /// <summary>
            /// The playlist is shuffled after every cycle, and a cycle never starts with the track that ended the last one.
            /// </summary>
            Shuffle
        }

        /// <summary>
        /// Which sounds a sound effect ducks (turns down) while it plays.
        /// </summary>
        public enum DuckType
        {
            /// <summary>
            /// No ducking.
            /// </summary>
            None = 0,

            /// <summary>
            /// Ducks the music.
            /// </summary>
            MusicOnly = 1,

            /// <summary>
            /// Ducks the music and the other sound effects.
            /// </summary>
            AllSources = 2,
        }

        // The package mixer, in the package's Resources folder
        private const string DefaultMixerPath = "SoundManeger/AudioMixer";


        [SerializeField] private AudioMixer _mixer;
        [SerializeField] private AudioListener _listener;

        [Foldout("Sources")]
        [SerializeField] private AudioSource _musicSource1;
        [SerializeField] private AudioSource _musicSource2;
        [SerializeField] private AudioSource _sfxSource;
        [SerializeField] private AudioSource _duckBgmSource;
        [SerializeField] private AudioSource _duckAllSource;
        
        [Header("Settings")]
        [Tooltip("Crossfade time in seconds between music tracks.")]
        [SerializeField] private float _transitionTime = 1.5f;
        [Tooltip("Time in seconds to turn the music lowpass on or off.")]
        [SerializeField] private float _lowpassTime = 0.4f;
        [Tooltip("Order of playlist tracks, unless a PlayPlaylist call sets another one for its playlist.")]
        [SerializeField] private PlayingOrder _playlistPlayingOrder = PlayingOrder.Default;
        [Tooltip("How many copies of the same sound effect can play at once.")]
        [SerializeField] private int _maxSameSoundsCount = 3;
        [Tooltip("Keep the manager's audio playing while AudioListener.pause is set.")]
        [SerializeField] private bool _continueAudioOnPause = true;

        [Header("Optimization")]
        [Tooltip("How often, in seconds, the manager forgets sound effects that have finished playing.")]
        [SerializeField] private float _sfxTimingsCleanupThreshold = 60f;
        [Tooltip("How many different sound effect clips are protected from unloading while they play.")]
        [SerializeField] private int _maxConcurrentSfx = 100;
        [Tooltip("How long, in seconds, a music load waits for the one before it.")]
        [SerializeField] private float _loadingTimeout = 30f;

        
        private AudioMixerSnapshot _music1Snapshot;
        private AudioMixerSnapshot _music2Snapshot;
        private AudioMixerSnapshot _music1LowSnapshot;
        private AudioMixerSnapshot _music2LowSnapshot;

        // Set here, not in Awake(), so a SetClipLoader() call made before Awake() is kept
        private IClipLoader _clipLoader = new ResourcesClipLoader();
        // Own random source, so playlists do not change UnityEngine.Random or a seeded global random.
        private readonly IRandom _random = new ManeRandom();
        // Music loads one at a time. All the manager's code runs on the main thread, so this orders async loads,
        // it does not guard against other threads
        private readonly SemaphoreSlim _musicLoadingSemaphore = new(1, 1);
        // Music loads in progress, the playlist waits for them before it changes the track
        private int _musicLoads;
        // Bumped by every PlayMusic and StopMusic call, see StartMusicRequest()
        private int _musicRequest;
        private UnityCancellationTokenSource _sfxCancellationSource;
        private UnityCancellationTokenSource _musicCancellationSource;

        private Coroutine _transitionAwaiter;

        /// <summary>
        /// Raised when the current playlist track has ended and the next one starts.
        /// </summary>
        public event Action PlaylistTrackChange;


        // The running playlist, null when none plays
        private Playlist _playlist;
        private AudioClip _nextPlaylistTrack;
        // Music clips loaded by path, with the loader to release them with (a clip loaded twice is listed twice).
        // One is released when a music source or the preloaded track lets it go, or when nothing takes it, see ReleaseMusic()
        private readonly List<(AudioClip clip, IClipLoader loader)> _loadedMusic = new();

        private bool _activeFirstMusicSource;
        private bool _lowpass;

        private bool _muteMusic;
        private bool _muteSfx;
        private float _cachedBgmVolume = .8f;
        private float _cachedSfxVolume = .8f;

        private readonly SfxLimiter _sfxLimiter = new();
        private float _lastCleanupTime;

        private bool _isDestroyed;

        /// <summary>
        /// Whether the manager's music, sound effects and voice keep playing while <see cref="AudioListener.pause"/>
        /// is set (for example, with <see cref="AudioPaused"/>). <see cref="Time.timeScale"/> never affects them.
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
        /// Pauses or unpauses the game audio through <see cref="AudioListener.pause"/>, separately from
        /// <see cref="Time.timeScale"/>. The manager's own sources keep playing while <see cref="ContinueAudioOnPause"/> is on.
        /// </summary>
        public bool AudioPaused
        {
            get => AudioListener.pause;
            set => AudioListener.pause = value;
        }

        /// <summary>
        /// Registers the manager (a copy loaded later destroys itself), looks up the mixer snapshots
        /// and creates the audio sources if none are assigned. Overrides should call <c>base.Awake()</c> first.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (Instance != this)
                return;

            FillMixerStuff();

            // The volumes restored on unmute start from the mixer
            if (TryGetVolume("BgmVolume", out float bgmVolume))
                _cachedBgmVolume = bgmVolume;
            if (TryGetVolume("SfxVolume", out float sfxVolume))
                _cachedSfxVolume = sfxVolume;

            CreateMissingSources();

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
            // Your own mixer in a Resources folder first, then the one from this package
            if (_mixer == null)
                _mixer = Resources.Load<AudioMixer>("AudioMixer");
            if (_mixer == null)
                _mixer = Resources.Load<AudioMixer>(DefaultMixerPath);

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

        // A manager without any source assigned (for example, the one Instance creates when the scene has none)
        // gets its own sources, routed like the ones in the prefab
        private void CreateMissingSources()
        {
            if (_musicSource1 != null || _musicSource2 != null || _sfxSource != null ||
                _duckBgmSource != null || _duckAllSource != null)
                return;

            _musicSource1 = CreateSource("Master/BGM/Music1");
            _musicSource2 = CreateSource("Master/BGM/Music2");
            _sfxSource = CreateSource("Master/SFX/Effects");
            _duckBgmSource = CreateSource("Master/SFX/FadeBgm");
            _duckAllSource = CreateSource("Master/SFX/FadeAll");
        }

        private AudioSource CreateSource(string groupPath)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = FindGroup(groupPath);

            return source;
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

        /// <summary>
        /// Mixer the manager plays through. Null when it is missing or has no <c>Music1</c> / <c>Music2</c> snapshots.
        /// </summary>
        public AudioMixer Mixer => _mixer;

        /// <summary>
        /// The <see cref="AudioListener"/> of the manager prefab, or null if none is assigned.
        /// </summary>
        public AudioListener Listener => _listener;

        /// <summary>
        /// Mixer group (<c>Master/BGM/MusicAdd</c>) for your own music sources, so they follow the music volume.
        /// Null without a mixer or such a group.
        /// </summary>
        public AudioMixerGroup MusicGroup => FindGroup("Master/BGM/MusicAdd");

        /// <summary>
        /// Mixer group (<c>Master/SFX/Effects</c>) for your own sound effect sources.
        /// Null without a mixer or such a group.
        /// </summary>
        public AudioMixerGroup SfxGroup => FindGroup("Master/SFX/Effects");

        /// <summary>
        /// Mixer group (<c>Master/SFX/FadeBgm</c>) for your own sources that duck the music, like <see cref="DuckType.MusicOnly"/>.
        /// Null without a mixer or such a group.
        /// </summary>
        public AudioMixerGroup SfxDuckMusicGroup => FindGroup("Master/SFX/FadeBgm");

        /// <summary>
        /// Mixer group (<c>Master/SFX/FadeAll</c>) for your own sources that duck the music and the other sound effects,
        /// like <see cref="DuckType.AllSources"/>. Null without a mixer or such a group.
        /// </summary>
        public AudioMixerGroup SfxDuckAllGroup => FindGroup("Master/SFX/FadeAll");

        private AudioMixerGroup FindGroup(string path) =>
            _mixer != null ? _mixer.FindMatchingGroups(path).FirstOrDefault() : null;


        /// <summary>
        /// Crossfade time in seconds between music tracks. A playlist track starts after the previous one has ended,
        /// so for it this is a fade-in.
        /// </summary>
        public float TransitionTime
        {
            get => _transitionTime;
            set => _transitionTime = value;
        }

        /// <summary>
        /// Order of playlist tracks for <see cref="PlayPlaylist"/> calls that do not set one. Starts with the inspector's value.
        /// A change applies to the next playlist, not to the one that plays.
        /// </summary>
        public PlayingOrder PlaylistPlayingOrder
        {
            get => _playlistPlayingOrder;
            set => _playlistPlayingOrder = value;
        }

        /// <summary>
        /// Indicates that music is playing.
        /// </summary>
        public bool IsMusicPlaying => _musicSource1.isPlaying || _musicSource2.isPlaying;
        
        /// <summary>
        /// Mutes the music, keeping <see cref="BgmVolume"/>. Playlist tracks do not change while the music is muted.
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
        /// Mutes the sound effects and voice, keeping <see cref="SfxVolume"/>.
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
        /// Master volume from 0 to 1, mapped linearly to -80..+20 dB in the mixer (0.8 is 0 dB). Reads 0 without a mixer.
        /// </summary>
        public float MasterVolume
        {
            get => GetVolume("MasterVolume");
            set => SetVolume("MasterVolume", value);
        }

        /// <summary>
        /// Music volume from 0 to 1, mapped linearly to -80..+20 dB in the mixer (0.8 is 0 dB).
        /// Keeps its value while <see cref="MuteMusic"/> is on and applies again on unmute.
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
        /// Sound effects and voice volume from 0 to 1, mapped linearly to -80..+20 dB in the mixer (0.8 is 0 dB).
        /// Keeps its value while <see cref="MuteSfx"/> is on and applies again on unmute.
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
        /// Sets the loader for every clip loaded by path (music, sound effects and voice).
        /// <see cref="ResourcesClipLoader"/> is used by default; use <c>AddressablesClipLoader</c>
        /// (available with the Addressables package) or your own <see cref="IClipLoader"/>.
        /// Music loaded before is still released through the loader that loaded it.
        /// </summary>
        /// <param name="clipLoader">Loader to use. Null is ignored.</param>
        public void SetClipLoader(IClipLoader clipLoader)
        {
            if (clipLoader == null) return;

            _clipLoader = clipLoader;
        }


        /// <summary>
        /// Turns the lowpass filter on the music on or off, over the inspector's lowpass time. It stays on across track changes.
        /// Does nothing when the mixer has no <c>Music1Lowpass</c> / <c>Music2Lowpass</c> snapshots.
        /// </summary>
        /// <param name="enable">True to turn the lowpass on.</param>
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
        /// Plays a single looped music track, crossfading from the current one, and stops a running playlist.
        /// Cancels music that is still loading from earlier calls.
        /// </summary>
        /// <param name="clip">Track to play. Null does nothing.</param>
        public void PlayMusic(AudioClip clip)
        {
            if (clip == null) return;

            StartMusicRequest();
            PlaySingleTrack(clip);
        }

        /// <summary>
        /// Loads and plays a single looped music track, like <see cref="PlayMusic(AudioClip)"/>.
        /// Cancels music that is still loading from earlier calls. If the track fails to load, the music stays as it was.
        /// The clip is released through the loader once the track stops.
        /// </summary>
        /// <param name="path">Path of the track for the current <see cref="IClipLoader"/>.</param>
        /// <returns>True once the track plays. False if it failed to load, a newer music call replaced it,
        /// or the music is disabled (no mixer).</returns>
        public Task<bool> PlayMusic(string path) =>
            LoadSingleTrack(StartMusicRequest(), path);

        /// <summary>
        /// Plays a playlist, or a single looped track when it has one entry.
        /// Cancels music that is still loading from earlier calls. The next track is preloaded while the current one plays,
        /// a track that fails to load is skipped, and every track is released through the loader once it stops.
        /// </summary>
        /// <param name="tracks">Paths of the tracks. Null or empty does nothing.</param>
        /// <param name="firstTrackIsIntro">If true, the first entry plays once before the others.
        /// Otherwise it is just the first entry (with Random or Shuffle order it can play at any position).</param>
        /// <param name="playingOrder">Order of the tracks of this playlist only, see <see cref="PlayingOrder"/>.
        /// If null, <see cref="PlaylistPlayingOrder"/> is used.</param>
        /// <returns>True once the intro, the first track or the only track plays. False if the list is null or empty,
        /// a newer music call replaced it, the music is disabled (no mixer), or the first track failed to load
        /// (the playlist then still moves on to the next one).</returns>
        public async Task<bool> PlayPlaylist(IReadOnlyList<string> tracks, bool firstTrackIsIntro = false,
            PlayingOrder? playingOrder = null)
        {
            // empty call
            if (tracks == null || tracks.Count == 0) return false;

            PlayingOrder order = playingOrder ?? _playlistPlayingOrder;
            int request = StartMusicRequest();

            // single track
            if (tracks.Count == 1)
                return await LoadSingleTrack(request, tracks[0]);

            // playlist, copied before the intro loads, so a caller that changes its list later does not change it
            if (firstTrackIsIntro)
                return await StartPlaylistAfterIntro(request, tracks[0], tracks.Skip(1).ToList(), order);

            return await StartPlaylist(tracks, order, false);
        }

        /// <summary>
        /// Stops the music and the playlist at once (no fade-out), and cancels music that is still loading.
        /// </summary>
        public void StopMusic()
        {
            StartMusicRequest();
            _musicSource1.Stop();
            _musicSource2.Stop();
            ClearPlaylist();

            // Release the loaded clips and clear the references to allow GC
            SetMusicClip(_musicSource1, null);
            SetMusicClip(_musicSource2, null);
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

        private bool PlaySingleTrack(AudioClip clip)
        {
            _musicSource1.loop = true;
            _musicSource2.loop = true;
            ClearPlaylist();

            return PlayMusicClip(clip);
        }

        private async Task<bool> LoadSingleTrack(int request, string path)
        {
            AudioClip clip = await GetClip(path, true);
            if (clip == null) return false;

            if (request == _musicRequest)
                return PlaySingleTrack(clip);

            ReleaseMusic(clip);
            return false;
        }

        // The intro plays once and the playlist starts after it. If the intro does not play, the playlist starts at once.
        private async Task<bool> StartPlaylistAfterIntro(int request, string intro, List<string> tracks,
            PlayingOrder order)
        {
            AudioClip clip = await GetClip(intro, true);
            if (request != _musicRequest)
            {
                ReleaseMusic(clip);
                return false;
            }

            bool introPlays = clip != null && PlaySingleTrack(clip);

            return await StartPlaylist(tracks, order, introPlays);
        }


        // True once the first track plays, or at once when it starts after the current track
        private Task<bool> StartPlaylist(IEnumerable<string> tracks, PlayingOrder order, bool startAfterCurrentTrack)
        {
            // Music is disabled without a mixer, see FillMixerStuff()
            if (_mixer == null) return Task.FromResult(false);

            // Drops the previous playlist with its preloaded track
            ClearPlaylist();
            // Update() moves on from here, even if the first track fails to load
            _playlist = new Playlist(tracks, order, _random);
            _musicSource1.loop = false;
            _musicSource2.loop = false;

            if (!startAfterCurrentTrack)
                return ChangeTrack();

            CacheNextTrack();
            return Task.FromResult(true);
        }

        // Preloads the current playlist track, to play it with the next ChangeTrack()
        private async void CacheNextTrack()
        {
            Playlist playlist = _playlist;
            // An empty path fails when ChangeTrack() loads it, one error is enough
            if (playlist == null || string.IsNullOrEmpty(playlist.Current)) return;

            int request = _musicRequest;
            AudioClip clip = await GetClip(playlist.Current, true);
            if (request != _musicRequest || playlist != _playlist)
            {
                ReleaseMusic(clip);
                return;
            }

            // A track preloaded before is let go
            AudioClip previous = _nextPlaylistTrack;
            _nextPlaylistTrack = clip;
            ReleaseMusic(previous);
        }

        private async void PlayNextTrack() => await ChangeTrack();

        // Plays the preloaded track, or loads the current one, then preloads the one after it. True once a track plays
        private async Task<bool> ChangeTrack()
        {
            Playlist playlist = _playlist;
            if (playlist == null) return false;

            AudioClip clip = _nextPlaylistTrack;
            _nextPlaylistTrack = null;
            if (!clip)
            {
                // A preloaded clip that was destroyed is still listed in _loadedMusic
                ReleaseMusic(clip);

                int request = _musicRequest;
                clip = await GetClip(playlist.Current, true);
                if (request != _musicRequest || playlist != _playlist)
                {
                    ReleaseMusic(clip);
                    return false;
                }

                if (clip == null)
                {
                    // The next Update() tries the track after it. When none of the tracks loads, the playlist stops
                    if (!playlist.SkipFailedTrack())
                    {
                        Debug.LogError("[SoundManeger] None of the playlist tracks could be loaded, the playlist is stopped.");
                        ClearPlaylist();
                    }

                    return false;
                }
            }

            playlist.TrackLoaded();
            bool plays = PlayMusicClip(clip);
            playlist.MoveNext();
            CacheNextTrack();
            return plays;
        }

        private void ClearPlaylist()
        {
            AudioClip next = _nextPlaylistTrack;
            _playlist = null;
            _nextPlaylistTrack = null;
            ReleaseMusic(next);
        }


        /// <summary>
        /// Plays a sound effect, unless the same clip already plays <c>Max Same Sounds Count</c> times.
        /// The clip is protected from <see cref="Resources.UnloadUnusedAssets"/> until it ends.
        /// </summary>
        /// <param name="clip">Sound effect to play. Null does nothing.</param>
        /// <param name="duck">What the sound effect ducks. Without the matching duck source it plays without ducking.</param>
        public void PlaySfx(AudioClip clip, DuckType duck = DuckType.None) => TryPlaySfx(clip, duck);

        // True when the sound effect plays
        private bool TryPlaySfx(AudioClip clip, DuckType duck)
        {
            if (_mixer == null || clip == null || _sfxSource == null ||
                !_sfxLimiter.TryPlay(clip, Time.unscaledTime, _maxSameSoundsCount, _maxConcurrentSfx))
                return false;

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

            return true;
        }

        /// <summary>
        /// Loads and plays a sound effect, like <see cref="PlaySfx(AudioClip, DuckType)"/>. A failed load is not retried.
        /// </summary>
        /// <param name="path">Path of the sound effect for the current <see cref="IClipLoader"/>.</param>
        /// <param name="duck">What the sound effect ducks.</param>
        /// <param name="owner">Owner of the loaded clip: <c>AddressablesClipLoader</c> keeps it loaded until the owner is destroyed.
        /// If null, the manager owns it, so it stays loaded while the manager lives.</param>
        /// <returns>True once the sound effect plays. False if it failed to load, or is not played: the same clip
        /// already plays <c>Max Same Sounds Count</c> times, or there is no mixer or SFX source.</returns>
        public async Task<bool> PlaySfx(string path, DuckType duck = DuckType.None, MonoBehaviour owner = null) =>
            TryPlaySfx(await GetClip(path, false, owner), duck);

        /// <summary>
        /// Stops all sound effects, and the voice too, because it plays on the music-ducking source.
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
        /// Plays a voice line on the music-ducking source, so the music fades out while it plays, depending on its volume.
        /// A new voice line replaces the playing one. Does nothing without the duck BGM source.
        /// </summary>
        /// <param name="clip">Voice clip to play. Null does nothing.</param>
        /// <param name="delay">Delay in seconds before it starts.</param>
        public void PlayVoice(AudioClip clip, float delay = 0) => TryPlayVoice(clip, delay);

        // True when the voice line plays
        private bool TryPlayVoice(AudioClip clip, float delay)
        {
            if (clip == null || _duckBgmSource == null)
                return false;

            _duckBgmSource.clip = clip;
            if (delay > 0)
                _duckBgmSource.PlayDelayed(delay);
            else
                _duckBgmSource.Play();

            return true;
        }

        /// <summary>
        /// Loads and plays a voice line, like <see cref="PlayVoice(AudioClip, float)"/>. A failed load is not retried.
        /// </summary>
        /// <param name="path">Path of the voice clip for the current <see cref="IClipLoader"/>.</param>
        /// <param name="owner">Owner of the loaded clip: <c>AddressablesClipLoader</c> keeps it loaded until the owner is destroyed.
        /// If null, the manager owns it, so it stays loaded while the manager lives.</param>
        /// <returns>True once the voice line plays. False if it failed to load or there is no duck BGM source.</returns>
        public async Task<bool> PlayVoice(string path, MonoBehaviour owner = null) =>
            TryPlayVoice(await GetClip(path, false, owner), 0);
        
        /// <summary>
        /// Stops the voice, and the sound effects played with <see cref="DuckType.MusicOnly"/> too, because they share its source.
        /// </summary>
        public void StopVoice()
        {
            if (_duckBgmSource != null)
                _duckBgmSource.Stop();
        }

        
        // Takes over a loaded clip: it is released when its source lets it go, or at once when it does not play.
        // True when the clip plays
        private bool PlayMusicClip(AudioClip clip)
        {
            if (clip == null) return false;

            // No music without a mixer
            if (_mixer == null)
            {
                ReleaseMusic(clip);
                return false;
            }

            // The clip already plays on the active source: keep it playing
            if (_musicSource2.clip == clip && _musicSource2.isPlaying && !_activeFirstMusicSource ||
                _musicSource1.clip == clip && _musicSource1.isPlaying && _activeFirstMusicSource)
            {
                ReleaseMusic(clip);
                return true;
            }

            // The sources loop already, or not for a playlist, see PlaySingleTrack() and StartPlaylist()
            try
            {
                if (_activeFirstMusicSource)
                {
                    SetMusicClip(_musicSource2, clip);
                    _musicSource2.Play();
                    GetMusicSnapshot(false).TransitionTo(_transitionTime);
                    _activeFirstMusicSource = false;
                    this.TryKillCoroutine(ref _transitionAwaiter);
                    _transitionAwaiter = StartCoroutine(StopMusicSource(_musicSource1, _transitionTime));
                }
                else
                {
                    SetMusicClip(_musicSource1, clip);
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
                return false;
            }

            return true;
        }

        private IEnumerator StopMusicSource(AudioSource source, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);

            source.Stop();
            // The faded out track is no longer needed
            SetMusicClip(source, null);
            _transitionAwaiter = null;
        }

        // Puts a clip on a music source and releases the loaded clip it replaces
        private void SetMusicClip(AudioSource source, AudioClip clip)
        {
            AudioClip previous = source.clip;
            source.clip = clip;
            ReleaseMusic(previous);
        }

        // Gives a music clip loaded by path back to its loader. A clip passed directly is not listed, so it is left alone.
        // References are compared, so a destroyed clip is still found (Unity's == would match any destroyed clip)
        private void ReleaseMusic(AudioClip clip)
        {
            if (clip is null) return;

            int index = _loadedMusic.FindIndex(loaded => ReferenceEquals(loaded.clip, clip));
            if (index < 0) return;

            IClipLoader loader = _loadedMusic[index].loader;
            _loadedMusic.RemoveAt(index);
            loader.Release(clip);
        }

        // Music loads without an owner and is listed for ReleaseMusic(). Sound effects and voice belong to the owner,
        // or to the manager when there is none.
        private async Task<AudioClip> GetClip(string path, bool isMusic, MonoBehaviour owner = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[SoundManeger] Attempted to load clip with null or empty path!");
                return null;
            }

            if (_isDestroyed)
            {
                Debug.LogWarning("[SoundManeger] Attempted to load clip after the manager was destroyed!");
                return null;
            }

            try
            {
                if (isMusic)
                {
                    // Taken once, so a newer music request cancels the waiting and the retries of this load too
                    CancellationToken musicToken = GetCancellationToken(true);
                    // The clip is released through the loader that loaded it, even if another one is set later
                    IClipLoader loader = _clipLoader;
                    _musicLoads++;
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
                            float retryDelay = loader.RetryDelay;

                            do
                            {
                                clip = await loader.LoadClipAsync(null, path, musicToken);

                                if (clip != null || retryDelay <= 0f || musicToken.IsCancellationRequested)
                                    break;

                                Debug.LogWarning($"[SoundManeger] Failed to load music, retrying after {retryDelay} seconds: {path}");
                                await Task.Delay(TimeSpan.FromSeconds(retryDelay), musicToken);

                            } while (!_isDestroyed);

                            if (clip == null)
                            {
                                if (!musicToken.IsCancellationRequested)
                                    Debug.LogError($"[SoundManeger] Failed to load music clip at path: {path}");

                                return null;
                            }

                            // OnDestroy() has released the other clips already
                            if (_isDestroyed)
                            {
                                loader.Release(clip);
                                return null;
                            }

                            _loadedMusic.Add((clip, loader));
                            return clip;
                        }
                        finally
                        {
                            _musicLoadingSemaphore.Release();
                        }
                    }
                    finally
                    {
                        _musicLoads--;
                    }
                }

                CancellationToken token = GetCancellationToken(false);
                if (owner == null)
                    owner = this;

                // No retries: a sound effect or voice line played seconds late is worse than none
                AudioClip sfxClip = await _clipLoader.LoadClipAsync(owner, path, token);

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
                if (_sfxCancellationSource == null || _sfxCancellationSource.IsCancellationRequested)
                    CreateNewSfxCancellationSource();

                return _sfxCancellationSource.Token;
            }

            if (_musicCancellationSource == null || _musicCancellationSource.IsCancellationRequested)
                CreateNewMusicCancellationSource();

            return _musicCancellationSource.Token;
        }

        private void Update()
        {
            float currentTime = Time.unscaledTime;
            
            if (currentTime - _lastCleanupTime >= _sfxTimingsCleanupThreshold)
            {
                _sfxLimiter.Cleanup(currentTime);
                _lastCleanupTime = currentTime;
            }

            // The playlist track has ended. Tracks do not change while the music is muted, or while music loads
            if (_playlist != null && !_muteMusic && !IsMusicPlaying && _musicLoads == 0)
            {
                PlayNextTrack();
                PlaylistTrackChange?.Invoke();
            }
        }

        /// <summary>
        /// Clears the singleton instance, cancels pending loads, stops the playlist, releases the music loaded by path
        /// and forgets the sound effects bookkeeping. Overrides should call <c>base.OnDestroy()</c>.
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();

            _isDestroyed = true;

            _sfxLimiter.Clear();

            // Disposing a token source does not cancel it, so pending loads are canceled first
            _sfxCancellationSource?.Cancel();
            _sfxCancellationSource?.Dispose();
            _sfxCancellationSource = null;
            CancelMusicLoading();
            // The semaphore is not disposed: canceled loads still release it when they finish

            ClearPlaylist();

            // The music sources are not touched: on destroy they can be gone already
            foreach ((AudioClip clip, IClipLoader loader) in _loadedMusic)
                loader.Release(clip);
            _loadedMusic.Clear();
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
            if (_sfxCancellationSource != null)
            {
                _sfxCancellationSource.Cancel();
                _sfxCancellationSource.Dispose();
            }
            _sfxCancellationSource = new UnityCancellationTokenSource();
        }
    }
}