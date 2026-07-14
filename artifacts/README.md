# macOS Runtime Package

## Ryujinx 1.3.336 buffer-bilinear

- File: `Ryujinx-1.3.336-buffer-bilinear-macos-arm64.zip`
- Platform: macOS arm64 (Apple Silicon)
- Bundle version: `1.3.336`
- Source branch: `codex/buffer-bilinear-1.3.336`
- Source commit: `bd17fb51c9c65e02fdbf647347c3bd5c20e34ac8`
- SHA-256: `1b7f99a900aa9ad60f13942017c6f40fd9d76971c12affd4093b6fbf8a008de5`

The previous `1.3.335` package remains in this directory as a backup.

The application bundle passed `codesign --verify --deep --strict`, and the ZIP archive passed an integrity test before being committed.

This package contains the buffer-backed sampling implementation for oversized linear R8 textures on macOS. Floating-point samples use manual bilinear filtering, while integer texel fetches retain nearest-neighbor sampling.
