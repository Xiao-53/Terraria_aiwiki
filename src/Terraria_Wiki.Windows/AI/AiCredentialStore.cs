using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Terraria_Wiki.Windows.AI;

internal interface IAiCredentialStore
{
    string? Read();
    void Write(string secret);
    void Delete();
}

internal sealed class WindowsAiCredentialStore : IAiCredentialStore
{
    internal const string DefaultTargetName = "TerrariaAIWiki.Windows/AI/APIKey";
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxCredentialBlobBytes = 2560;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _targetName;

    public WindowsAiCredentialStore(string targetName = DefaultTargetName)
    {
        if (string.IsNullOrWhiteSpace(targetName) || targetName.Length > 32767)
            throw new ArgumentException("Credential target name is invalid.", nameof(targetName));
        _targetName = targetName;
    }

    public string? Read()
    {
        EnsureWindows();
        if (!CredRead(_targetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
                return null;
            throw CredentialFailure("ai-credential-read-failed", "Windows 凭据无法读取。", error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlobSize == 0)
                return string.Empty;
            if (credential.CredentialBlob == IntPtr.Zero
                || credential.CredentialBlobSize > MaxCredentialBlobBytes)
            {
                throw new AiConfigurationException("ai-credential-invalid", "Windows 凭据内容无效。");
            }

            var bytes = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new AiConfigurationException(
                    "ai-credential-invalid",
                    "Windows 凭据内容无效。",
                    exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void Write(string secret)
    {
        EnsureWindows();
        AiRuntimeConfiguration.ValidateApiKey(secret);
        var bytes = StrictUtf8.GetBytes(secret);
        if (bytes.Length > MaxCredentialBlobBytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new AiConfigurationException(
                "model_authentication",
                "AI API Key 超过 Windows Credential Manager 的安全存储上限。");
        }

        var blob = IntPtr.Zero;
        try
        {
            blob = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = _targetName,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName,
            };
            if (!CredWrite(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw CredentialFailure("ai-credential-write-failed", "Windows 凭据无法保存。", error);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (blob != IntPtr.Zero)
            {
                for (var index = 0; index < bytes.Length; index++)
                    Marshal.WriteByte(blob, index, 0);
                Marshal.FreeHGlobal(blob);
            }
        }
    }

    public void Delete()
    {
        EnsureWindows();
        if (CredDelete(_targetName, CredentialTypeGeneric, 0))
            return;
        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw CredentialFailure("ai-credential-delete-failed", "Windows 凭据无法删除。", error);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Credential Manager requires Windows.");
    }

    private static AiConfigurationException CredentialFailure(string code, string message, int error) =>
        new(code, message, new Win32Exception(error));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        int type,
        int reservedFlag,
        out IntPtr credentialPointer);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
