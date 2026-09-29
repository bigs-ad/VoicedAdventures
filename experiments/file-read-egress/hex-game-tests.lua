local ok, err = xpcall(function()
local queue, paths, handles = {}, {}, {}
local now=0
print=function() end
SlashCmdList={}
C_Timer={After=function(delay,fn) queue[#queue+1]={at=now+delay,fn=fn} end}
PlaySoundFile=function(path) paths[#paths+1]=path;return true,#paths end
StopSound=function(handle) handles[#handles+1]=handle end
dofile('experiments/file-read-egress/addon/VoicedAdventures/Main.lua')
QVRDiagnosticCommand('hex')
assert(#queue==1, 'hex sender should start')
QVRDiagnosticCommand('fast')
assert(#queue==1, 'shared busy guard')
while #queue>0 do local e=table.remove(queue,1);now=e.at;e.fn() end
assert(#paths==22 and #handles==22, '22 symbols and own stops')
local encoded=''
for i,path in ipairs(paths) do
    local hex=path:match('H([0-9A-F])%.wav$');assert(hex, 'exact hex path')
    encoded=encoded..hex;assert(handles[i]==i)
end
local frame=string.char(81,86,6,228,189,160,229,165,189)
local a,b=0,0
for i=1,#frame do a=(a+frame:byte(i))%255;b=(b+a)%255 end
frame=frame..string.char(a,b)
local expected=''
for i=1,#frame do expected=expected..string.format('%02X',frame:byte(i)) end
assert(encoded==expected,'same complete UTF-8 frame as binary')
assert(now<=2.3,'bounded nominal duration')
io.write('PASS hex paths, frame parity, timings, busy guard and own stops\n')
end,debug.traceback)
if not ok then io.stderr:write(err..'\n');os.exit(1) end
