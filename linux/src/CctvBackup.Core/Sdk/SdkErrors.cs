using CctvBackup.Core.Localization;

namespace CctvBackup.Core.Sdk;

/// <summary>Readable names for the XM SDK error codes (enum SDK_RET_CODE, read from the CMS symbols).</summary>
public static class SdkErrors
{
    private static readonly Dictionary<int, (string Name, string Hint)> Table = new()
    {
        [-10000] = ("SDK_NOTVALID", "The SDK rejected the call."),
        [-10001] = ("NO_INIT", "SDK is not initialised."),
        [-10002] = ("ILLEGAL_PARAM", "A parameter is invalid."),
        [-10003] = ("INVALID_HANDLE", "The login session is no longer valid."),
        [-10004] = ("SDK_UNINIT_ERROR", "SDK uninitialised."),
        [-10005] = ("SDK_TIMEOUT", "The request timed out."),
        [-10006] = ("SDK_MEMORY_ERROR", "SDK ran out of memory."),
        [-10007] = ("SDK_NET_ERROR", "Network error."),
        [-10008] = ("SDK_OPEN_FILE_ERROR", "Could not open the output file."),
        [-10009] = ("SDK_UNKNOWNERROR", "Unknown SDK error."),
        [-11000] = ("DEV_VER_NOMATCH", "Device firmware version does not match."),
        [-11001] = ("SDK_NOTSUPPORT", "Not supported by this device."),
        [-11200] = ("OPEN_CHANNEL_ERROR", "Could not open the channel."),
        [-11201] = ("CLOSE_CHANNEL_ERROR", "Could not close the channel."),
        [-11202] = ("SUB_CONNECT_ERROR", "Secondary connection failed."),
        [-11203] = ("SUB_CONNECT_SEND_ERROR", "Secondary connection send failed."),
        [-11204] = ("NATCONNET_REACHED_MAX", "Too many NAT/cloud connections to this device."),
        [-11205] = ("NOTSUPPORT", "Not supported."),
        [-11206] = ("NOTVALID", "Not valid."),
        [-11207] = ("TCPCONNET_REACHED_MAX", "Too many TCP connections to this device."),
        [-11208] = ("OPENEDPREVIEW", "Preview already open."),
        [-11300] = ("NOPOWER", "This account has no permission for that operation."),
        [-11301] = ("PASSWORD_NOT_VALID", "Wrong password."),
        [-11302] = ("LOGIN_USER_NOEXIST", "That user does not exist on the DVR."),
        [-11303] = ("USER_LOCKED", "Account is locked after too many failed logins. Wait and try again."),
        [-11304] = ("USER_IN_BLACKLIST", "Account is blacklisted."),
        [-11305] = ("USER_HAS_USED", "That user is already logged in elsewhere."),
        [-11306] = ("USER_NOT_LOGIN", "Not logged in."),
        [-11307] = ("CONNECT_DEVICE_ERROR", "Could not connect to the device."),
        [-11500] = ("CTRL_PAUSE_ERROR", "Pause/resume failed."),
        [-11501] = ("SDK_NOTFOUND", "Nothing found."),
        [-11502] = ("CFG_NOT_ENABLE", "Feature not enabled on the device."),
        [-11600] = ("SOCKET_ERROR", "Socket error."),
        [-11601] = ("SOCKET_CONNECT", "Could not open a socket to the server."),
        [-11602] = ("SOCKET_DOMAIN", "Could not resolve the server name."),
        [-11603] = ("SOCKET_SEND", "Could not send data."),
        [-11604] = ("ARSP_NO_DEVICE", "The cloud server does not know this device ID (wrong ID or device offline)."),
        [-11605] = ("ARSP_BUSING", "The cloud server is busy."),
        [-11606] = ("ARSP_BUSING_SELECT", "The cloud server is busy."),
        [-11607] = ("ARSP_BUSING_RECVICE", "The cloud server is busy."),
        [-11608] = ("CONNECTSERVER_ERROR", "Could not connect to the cloud server."),
        [-11609] = ("CONNECT_AGNET", "Could not connect through the cloud relay."),
        [-11610] = ("CONNECT_NAT", "Could not connect through NAT."),
        [-11611] = ("CONNECT_FAILED", "Connection failed."),
        [-11612] = ("CONNECT_FULL", "The device has no free connections."),
        [-11613] = ("CLOUD_LOGIN_ERR", "Cloud login error."),
        [-11614] = ("NO_CONNECT_FRONT", "No connection to the front end."),
        [-11615] = ("LOGIN_FULL", "The device has reached its maximum number of logins."),
        [-11619] = ("ARSP_USER_NOEXIST", "The cloud server says that user does not exist."),
        [-11620] = ("ARSP_PASSWORD_ERROR", "The cloud server says the password is wrong."),
        [-11621] = ("ARSP_QUERY_ERROR", "The cloud server query failed."),
        [-12000] = ("NAT_INIT_TIMEOUT", "NAT/cloud initialisation timed out."),
    };

    public static string Describe(int code)
    {
        if (code == 0) return "OK";
        return Table.TryGetValue(code, out var e)
            ? $"{e.Name} ({code}): {Loc.T(e.Hint)}"
            : Loc.F("Unknown SDK error ({0})", code);
    }
}
