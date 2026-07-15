# macOS Runtime Package

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
