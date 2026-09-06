using Microsoft.Win32;

namespace TimeLens.TrayApp.Watchers;

public sealed class SessionWatcher : IDisposable
{
    public event Action<string>? StateChanged;

    public void Start()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
            case SessionSwitchReason.SessionLogoff:
            case SessionSwitchReason.SessionLock:
                Publish("locked");
                break;
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
            case SessionSwitchReason.SessionLogon:
            case SessionSwitchReason.SessionUnlock:
                Publish("unlocked");
                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Publish("sleep");
        if (e.Mode == PowerModes.Resume) Publish("wake");
    }

    private void Publish(string state) => RuntimeDiagnostics.TryRun("Session callback", () => StateChanged?.Invoke(state));

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
