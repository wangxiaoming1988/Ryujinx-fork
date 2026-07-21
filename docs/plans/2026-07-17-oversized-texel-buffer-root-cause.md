# Oversized Texel Buffer Root Cause Fix Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement the plan task-by-task.

**Goal:** Make the macOS Vulkan fallback for the observed `1024x32768 R8Unorm` linear texture use a valid, bounded texel-buffer resource without introducing black frames, GPU stalls, or unbounded memory growth.

**Architecture:** Keep normal textures and native guest buffer textures on their existing paths. For the narrowly identified oversized linear R8 texture, use a dedicated host buffer imported from guest host memory when possible, declare the Vulkan buffer usage required by texel-buffer views, and bind it directly as `Target.TextureBuffer`. Do not route this texture through the guest `BufferCache` or through an image target. Validate the resource contract with CPU tests before any device or game launch.

**Tech Stack:** .NET, C#, Vulkan/MoltenVK, NUnit, Ryujinx GAL/GPU/shader internals.

---

### Task 1: Capture the failing contract

**Files:**
- Modify: `src/Ryujinx.Tests/Graphics/Gpu/TextureHostLayoutTests.cs`
- Create or modify: `src/Ryujinx.Tests/Graphics/Vulkan/HostImportedBufferUsageTests.cs`

**Steps:**

1. Add a CPU-testable helper or constant-level assertion that the host-imported buffer usage includes `UniformTexelBufferBit` and `StorageTexelBufferBit`.
2. Keep transfer usage and indirect usage behavior covered.
3. Run the focused tests and confirm the new usage assertion fails against the current transfer-only declaration.

### Task 2: Correct the Vulkan host-imported buffer declaration

**Files:**
- Modify: `src/Ryujinx.Graphics.Vulkan/BufferManager.cs`

**Steps:**

1. Add texel-buffer usage flags to the host-imported usage set.
2. Ensure `GetHostImportedUsageRequirements` uses the same exact usage mask as `CreateHostImported`.
3. Preserve the existing host-memory import fallback and indirect-buffer conditional behavior.
4. Run the focused CPU tests and a Release build.

### Task 3: Lock the dedicated buffer target and lifetime behavior

**Files:**
- Modify: `src/Ryujinx.Graphics.Vulkan/TextureBuffer.cs` only if required by tests or compile diagnostics.
- Modify: `src/Ryujinx.Graphics.Gpu/Image/Texture.cs` only if the target/lifetime contract is violated.
- Modify: `src/Ryujinx.Tests/Graphics/Gpu/TextureTargetContractTests.cs`

**Steps:**

1. Verify a buffer-backed guest 2D texture resolves only to `Target.TextureBuffer`.
2. Verify it does not call the guest `BufferCache` storage path.
3. Verify views share the owned buffer and release it exactly once after all views are released.
4. Add only the smallest implementation change needed for a failing test.

### Task 4: Verify shader sampling and warning boundaries

**Files:**
- Modify: `src/Ryujinx.Tests/Graphics/Shader/GlobalToStorageTests.cs` only if a real regression is found.
- Inspect: `src/Ryujinx.Graphics.Shader/Translation/Transforms/TexturePass.cs`
- Inspect: `src/Ryujinx.Graphics.Shader/Translation/Optimizations/GlobalToStorage.cs`

**Steps:**

1. Confirm the buffer-backed R8 path emits one nearest texel-buffer load with the expected row/stride coordinate conversion.
2. Confirm unresolved global stores are removed from IR and are not allowed to reach generated shaders.
3. Do not suppress the warning globally; correlate it with the failing shader log before changing behavior.

### Task 5: Device-gated validation

**Steps:**

1. Run the focused graphics CPU tests.
2. Build the macOS arm64 Release app.
3. Start one short monitored run with the game, stopping on GPU stall, syncpoint timeout, black frame, or more than 1 GiB RSS growth.
4. Inspect the log for host-imported usage, buffer-backed texture creation, shader warnings, and GPU progress.
5. Only after the short run is stable, run the animation transition and confirm the menu remains interactive.
