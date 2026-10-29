# Tracking and data accounting

## Windows

One foreground executable owns each interval. Idle, lock, sleep and unknown observation gaps are separate from active use. Short switches retain seconds, and delayed aggregate input flushes keep their capture timestamps. Database startup closes unfinished records at their last durable checkpoint rather than assuming activity until the next launch.

Browser observations are accepted only for the matching foreground browser family. A finite observation lease and durable checkpoints prevent indefinite attribution after a missing extension. Repeated identical observations refresh that lease without a SQLite write on every notification. Background media is a separate concurrent ledger and is never added to exclusive foreground time.

Website detail is clipped to browser foreground intervals and excludes idle spans. URL detail, title storage, exclusions, collection pauses and retention follow privacy settings. Input is aggregate counts, not text or key values. Retried input batches use stable IDs to avoid double counting.

## Browser companions

Focused normal tabs produce current observations; disconnected old snapshots are not replayed as current tabs. Domain/path/full URL privacy is applied before sending data to the local API. Private tabs are excluded. Durable aggregate input queues are bounded and validated. Supported media-element timing and audible fallback are labeled separately, with close events ending known playback.

## Android

Usage access reads foreground app events into a local archive. Day boundaries use local dates rather than assuming every day has exactly 24 hours. History groups app totals and lazily expands visits. Comfort rules measure a rolling 24-hour allowance; Strict ignores allowance. Optional website tracking reads known exposed browser address bars and counts website time within browser app time.

Android has its own data and permission model. It does not collect global input counts, duplicate Windows background-media reports or sync with Windows. Hidden browser bars, unsupported versions, expired operating-system history and OEM restrictions leave genuine gaps.

## Verification

Repository tests cover browser events, privacy, recovery, package versions and release selection. .NET regression programs exercise startup/migrations, long sessions, observation gaps, input attribution, media and local deletion. Android has JVM and dedicated-emulator UI/permission tests. scripts/test-startup.ps1 and scripts/test-resource-budget.ps1 run the actual Windows release with isolated data. Emulator and local measurements do not claim every device/browser version has been validated.
