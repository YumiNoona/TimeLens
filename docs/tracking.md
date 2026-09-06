# Tracking behavior and regression checks

TimeLens counts observed foreground app use. The active total includes browsers;
website breakdowns describe that browser time and are not added a second time.
Apps merely left running in the background are not counted as active work.

## Reliability

- Foreground hooks provide immediate updates, with a five-second poll as a fallback.
- Repeated observations extend one persisted event. Its end is saved on every
  observation, so a crash cannot reduce a three-hour session to a 30-minute guess.
- A gap longer than 30 seconds starts a new event rather than crediting unobserved
  sleep, downtime or a stalled tracker. A backwards wall-clock jump also splits it.
- Normal shutdown closes the current app/idle segments and drains queued writes.
- Legacy timestamp comparisons use SQLite date arithmetic, not text ordering.
  Missing historical observations cannot be reconstructed reliably.

## Idle and away

The saved idle timeout takes effect on the next poll (180 seconds by default).
Short pauses for reading or thinking stay active until that threshold. Input
resumes activity even if the foreground window never changes. Idle begins when
the threshold is detected; the grace period is retained as active time.

Lock, session disconnect and suspend are away states. Waking does not clear a
known lock. Unlock emits a transition, allowing the idle span to close properly.

Audio from the foreground app, or a fresh browser audio signal while a browser
is foreground, may sustain activity without input for up to two hours. Background
music does not keep a silent desktop editor active. Browser audio is currently
browser-wide, not a guarantee that the audible tab itself is selected. Time since
last input remains visible even when playback sustains activity.

Rendering with no input eventually becomes idle. Background rendering is not
proof of a person's attendance. Adjust the idle timeout in Settings if longer
reading or review pauses are normal for your work.

## Calendar days

Summary, top apps, categories, heatmap, timeline and activity exports intersect
sessions with local-day boundaries converted to UTC. A session from 23:00 to
02:00 contributes one hour to the first day and two to the next. A day is not
assumed to be 24 hours, so daylight-saving boundaries use local calendar dates.
Short foreground segments remain visible instead of discarding all under five
seconds. Historical records are read with corrected boundaries, not rewritten
merely to change the date allocation.

## Verification

```powershell
dotnet run --project tests/TimeLens.Tracking.Tests -c Release --self-contained true -r win-x64
dotnet run --project tests/TimeLens.Startup.Tests -c Release --self-contained true
```

Tracking checks exercise the production writer and analytics with a controlled
clock and temporary databases: multi-hour persistence, restart, observation gaps,
midnight allocation, legacy timestamp formats, and idle/media/lock transitions.
Both the publish script and release workflow run tracking regressions.

## Sources consulted

- [ActivityWatch heartbeats](https://docs.activitywatch.net/en/latest/buckets-and-events.html#heartbeats): persist observations and merge adjacent identical state within a bounded pulse interval.
- [ActivityWatch idle detection](https://docs.activitywatch.net/en/latest/faq.html): separate foreground and input-idle observations, with playback as an exception.
- [Windows GetLastInputInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo): session-specific input timestamps and tick-count caveats.

## Runtime resilience in 6.2.0

Foreground, input timer, session and tray callbacks contain recoverable subscriber
exceptions so a database or dashboard-launch failure does not escape a native
callback and stop the tracker. `%LOCALAPPDATA%\TimeLens\runtime.log` records
startup version/path, shutdown requests, callback errors and unhandled failures.
The log rotates at 1 MiB. Forced process termination and power loss may not leave
an exit record; durable activity checkpoints remain the recovery boundary.
