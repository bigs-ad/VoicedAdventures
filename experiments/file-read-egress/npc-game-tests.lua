local ok,err=xpcall(function()
local command = arg and arg[1] == 'burst' and 'npcburst' or 'npc'
local queue,paths,messages={}, {}, {}
local active,maximum={},0
local shown=true
local handler
local body=string.char(228,189,160,229,165,189):rep(30)
print=function(...)local a={...};for i,v in ipairs(a)do a[i]=tostring(v)end;messages[#messages+1]=table.concat(a,' ')end
SlashCmdList={}
C_Timer={After=function(delay,fn)queue[#queue+1]=fn end}
CreateFrame=function()return {RegisterEvent=function()end,SetScript=function(_,kind,fn)handler=fn end}end
GossipFrame={IsVisible=function()return shown end}
QuestFrame={IsVisible=function()return false end}
C_GossipInfo={GetText=function()return body end}
PlaySoundFile=function(path)
    paths[#paths+1]=path;active[#paths]=true
    local n=0;for _ in pairs(active)do n=n+1 end;maximum=math.max(maximum,n)
    return true,#paths
end
StopSound=function(handle)assert(active[handle]);active[handle]=nil end
dofile('experiments/file-read-egress/addon/VoicedAdventures/Main.lua')
local original=QVRDiagnosticCommand
QVRDiagnosticCommand=function(value)original(value=='npc' and command or value)end
assert(handler,'NPC event listener required')
QVRDiagnosticCommand(command);assert(#queue==0,'no event means no stale send')
handler(nil,'GOSSIP_SHOW')
assert(#queue==0,'no automatic send')
QVRDiagnosticCommand(command);assert(#queue==1)
while #queue>0 do table.remove(queue,1)()end
assert(#paths==252,'UTF16 compact payload 121 bytes plus 5 header/checksum')
assert(next(active)==nil and maximum==(command=='npcburst' and 8 or 1),'bounded own handles')
local bytes={}
for i=1,#paths,2 do bytes[#bytes+1]=tonumber(paths[i]:match('H([0-9A-F])%.wav$'),16)*16+tonumber(paths[i+1]:match('H([0-9A-F])%.wav$'),16)end
assert(bytes[1]==81 and bytes[2]==86 and bytes[3]==121 and bytes[4]==255)
for i=5,124,4 do assert(bytes[i]==96 and bytes[i+1]==79 and bytes[i+2]==125 and bytes[i+3]==89)end
local a,b=0,0
for i=1,#bytes-2 do a=(a+bytes[i])%255;b=(b+a)%255 end
assert(bytes[#bytes-1]==a and bytes[#bytes]==b)
paths={};shown=false
QVRDiagnosticCommand('npc');assert(#queue==0,'hidden dialog refused')
shown=true;handler(nil,'GOSSIP_CLOSED')
QVRDiagnosticCommand('npc');assert(#queue==0,'closed dialog refused')
handler(nil,'GOSSIP_SHOW');body=body:rep(10)
QVRDiagnosticCommand('npc');assert(#queue==0 and messages[#messages]:find('TOO LONG'),'no silent truncation')
body='hello';QVRDiagnosticCommand('npc')
while #queue>0 do table.remove(queue,1)()end
assert(#paths==20,'ASCII stays UTF8 and byte-exact')
paths={};body=string.char(255)
QVRDiagnosticCommand('npc');assert(#queue==0 and messages[#messages]:find('INVALID TEXT'))
body=string.char(228,189,160,229,165,189):rep(10)..string.char(240,159,152,128)
QVRDiagnosticCommand('npc')
while #queue>0 do table.remove(queue,1)()end
assert(#paths==100,'supplementary code point uses surrogate pair')
local words={}
for i=1,#paths,2 do words[#words+1]=tonumber(paths[i]:match('H([0-9A-F])%.wav$'),16)*16+tonumber(paths[i+1]:match('H([0-9A-F])%.wav$'),16)end
assert(words[45]==61 and words[46]==216 and words[47]==0 and words[48]==222)
paths={};body='quest content';GetQuestText=function()return body end
shown=false;QuestFrame.IsVisible=function()return true end;GossipFrame=nil
QVRDiagnosticCommand('npc');assert(#queue==0,'gossip cannot use unrelated quest window')
handler(nil,'QUEST_DETAIL');QVRDiagnosticCommand('npc')
while #queue>0 do table.remove(queue,1)()end
assert(#paths==36,'quest API selected by event')
handler(nil,'QUEST_FINISHED');QVRDiagnosticCommand('npc');assert(#queue==0)
handler(nil,'GOSSIP_SHOW');LWDialogFrame={IsVisible=function()return true end}
body='lorewalker';QVRDiagnosticCommand('npc')
assert(#queue==1,'optional Lorewalker visibility')
while #queue>0 do table.remove(queue,1)()end
io.write('PASS real NPC API capture, compact Chinese, no autorun, visibility/close guards, length refusal\n')
end,debug.traceback)
if not ok then io.stderr:write(err..'\n');os.exit(1)end
