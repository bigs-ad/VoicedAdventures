using System;
using System.IO;
using System.Text;
using QuestVoiceStreaming;
class CredentialTests {
    public static void Run() {
        string dir=Path.Combine(Path.GetTempPath(),"QuestVoice-credential-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,"credentials.bin"); var store=new CredentialStore(path);
        try {
            new CredentialStore(Path.Combine(dir,"missing","credentials.bin")).Clear();
            if(store.Load()!=null) throw new Exception("missing file");
            store.Save("123456","fake-id-only","fake-secret-only");
            byte[] file=File.ReadAllBytes(path);
            if(Encoding.UTF8.GetString(file).Contains("fake-secret") || Encoding.Unicode.GetString(file).Contains("fake-secret")) throw new Exception("plaintext persisted");
            var restored=new CredentialStore(path).Load();
            if(restored.AppId!="123456"||restored.SecretId!="fake-id-only"||restored.SecretKey!="fake-secret-only") throw new Exception("roundtrip");
            if(restored.Consent) throw new Exception("default consent");
            store.Save("123456","fake-id-only","fake-secret-only",true);
            if(!store.Load().Consent) throw new Exception("checked restore");
            store.Save("123456","fake-id-only","fake-secret-only",false);
            if(store.Load().Consent) throw new Exception("unchecked restore");
            using(var legacy=new MemoryStream()) {
                using(var writer=new BinaryWriter(legacy,Encoding.UTF8,true)) { writer.Write(1); writer.Write("123456"); writer.Write("legacy-id"); writer.Write("legacy-key"); }
                File.WriteAllBytes(path,System.Security.Cryptography.ProtectedData.Protect(legacy.ToArray(),Encoding.UTF8.GetBytes("QuestVoiceStreaming/Credentials/v1"),System.Security.Cryptography.DataProtectionScope.CurrentUser));
            }
            if(store.Load().Consent || store.Load().SecretId!="legacy-id") throw new Exception("legacy consent default");
            store.Save("654321","second-id","second-secret");
            if(store.Load().AppId!="654321" || Directory.GetFiles(dir).Length!=1) throw new Exception("atomic replace");
            try { store.Save("","","bad"); throw new Exception("invalid accepted"); } catch(ArgumentException) { }
            if(store.Load().SecretKey!="second-secret") throw new Exception("invalid overwrite");
            File.WriteAllBytes(path,new byte[]{1,2,3});
            try { store.Load(); throw new Exception("corruption accepted"); } catch(System.Security.Cryptography.CryptographicException) { }
            store.Clear(); store.Clear();
            if(store.Load()!=null || File.Exists(path)) throw new Exception("clear");
            Console.WriteLine("PASS credential persistence: encrypted roundtrip, update, validation, corruption, clear (dummy data only)");
        } finally { if(File.Exists(path)) File.Delete(path); Directory.Delete(dir); }
    }
}
