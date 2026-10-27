using SpireCompanion.Core;
using System.Text.Json.Nodes;

internal static class VaultChecks
{
    private static void Require(bool value){if(!value)throw new Exception("Vault assertion failed");}
    public static int Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"spire-vault-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);int passed=0;
        void Check(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
        try
        {
            var store=new EncryptedProfileStore(root);var endpoint="https://api.deepseek.com";
            using var alice=store.Create("Alice","test-password-123");
            using var bob=store.Create("Bob","test-password-123");
            Check("Encrypted profiles persist keys across instances without plaintext API keys",()=>
            {
                alice.SaveApiKey(endpoint,"synthetic-alice-secret");
                var path=Path.Combine(root,alice.Profile.Id.ToString("N")+".vault.json");
                var text=File.ReadAllText(path);Require(!text.Contains("synthetic-alice-secret")&&!text.Contains("test-password-123"));
                using var reopened=new EncryptedProfileStore(root).Unlock(alice.Profile.Id,"test-password-123");
                Require(reopened.GetApiKey(endpoint)=="synthetic-alice-secret");
            });
            Check("Same password does not share keys between profiles or providers",()=>
            {
                bob.SaveApiKey(endpoint,"synthetic-bob-secret");alice.SaveApiKey("https://other.example","synthetic-other-secret");
                Require(bob.GetApiKey(endpoint)=="synthetic-bob-secret"&&alice.GetApiKey(endpoint)=="synthetic-alice-secret");
                Require(bob.GetApiKey("https://other.example")==null);
                alice.DeleteApiKey(endpoint);Require(alice.GetApiKey(endpoint)==null&&bob.GetApiKey(endpoint)=="synthetic-bob-secret");
                Require(alice.GetApiKey("https://other.example")=="synthetic-other-secret");
            });
            Check("Incorrect password cannot unlock or overwrite profile",()=>
            {
                var path=Path.Combine(root,alice.Profile.Id.ToString("N")+".vault.json");var before=File.ReadAllBytes(path);
                bool failed=false;try{using var bad=store.Unlock(alice.Profile.Id,"wrong-password-123");}catch(ProfileVaultException){failed=true;}
                Require(failed&&before.SequenceEqual(File.ReadAllBytes(path)));
            });
            Check("Cached random data key auto-unlocks independently of password",()=>
            {
                var key=bob.ExportAutoUnlockKey();
                try{using var auto=store.UnlockWithKey(bob.Profile.Id,key);Require(auto.GetApiKey(endpoint)=="synthetic-bob-secret");
                    bool failed=false;try{using var bad=store.UnlockWithKey(alice.Profile.Id,key);}catch(ProfileVaultException){failed=true;}Require(failed);}
                finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);}
            });
            Check("Authenticated encryption detects file tampering",()=>
            {
                var path=Path.Combine(root,bob.Profile.Id.ToString("N")+".vault.json");var original=File.ReadAllText(path);
                try
                {
                    var json=JsonNode.Parse(original)!;var data=Convert.FromBase64String(json["Payload"]!["Ciphertext"]!.GetValue<string>());
                    data[0]^=1;json["Payload"]!["Ciphertext"]=Convert.ToBase64String(data);File.WriteAllText(path,json.ToJsonString());
                    bool failed=false;try{using var bad=store.Unlock(bob.Profile.Id,"test-password-123");}catch(ProfileVaultException){failed=true;}Require(failed);
                }
                finally{File.WriteAllText(path,original);}
            });
            Check("Unsupported formats and duplicate names fail without erasing data",()=>
            {
                bool duplicate=false;try{using var bad=store.Create("alice","test-password-123");}catch(ProfileVaultException){duplicate=true;}Require(duplicate);
                var path=Path.Combine(root,bob.Profile.Id.ToString("N")+".vault.json");var original=File.ReadAllText(path);
                try
                {
                    var json=JsonNode.Parse(original)!;json["Version"]=99;var changed=json.ToJsonString();File.WriteAllText(path,changed);
                    bool failed=false;try{using var bad=store.Unlock(bob.Profile.Id,"test-password-123");}catch(ProfileVaultException){failed=true;}
                    Require(failed&&File.ReadAllText(path)==changed);
                }
                finally{File.WriteAllText(path,original);}
                Require(store.ListProfiles().Length==2);
            });
            Check("Locking a session prevents later secret access",()=>
            {using var locked=store.Unlock(bob.Profile.Id,"test-password-123");locked.Dispose();bool failed=false;try{_=locked.GetApiKey(endpoint);}catch(ObjectDisposedException){failed=true;}Require(failed);});
            return passed;
        }
        finally{Directory.Delete(root,true);}
    }
}
