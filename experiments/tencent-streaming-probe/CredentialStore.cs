using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace QuestVoiceStreaming {
    public sealed class Credentials {
        public string AppId,SecretId,SecretKey;
        public bool Consent;
    }
    public sealed class CredentialStore {
        readonly string path;
        public string PreferencesPath {get {return path+".preferences.json";}}
        static readonly byte[] entropy=Encoding.UTF8.GetBytes("QuestVoiceStreaming/Credentials/v1");
        public CredentialStore(string filePath) { path=Path.GetFullPath(filePath); }
        public static CredentialStore ForCurrentUser() {
            return new CredentialStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"QuestVoiceProbe","streaming-credentials.bin"));
        }
        public void Save(string app,string id,string key,bool consent=false) {
            long number;
            if(!Int64.TryParse(app,out number)||number<=0||String.IsNullOrWhiteSpace(id)||String.IsNullOrWhiteSpace(key)||app.Length>256||id.Length>256||key.Length>256)
                throw new ArgumentException("Invalid credentials");
            byte[] plain=null;
            string temp=null;
            try {
                using(var data=new MemoryStream()) {
                    using(var writer=new BinaryWriter(data,Encoding.UTF8,true)) { writer.Write(2); writer.Write(app); writer.Write(id); writer.Write(key); writer.Write(consent); }
                    plain=data.ToArray();
                    Array.Clear(data.GetBuffer(),0,(int)data.Length);
                }
                byte[] encrypted=ProtectedData.Protect(plain,entropy,DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { file.Write(encrypted,0,encrypted.Length); file.Flush(true); }
                File.Move(temp,path,true); temp=null;
            } finally {
                if(plain!=null) Array.Clear(plain,0,plain.Length);
                if(temp!=null && File.Exists(temp)) File.Delete(temp);
            }
        }
        public Credentials Load() {
            byte[] encrypted;
            try {
                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    if(file.Length<=0||file.Length>16384) throw new CryptographicException("Invalid credential file");
                    encrypted=new byte[(int)file.Length]; file.ReadExactly(encrypted,0,encrypted.Length);
                }
            } catch(FileNotFoundException) { return null; } catch(DirectoryNotFoundException) { return null; }
            byte[] plain=ProtectedData.Unprotect(encrypted,entropy,DataProtectionScope.CurrentUser);
            try {
                using(var data=new MemoryStream(plain)) using(var reader=new BinaryReader(data,Encoding.UTF8)) {
                    int version=reader.ReadInt32();
                    if(version!=1 && version!=2) throw new CryptographicException("Unsupported credential file");
                    var result=new Credentials { AppId=reader.ReadString(),SecretId=reader.ReadString(),SecretKey=reader.ReadString() };
                    result.Consent=version>=2 && reader.ReadBoolean();
                    if(data.Position!=data.Length || result.AppId.Length>256 || result.SecretId.Length>256 || result.SecretKey.Length>256) throw new CryptographicException("Invalid credential file");
                    return result;
                }
            } finally { Array.Clear(plain,0,plain.Length); }
        }
        public void Clear() { try { File.Delete(path); } catch(DirectoryNotFoundException) { } }
    }
}
