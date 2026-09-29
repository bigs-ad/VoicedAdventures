local ok,err=xpcall(function()
local queue,paths={},{}
local now=0
print=function()end
SlashCmdList={}
C_Timer={After=function(delay,fn)queue[#queue+1]={at=now+delay,fn=fn}end}
PlaySoundFile=function(path)paths[#paths+1]=path;return true,#paths end
local stopped=0
StopSound=function(handle)stopped=stopped+1;assert(handle==stopped)end
dofile('experiments/file-read-egress/addon/VoicedAdventures/Main.lua')
QVRDiagnosticCommand('repeat')
assert(#queue==1,'repeat must start')
QVRDiagnosticCommand('hex');assert(#queue==1)
while #queue>0 do local e=table.remove(queue,1);now=e.at;e.fn()end
assert(#paths==stopped)
local bytes={}
for i=1,#paths,2 do
    local high=tonumber(paths[i]:match('H([0-9A-F])%.wav$'),16)
    local low=tonumber(paths[i+1]:match('H([0-9A-F])%.wav$'),16)
    bytes[#bytes+1]=high*16+low
end
local cursor=1
local payloads={}
for frame=1,3 do
    assert(bytes[cursor]==81 and bytes[cursor+1]==86)
    local length=bytes[cursor+2];assert(length>=50 and length<=255)
    local a,b=0,0
    for j=cursor,cursor+length+2 do a=(a+bytes[j])%255;b=(b+a)%255 end
    assert(bytes[cursor+length+3]==a and bytes[cursor+length+4]==b)
    local text=''
    for j=cursor+3,cursor+length+2 do text=text..string.char(bytes[j]) end
    assert(text:sub(1,2)==tostring(frame)..':')
    payloads[frame]=text:sub(3)
    cursor=cursor+length+5
end
assert(cursor==#bytes+1 and payloads[1]==payloads[2] and payloads[2]==payloads[3])
assert(now<60,'bounded repeat test')
io.write('PASS three numbered long UTF-8 frames, checksums, sequential own stops, bounded schedule\n')
end,debug.traceback)
if not ok then io.stderr:write(err..'\n');os.exit(1)end
