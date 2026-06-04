<table align="center">
    <tr>
        <td align="center" width="25%">
            <img src="https://raw.githubusercontent.com/Ryubing/Assets/refs/heads/main/RyujinxApp_1024.png" alt="Ryujinx" >
        </td>
        <td align="center" width="75%">
          
<h1 class="ryu-gradient-text">Ryujinx</h1>

[![Latest release](https://git.ryujinx.app/projects/Ryubing/badges/release.svg?label=stable&color=32cd32)](https://update.ryujinx.app/latest/stable)
[![Latest canary release](https://git.ryujinx.app/Ryubing/Canary/badges/release.svg?label=canary&color=FF4500)](https://update.ryujinx.app/latest/canary)
<br>
<a href="https://discord.gg/PEuzjrFXUA">
<img src="https://img.shields.io/discord/1294443224030511104?color=5865F2&label=Ryubing&logo=discord&logoColor=white" alt="Discord">
</a>
        </td>
    </tr>
</table>

<p align="center">
  Ryujinx is an open-source Nintendo Switch emulator, originally created by gdkchan, written in C#.
  This emulator aims at providing excellent accuracy and performance, a user-friendly interface and consistent builds.
  It was written from scratch and development on the project began in September 2017.
  Ryujinx is available on a self-managed <a class="forgejo-gradient-text" href="https://github.com/Ryubing/forgejo" target="_blank">modified Forgejo</a> instance under the <a href="https://git.ryujinx.app/projects/Ryubing/src/branch/master/LICENSE.txt" target="_blank">MIT license</a>.
  <br />
</p>
<p align="center">
  On October 1st 2024, Ryujinx was discontinued as the creator was forced to abandon the project.
  <br>
  This fork is intended to be a QoL uplift for existing Ryujinx users.
  <br>
  This is not a Ryujinx revival project. This is not a Phoenix project.
  <br>
  Guides and documentation can be found on the <a href="https://git.ryujinx.app/projects/Ryubing/wiki/Home">Wiki tab</a>.
</p>

<p align="center">
    <img src="https://git.ryujinx.app/projects/Ryubing/raw/branch/master/docs/shell.png" alt="Ryujinx example">
</p>

## Usage

To run this emulator, your PC must be equipped with at least:
- 8GiB of RAM 
- 6 cores
- A GPU released within the last 10 years.
- OpenGL 4.6 | Vulkan 1.4
- Windows 10 version 20H1 | macOS Big Sur (Apple Silicon)

Failing to meet these requirements may result in a poor gameplay experience or unexpected crashes.

## Latest build

Stable builds are made every so often, based on the `master` branch, that then gets put into the releases you know and love.
These stable builds exist so that the end user can get a more **enjoyable and stable experience**.
They are released every month or so, to ensure consistent updates, while not being an annoying amount of individual updates to download over the course of that month.

You can find the stable releases [here](https://git.ryujinx.app/projects/Ryubing/releases).

Canary builds are compiled automatically for each commit on the `master` branch.
While we strive to ensure optimal stability and performance prior to pushing an update, these builds **may be unstable or completely broken**.
These canary builds are only recommended for experienced users.

You can find the canary releases [here](https://git.ryujinx.app/Ryubing/Canary/releases).

## Documentation

If you are planning to contribute or just want to learn more about this project please read through our [documentation](https://git.ryujinx.app/projects/Ryubing/src/branch/master/docs/README.md).

## Features

- **Audio**

  Audio output is entirely supported, audio input (microphone) isn't supported.
  We use C# wrappers for [OpenAL](https://openal-soft.org/), and [SDL3](https://www.libsdl.org/) & [libsoundio](http://libsound.io/) as fallbacks.

- **CPU**

  The CPU emulator, ARMeilleure, emulates an ARMv8 CPU and currently has support for most 64-bit ARMv8 and some of the ARMv7 (and older) instructions, including partial 32-bit support.
  It translates the ARM code to a custom IR, performs a few optimizations, and turns that into x86 code.
  There are three memory manager options available depending on the user's preference, leveraging both software-based (slower) and host-mapped modes (much faster).
  The fastest option (host, unchecked) is set by default.
  Ryujinx also features an optional Profiled Persistent Translation Cache, which essentially caches translated functions so that they do not need to be translated every time the game loads.
  The net result is a significant reduction in load times (the amount of time between launching a game and arriving at the title screen) for nearly every game.
  NOTE: This feature is enabled by default in the Options menu > System tab.
  You must launch the game at least twice to the title screen or beyond before performance improvements are unlocked on the third launch!
  These improvements are permanent and do not require any extra launches going forward.

- **GPU**

  The GPU emulator emulates the Switch's Maxwell GPU using either the OpenGL (version 4.5 minimum), Vulkan, or Metal (via MoltenVK) APIs through a custom build of OpenTK or Silk.NET respectively.
  There are currently six graphics enhancements available to the end user in Ryujinx: Disk Shader Caching, Resolution Scaling, Anti-Aliasing, Scaling Filters (including FSR), Anisotropic Filtering and Aspect Ratio Adjustment.
  These enhancements can be adjusted or toggled as desired in the GUI.

- **Input**

  We currently have support for keyboard, mouse, touch input, Joy-Con input support, and nearly all controllers.
  Motion controls are natively supported in most cases; for dual-JoyCon motion support, DS4Windows or BetterJoy are currently required.
  In all scenarios, you can set up everything inside the input configuration menu.

- **DLC & Modifications**

  Ryujinx is able to manage add-on content/downloadable content through the GUI.
  Mods (romfs, exefs, and runtime mods such as cheats) are also supported;
  the GUI contains a shortcut to open the respective mods folder for a particular game.

- **Configuration**

  The emulator has settings for enabling or disabling some logging, remapping controllers, and more.
  You can configure all of them through the graphical interface or manually through the config file, `Config.json`, found in the Ryujinx data folder which can be accessed by clicking `Open Ryujinx Folder` under the File menu in the GUI.

## License

This software is licensed under the terms of the [MIT license](https://git.ryujinx.app/projects/Ryubing/src/branch/master/LICENSE.txt).
This project makes use of code authored by the libvpx project, licensed under BSD and the ffmpeg project, licensed under LGPLv3.
See [LICENSE.txt](https://git.ryujinx.app/projects/Ryubing/src/branch/master/LICENSE.txt) and [THIRDPARTY.md](https://git.ryujinx.app/projects/Ryubing/src/branch/master/distribution/legal/THIRDPARTY.md) for more details.

## Credits

- [LibHac](https://git.ryujinx.app/projects/LibHac) is used for our file-system.
- [AmiiboAPI](https://www.amiiboapi.com) is used in our Amiibo emulation.
- [ldn_mitm](https://github.com/spacemeowx2/ldn_mitm) is used for one of our available multiplayer modes.
- [ShellLink](https://github.com/securifybv/ShellLink) is used for Windows shortcut generation.
