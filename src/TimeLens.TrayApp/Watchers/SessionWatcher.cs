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
            case SessionSwitchReason.SessionLock:
                StateChanged?.Invoke("locked");
                break;
            case SessionSwitchReason.SessionUnlock:
                StateChanged?.Invoke("unlocked");
                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) StateChanged?.Invoke("sleep");
        if (e.Mode == PowerModes.Resume) StateChanged?.Invoke("wake");
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
