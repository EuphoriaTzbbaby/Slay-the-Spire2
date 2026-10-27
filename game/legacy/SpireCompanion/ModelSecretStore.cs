using System.Runtime.InteropServices;
using System.Text;

namespace SpireCompanion;

/// <summary>Uses the current user's OS credential store. Never writes a plaintext key file.</summary>
internal static class ModelSecretStore
{
    public static string Name => OperatingSystem.IsMacOS() ? "macOS 钥匙串" : "Windows 凭据管理器";
    private static string Account(string endpoint) => new Uri(endpoint.Trim()).GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    public static string? Load(string endpoint) => OperatingSystem.IsMacOS() ? Mac.Read(Account(endpoint)) :
        OperatingSystem.IsWindows() ? Windows.Read(Account(endpoint)) : throw new PlatformNotSupportedException("该系统暂不支持记住密钥。");
    public static void Save(string endpoint, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("密钥为空，不会覆盖已保存的密钥。");
        if (OperatingSystem.IsMacOS()) Mac.Write(Account(endpoint), value.Trim());
        else if (OperatingSystem.IsWindows()) Windows.Write(Account(endpoint), value.Trim());
        else throw new PlatformNotSupportedException("该系统暂不支持记住密钥。");
    }
    public static void Delete(string endpoint)
    {
        if (OperatingSystem.IsMacOS()) Mac.Delete(Account(endpoint));
        else if (OperatingSystem.IsWindows()) Windows.Delete(Account(endpoint));
        else throw new PlatformNotSupportedException("该系统暂不支持记住密钥。");
    }
    // New portable vaults cache a random data key here, never the model API Key.
    public static byte[]? LoadUnlockKey(Guid profile)
    {
        var account = "profile-key:" + profile.ToString("N");
        var text = OperatingSystem.IsMacOS() ? Mac.Read(account) : OperatingSystem.IsWindows() ? Windows.Read(account)
            : throw new PlatformNotSupportedException("该系统不支持自动解锁。");
        if (text == null) return null;
        try { return Convert.FromBase64String(text); }
        catch (FormatException) { throw new InvalidOperationException("自动解锁凭据无效，请使用档案密码。"); }
    }
    public static void SaveUnlockKey(Guid profile, byte[] key)
    {
        var account = "profile-key:" + profile.ToString("N"); var text = Convert.ToBase64String(key);
        if (OperatingSystem.IsMacOS()) Mac.Write(account, text);
        else if (OperatingSystem.IsWindows()) Windows.Write(account, text);
        else throw new PlatformNotSupportedException("该系统不支持自动解锁。");
    }
    public static void DeleteUnlockKey(Guid profile)
    {
        var account = "profile-key:" + profile.ToString("N");
        if (OperatingSystem.IsMacOS()) Mac.Delete(account);
        else if (OperatingSystem.IsWindows()) Windows.Delete(account);
        else throw new PlatformNotSupportedException("该系统不支持自动解锁。");
    }
    private static Exception Failure(int status) => new InvalidOperationException($"无法访问系统凭据存储（错误码 {status}），请检查系统授权。");

    private static class Mac
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string Core = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private static readonly IntPtr security = NativeLibrary.Load(Security);
        private static readonly IntPtr core = NativeLibrary.Load(Core);
        private static IntPtr Constant(string name, bool cf = false) => Marshal.ReadIntPtr(NativeLibrary.GetExport(cf ? core : security, name));
        [DllImport(Core)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
        [DllImport(Core)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint count, IntPtr keys, IntPtr values);
        [DllImport(Core)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
        [DllImport(Core)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint count);
        [DllImport(Core)] private static extern nint CFDataGetLength(IntPtr data);
        [DllImport(Core)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
        [DllImport(Core)] private static extern void CFRelease(IntPtr value);
        [DllImport(Security)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
        [DllImport(Security)] private static extern int SecItemAdd(IntPtr attributes, out IntPtr result);
        [DllImport(Security)] private static extern int SecItemUpdate(IntPtr query, IntPtr attributes);
        [DllImport(Security)] private static extern int SecItemDelete(IntPtr query);
        private sealed class Query : IDisposable
        {
            public IntPtr Value { get; } = CFDictionaryCreateMutable(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
            private readonly List<IntPtr> owned = [];
            public Query(string account)
            {
                Set("kSecClass", Constant("kSecClassGenericPassword"));
                AddString("kSecAttrService", "SpireCompanion.ModelAPI"); AddString("kSecAttrAccount", account);
            }
            public void Set(string key, IntPtr value) => CFDictionarySetValue(Value, Constant(key), value);
            private void AddString(string key, string text)
            {
                var value = CFStringCreateWithCString(IntPtr.Zero, text, 0x08000100); owned.Add(value); Set(key, value);
            }
            public void Dispose() { CFRelease(Value); foreach (var value in owned) CFRelease(value); }
        }
        public static string? Read(string account)
        {
            using var query = new Query(account); query.Set("kSecReturnData", Constant("kCFBooleanTrue", true));
            query.Set("kSecMatchLimit", Constant("kSecMatchLimitOne"));
            var status = SecItemCopyMatching(query.Value, out var result);
            if (status == -25300) return null;
            if (status != 0) throw Failure(status);
            try
            {
                int count = checked((int)CFDataGetLength(result));
                if (count > 16384) throw new InvalidOperationException("系统中保存的密钥长度异常。");
                var data = new byte[count]; Marshal.Copy(CFDataGetBytePtr(result), data, 0, count);
                try { return Encoding.UTF8.GetString(data); } finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(data); }
            }
            finally { CFRelease(result); }
        }
        public static void Write(string account, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value); var data = CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            var attributes = CFDictionaryCreateMutable(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
            try
            {
                CFDictionarySetValue(attributes, Constant("kSecValueData"), data);
                using var query = new Query(account);
                int status = SecItemUpdate(query.Value, attributes);
                if (status == -25300)
                {
                    query.Set("kSecValueData", data);
                    status = SecItemAdd(query.Value, out var result);
                    if (result != IntPtr.Zero) CFRelease(result);
                }
                if (status != 0) throw Failure(status);
            }
            finally { CFRelease(attributes); CFRelease(data); }
        }
        public static void Delete(string account)
        { using var query = new Query(account); int status = SecItemDelete(query.Value); if (status is not (0 or -25300)) throw Failure(status); }
    }

    private static class Windows
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags; public uint Type;
            public string TargetName; public string? Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize; public IntPtr CredentialBlob;
            public uint Persist; public uint AttributeCount; public IntPtr Attributes;
            public string? TargetAlias; public string? UserName;
        }
        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
        [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("Advapi32.dll")] private static extern void CredFree(IntPtr value);
        private static string Target(string account) => "SpireCompanion.ModelAPI:" + account;
        public static string? Read(string account)
        {
            if (!CredRead(Target(account), 1, 0, out var pointer))
            { int error = Marshal.GetLastWin32Error(); if (error == 1168) return null; throw Failure(error); }
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                if (credential.CredentialBlobSize > 16384) throw new InvalidOperationException("系统中保存的密钥长度异常。");
                var bytes = new byte[credential.CredentialBlobSize]; Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                try { return Encoding.UTF8.GetString(bytes); } finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
            }
            finally { CredFree(pointer); }
        }
        public static void Write(string account, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value); var pointer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                var credential = new Credential { Type = 1, TargetName = Target(account), CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = pointer, Persist = 2, UserName = "SpireCompanion" };
                if (!CredWrite(ref credential, 0)) throw Failure(Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.Copy(new byte[bytes.Length], 0, pointer, bytes.Length); Marshal.FreeHGlobal(pointer);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            }
        }
        public static void Delete(string account)
        { if (!CredDelete(Target(account), 1, 0)) { int error=Marshal.GetLastWin32Error(); if (error != 1168) throw Failure(error); } }
    }
}
