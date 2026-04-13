# ADR 001: Viewport-Aware Downsampling for Large Dataset Rendering

**Status**: Accepted
**Date**: 2026-04
**PR**: #467 (supersedes #457)

## Context

The DAQiFi Desktop application needs to display logged data sessions that can be very large — a typical worst case is 16 channels at 1000 Hz for 24 hours, producing ~1.38 billion data points. PR #458 added an overview minimap for navigating these sessions, but the main plot still rendered every data point, making the UI sluggish with large sessions.

OxyPlot (our charting library) iterates every point in a series during render, even when zoomed into a tiny region. With 1M points per channel and 16 channels, that's 16M point iterations per frame — far too slow for interactive pan/zoom.

We needed a downsampling strategy that:

1. Keeps the main plot under ~4000 points per channel regardless of dataset size
2. Shows full detail when zoomed in (not a blurry approximation)
3. Works with the minimap's ability to select arbitrary time ranges
4. Maintains 60fps during drag/resize interactions

## Decision

Use **viewport-aware MinMax downsampling**: on every viewport change, binary search the source data for the visible time range, then downsample only that slice to ~4000 points using min/max aggregation per bucket.

### How it works

**Session loading (two-phase progressive):**
1. **Phase 1** (<1s): Load first 100K samples via index scan for immediate display
2. **Phase 2** (~1-3s): Load a sampled overview via ~3000 targeted index seeks spread across the full time range. Each seek reads one batch of interleaved channel data. Result: ~3000 points/channel covering the full range

**Viewport updates (drag vs settle):**
1. **During interaction** (minimap drag, pan/zoom): `UpdateMainPlotViewport(highFidelity: false)` uses only in-memory sampled data. Binary searches for the visible range — O(log n) via `MinMaxDownsampler.FindVisibleRange()` — then downsamples to ~4000 points per channel via min/max aggregation. Written into a **reusable cached list** per series (no allocation).
2. **On settle** (mouse-up or 200ms idle): `UpdateMainPlotViewport(highFidelity: true)` checks if the in-memory data is too sparse for the current zoom level. If so, fires an async background DB fetch using sampled index seeks within the visible window, then marshals results back to the UI thread. A `CancellationToken` ensures only the latest fetch completes.
3. Viewport updates are **throttled to 60fps** via DispatcherTimer + dirty flag, both for minimap-driven changes and main plot pan/zoom

### Key files

- `MinMaxDownsampler.cs` — binary search + min/max downsampling algorithm
- `DatabaseLogger.cs` — two-phase loading, viewport updates, async DB fetch (`UpdateMainPlotViewport`, `FetchViewportDataFromDb`, `LoadSampledData`)
- `MinimapInteractionController.cs` — 60fps throttled minimap interaction with drag/settle distinction
- `LoggingContext.cs` — composite DB index `IX_Samples_SessionTime` on `(LoggingSessionID, TimestampTicks)`

## Alternatives Considered

### 1. Global LTTB Decimation (PR #457)

PR #457 applied Largest Triangle Three Buckets (LTTB) decimation globally to ~5000 points per channel at load time.

**Rejected because**: Global decimation fundamentally conflicts with the minimap. If you decimate 24 hours of data to 5000 points and then zoom into a 1-minute window, only ~3 points would be visible — the plot would be empty or misleadingly sparse. The minimap's entire purpose is to let users zoom into arbitrary ranges, so the downsampling must be viewport-aware.

LTTB also has higher computational cost per point than MinMax (triangle area calculations vs simple comparisons), which matters when re-downsampling at 60fps.

### 2. Pre-computed Multi-Resolution Pyramid

Build multiple resolution levels at load time (e.g., 1:1, 1:10, 1:100, 1:1000) and select the appropriate level based on zoom.

**Rejected because**: Significant memory overhead (nearly 2x the raw data), complex invalidation if data changes, and overkill for our use case. The binary search + linear scan approach is fast enough (~1ms for 16 channels) that on-the-fly downsampling at 60fps is feasible without pre-computation.

### 3. GPU-Accelerated Rendering

Replace OxyPlot with a GPU-backed charting library (e.g., SciChart, LiveCharts2 with SkiaSharp).

**Rejected because**: Major dependency change with significant migration cost. OxyPlot is well-integrated with our WPF MVVM architecture. Viewport-aware downsampling reduces the point count enough (~64K total) that OxyPlot renders comfortably within a 16ms frame budget. If we outgrow this approach, GPU rendering remains a future option.

### 4. Virtual Scrolling / On-Demand DB Queries (Partially Adopted)

Only load the visible time range from SQLite on each viewport change, avoiding keeping all data in memory.

**Initially rejected** for pure on-demand because SQLite query latency is too high for 60fps continuous interaction (minimap drag, pan). **However**, we adopted a hybrid approach:

- **During drag**: use in-memory sampled data only (3000 points/channel, loaded at session start via sampled index seeks). This guarantees <1ms viewport updates for smooth 60fps.
- **On settle** (mouse-up or 200ms idle): fetch high-resolution data from SQLite for just the visible window via async sampled index seeks on a background thread. The composite index (`IX_Samples_SessionTime`) makes these seeks fast regardless of total dataset size.

This gives the best of both worlds: instantaneous interaction with progressive refinement to full fidelity when the user stops moving.

## Consequences

### Positive

- **60fps interaction**: <1ms per viewport update during drag (in-memory sampled data only)
- **Full zoom fidelity**: Zooming into a 1-second window triggers an async DB fetch that fills in full-resolution data
- **Fast session loading**: Two-phase progressive — data visible in <1s, full overview in ~1-3s regardless of dataset size
- **Non-blocking UI**: DB fetches run on background thread with cancellation. A thin progress bar indicates when refinement is in progress
- **Low complexity**: The core algorithm is ~80 lines (binary search + min/max loop)
- **No external dependencies**: Pure C# implementation

### Negative

- **Memory usage for overview**: Sampled data (~3000 points/channel) lives in memory. Much smaller than keeping the full dataset in memory.
- **MinMax doesn't preserve exact X boundaries**: Downsampled first/last X values may differ from source data. This required explicit time axis ranging in `ResetZoom` instead of relying on OxyPlot auto-range (see `InvalidatePlot` gotchas in CLAUDE.md).
- **List mutation pattern**: Reusing cached lists and calling `InvalidatePlot(true)` is non-obvious. `InvalidatePlot(false)` silently renders stale data. This is documented in CLAUDE.md but remains a footgun for future changes.
- **Thread safety**: `_allSessionPoints` is written on background threads (session loading) and read on the UI thread (viewport updates). Currently relies on non-overlapping access patterns rather than explicit synchronization. A future refactor should add proper locking.

### Follow-up Work

- Add thread synchronization for `_allSessionPoints` (lock or move all mutations to UI thread)
- Add cancellation token to the consumer thread for clean shutdown on `Dispose()`
- Existing DB migration path: the composite index `IX_Samples_SessionTime` is only created via `EnsureCreated`, not applied to existing databases. See GitHub issue #468 for the `EnsureCreated` → EF Core migrations switch.
- PR #457 should be closed as superseded
