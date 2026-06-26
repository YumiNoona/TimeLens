using System.Runtime.InteropServices;

namespace TimeLens.TrayApp.Watchers;

public sealed class AudioMonitor : IDisposable
{
    private IMMDevice? _device;
    private IAudioSessionManager2? _sessionManager;
    private SessionNotificationSink? _notificationSink;
    private IntPtr _notificationSinkPtr;
    private readonly HashSet<(int pid, string exe)> _activeSessions = [];
    private readonly List<(SessionEventsSink sink, IntPtr ptr, IAudioSessionControl2 ctl2)> _sessionSinks = [];

    // COM callbacks must fire on the thread that registered them (must be STA).
    // Capture the main thread context on first Start() and marshal subsequent calls.
    private System.Threading.SynchronizationContext? _mainCtx;

    public bool AnyAudioPlaying => _activeSessions.Count > 0;
    public event Action<int, string, bool>? SessionAudioChanged;

    public void Start()
    {
        if (System.Threading.Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            // Marshal back to the main STA thread if we have a captured context
            if (_mainCtx is not null)
            {
                _mainCtx.Post(_ => StartOnStaThread(), null);
                return;
            }
            throw new InvalidOperationException("AudioMonitor must be started from an STA thread");
        }
        StartOnStaThread();
    }

    private void StartOnStaThread()
    {
        _mainCtx = System.Threading.SynchronizationContext.Current;
        try
        {
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(AudioClsids.MMDeviceEnumerator)!)!;

            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out _device);
            _sessionManager = Activate<IAudioSessionManager2>(_device, typeof(IAudioSessionManager2).GUID);
            Marshal.ReleaseComObject(enumerator);

            EnumerateExistingSessions();

            _notificationSink = new SessionNotificationSink(OnSessionCreated);
            _notificationSinkPtr = Marshal.GetComInterfaceForObject(_notificationSink, typeof(IAudioSessionNotification));
            _sessionManager.RegisterSessionNotification(_notificationSinkPtr);
        }
        catch
        {
            // Core Audio API not available or error — silently degrade
        }
    }

    private void EnumerateExistingSessions()
    {
        try
        {
            _sessionManager!.GetSessionEnumerator(out var sessions);
            sessions.GetCount(out var count);

            for (uint i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var session);
                var ctl2 = session as IAudioSessionControl2;
                if (ctl2 == null) { Marshal.ReleaseComObject(session); continue; }

                ctl2.GetState(out var state);
                if (state == 1)
                {
                    // TrackSession stores ctl2 — do NOT release it here
                    TrackSession(ctl2);
                }
                else
                {
                    Marshal.ReleaseComObject(ctl2);
                }

                Marshal.ReleaseComObject(session);
            }

            Marshal.ReleaseComObject(sessions);
        }
        catch { }
    }

    private void OnSessionCreated(IAudioSessionControl session)
    {
        try
        {
            var ctl2 = session as IAudioSessionControl2;
            if (ctl2 == null) return;

            ctl2.GetState(out var state);
            if (state == 1)
                TrackSession(ctl2);
            else
                Marshal.ReleaseComObject(ctl2);
        }
        catch { }
        finally
        {
            Marshal.ReleaseComObject(session);
        }
    }

    private void TrackSession(IAudioSessionControl2 ctl2)
    {
        var (pid, exe) = GetSessionInfo(ctl2);
        var key = (pid, exe);

        if (!_activeSessions.Add(key)) { Marshal.ReleaseComObject(ctl2); return; }

        var sink = new SessionEventsSink(key, OnSessionStateChanged, OnSessionDisconnected);
        var ptr = Marshal.GetComInterfaceForObject(sink, typeof(IAudioSessionEvents));
        ctl2.RegisterAudioSessionNotification(ptr);

        lock (_sessionSinks)
            _sessionSinks.Add((sink, ptr, ctl2));

        SessionAudioChanged?.Invoke(pid, exe, true);
    }

    private void OnSessionStateChanged((int pid, string exe) key, AudioSessionState state)
    {
        if (state == AudioSessionState.Active)
        {
            if (_activeSessions.Add(key))
                SessionAudioChanged?.Invoke(key.pid, key.exe, true);
        }
        else
        {
            if (_activeSessions.Remove(key))
                SessionAudioChanged?.Invoke(key.pid, key.exe, false);
        }
    }

    private void OnSessionDisconnected((int pid, string exe) key)
    {
        if (_activeSessions.Remove(key))
            SessionAudioChanged?.Invoke(key.pid, key.exe, false);

        lock (_sessionSinks)
        {
            var idx = _sessionSinks.FindIndex(s => s.sink.Matches(key));
            if (idx >= 0)
            {
                var (_, ptr, ctl2) = _sessionSinks[idx];
                _sessionSinks.RemoveAt(idx);
                try { ctl2.UnregisterAudioSessionNotification(ptr); } catch { }
                try { Marshal.Release(ptr); } catch { }
                try { Marshal.ReleaseComObject(ctl2); } catch { }
            }
        }
    }

    private static (int pid, string exe) GetSessionInfo(IAudioSessionControl2 ctl2)
    {
        uint pid = 0;
        try { ctl2.GetProcessId(out pid); } catch { }
        string exe = "unknown";
        if (pid > 0)
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                exe = p.ProcessName + ".exe";
            }
            catch { }
        }
        return ((int)pid, exe);
    }

    private static T Activate<T>(IMMDevice device, Guid iid)
    {
        device.Activate(iid, 0, IntPtr.Zero, out var obj);
        return (T)obj;
    }

    public void Stop()
    {
        foreach (var (sink, ptr, ctl2) in _sessionSinks)
        {
            try { ctl2.UnregisterAudioSessionNotification(ptr); } catch { }
            try { Marshal.Release(ptr); } catch { }
            try { Marshal.ReleaseComObject(ctl2); } catch { }
        }
        _sessionSinks.Clear();
        _activeSessions.Clear();

        if (_notificationSinkPtr != IntPtr.Zero && _sessionManager is not null)
        {
            try { _sessionManager.UnregisterSessionNotification(_notificationSinkPtr); } catch { }
            Marshal.Release(_notificationSinkPtr);
            _notificationSinkPtr = IntPtr.Zero;
        }
        _notificationSink = null;
        if (_device is not null) { Marshal.ReleaseComObject(_device); _device = null; }
        _sessionManager = null;
    }

    public void Dispose() => Stop();
}

// -- COM notification interfaces --

[ComImport, Guid("641DD20B-4D41-49CC-ABA3-1B6CB3F132BC"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    void OnSessionCreated(IAudioSessionControl newSession);
}

internal sealed class SessionNotificationSink : IAudioSessionNotification
{
    private readonly Action<IAudioSessionControl> _onCreated;
    public SessionNotificationSink(Action<IAudioSessionControl> onCreated) => _onCreated = onCreated;
    public void OnSessionCreated(IAudioSessionControl newSession) => _onCreated(newSession);
}

[ComImport, Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    void OnDisplayNameChanged(IntPtr newDisplayName, Guid eventContext);
    void OnIconPathChanged(IntPtr newIconPath, Guid eventContext);
    void OnSimpleVolumeChanged(float newVolume, int newMute, Guid eventContext);
    void OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, Guid eventContext);
    void OnGroupingParamChanged(Guid newGroupingParam, Guid eventContext);
    void OnStateChanged(AudioSessionState newState);
    void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason);
}

internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2,
}

internal enum AudioSessionDisconnectReason
{
    DisconnectReasonDeviceRemoval = 0,
    DisconnectReasonServerShutdown = 1,
    DisconnectReasonFormatNotSupported = 2,
    DisconnectReasonEndpointLost = 3,
    DisconnectReasonInvalidId = 4,
}

internal sealed class SessionEventsSink : IAudioSessionEvents
{
    private readonly (int pid, string exe) _key;
    private readonly Action<(int pid, string exe), AudioSessionState> _onStateChanged;
    private readonly Action<(int pid, string exe)> _onDisconnected;

    public SessionEventsSink(
        (int pid, string exe) key,
        Action<(int pid, string exe), AudioSessionState> onStateChanged,
        Action<(int pid, string exe)> onDisconnected)
    {
        _key = key;
        _onStateChanged = onStateChanged;
        _onDisconnected = onDisconnected;
    }

    public void OnDisplayNameChanged(IntPtr newDisplayName, Guid eventContext) { }
    public void OnIconPathChanged(IntPtr newIconPath, Guid eventContext) { }
    public void OnSimpleVolumeChanged(float newVolume, int newMute, Guid eventContext) { }
    public void OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, Guid eventContext) { }
    public void OnGroupingParamChanged(Guid newGroupingParam, Guid eventContext) { }
    public void OnStateChanged(AudioSessionState newState) => _onStateChanged(_key, newState);
    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => _onDisconnected(_key);
    public bool Matches((int pid, string exe) key) => _key.pid == key.pid && _key.exe == key.exe;
}

// --- COM CLSID ---
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator { }

internal static class AudioClsids
{
    internal static readonly Guid MMDeviceEnumerator = Guid.Parse("BCDE0395-E52F-467C-8E3D-C4579291692E");
}

// --- COM enums ---
internal enum EDataFlow { eRender, eCapture, eAll }
internal enum ERole { eConsole, eMultimedia, eCommunications }

// --- COM interfaces ---
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDevice ppDevices);
    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
    void RegisterEndpointNotificationCallback(IntPtr pNotify);
    void UnregisterEndpointNotificationCallback(IntPtr pNotify);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    void OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    void GetState(out uint pdwState);
}

[ComImport, Guid("77AA99A0-1BD6-484D-8F3D-8FB6E0E72E6C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(IntPtr AudioSessionGuid, uint StreamFlags, out IntPtr pSessionControl);
    void GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
    void RegisterSessionNotification(IntPtr pSessionNotification);
    void UnregisterSessionNotification(IntPtr pSessionNotification);
    void RegisterDuckNotification(IntPtr sessionId, IntPtr pDuckNotification);
    void UnregisterDuckNotification(IntPtr sessionId);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out uint SessionCount);
    void GetSession(uint SessionCount, out IAudioSessionControl Session);
}

[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    void GetState(out uint pRetVal);
    void GetDisplayName(out IntPtr pRetVal);
    void GetIconPath(out IntPtr pRetVal);
    void GetGroupingParam(out Guid pRetVal);
    void SetGroupingParam(Guid Override, Guid pGroupingParam);
    void RegisterAudioSessionNotification(IntPtr pNewNotifications);
    void UnregisterAudioSessionNotification(IntPtr pNewNotifications);
}

[ComImport, Guid("BFB7FF88-5589-4FB6-8758-4A4A4600E0E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    void GetState(out uint pRetVal);
    void GetDisplayName(out IntPtr pRetVal);
    void GetIconPath(out IntPtr pRetVal);
    void GetGroupingParam(out Guid pRetVal);
    void SetGroupingParam(Guid Override, Guid pGroupingParam);
    void RegisterAudioSessionNotification(IntPtr pNewNotifications);
    void UnregisterAudioSessionNotification(IntPtr pNewNotifications);
    // IAudioSessionControl2
    void GetSessionIdentifier(out IntPtr pRetVal);
    void GetSessionInstanceIdentifier(out IntPtr pRetVal);
    void GetProcessId(out uint pRetVal);
    void IsSystemSoundsSession();
    void SetDuckingPreference(bool optOut);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    void GetPeakValue(out float pfPeak);
    void GetMeteringChannelCount(out uint pnChannelCount);
    void GetChannelsPeakValues(uint u32ChannelCount, [Out] float[] afPeakValues);
    void QueryHardwareSupport(out uint pdwHardwareSupportMask);
}
