local succeeded, failure = xpcall(function()
local queue, calls, stops, messages = {}, {}, {}, {}
local now = 0
print = function(...) local a={...}; for i,v in ipairs(a) do a[i]=tostring(v) end; messages[#messages+1]=table.concat(a," ") end
SlashCmdList = {}
C_Timer = {After=function(delay, fn) queue[#queue+1]={at=now+delay, fn=fn} end}
PlaySoundFile=function(path) calls[#calls+1]=path; return true, 100+#calls end
StopSound=function(handle) stops[#stops+1]=handle end
dofile("experiments/file-read-egress/addon/VoicedAdventures/Main.lua")
QVRDiagnosticCommand("long")
assert(#queue==9, "nine long-mode trials required")
QVRDiagnosticCommand("test")
assert(#queue==9, "shared reentry guard")
while #queue>0 do
    table.sort(queue,function(a,b) return a.at<b.at end)
    local event=table.remove(queue,1); now=event.at; event.fn()
end
assert(#calls==9 and #stops==9)
for i=1,9 do
    local symbol = i<=3 and "A" or (i<=6 and "B" or "S")
    assert(calls[i]:sub(-5)==symbol..".wav", "three repeats per size")
    assert(stops[i]==100+i, "only own sound handle is stopped")
end
assert(now==13.75 and messages[#messages]:find("LONG DONE"), "bounded completion after stop")
calls={}; stops={}; queue={}
PlaySoundFile=function() return false,nil end
QVRDiagnosticCommand("long")
while #queue>0 do table.sort(queue,function(a,b)return a.at<b.at end);local e=table.remove(queue,1);now=e.at;e.fn() end
assert(#stops==0, "do not stop invalid handles")
StopSound=nil
QVRDiagnosticCommand("long")
assert(#queue==0, "StopSound is required")
io.write("PASS long resource schedule, own-handle stops, failures and bounded completion\n")
end, debug.traceback)
if not succeeded then io.stderr:write(failure.."\n"); os.exit(1) end
