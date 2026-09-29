local ok,err=xpcall(function()
local worstGap=0
for _,fps in ipairs({30,60,144}) do
    for _,length in ipairs({24,200,1000}) do
        local tick,tasks,live,paths,serial=0,{},{},{},0
        local frame,lastSend,firstTextAt=nil,nil,nil
        local previousGap=0
        SlashCmdList={};QuestVoiceResourceDB={};QVRIdentity=nil
        time=function() return 1790300000 end
        CreateFrame=function() return {RegisterEvent=function()end,SetScript=function()end} end
        C_Timer={After=function(delay,fn) tasks[#tasks+1]={at=tick+math.max(1,math.ceil(delay*fps)),fn=fn} end}
        PlaySoundFile=function(path)
            local name=path:match('([^\\]+)%.wav$')
            if lastSend and tick~=lastSend then previousGap=math.max(previousGap,tick-lastSend) end
            lastSend=tick;serial=serial+1;live[serial]=true
            local n=0;for _ in pairs(live) do n=n+1 end;assert(n<=8,'same handle bound')
            paths[#paths+1]=name
            if name=='B' then assert(not frame,'no overlapping frames');frame={}
            elseif name=='S' then
                assert(frame and #frame%2==0,'complete nibble pairs')
                local bytes={};for i=1,#frame,2 do bytes[#bytes+1]=frame[i]*16+frame[i+1] end
                assert(bytes[1]==81 and bytes[2]==65 and #bytes==bytes[4]+6,'frame length')
                local a,b=0,0;for i=1,#bytes-2 do a=(a+bytes[i])%255;b=(b+a)%255 end
                assert(a==bytes[#bytes-1] and b==bytes[#bytes],'wire checksum unchanged')
                if bytes[3]==2 and not firstTextAt then firstTextAt=tick end
                frame=nil
            else assert(frame,'symbol outside frame');frame[#frame+1]=tonumber(name:sub(2),16) end
            return true,serial
        end
        StopSound=function(handle) assert(live[handle],'only stop own handle once');live[handle]=nil end
        dofile('experiments/file-read-egress/addon/VoicedAdventures/Main.lua')
        dofile('experiments/file-read-egress/addon/VoicedAdventures/Auto.lua')
        assert(QVRPlayback.Play(string.rep('x',length),true,{SpeakerName='NPC',SpeakerSex='male',Source='npc'}))
        local steps=0
        while #tasks>0 do
            table.sort(tasks,function(a,b)return a.at<b.at end)
            local nextTask=table.remove(tasks,1);tick=nextTask.at;nextTask.fn()
            steps=steps+1;assert(steps<4000,'bounded scheduler')
        end
        assert(firstTextAt and not frame and next(live)==nil,'complete and all resources released')
        worstGap=math.max(worstGap,previousGap)
        io.write(string.format('SIM fps=%d chars=%d first-text=%.0fms maximum-batch-gap=%d frames\n',fps,length,firstTextAt*1000/fps,previousGap))
    end
end
assert(worstGap<=1,'redundant idle frame between released batch and next batch')
print('PASS simulated frame pacing, checksum, resource limits and completion')
end,debug.traceback)
if not ok then io.stderr:write(err..'\n');os.exit(1) end
