# Appearance, detailed history and APK size

- Theme choices no longer wait for history work to release a database lock. Preference memory updates immediately; persistence is asynchronous.
- A circular reveal composites one short-lived old frame over the new theme. Animation ticks redraw the overlay, not the entire palette. Screen/scroll state remains intact. The frame is never saved or sent; it is released after the transition. Device animation-duration/accessibility settings still apply.
- Material tap ripples are disabled. Filled/outlined icons and selected semantics remain, without persistent selection bubbles.
- Original app icon colors are the default in both themes. The old monochrome default is reset once; later deliberate grayscale choices are retained. A scalable stopwatch launcher vector has a separate system-themed monochrome asset.
- Repeated device/build labels are removed. Android App info and export schema metadata remain normal platform/data metadata.
- History supports a rolling year, app totals and per-day visits. Selecting a day keeps the year overview fixed. Aggregation runs on the I/O worker and publishes summary rows; only a short raw history window reaches the UI. Daily buckets visit each interval once and respect midnight/DST boundaries.
- New installs keep a year by default. Explicit shorter retention is respected and can be changed from History. Deleted or expired source history cannot be reconstructed. Saved days retain detailed visits and optional website detail.
- The preview pipeline previously copied the unoptimized debug APK. The new preview build uses release code/resource shrinking with development signing. It remains a testing artifact, not production signing or a promise of matching earlier preview certificates.

## Validation

JVM and device checks cover a synthetic full-year archive, oldest-day detail, shorter-retention boundaries, DST, theme changes under an occupied history lock, original icon colors in both themes, absence of build labels, navigation state and an intermediate reveal frame. Debug/preview lint and optimized packaging are checked too.

Verified locally: 32 JVM tests and 23 API device tests pass. Debug and optimized-preview lint have zero errors. The debug APK measures 58.63 MiB; the optimized, development-signed preview measures approximately 2.62 MiB and its APK signature verifies. The Android release now has its own version while Windows and companions remain unchanged. An earlier downloaded APK with a different certificate cannot be updated in place with this testing build.

The graphics implementation follows [Android's graphics-layer capture API](https://developer.android.com/develop/ui/compose/graphics/draw/modifiers). Emulator tests are not a physical-device FPS or battery benchmark. Windows and extension source are unchanged.
