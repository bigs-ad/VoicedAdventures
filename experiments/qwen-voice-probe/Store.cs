using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Security.Cryptography;
namespace VoicedAdventures.Qwen {
    public sealed class QwenStore {
        readonly string directory;
        static readonly byte[] entropy=Encoding.UTF8.GetBytes("VoicedAdventures/Qwen/Beijing/v1");
        public QwenStore(string path){directory=Path.GetFullPath(path);}
        public static QwenStore Current(){return new QwenStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VoicedAdventures","Qwen"));}
        void Write(string name,byte[] bytes){
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,name),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public void SaveKey(string key){
            if(String.IsNullOrWhiteSpace(key)||key.Length>512||key.Contains("\r")||key.Contains("\n"))throw new QwenFailure("API Key 格式无效。");
            byte[] raw=Encoding.UTF8.GetBytes(key);
            try{Write("key.bin",ProtectedData.Protect(raw,entropy,DataProtectionScope.CurrentUser));}finally{Array.Clear(raw,0,raw.Length);}
        }
        public string LoadKey(){
            string path=Path.Combine(directory,"key.bin");if(!File.Exists(path))return "";
            if(new FileInfo(path).Length>8192)throw new QwenFailure("本机 Key 文件无效。");
            byte[] raw=ProtectedData.Unprotect(File.ReadAllBytes(path),entropy,DataProtectionScope.CurrentUser);
            try{return Encoding.UTF8.GetString(raw);}finally{Array.Clear(raw,0,raw.Length);}
        }
    }
}
