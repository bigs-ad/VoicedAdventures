using System;
using QuestVoiceStreaming;
static class AutoConnectionTests {
 static void Check(bool value) {if(!value)throw new Exception("connection assertion");}
 static int Main() {
  var s=new AutoConnectionState();Check(!s.TryBegin(false));Check(s.TryBegin(true));Check(!s.TryBegin(true));
  s.Finish(true);Check(s.TryBegin(true));s.Pause(true);Check(!s.TryBegin(true));s.Finish(true);Check(!s.TryBegin(true));
  s.Pause(false);Check(s.TryBegin(true));s.Finish(false);Check(s.Paused);Check(!s.TryBegin(true));
  s.Pause(false);Check(s.TryBegin(true));s.Finish(true);Check(!s.Paused);Check(!s.Busy);
  var focus=new GameFocusState();
  focus.Add("a",10);focus.Add("b",20);
  Check(!focus.Select(99,100));Check(focus.Active==null);
  Check(!focus.Select(10,110));Check(focus.Active==null);
  Check(focus.Choose("a",110));Check(focus.Accepts("a",111));Check(!focus.Accepts("b",111));
  focus.Settings["a"]="settings:system:12";focus.Settings["b"]="settings:system:16";
  Check(!focus.Select(99,120));Check(focus.Active=="a");
  Check(!focus.Select(20,125));Check(focus.Active=="a");
  Check(focus.Choose("b",130));Check(!focus.Accepts("b",129));Check(focus.Accepts("b",131));
  Check(focus.Settings[focus.Active]=="settings:system:16");
  Check(focus.Choose("a",140));Check(focus.Settings[focus.Active]=="settings:system:12");
  focus.Remove("a");Check(focus.Select(99,150));Check(focus.Active==null);
  Check(!focus.Select(20,151));Check(!focus.Accepts("a",152));
  focus.Remove("b");Check(!focus.Select(99,160));Check(focus.Active==null);
  focus.Clear();focus.Add("new-pid-10",10);Check(!focus.Select(10,170));Check(!focus.Accepts("a",180));Check(!focus.Settings.ContainsKey("a"));
  Check(focus.Choose("new-pid-10",180));Check(!focus.Accepts("new-pid-10",180));Check(focus.Accepts("new-pid-10",181));
  var single=new GameFocusState();single.Add("one",1);Check(single.Select(0,200));single.Add("two",2);Check(single.Select(1,201));Check(single.Active==null);
  Console.WriteLine("Auto connection state checks passed");return 0;
 }
}
