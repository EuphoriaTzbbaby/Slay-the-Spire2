using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpireCompanion.Core;

public sealed record LocalProfileInfo(Guid Id, string Name);
public sealed class ProfileVaultException(string message) : Exception(message);

/// <summary>Portable encrypted files; platform-specific code only caches a random data key.</summary>
public sealed class EncryptedProfileStore(string directory)
{
    private const int Iterations = 600_000;
    private const int MaximumFileBytes = 1024 * 1024;
    private sealed record Box(byte[] Nonce, byte[] Ciphertext, byte[] Tag);
    private sealed record Document(int Version, Guid Id, string Name, string Kdf, int Iterations, byte[] Salt, Box WrappedKey, Box Payload);
    private string PathFor(Guid id) => Path.Combine(directory, id.ToString("N") + ".vault.json");
    public LocalProfileInfo[] ListProfiles() => !Directory.Exists(directory) ? [] :
        Directory.GetFiles(directory, "*.vault.json").Select(path =>
        {
            if (!Guid.TryParseExact(Path.GetFileName(path).Split('.')[0], "N", out var id))
                throw new ProfileVaultException("档案文件名无效，请检查本地档案目录。");
            var document = Read(id); return new LocalProfileInfo(id, document.Name);
        }).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public Session Create(string name, string password)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 64) throw new ArgumentException("档案名应为 1 到 64 字。");
        ValidatePassword(password);
        Directory.CreateDirectory(directory);
        using var registryLock = AcquireLock(Path.Combine(directory, ".profiles.lock"));
        if (ListProfiles().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ProfileVaultException("档案名已存在，请选择解锁而非重复创建。");
        var id = Guid.NewGuid(); var salt = RandomNumberGenerator.GetBytes(16);
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var wrappingKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            var wrapped = Encrypt(dataKey, wrappingKey, Aad(id, name, "key"));
            var empty = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>());
            var document = new Document(1, id, name, "PBKDF2-SHA256", Iterations, salt, wrapped, Encrypt(empty, dataKey, Aad(id, name, "payload")));
            Write(document);
            return new Session(this, new(id, name), dataKey.ToArray());
        }
        finally { CryptographicOperations.ZeroMemory(wrappingKey); CryptographicOperations.ZeroMemory(dataKey); }
    }
    public Session Unlock(Guid id, string password)
    {
        ValidatePassword(password);
        var document = Read(id);
        var wrappingKey = Rfc2898DeriveBytes.Pbkdf2(password, document.Salt, document.Iterations, HashAlgorithmName.SHA256, 32);
        byte[]? dataKey = null;
        try
        {
            dataKey = Decrypt(document.WrappedKey, wrappingKey, Aad(id, document.Name, "key"));
            ValidatePayload(document, dataKey);
            return new Session(this, new(id, document.Name), dataKey.ToArray());
        }
        catch (CryptographicException) { throw new ProfileVaultException("密码不正确或档案已损坏，未修改原文件。"); }
        finally { CryptographicOperations.ZeroMemory(wrappingKey); if (dataKey != null) CryptographicOperations.ZeroMemory(dataKey); }
    }
    public Session UnlockWithKey(Guid id, byte[] key)
    {
        if (key.Length != 32) throw new ProfileVaultException("自动解锁凭据无效，请输入档案密码。");
        var document = Read(id);
        try { ValidatePayload(document, key); return new Session(this, new(id, document.Name), key.ToArray()); }
        catch (CryptographicException) { throw new ProfileVaultException("自动解锁凭据失效，请输入档案密码。"); }
    }
    private Document Read(Guid id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) throw new ProfileVaultException("本地档案不存在。");
        if (new FileInfo(path).Length > MaximumFileBytes) throw new ProfileVaultException("档案文件过大，已拒绝读取。");
        try
        {
            var d = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(path)) ?? throw new JsonException();
            if (d.Version != 1 || d.Id != id || string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 64
                || d.Kdf != "PBKDF2-SHA256" || d.Iterations != Iterations || d.Salt?.Length != 16
                || d.WrappedKey?.Nonce?.Length != 12 || d.WrappedKey?.Tag?.Length != 16 || d.WrappedKey?.Ciphertext?.Length != 32
                || d.Payload?.Nonce?.Length != 12 || d.Payload?.Tag?.Length != 16 || d.Payload.Ciphertext == null)
                throw new ProfileVaultException("档案格式或版本无效，不会覆盖原文件。");
            return d;
        }
        catch (JsonException) { throw new ProfileVaultException("档案文件损坏，未修改原文件。"); }
    }
    private void Write(Document document)
    {
        var path = PathFor(document.Id); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private Dictionary<string, string> ReadSecrets(Document document, byte[] key)
    {
        var bytes = Decrypt(document.Payload, key, Aad(document.Id, document.Name, "payload"));
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(bytes) ?? throw new ProfileVaultException("档案内容无效。"); }
        catch (JsonException) { throw new ProfileVaultException("档案内容无效。"); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private void ValidatePayload(Document document, byte[] key) => _ = ReadSecrets(document, key);
    private void Update(Session session, string origin, string? secret)
    {
        using var writeLock = AcquireLock(PathFor(session.Profile.Id) + ".lock");
        var document = Read(session.Profile.Id); var secrets = ReadSecrets(document, session.Key);
        if (secret == null) secrets.Remove(origin); else secrets[origin] = secret;
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(secrets);
        try { Write(document with { Payload = Encrypt(plaintext, session.Key, Aad(document.Id, document.Name, "payload")) }); }
        finally { CryptographicOperations.ZeroMemory(plaintext); secrets.Clear(); }
    }
    private static FileStream AcquireLock(string path)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new ProfileVaultException("档案正被其他进程使用，请稍后重试。"); }
    }
    private static byte[] Aad(Guid id, string name, string purpose) => Encoding.UTF8.GetBytes($"SpireCompanion/v1/{id:N}/{name}/{purpose}");
    private static Box Encrypt(byte[] plaintext, byte[] key, byte[] aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var ciphertext = new byte[plaintext.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        return new(nonce, ciphertext, tag);
    }
    private static byte[] Decrypt(Box box, byte[] key, byte[] aad)
    {
        var plaintext = new byte[box.Ciphertext.Length];
        try { using var aes = new AesGcm(key, 16); aes.Decrypt(box.Nonce, box.Ciphertext, box.Tag, plaintext, aad); return plaintext; }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
    }
    private static void ValidatePassword(string password)
    { if (password.Length is < 8 or > 1024) throw new ArgumentException("档案解锁密码应为 8 到 1024 字符。"); }
    private static string Origin(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("服务地址无效。");
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
    public sealed class Session : IDisposable
    {
        private readonly EncryptedProfileStore store;
        private byte[]? key;
        internal byte[] Key => key ?? throw new ObjectDisposedException(nameof(Session));
        public LocalProfileInfo Profile { get; }
        internal Session(EncryptedProfileStore store, LocalProfileInfo profile, byte[] key) { this.store = store; Profile = profile; this.key = key; }
        public byte[] ExportAutoUnlockKey() => Key.ToArray();
        public string? GetApiKey(string endpoint)
        {
            var secrets = store.ReadSecrets(store.Read(Profile.Id), Key);
            try { return secrets.GetValueOrDefault(Origin(endpoint)); }
            finally { secrets.Clear(); }
        }
        public void SaveApiKey(string endpoint, string secret)
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.Length > 16384) throw new ArgumentException("API Key 为空或过长。");
            store.Update(this, Origin(endpoint), secret.Trim());
        }
        public void DeleteApiKey(string endpoint) => store.Update(this, Origin(endpoint), null);
        public void Dispose() { if (key != null) { CryptographicOperations.ZeroMemory(key); key = null; } }
    }
}
