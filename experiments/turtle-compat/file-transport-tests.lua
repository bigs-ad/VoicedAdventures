table.getn=table.getn or function(t) return #t end
local files={};ImportFile=function(name)return files[name] end
time=function()return 1 end;GetTime=function()return 1 end
ExportFile=function(name,value)files[name]=value end
dofile("experiments/turtle-compat/FileTransport.lua")
local queue={"first","second"}
assert(not VATurtleFileTransport.Update(queue) and #queue==2)
files.VoicedAdventures_session=string.rep("a",32)
assert(VATurtleFileTransport.Update(queue))
assert(not VATurtleFileTransport.Update(queue) and #queue==1)
local first=files.VoicedAdventures_outbox
assert(not VATurtleFileTransport.Update(queue) and #queue==1)
assert(files.VoicedAdventures_outbox==first)
files.VoicedAdventures_ack=string.rep("a",32)..":00000001000003E8:1"
VATurtleFileTransport.Update(queue)
assert(#queue==0 and files.VoicedAdventures_outbox~=first)
files.VoicedAdventures_session=string.rep("b",32)
assert(VATurtleFileTransport.Update(queue))
GetTime=function()return 2 end
dofile("experiments/turtle-compat/FileTransport.lua")
assert(VATurtleFileTransport.Update(queue))
queue={"third","fourth"};VATurtleFileTransport.Update(queue)
assert(string.find(files.VoicedAdventures_outbox,"00000001000007D0",1,true))
files.VoicedAdventures_ack=string.rep("b",32)..":00000001000003E8:1"
VATurtleFileTransport.Update(queue);assert(#queue==1,"old character ack must not release new packet")
print("PASS session handshake, ack pacing, no overwrite and receiver restart")
