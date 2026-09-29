local ok,err=xpcall(function()
local queue,paths,active,messages={}, {}, {}, {}
local maximum=0
print=function(...)local a={...};for i,v in ipairs(a)do a[i]=tostring(v)end;messages[#messages+1]=table.concat(a,' ')end
SlashCmdList={}
C_Timer={After=function(_,fn)queue[#queue+1]=fn end}
PlaySoundFile=function(path)
    paths[#paths+1]=path;active[#paths]=true
    local n=0;for _ in pairs(active)do n=n+1 end;maximum=math.max(maximum,n)
    return true,#paths
end
StopSound=function(handle)assert(active[handle]);active[handle]=nil end
dofile('experiments/file-read-egress/addon/VoicedAdventures/Main.lua')
QVRDiagnosticCommand('idbench');assert(#queue==1,'benchmark should start')
QVRDiagnosticCommand('hex');assert(#queue==1)
while #queue>0 do table.remove(queue,1)()end
assert(#paths==84 and maximum==4 and next(active)==nil,'bounded batches and own-handle cleanup')
local raw=''
for i=1,#paths,2 do
    raw=raw..string.char(tonumber(paths[i]:match('H([0-9A-F])%.wav$'),16)*16+tonumber(paths[i+1]:match('H([0-9A-F])%.wav$'),16))
end
for n=1,3 do
    local f=raw:sub((n-1)*14+1,n*14)
    assert(f:sub(1,3)=='QV'..string.char(9) and f:sub(4,12)=='ID:'..(100000+n))
    local a,b=0,0;for i=1,12 do a=(a+f:byte(i))%255;b=(b+a)%255 end
    assert(f:byte(13)==a and f:byte(14)==b)
end
assert(messages[#messages]:find('IDBENCH DONE'))
local calls=0;local stopped=0
PlaySoundFile=function()calls=calls+1;if calls==2 then error('fail')end;return true,100 end
StopSound=function(handle)assert(handle==100);stopped=stopped+1 end
QVRDiagnosticCommand('idbench')
while #queue>0 do table.remove(queue,1)()end
assert(calls==2 and stopped==1 and messages[#messages]:find('FAILED'),'partial batch stops own sound and aborts')
io.write('PASS ID benchmark framing, four-sound bound, busy guard, partial batch failure cleanup\n')
end,debug.traceback)
if not ok then io.stderr:write(err..'\n');os.exit(1)end
