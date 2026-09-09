using System.Runtime.InteropServices;

namespace TimeLens.TrayApp.Watchers;

public sealed class AudioMonitor : IDisposable
{
    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioSessionManager2? _sessionManager;
    private EndpointNotificationSink? _endpointNotificationSink;
    private IntPtr _endpointNotificationSinkPtr;
    private SessionNotificationSink? _notificationSink;
    private IntPtr _notificationSinkPtr;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int pid, string exe)> _activeSessions = new(StringComparer.Ordinal);
    private readonly List<(string id, SessionEventsSink sink, IntPtr ptr, IAudioSessionControl2 ctl2)> _sessionSinks = [];

    // COM callbacks must fire on the thread that registered them (must be STA).
    // Capture the main thread context on first Start() and marshal subsequent calls.
    private System.Threading.SynchronizationContext? _mainCtx;
    private volatile bool _enabled;

    /// <summary>Optional dispatcher for operations that must return to the tray STA thread.</summary>
    public Action<Action>? MessageLoopDispatcher { get; set; }

    public bool AnyAudioPlaying => !_activeSessions.IsEmpty;
    public bool IsPlayingFor(string exe) => _activeSessions.Values.Any(session => string.Equals(session.exe, exe, StringComparison.OrdinalIgnoreCase));
    public event Action<int, string, bool>? SessionAudioChanged;

    public void Start()
    {
        _enabled = true;
        if (System.Threading.Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            if (MessageLoopDispatcher is not null)
            {
                MessageLoopDispatcher(StartOnStaThread);
                return;
            }
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
        if (!_enabled) return;
        if (_sessionManager is not null || _notificationSinkPtr != IntPtr.Zero) return;
        _mainCtx = System.Threading.SynchronizationContext.Current;
        try
        {
            if (_deviceEnumerator is null)
            {
                _deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(AudioClsids.MMDeviceEnumerator)!)!;
                _endpointNotificationSink = new EndpointNotificationSink(OnDefaultDeviceChanged);
                _endpointNotificationSinkPtr = Marshal.GetComInterfaceForObject(
                    _endpointNotificationSink, typeof(IMMNotificationClient));
                _deviceEnumerator.RegisterEndpointNotificationCallback(_endpointNotificationSinkPtr);
            }
            ConnectDefaultEndpoint();
        }
        catch
        {
            Stop(); // Core Audio is optional; release any partially-created COM state.
        }
    }

    private void ConnectDefaultEndpoint()
    {
        _deviceEnumerator!.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out _device);
        _sessionManager = Activate<IAudioSessionManager2>(_device, typeof(IAudioSessionManager2).GUID);
        EnumerateExistingSessions();

        _notificationSink = new SessionNotificationSink(OnSessionCreated);
        _notificationSinkPtr = Marshal.GetComInterfaceForObject(_notificationSink, typeof(IAudioSessionNotification));
        _sessionManager.RegisterSessionNotification(_notificationSinkPtr);
    }

    private void OnDefaultDeviceChanged(EDataFlow flow, ERole role)
    {
        if (!_enabled || flow != EDataFlow.eRender || role != ERole.eMultimedia) return;
        void Reconnect()
        {
            if (!_enabled) return;
            ReleaseEndpointResources();
            try { ConnectDefaultEndpoint(); }
            catch { Stop(); }
        }

        try
        {
            if (MessageLoopDispatcher is not null) MessageLoopDispatcher(Reconnect);
            else if (_mainCtx is not null) _mainCtx.Post(_ => Reconnect(), null);
            else if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) Reconnect();
        }
        catch (Exception ex) { RuntimeDiagnostics.Write($"Audio endpoint change: {ex}"); }
    }

    private void EnumerateExistingSessions()
    {
        IAudioSessionEnumerator? sessions = null;
        try
        {
            _sessionManager!.GetSessionEnumerator(out sessions);
            sessions.GetCount(out var count);

            for (uint i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var session);
                var retained = false;
                try
                {
                    if (session is not IAudioSessionControl2 ctl2) continue;
                    ctl2.GetState(out var state);
                    // Register inactive sessions too: many applications create their
                    // audio session before playback starts.
                    retained = TrackSession(ctl2, (AudioSessionState)state);
                }
                catch { }
                finally { if (!retained) Marshal.ReleaseComObject(session); }
            }
        }
        catch { }
        finally { if (sessions is not null) Marshal.ReleaseComObject(sessions); }
    }

    private void OnSessionCreated(IAudioSessionControl session)
    {
        var retained = false;
        try
        {
            var ctl2 = session as IAudioSessionControl2;
            if (ctl2 == null) return;

            ctl2.GetState(out var state);
            retained = TrackSession(ctl2, (AudioSessionState)state);
        }
        catch { }
        finally
        {
            if (!retained) Marshal.ReleaseComObject(session);
        }
    }

    private bool TrackSession(IAudioSessionControl2 ctl2, AudioSessionState initialState)
    {
        var (pid, exe) = GetSessionInfo(ctl2);
        var id = GetSessionId(ctl2);
        var key = (id, pid, exe);
        lock (_sessionSinks)
        {
            if (_sessionSinks.Any(session => session.id == id))
            {
                return false;
            }
        }

        var sink = new SessionEventsSink(key, OnSessionStateChanged, OnSessionDisconnected);
        var ptr = Marshal.GetComInterfaceForObject(sink, typeof(IAudioSessionEvents));
        try { ctl2.RegisterAudioSessionNotification(ptr); }
        catch
        {
            Marshal.Release(ptr);
            return false;
        }

        lock (_sessionSinks)
            _sessionSinks.Add((id, sink, ptr, ctl2));

        if (initialState == AudioSessionState.Active) MarkActive(key);
        return true;
    }

    private void OnSessionStateChanged((string id, int pid, string exe) key, AudioSessionState state)
    {
        if (state == AudioSessionState.Active) MarkActive(key);
        else MarkInactive(key);
    }

    private void MarkActive((string id, int pid, string exe) key)
    {
        var alreadyPlaying = _activeSessions.Values.Any(value => value.pid == key.pid &&
            string.Equals(value.exe, key.exe, StringComparison.OrdinalIgnoreCase));
        if (_activeSessions.TryAdd(key.id, (key.pid, key.exe)) && !alreadyPlaying)
            SessionAudioChanged?.Invoke(key.pid, key.exe, true);
    }

    private void MarkInactive((string id, int pid, string exe) key)
    {
        if (!_activeSessions.TryRemove(key.id, out _)) return;
        if (!_activeSessions.Values.Any(value => value.pid == key.pid &&
            string.Equals(value.exe, key.exe, StringComparison.OrdinalIgnoreCase)))
            SessionAudioChanged?.Invoke(key.pid, key.exe, false);
    }

    private void OnSessionDisconnected((string id, int pid, string exe) key)
    {
        MarkInactive(key);

        lock (_sessionSinks)
        {
            var idx = _sessionSinks.FindIndex(s => s.id == key.id);
            if (idx >= 0)
            {
                var (_, _, ptr, ctl2) = _sessionSinks[idx];
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

    private static string GetSessionId(IAudioSessionControl2 ctl2)
    {
        IntPtr value = IntPtr.Zero;
        try
        {
            ctl2.GetSessionInstanceIdentifier(out value);
            return Marshal.PtrToStringUni(value) ?? Guid.NewGuid().ToString("N");
        }
        catch { return Guid.NewGuid().ToString("N"); }
        finally { if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value); }
    }

    private static T Activate<T>(IMMDevice device, Guid iid)
    {
        device.Activate(iid, 0, IntPtr.Zero, out var obj);
        return (T)obj;
    }

    public void Stop()
    {
        _enabled = false;
        ReleaseEndpointResources();

        if (_endpointNotificationSinkPtr != IntPtr.Zero && _deviceEnumerator is not null)
        {
            try { _deviceEnumerator.UnregisterEndpointNotificationCallback(_endpointNotificationSinkPtr); } catch { }
            try { Marshal.Release(_endpointNotificationSinkPtr); } catch { }
            _endpointNotificationSinkPtr = IntPtr.Zero;
        }
        _endpointNotificationSink = null;
        if (_deviceEnumerator is not null)
        {
            try { Marshal.ReleaseComObject(_deviceEnumerator); } catch { }
            _deviceEnumerator = null;
        }
    }

    private void ReleaseEndpointResources()
    {
        (string id, SessionEventsSink sink, IntPtr ptr, IAudioSessionControl2 ctl2)[] sinks;
        lock (_sessionSinks)
        {
            sinks = _sessionSinks.ToArray();
            _sessionSinks.Clear();
        }
        foreach (var (_, sink, ptr, ctl2) in sinks)
        {
            try { ctl2.UnregisterAudioSessionNotification(ptr); } catch { }
            try { Marshal.Release(ptr); } catch { }
            try { Marshal.ReleaseComObject(ctl2); } catch { }
        }
        var stoppedSessions = _activeSessions.Values
            .GroupBy(value => (value.pid, value.exe), new AudioProcessComparer())
            .Select(group => group.Key)
            .ToArray();
        _activeSessions.Clear();
        foreach (var stopped in stoppedSessions)
            SessionAudioChanged?.Invoke(stopped.pid, stopped.exe, false);

        if (_notificationSinkPtr != IntPtr.Zero && _sessionManager is not null)
        {
            try { _sessionManager.UnregisterSessionNotification(_notificationSinkPtr); } catch { }
            Marshal.Release(_notificationSinkPtr);
            _notificationSinkPtr = IntPtr.Zero;
        }
        _notificationSink = null;
        if (_sessionManager is not null) { Marshal.ReleaseComObject(_sessionManager); _sessionManager = null; }
        if (_device is not null) { Marshal.ReleaseComObject(_device); _device = null; }
    }

    public void Dispose() => Stop();

    private sealed class AudioProcessComparer : IEqualityComparer<(int pid, string exe)>
    {
        public bool Equals((int pid, string exe) x, (int pid, string exe) y) =>
            x.pid == y.pid && string.Equals(x.exe, y.exe, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((int pid, string exe) value) =>
            HashCode.Combine(value.pid, StringComparer.OrdinalIgnoreCase.GetHashCode(value.exe));
    }
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
    private readonly (string id, int pid, string exe) _key;
    private readonly Action<(string id, int pid, string exe), AudioSessionState> _onStateChanged;
    private readonly Action<(string id, int pid, string exe)> _onDisconnected;

    public SessionEventsSink(
        (string id, int pid, string exe) key,
        Action<(string id, int pid, string exe), AudioSessionState> onStateChanged,
        Action<(string id, int pid, string exe)> onDisconnected)
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
}

[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

internal sealed class EndpointNotificationSink : IMMNotificationClient
{
    private readonly Action<EDataFlow, ERole> _onDefaultDeviceChanged;
    public EndpointNotificationSink(Action<EDataFlow, ERole> onDefaultDeviceChanged) =>
        _onDefaultDeviceChanged = onDefaultDeviceChanged;
    public void OnDeviceStateChanged(string deviceId, uint newState) { }
    public void OnDeviceAdded(string deviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) =>
        _onDefaultDeviceChanged(flow, role);
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
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
