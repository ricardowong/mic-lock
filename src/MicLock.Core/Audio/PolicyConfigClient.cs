using System.Runtime.InteropServices;

namespace MicLock.Core.Audio;

public enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
    Count = 3
}

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class CPolicyConfigClient
{
}

[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bFlow, IntPtr ppFormat);
    [PreserveSig] int ResetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
    [PreserveSig] int SetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);
    [PreserveSig] int GetProcessingPeriod([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bFlow, IntPtr pDefaultPeriod, IntPtr pMinimumPeriod);
    [PreserveSig] int SetProcessingPeriod([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pPeriod);
    [PreserveSig] int GetShareMode([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
    [PreserveSig] int SetShareMode([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
    [PreserveSig] int GetPropertyValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pKey, IntPtr pProp);
    [PreserveSig] int SetPropertyValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pKey, IntPtr pProp);
    [PreserveSig] int SetDefaultEndpoint([In, MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, [In, MarshalAs(UnmanagedType.U4)] ERole eRole);
    [PreserveSig] int SetEndpointVisibility([In, MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int bVisible);
}

[ComImport]
[Guid("568b0058-072d-444d-b639-611082b45d97")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigVista
{
    [PreserveSig] int GetMixFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bFlow, IntPtr ppFormat);
    [PreserveSig] int SetDeviceFormat([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);
    [PreserveSig] int GetProcessingPeriod([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bFlow, IntPtr pDefaultPeriod, IntPtr pMinimumPeriod);
    [PreserveSig] int SetProcessingPeriod([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pPeriod);
    [PreserveSig] int GetShareMode([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
    [PreserveSig] int SetShareMode([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
    [PreserveSig] int GetPropertyValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pKey, IntPtr pProp);
    [PreserveSig] int SetPropertyValue([In, MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pKey, IntPtr pProp);
    [PreserveSig] int SetDefaultEndpoint([In, MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, [In, MarshalAs(UnmanagedType.U4)] ERole eRole);
    [PreserveSig] int SetEndpointVisibility([In, MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int bVisible);
}

/// <summary>
/// COM client implementing IPolicyConfig / IPolicyConfigVista to change default Windows audio endpoints.
/// Used for SoundSwitch-compatible recording device switching.
/// </summary>
public static class PolicyConfigClient
{
    public static bool SetDefaultEndpoint(string deviceId, ERole role)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;

        try
        {
            var client = new CPolicyConfigClient();
            if (client is IPolicyConfig policyConfig)
            {
                int hr = policyConfig.SetDefaultEndpoint(deviceId, role);
                return hr == 0;
            }

            if (client is IPolicyConfigVista policyVista)
            {
                int hr = policyVista.SetDefaultEndpoint(deviceId, role);
                return hr == 0;
            }
        }
        catch (Exception)
        {
            // COM activation failed or unsupported OS platform
        }

        return false;
    }

    public static bool SetDefaultDeviceAllRoles(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;

        bool success = false;
        try
        {
            var client = new CPolicyConfigClient();
            if (client is IPolicyConfig policyConfig)
            {
                int hrConsole = policyConfig.SetDefaultEndpoint(deviceId, ERole.Console);
                int hrMedia = policyConfig.SetDefaultEndpoint(deviceId, ERole.Multimedia);
                int hrComm = policyConfig.SetDefaultEndpoint(deviceId, ERole.Communications);
                success = (hrConsole == 0) || (hrMedia == 0) || (hrComm == 0);
            }
            else if (client is IPolicyConfigVista policyVista)
            {
                int hrConsole = policyVista.SetDefaultEndpoint(deviceId, ERole.Console);
                int hrMedia = policyVista.SetDefaultEndpoint(deviceId, ERole.Multimedia);
                int hrComm = policyVista.SetDefaultEndpoint(deviceId, ERole.Communications);
                success = (hrConsole == 0) || (hrMedia == 0) || (hrComm == 0);
            }
        }
        catch
        {
            success = false;
        }

        return success;
    }
}
