# macOS Runtime Package

## Ryujinx 1.3.366 macos-gpu-submit-guard

- File: `Ryujinx-1.3.366-macos-gpu-submit-guard-macos-arm64.zip`
- Platform: macOS arm64 (Apple Silicon)
- Bundle version: `1.3.366`
- Source branch: `codex/vtg-compute-fix-1.3.337`
- SHA-256: `62e8d24dd1b63377d0addcb67069678fddff0b149c5c57f0c7b43c32a0e8c2df`

This safety build blocks oversized paged linear Metal textures before host texture creation. On macOS, the known `1024x32768 R8Unorm` path stops emulation on the CPU side and returns to the game list instead of waiting for an AGX/MoltenVK device loss. The experimental path can only be enabled explicitly with `RYUJINX_ALLOW_UNSAFE_MACOS_PAGED_TEXTURES=1` for isolated developer testing.

Graphics tests: `57/57` passed. Release build completed with zero warnings and zero errors. The application bundle passed `codesign --verify --deep --strict`, and the ZIP archive passed a full integrity test. A direct Nintendo Switch Sports 1.5.0 XCI run hit the CPU-side guard before NvDec initialization and produced no Vulkan device-loss error, no MoltenVK out-of-device-memory error, and no new macOS Ryujinx crash report.

## Ryujinx 1.3.365 graceful-device-loss

- File: `Ryujinx-1.3.365-graceful-device-loss-macos-arm64.zip`
- Platform: macOS arm64 (Apple Silicon)
- Bundle version: `1.3.365`
- Source branch: `codex/vtg-compute-fix-1.3.337`
- SHA-256: `44fe2d99d3ecf83df41f6a5abd2ac926e103a7b50f2572445f0912b3a7425ed9`

This package splits paged texture uploads and 2D copies at the Metal 16384-row boundary before Vulkan command submission. It maps each segment to a cached 2D view of the correct array layer, rejects unsupported page copies on the CPU, validates Vulkan upload regions, keeps the unsafe texel-buffer path disabled, and forces backend threading off on macOS after all configuration overrides. On macOS it also forces `HostMappedUnsafe` to `HostMapped`, disables MoltenVK device resumption after an AGX command-buffer fault, and stops emulation on Vulkan device loss before more GPU work is submitted.

Graphics tests: `52/52` passed. Release and osx-arm64 self-contained builds completed with zero warnings and zero errors. The application bundle passed `codesign --verify --deep --strict`, the runtime executable carries the macOS Hypervisor entitlement, and the ZIP archive passed a full integrity test. Interactive testing reproduced the animation GPU fault and verified that the fail-closed build stops instead of resuming the lost device; full 1.3.365 animation re-test was not completed after the desktop locked.

## Ryujinx 1.3.359 paged-r8-descriptor-guard

- File: `Ryujinx-1.3.359-paged-r8-descriptor-guard-macos-arm64.zip`
- Platform: macOS arm64 (Apple Silicon)
- Bundle version: `1.3.359`
- Source branch: `codex/vtg-compute-fix-1.3.337`
- SHA-256: `754dff6174cb2328f584df949daf352e987e724e113d946261f3d77f9d379cb8`

This package uses a two-page `Texture2DArray` for the observed `1024x32768 R8Unorm` linear texture, lowers sampling coordinates in the shader, keeps the old texel-buffer path disabled by default, and clamps any remaining oversized MoltenVK 2D host descriptor before `vkCreateImage`.

CPU Graphics tests: `40/40` passed. The bundle is ad-hoc signed and verified with `codesign --verify --deep --strict`.

## Ryujinx 1.3.339 vtg-oom-fix

- File: `Ryujinx-1.3.339-vtg-oom-fix-macos-arm64.zip`
- Platform: macOS arm64 (Apple Silicon)
- Bundle version: `1.3.339`
- Source branch: `codex/vtg-compute-fix-1.3.337`
- SHA-256: `6cca0eb4a958c5bbaea94d5189619a4b6d98a05f8de7d723d80f90ab331b5613`

This package fixes a VTG-as-compute geometry output addressing bug that could write outside the allocated geometry output buffers on instanced geometry draws. The symptom matched the observed failure chain: GPU backpressure, MoltenVK `VK_ERROR_OUT_OF_DEVICE_MEMORY`, black screen, and eventual WindowServer watchdog recovery.

The application bundle passed `codesign --verify --deep --strict`. The local graphics regression tests passed after the fix, including coverage for VTG geometry output buffer sizing.

## Ryujinx 1.3.337 vtg-compute-fix

- File: `Ryujinx-1.3.337-vtg-compute-fix-macos-arm64.zip`
- Platform: macOS arm64 (Apple Silicon)
- Bundle version: `1.3.337`
- Source branch: `codex/vtg-compute-fix-1.3.337`
- Source commit: `7cc370d0ef735202c47599278a4e2d4fbadb66ab`
- SHA-256: `ee97ddf1485af58479addd53490a410d5db8958e32f83fb9bb0506ff895dbe17`

The previous `1.3.335` and `1.3.336` packages remain in this directory as backups.

The application bundle passed `codesign --verify --deep --strict`, and the ZIP archive passed an integrity test before being committed.

This package retains the buffer-backed sampling implementation for oversized linear R8 textures on macOS. It also prevents invalid Metal compute kernels when vertex or geometry shaders are converted to compute by replacing unmapped input/output loads with zero, dropping unmapped stores, and correctly reserving `ViewportIndex`.

Nintendo Switch Sports ran for 3 minutes 22 seconds in the controlled test without a Metal shader-library compile failure, GPU recovery, device timeout, or a new macOS `gpuEvent` report. The previous failures occurred around 57 and 91 seconds.
