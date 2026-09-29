using System.Collections.Generic;
namespace QuestVoiceStreaming {
 public sealed class GameFocusState {
  readonly Dictionary<string,int> clients=new Dictionary<string,int>();
  public readonly Dictionary<string,string> Settings=new Dictionary<string,string>();
  public string Active {get;private set;}
  long since;bool explicitChoice,requiresChoice;
  public void Add(string key,int pid){clients[key]=pid;}
  public void Remove(string key){clients.Remove(key);Settings.Remove(key);}
  public void Clear(){if(Active!=null)requiresChoice=true;clients.Clear();Settings.Clear();Active=null;since=0;explicitChoice=false;}
  public bool Choose(string key,long now){
   if(key==null || !clients.ContainsKey(key))return false;
   explicitChoice=true;requiresChoice=true;
   if(key==Active)return false;
   Active=key;since=now;return true;
  }
  public bool Select(int foregroundPid,long now){
   string next=Active!=null && clients.ContainsKey(Active)?Active:null;
   if(Active!=null && next==null)requiresChoice=true;
   if(!explicitChoice && clients.Count>1){next=null;requiresChoice=true;}
   if(next==null && clients.Count==1 && !requiresChoice)foreach(var client in clients)next=client.Key;
   if(next==Active)return false;
   since=now;Active=next;return true;
  }
  public bool Accepts(string key,long firstEvent){return Active!=null && key==Active && clients.ContainsKey(key) && firstEvent>since;}
 }
 public sealed class AutoConnectionState {
  public bool Paused {get;private set;}
  public bool Busy {get;private set;}
  public void Pause(bool value){Paused=value;}
  public bool TryBegin(bool available){if(Paused || Busy || !available)return false;Busy=true;return true;}
  public void Finish(bool safeToWait){Busy=false;if(!safeToWait)Paused=true;}
 }
}
