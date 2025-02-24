1.6.0:
    ! Package in a preview state due to significant changes requiring thorough testing.
    - Made SoundManager disposable.
    - Fixed possible memory leaks with SFX caching.
    - Fixed race conditions with SFX and playlist operations.
    - Simplified SFX caching logic with Queue instead of manually indexed list.
    - Drastically improved error handling and logging.

1.5.3:
    - Changed TaskCanceledException to OperationCanceledException on music loading to prevent uncontrolled exceptions flood.

1.5.2:
    - Fixed an issue when SFX downloads can be cancelled with some Music tracks.

1.5.0:
    - Added support of Cancellation Token when loading music.
    - Fixed some issues with playlists.

1.4.9:
    - Additional fix for game pause and SFX playing.

1.4.8:
    - Replaced WaitForSeconds to WaitForSecondsRealtime for proper work with game pause and other time magic.

1.4.7:
    - Do not try to load music via empty string anymore. 

1.4.5:
    - Added AddressableMusicLink component to allow assets releasing on owner object destroy.
    - Fixed some issues with new playlist modes.
    - Fixed an issue with tracks changing in some playlists.

1.4.0:
    - All music loading is now async.
    - Added ResourcesMusicLoader to load music from Resources folder.
    - Added AddressablesMusicLoader to load music from Addressables.
    - Added IMusicLoader interface to allow custom music loading.
    - Added feature to preload next music track when using playlist.
    - Added feature to start playlist with an opening track that play only once.
    - Added a lot of documentation summary notes.

1.3.2:
    - Updated Mane Tools dependency to 1.10.22.

1.3.1:
    - Sound Maneger component was placed under the Audio menu.

1.3.0:
    - Shuffled mode now shuffles the playlist after each cycle.
    - Added PlayMusic() overloads that allow to set a playing order.

1.2.1:
    - Reserealize main prefab to prevent it's constant changing in target projects.

1.2.0:
    - Improved same SFX suppression. Now it depends on the SFX length, and you control how many SFX you allow simultaneously.

1.1.5:
    - Fixed an issue caused music change in some cases if you are playing a single track after a playlist.

1.1.4:
    - Fixed an issue that prevent playlist song changing.
    - Fixed an issue that brake the right playlist order after the second PlayMusic execution.

1.1.3:
    - Fixed broken dependencies by the last commit, upped again to 1.6.0.

1.1.2:
    - Fixed Mane Tools dependencies, updated to the latest at the time (1.5.38).

1.1.1:
    - Added code to access default AudioListener.

1.1.0:
    - Added exposed properties for different audio channels and a Mixer.
    - Added custom music channel to connect your own music audio sources.

1.0.6:
    - Reserealized assets.

1.0.5:
    - Updated some Mane dependencies to made Sound Manager compatible with Utils 1.4.3 and higher.

1.0.4:
    - Added npm packages.

1.0.3:
    - Count this as an init public version of this tool :)