# macOS Runtime Package

## Ryujinx 1.3.335 buffer-bilinear

- File: `Ryujinx-1.3.335-buffer-bilinear-macos-arm64.zip`
- Platform: macOS arm64 (Apple Silicon)
- Bundle version: `1.3.335`
- Source branch: `codex/buffer-bilinear-1.3.336`
- Source commit: `63b83d32aee0a04de4ad49e30ce667b9e64bdd1b`
- SHA-256: `0cc56b2e9e3476ebd394a45d4260c7c6b8772e7bbc5fe176ff2a08e68ef72048`

The application bundle passed `codesign --verify --deep --strict`, and the ZIP archive passed an integrity test before being committed.

This package contains the buffer-backed sampling implementation for oversized linear R8 textures on macOS. Floating-point samples use manual bilinear filtering, while integer texel fetches retain nearest-neighbor sampling.
