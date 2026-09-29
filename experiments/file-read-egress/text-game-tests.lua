local ok, err = xpcall(function()
local fast = arg and arg[1] == 'fast'
local command = fast and 'fast' or 'text'
local queue, paths, handles = {}, {}, {}
local now = 0
print = function() end
SlashCmdList = {}
C_Timer = {After=function(delay,fn) queue[#queue+1]={at=now+delay,fn=fn} end}
PlaySoundFile=function(path) paths[#paths+1]=path; return true,#paths end
StopSound=function(handle) handles[#handles+1]=handle end
dofile('experiments/file-read-egress/addon/VoicedAdventures/Main.lua')
QVRDiagnosticCommand(command)
assert(#queue==1, 'text sender schedules sequentially')
QVRDiagnosticCommand('long')
assert(#queue==1, 'shared busy guard')
while #queue>0 do
    table.sort(queue,function(a,b)return a.at<b.at end)
    local e=table.remove(queue,1); now=e.at; e.fn()
end
assert(#paths==88 and #handles==88, '88 bits and own stops')
local bytes={}
for i=1,#paths,8 do
    local n=0
    for j=i,i+7 do
        local name=paths[j]:match('([BS])%.wav$'); assert(name)
        n=n*2+(name=='S' and 1 or 0)
        assert(handles[j]==j)
    end
    bytes[#bytes+1]=n
end
local expected={81,86,6,228,189,160,229,165,189}
local s1,s2=0,0
for i,b in ipairs(expected) do assert(bytes[i]==b); s1=(s1+b)%255; s2=(s2+s1)%255 end
assert(bytes[10]==s1 and bytes[11]==s2, 'Fletcher check')
assert(now <= (fast and 9 or 45), 'bounded duration')
queue={}; PlaySoundFile=function() return false,nil end
QVRDiagnosticCommand(command)
while #queue>0 do local e=table.remove(queue,1);now=e.at;e.fn() end
assert(#paths==88, 'failed playback aborts frame')
io.write('PASS text frame, UTF-8 bytes, checksum, sequential pacing, stops and failure abort\n')
end,debug.traceback)
if not ok then io.stderr:write(err..'\n');os.exit(1) end
