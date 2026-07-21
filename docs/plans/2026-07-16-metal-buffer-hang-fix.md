# Metal Buffer-Backed Texture Hang Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Prevent the oversized linear R8 fallback from submitting mismatched texture descriptors or accumulating unbounded pending bindings when the GPU thread stalls.

**Architecture:** Keep ordinary textures on the existing Vulkan image path. Route only eligible oversized linear R8 textures through a `TextureBuffer`, enforce that target contract at the texture boundary, and replace append-only pending binding lists with destination-keyed upsert queues. Verify all policy and queue behavior with CPU-only tests before any GUI or Vulkan device test.

**Tech Stack:** .NET 10, C#, NUnit, Ryujinx GAL/GPU/shader internals.

---

## Incident Evidence

- Ryujinx log: `Ryujinx_1.3.343-metal-vtg-mask-index-reserve...2026-07-16_13-55-56.log`
- Oversized fallback: one `1024x32768`, stride `1024`, `R8Unorm` texture, about 32 MiB.
- NVDEC starts around 58 seconds; GPU syncpoint waits begin immediately afterward.
- Ryujinx footprint grows from 939.12 MiB to 6127.47 MiB in about 147 seconds.
- WindowServer blocks in `IOSurface -> IOGPUFamily` and misses watchdog check-ins for 40 seconds.
- The current dirty `Texture.GetTargetTexture` change can return a Vulkan `TextureBuffer` for a requested `Texture2D` target.
- `BufferManager.SetBufferTextureStorage` currently appends every rebind until commit, so a stalled GPU/commit path can retain repeated work without a bound.

## Safety Gates

1. Do not launch Ryujinx, create a Vulkan/Metal device, or record the screen while unit tests are failing.
2. Keep all first-stage tests CPU-only and filterable with `FullyQualifiedName~Ryujinx.Tests.Graphics`.
3. Treat queue count growth, per-rebind allocation growth, or a buffer/image target mismatch as release blockers.
4. Run a GUI smoke test only after focused tests, the full graphics test group, and the release build pass.
5. During a later GUI smoke test, stop automatically if resident memory growth exceeds 1 GiB, the GPU thread misses progress for 10 seconds, or a syncpoint timeout repeats.

### Task 1: Lock The Buffer Target Contract

**Files:**
- Modify: `src/Ryujinx.Graphics.Gpu/Image/Texture.cs`
- Create: `src/Ryujinx.Tests/Graphics/Gpu/TextureTargetContractTests.cs`

**Steps:**

1. Add a CPU-testable direct-target policy used by `GetTargetTexture`.
2. Verify a buffer-backed `Texture2D` is handled as `TextureBuffer` only.
3. Verify requesting its original `Texture2D` target returns null and cannot continue into image-view creation.
4. Verify ordinary textures still return their matching host target.
5. Run the focused contract tests and confirm the current dirty behavior fails before fixing it.

### Task 2: Make Pending Bindings Bounded

**Files:**
- Create: `src/Ryujinx.Graphics.Gpu/Memory/BufferTextureBindingQueue.cs`
- Modify: `src/Ryujinx.Graphics.Gpu/Memory/BufferManager.cs`
- Modify: `src/Ryujinx.Graphics.Gpu/Memory/BufferTextureBinding.cs`
- Modify: `src/Ryujinx.Graphics.Gpu/Memory/BufferTextureArrayBinding.cs`
- Create: `src/Ryujinx.Tests/Graphics/Gpu/BufferTextureBindingQueueTests.cs`

**Steps:**

1. Add tests showing 10,000 identical single bindings collapse to one pending destination.
2. Add tests showing a later range replaces the earlier range for the same destination.
3. Add tests for texture-array and image-array identity plus element index.
4. Add tests showing different stages, bindings, arrays, and indexes remain distinct.
5. Add a warmed allocation test with a 4 KiB ceiling for 10,000 repeated upserts.
6. Implement list-backed upsert queues keyed by the actual pipeline destination.
7. Clear all queues after commit and test that counts return to zero.

### Task 3: Cover Layout And Shader Specialization

**Files:**
- Modify: `src/Ryujinx.Tests/Graphics/Gpu/TextureHostLayoutTests.cs`
- Modify: `src/Ryujinx.Tests/Graphics/Shader/AttributeUsageTests.cs`

**Steps:**

1. Test the exact `1024x32768 R8Unorm` descriptor selects the buffer-backed state.
2. Test boundary dimensions, non-R8 formats, compressed formats, mip levels, and invalid stride stay on the image path.
3. Test the host create target is `TextureBuffer` with `33,554,432` texels.
4. Test viewport-mask fallback reserves viewport index only when the host supports it.
5. Test unsupported viewport index neither reserves nor emits an invalid output.

### Task 4: Verify Failed Global Stores Are Removed

**Files:**
- Create or modify: `src/Ryujinx.Tests/Graphics/Shader/GlobalToStorageTests.cs`
- Modify only if a regression appears: `src/Ryujinx.Graphics.Shader/Translation/Optimizations/GlobalToStorage.cs`

**Steps:**

1. Build a minimal IR block containing an unresolved global store.
2. Run `GlobalToStorage.RunPass` with an accessor that exposes no storage target.
3. Assert no global store survives and the pass logs the failure.
4. Add the equivalent unresolved-load test and assert it becomes zero.

### Task 5: Performance And Memory Verification

**Files:**
- Tests from Tasks 1-4
- Optional diagnostics only: `src/Ryujinx.Graphics.Gpu/Memory/BufferManager.cs`

**Budgets:**

- Pending rebinds for one destination after 10,000 updates: exactly 1.
- Warmed managed allocations for 10,000 identical queue updates: at most 4 KiB.
- Queue update complexity: at most the number of active buffer texture destinations; expected below 64 for scalar bindings.
- Oversized host storage: about 32 MiB for the observed texture, with no duplicate image allocation.
- Nearest buffer sampling: one texel-buffer load plus coordinate clamp/index arithmetic.
- Manual bilinear buffer sampling: four texel-buffer loads plus interpolation arithmetic; expect roughly 2-4x the sampling cost of nearest for this texture operation, not necessarily the whole frame.
- Shader specialization: only shaders whose descriptor enters or leaves the buffer-backed state should be invalidated.

**Verification commands:**

```bash
env MSBUILDNOINPROCNODE=1 \
  DOTNET_CLI_HOME=$PWD/.dotnet_home \
  NUGET_PACKAGES=$PWD/.nuget/packages \
  DOTNET_ROOT=$PWD/.dotnet \
  DOTNET_ROOT_ARM64=$PWD/.dotnet \
  $PWD/.dotnet/dotnet test src/Ryujinx.Tests/Ryujinx.Tests.csproj \
  -c Release --no-restore -m:1 -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~Ryujinx.Tests.Graphics"
```

```bash
env DOTNET_CLI_HOME=$PWD/.dotnet_home \
  NUGET_PACKAGES=$PWD/.nuget/packages \
  DOTNET_ROOT=$PWD/.dotnet \
  DOTNET_ROOT_ARM64=$PWD/.dotnet \
  $PWD/.dotnet/dotnet build src/Ryujinx/Ryujinx.csproj \
  -c Release --no-restore -m:1 -p:UseSharedCompilation=false
```

## Deferred GPU Validation

The CPU suite can prevent known contract, queue, and allocation regressions, but it cannot prove a third-party Metal driver will never hang. The first GPU validation must therefore be short, non-recorded, memory-monitored, and abortable. Only after that passes should the game be allowed to reach the NVDEC animation transition for a longer run.

## Current Verification Status

- Graphics CPU suite: 27 passed.
- Non-CPU managed suite: 129 passed.
- Release build: passed with 0 warnings and 0 errors.
- Full suite remains environment-blocked because the checked-out test runtime does not contain `unicorn.dylib`; CPU parameterized tests cannot initialize their external reference emulator until that native dependency is restored.
- Queue microbenchmark on this machine: 10,000 upserts in 3.165 ms, peak one entry, 40 bytes of managed allocation after warm-up.
- Application GUI, Vulkan/Metal device creation, and screen recording were intentionally not run in this phase.
