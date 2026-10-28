# Sound Maneger

Just a neat sound manager for Unity: separate music and SFX channels, crossfaded playlists, ducking, async clip loading and some other handy features.

The package is supported from **Unity 6.0.81** (6000.0.81f1) and has dependencies managed automatically with `OpenUPM`. More information is in [Dependencies](#dependencies) section.

- [Features](#features)
- [Inspector](#inspector)
- [Mixer](#mixer)
- [Dependencies](#dependencies)
- [Installation](#installation)
- [Usage](#usage)
  - [Scripting](#scripting)
  - [Loading clips](#loading-clips)
  - [Your own mixer](#your-own-mixer)
- [Why Preview?](#why-preview)
- [Repository info](#repository-info)

## Features

- Music on two sources crossfaded through `AudioMixer` snapshots, so every track change is smooth.
- Playlists in default, random, or shuffled order (random and shuffle never play the same track twice in a row), with an optional intro track that plays only once. The next track is preloaded while the current one plays, and a track that fails to load is skipped.
- Sound effects that can duck the music, or the music and all other sound effects, with a limit of how many copies of the same clip play at once.
- Voice lines that duck the music by themselves.
- Lowpass filter for the music (pause menus, underwater, muffled scenes).
- Master, music, and SFX volume in the `0..1` range, and separate mute for music and SFX.
- Pause support: the manager's audio can keep playing while `AudioListener.pause` is set, or pause with the rest of the game.
- Async loading by path through `IClipLoader`: from `Resources` by default, from `Addressables` when the package is installed, or with your own loader. Failed music loads can be retried after a delay, and a newer call cancels the loads it makes useless.
- A ready-to-use prefab and mixer, a `ManeStyle` inspector, and a singleton that sets itself up when there is no prefab in the scene.

## Inspector

![](https://raw.githubusercontent.com/wiki/ManeFunction/sound-maneger/inspector.png)

## Mixer

![](https://raw.githubusercontent.com/wiki/ManeFunction/sound-maneger/mixer.png)

## Dependencies

![](https://raw.githubusercontent.com/wiki/ManeFunction/sound-maneger/dependencies.png)

[ManeTools-Unity](https://github.com/ManeFunction/ManeTools-Unity.git) and [ManeTools-dotNET](https://github.com/ManeFunction/ManeTools-dotNet.git) are required, and `OpenUPM` installs both.
[Addressables](https://docs.unity3d.com/Manual/com.unity.addressables.html) is optional and turns on when that package is in the project.

## Installation

I recommend installing this package with the `OpenUPM` CLI. It keeps dependencies and updates easy to manage. If you cannot use `OpenUPM`, download the package and place it anywhere in your Unity project.

Setting up `OpenUPM` for the first time takes a few minutes, but it is worth it. `OpenUPM` is the usual registry for open-source Unity packages and works with Unity’s Package Manager (dependency resolution and updates included).

On Windows, I recommend `Git Bash` (`MINGW`) for CLI work: it is a Unix-like shell, and it is often already installed.

1. Install `OpenUPM` (skip this if you already have it):
   - If you do not have `npm`, install [Node.js](https://nodejs.org) (or on macOS: `brew install node`).
   - In a terminal, run: `npm install -g openupm-cli`.
   - You can then install packages from the `OpenUPM` registry with no extra Unity setup.
1. Install this package:
   - Open a terminal in your Unity project folder: `cd /path/to/your/project`.
   - Run: `openupm add com.manefunction.sound-maneger`.
   - Switch back to Unity and wait for the package to finish importing.

## Usage

Drag the `SoundManeger` prefab from `Packages/Sound Maneger/Runtime/Resources` into your first scene, and you are good. It survives scene loads, and a copy in a scene that is loaded later destroys itself. Keep it a root object: Unity can only keep root objects between scenes.

Or use `GameObject -> Add SoundManeger` (also in the Hierarchy context menu). It adds the prefab as a root object to the scene of the selected object, or to the active scene when nothing is selected.

The prefab has its own `AudioListener`. Remove the one on your camera, or the prefab one if the listener has to follow the camera.

You can also add the component from `Add Component -> Mane Tools -> Audio -> Sound Maneger`. It picks up the package mixer and creates its audio sources by itself when none are assigned. The same happens when you access `SoundManeger.Instance` with no manager in the scene.

### Scripting

```csharp
using Mane.Unity.SoundManeger;

SoundManeger sound = SoundManeger.Instance;

// Music
sound.PlayMusic(menuTheme);                       // a looped track, crossfaded from the current one
sound.PlayMusic("Music/Menu");                    // loaded by path, then played the same way
sound.PlayPlaylist(new[] { "Music/Intro",         // the intro plays once,
    "Music/Level1", "Music/Level2" }, true);      // then the playlist
sound.PlayPlaylist(new[] { "Music/A", "Music/B", "Music/C" },
    playingOrder: SoundManeger.PlayingOrder.Shuffle);   // this playlist is shuffled
sound.PlaylistPlayingOrder = SoundManeger.PlayingOrder.Random;   // for playlists that set no order
sound.StopMusic();

// Sound effects and voice
sound.PlaySfx(clickClip);
sound.PlaySfx("Sfx/Explosion", SoundManeger.DuckType.MusicOnly);
sound.PlayVoice("Voice/Hello", this);             // the clip belongs to this object

// Mixing
sound.Lowpass(true);
sound.MasterVolume = 0.8f;                        // 0..1, 0.8 is 0 dB
sound.BgmVolume = 0.6f;
sound.SfxVolume = 1f;
sound.MuteMusic = true;                           // the volume is kept for unmute
sound.AudioPaused = true;                         // AudioListener.pause
sound.ContinueAudioOnPause = false;               // the manager pauses too
```

The calls that load by path (`PlayMusic(path)`, `PlayPlaylist`, `PlaySfx(path)` and `PlayVoice(path)`) return a `Task<bool>`. Call them and forget, as above, or await the result: `true` once the clip plays, `false` if it failed to load, a newer music call replaced it, or it could not play. Failures are logged either way.

```csharp
if (!await sound.PlayMusic("Music/Boss"))
    sound.PlayMusic(fallbackTheme);
```

Music calls take effect in the order you make them: a track or playlist that finishes loading after a newer `PlayMusic`, `PlayPlaylist` or `StopMusic` call is dropped, and the newer call cancels its loading.

Music loaded by path is released by the manager once the track stops. Sound effects and voice take an optional `owner`, the object the loaded clip belongs to. It matters for the Addressables loader, which keeps a clip loaded until its owner is destroyed. Pass `this` from the object whose lifetime matches the sound, or leave it out to give the clip to the manager, which keeps it loaded while the manager lives (good for sounds you play all the time).

`PlaylistTrackChange` is raised every time a playlist track ends and the next one starts.

### Loading clips

By default, clips are loaded from `Resources` folders: a path is relative to a `Resources` folder and has no file extension, like `Music/Menu` for `Assets/Resources/Music/Menu.ogg`.

To load clips with Addressables, install `com.unity.addressables` and switch the loader once, before you play anything by path:

```csharp
using Mane.Unity.SoundManeger.Addressables;

SoundManeger.Instance.SetClipLoader(new AddressablesClipLoader());
```

The Addressables loader lives in its own assembly, `manefunction.sound-maneger.addressables`, which compiles only when the Addressables package is installed. If your code has an assembly definition, reference it there. Paths are then Addressables addresses. Music is released once it stops playing, and a sound effect or voice clip stays in memory until its owner (or the manager) is destroyed. A music track that fails to load is retried after 5 seconds; pass another delay to the constructor, like `new AddressablesClipLoader(2f)`, or 0 to turn retries off. Sound effects and voice are not retried, because a sound that plays late is worse than none.

For any other source, implement `IClipLoader`: `LoadClipAsync(owner, path, token)` returns the clip (or null), `Release(clip)` frees a clip loaded without an owner, which is how the manager loads music (it does nothing by default), and `RetryDelay` tells the manager how many seconds to wait before it retries a failed music load (0 by default, which means no retries).

### Your own mixer

The package mixer works out of the box. If you make your own (or copy and change the package one), it needs:

- Snapshots `Music1` and `Music2`, where only that music source is audible, plus `Music1Lowpass` and `Music2Lowpass` for the lowpass filter.
- Exposed parameters `MasterVolume`, `BgmVolume`, and `SfxVolume`.
- Groups `Master/BGM/MusicAdd`, `Master/SFX/Effects`, `Master/SFX/FadeBgm`, and `Master/SFX/FadeAll`, if you use the matching group properties or let the manager create its own sources (it also uses `Master/BGM/Music1` and `Master/BGM/Music2`).

Assign it to the manager, or name it `AudioMixer` and put it in a `Resources` folder: it is then used instead of the package one by every manager that has no mixer assigned.

Volumes from `0` to `1` map linearly to -80..+20 dB, so `0.8` keeps a group at 0 dB.

## Why Preview?

The code itself is not new, but moving it to Unity 6 and to the new ManeTools packages, plus the pile of fixes that came along, is a great way to invent fresh bugs. Let's give it a little time to surface whatever I missed. Use at your own risk, I suppose.

## Repository info

This repo follows the [Conventional Commits](https://www.conventionalcommits.org/) specification (though not from the start).

[![GitHub Sponsors](https://img.shields.io/github/sponsors/ManeFunction?label=Sponsor&logo=GitHubSponsors&style=flat)](https://github.com/sponsors/ManeFunction)
[![openupm](https://img.shields.io/npm/v/com.manefunction.sound-maneger?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.manefunction.sound-maneger/)
[![openupm](https://img.shields.io/badge/dynamic/json?color=brightgreen&label=downloads&query=%24.downloads&suffix=%2Fmonth&url=https%3A%2F%2Fpackage.openupm.com%2Fdownloads%2Fpoint%2Flast-month%2Fcom.manefunction.sound-maneger)](https://openupm.com/packages/com.manefunction.sound-maneger/)
