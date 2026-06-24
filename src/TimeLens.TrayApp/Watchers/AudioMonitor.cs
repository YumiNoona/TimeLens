using System.Runtime.InteropServices;

namespace TimeLens.TrayApp.Watchers;

public sealed class AudioMonitor : IDisposable
{
    private Timer? _pollTimer;
    private HashSet<(int pid, string exe)> _previousPlaying = [];

    public bool AnyAudioPlaying { get; private set; }
    public event Action<int, string, bool>? SessionAudioChanged;

    public void Start()
    {
        PollAudio();
        _pollTimer = new Timer(_ => PollAudio(), null, 30_000, 30_000);
    }

    private void PollAudio()
    {
        try
        {
            var currentPlaying = new HashSet<(int pid, string exe)>();

            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(AudioClsids.MMDeviceEnumerator)!)!;

            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);

            var mgr = Activate<IAudioSessionManager2>(device, typeof(IAudioSessionManager2).GUID);
            mgr.GetSessionEnumerator(out var sessions);
            sessions.GetCount(out var count);

            for (uint i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var session);
                var ctl2 = session as IAudioSessionControl2;
                if (ctl2 == null) continue;

                var meter = session as IAudioMeterInformation;
                if (meter == null) continue;

                meter.GetPeakValue(out var peak);

                if (peak > 0.001f)
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

                    currentPlaying.Add(((int)pid, exe));
                }

                Marshal.ReleaseComObject(session);
                Marshal.ReleaseComObject(meter);
                Marshal.ReleaseComObject(ctl2);
            }

            foreach (var key in currentPlaying)
                if (!_previousPlaying.Contains(key))
                    SessionAudioChanged?.Invoke(key.pid, key.exe, true);

            foreach (var key in _previousPlaying)
                if (!currentPlaying.Contains(key))
                    SessionAudioChanged?.Invoke(key.pid, key.exe, false);

            _previousPlaying = currentPlaying;
            AnyAudioPlaying = currentPlaying.Count > 0;

            Marshal.ReleaseComObject(sessions);
            Marshal.ReleaseComObject(mgr);
            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
        catch
        {
            // Core Audio API not available or error — silently degrade
        }
    }

    private static T Activate<T>(IMMDevice device, Guid iid)
    {
        var activationParams = IntPtr.Zero;
        var type = typeof(T);
        device.Activate(iid, 0, activationParams, out var obj);
        return (T)obj;
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
    }
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
