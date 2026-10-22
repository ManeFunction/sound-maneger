# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [2.0.0-preview.1] - 2026-10-22
Moved the package to Unity 6 and to my new tool packages: [ManeTools-dotNET](https://github.com/ManeFunction/ManeTools-dotNET.git) and [ManeTools-Unity](https://github.com/ManeFunction/ManeTools-Unity.git) replace the legacy `ManeUtils` dependency. Versioning jumps to 2.0.0 to match the rest of my packages (see my [GitHub](https://github.com/ManeFunction) for more useful Unity tools).

### Added
- Added dependencies to [ManeTools-dotNET](https://github.com/ManeFunction/ManeTools-dotNET.git) and [ManeTools-Unity](https://github.com/ManeFunction/ManeTools-Unity.git).
- The inspector uses the `ManeStyle` layout from ManeTools-Unity.

### Changed
- BREAKING CHANGE: Namespace was changed from `Mane.SoundManeger` to `Mane.Unity.SoundManeger`.
- BREAKING CHANGE: The assembly was renamed from `Mane.SoundManeger` to `manefunction.sound-maneger`. Assembly definitions that reference it by GUID keep working; references by name need the new name.
- `SoundManeger` is now a `UnitySingleton` from ManeTools-Unity instead of the legacy `MonoSingleton`. A manager in a scene that is loaded again destroys its own GameObject, and the first one keeps playing.
- Moved context "Add component" menu to `Mane Tools -> Audio -> Sound Maneger`.
- Shuffle and random playlist orders use the manager's own random source (`ManeRandom` from ManeTools-dotNET), so they no longer change the state of `UnityEngine.Random`.
- `UnityCancellationTokenSource` (internal) moved out of the `Addressables` folder, because it is not Addressables-specific.

### Removed
- Removed the legacy `com.manefunction.utils` dependency.
- Removed support for Unity versions older than the latest patch of the earliest Unity 6 LTS (6.0.81).

### Fixed
- `Max Same Sounds Count` was never applied: when the timings of a clip expired, its queue was removed and the new timing went into a queue nothing tracked, so every sound effect played. The limit is now kept per clip asset instead of per clip name, so two clips with the same name no longer share it, and a sound effect that is not played (no SFX source) is no longer counted.
- Playing sound effects were not reliably protected from `Resources.UnloadUnusedAssets()`. The cleanup compared the game time with twice the clip length, so from the first cleanup on (a minute in) it dropped the protection of every playing clip, a finished sound effect released a random clip instead of its own, and clips over the `Max Concurrent Sfx` limit were dropped at random. Each clip is now kept until its own end time, without a coroutine per sound effect.
- `StopSfx`, `PlayVoice` and `StopVoice` threw `NullReferenceException` when the optional duck sources were not assigned, although the manager only warns about them. Voice now does nothing without the duck BGM source, and a voice clip that failed to load no longer stops the voice that is playing.

## [1.6.1] - 2025-04-08
### Added
- Made `SoundManeger` disposable.
- Added separate options to pause music and SFX.

### Changed
- Moved the 'retry loading with delay' feature from `AddressablesMusicLoader` to the `SoundManeger` itself, so any `IMusicLoader` can use it.
- Moved the asset handle releasing code from the link to the loader, so any `IMusicLoader` can release its handles itself.
- Simplified the SFX caching logic with a `Queue` instead of a manually indexed list.
- Optimized the SFX timings queue and added a cleanup.
- Drastically improved error handling and logging.
- Optimized a `while` loop to a single `Clear()` call.
- Increased the minimum Unity version to 2021.2 because of new C# 9.0 features.

### Fixed
- Fixed possible memory leaks with SFX caching.
- Fixed race conditions with SFX and playlist operations.
- Fixed an issue when `owner` in `IMusicLoader.GetMusicAsync()` could be null.

## [1.5.3] - 2024-06-12
### Changed
- Changed `TaskCanceledException` to `OperationCanceledException` on music loading to prevent an uncontrolled exceptions flood.

## [1.5.2] - 2024-06-04
### Fixed
- Fixed an issue when SFX downloads could be cancelled with some music tracks.

## [1.5.0] - 2024-06-04
### Added
- Added support of a cancellation token when loading music.

### Fixed
- Fixed some issues with playlists.

## [1.4.9] - 2024-02-01
### Fixed
- Additional fix for game pause and SFX playing.

## [1.4.8] - 2024-01-09
### Changed
- Replaced `WaitForSeconds` with `WaitForSecondsRealtime` for proper work with game pause and other time magic.

## [1.4.7] - 2023-12-13
### Fixed
- Music is not loaded via an empty string anymore.

## [1.4.5] - 2023-12-07
### Added
- Added `AddressablesMusicLink` component to allow assets releasing on owner object destroy.

### Fixed
- Fixed some issues with the new playlist modes.
- Fixed an issue with tracks changing in some playlists.

## [1.4.0] - 2023-12-07
### Added
- Added `ResourcesMusicLoader` to load music from the `Resources` folder.
- Added `AddressablesMusicLoader` to load music from Addressables.
- Added `IMusicLoader` interface to allow custom music loading.
- Added a feature to preload the next music track when using a playlist.
- Added a feature to start a playlist with an opening track that plays only once.
- Added a lot of documentation summary notes.

### Changed
- All music loading is now async.

## [1.3.2] - 2023-12-01
### Changed
- Updated Mane Tools dependency to 1.10.22.

## [1.3.1] - 2023-07-11
### Changed
- Sound Maneger component was placed under the `Audio` menu.

## [1.3.0] - 2023-04-19
### Added
- Added `PlayMusic()` overloads that allow to set a playing order.

### Changed
- Shuffled mode now shuffles the playlist after each cycle.

## [1.2.1] - 2022-10-19
### Changed
- Reserialized the main prefab to prevent its constant changing in target projects.

## [1.2.0] - 2022-10-19
### Changed
- Improved same SFX suppression. Now it depends on the SFX length, and you control how many SFX you allow simultaneously.

## [1.1.5] - 2022-03-15
### Fixed
- Fixed an issue that caused a music change in some cases, if you play a single track after a playlist.

## [1.1.4] - 2022-01-22
### Fixed
- Fixed an issue that prevented playlist song changing.
- Fixed an issue that broke the right playlist order after the second `PlayMusic` execution.

## [1.1.3] - 2022-01-19
### Fixed
- Fixed dependencies broken by the last commit, upped Mane Tools again to 1.6.0.

## [1.1.2] - 2022-01-19
### Fixed
- Fixed Mane Tools dependencies, updated to the latest at the time (1.5.38).

## [1.1.1] - 2021-10-15
### Added
- Added code to access the default `AudioListener`.

## [1.1.0] - 2021-10-14
### Added
- Added exposed properties for different audio channels and a mixer.
- Added a custom music channel to connect your own music audio sources.

## [1.0.6] - 2021-04-02
### Changed
- Reserialized assets.

## [1.0.5] - 2021-03-30
### Changed
- Updated some Mane dependencies to make Sound Maneger compatible with Utils 1.4.3 and higher.

## [1.0.4] - 2020-08-17
### Added
- Added npm packages.

## [1.0.3] - 2020-03-25
### Added
- Count this as an init public version of this tool :)
