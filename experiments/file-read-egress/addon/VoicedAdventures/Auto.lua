-- Automatic transport is loaded after Main.lua.
if not QVRTransport or not CreateFrame or not C_Timer then return end
local transport = QVRTransport
local base = "Interface\\AddOns\\VoicedAdventures\\Sounds\\"
local pending, sending, waiting, generation = nil, false, false, 0
local activeItem
local diagnostics={calls=0,frames=0,failures=0,bytes=0}
local queue={}
local pump
local function number(value, count)
    local bytes={}
    for i=1,count do bytes[i]=string.char(value%256);value=math.floor(value/256) end
    return table.concat(bytes)
end
local session=number(time(),4)..number(math.random(1,2147483646),4)
local serial=0
local questCompletion=false
local function split(text)
    local compact=transport.Compact(text)
    if #compact<=235 then return {compact} end
    local segments, start={},1
    while start<=#text do
        local limit=#segments==0 and 81 or 235
        local cursor, finish, boundary=start,start-1,nil
        while cursor<=#text do
            local lead=text:byte(cursor)
            local width=lead<128 and 1 or lead<224 and 2 or lead<240 and 3 or 4
            local last=cursor+width-1
            if #transport.Compact(text:sub(start,last))>limit then break end
            finish=last
            local char=text:sub(cursor,last)
            if #transport.Compact(text:sub(start,last))>=limit/2 and
                (char:match('[%.%!%?%,%;:\n]') or char=='。' or char=='！' or char=='？' or char=='，' or char=='；' or char=='：') then boundary=last end
            cursor=last+1
        end
        if cursor<=#text and boundary then finish=boundary end
        assert(finish>=start,'segment-progress')
        segments[#segments+1]=transport.Compact(text:sub(start,finish));start=finish+1
    end
    return segments
end
local function preferences()
    local db=type(QuestVoiceResourceDB)=="table" and QuestVoiceResourceDB or {}
    local provider=db.provider=="tencent" and "tencent" or "system"
    local rate=type(db.rate)=="number" and db.rate or 10
    if rate<6 or rate>20 or rate~=math.floor(rate) then rate=10 end
    local volume=type(db.volume)=="number" and db.volume or 100
    if volume<0 or volume>200 or volume~=math.floor(volume) then volume=100 end
    return provider,rate,db.auto~=false,db.queue==true,volume
end
local function settingsCommand()
    local provider,rate,_,queued,volume=preferences()
    return "settings:"..provider..":"..string.format("%d",rate)..":"..(queued and "queue" or "interrupt")..":"..string.format("%d",volume)
end
local function metadataPayload(metadata)
    metadata=metadata or {}
    local output={}
    for i,key in ipairs({"SpeakerName","NpcId","SpeakerRace","SpeakerSex","QuestTitle","Source"}) do
        local value=type(metadata[key])=="string" and metadata[key] or ""
        local limit=({64,20,40,6,96,8})[i]
        if value:find("[%z\1-\31\127]") or not pcall(transport.Compact,value) then value="" end
        while #value>limit do value=value:sub(1,-2);while #value>0 and not pcall(transport.Compact,value) do value=value:sub(1,-2) end end
        if key=="NpcId" and not value:match("^%d+$") then value="" end
        if key=="SpeakerSex" and value~="male" and value~="female" then value="" end
        if key=="Source" and value~="npc" and value~="questlog" and value~="book" and value~="turnin" and value~="npc-m" and value~="quest-m" and value~="book-m" and value~="turnin-m" then value="" end
        output[#output+1]=string.char(#value)..value
    end
    return table.concat(output)
end
local function scopedMetadata(metadata)
    if type(GetRealmName)~='function' or type(GetBuildInfo)~='function' or type(UnitName)~='function' then return nil end
    local realmOk,realm=pcall(GetRealmName)
    local buildOk,_,build=pcall(GetBuildInfo)
    local playerOk,player=pcall(UnitName,'player')
    if not realmOk or not buildOk or not playerOk or type(realm)~='string' or type(player)~='string' or
        realm=='' or player=='' then return nil end
    build=tostring(build);if not build:match('^%d+$') then return nil end
    local output={};local size=12
    local keys={'SpeakerName','NpcId','SpeakerRace','SpeakerSex','QuestTitle','Source'}
    local values={};metadata=metadata or {}
    for _,key in ipairs(keys) do values[#values+1]=type(metadata[key])=='string' and metadata[key] or '' end
    values[7],values[8],values[9]=realm,build,player
    for i,value in ipairs(values) do
        local limit=({64,20,40,6,96,8,96,10,72})[i]
        if #value>limit or value:find('[%z\1-\31\127]') or not pcall(transport.Compact,value) then return nil end
        if i==2 and value~='' and not value:match('^%d+$') then return nil end
        if i==4 and value~='' and value~='male' and value~='female' then return nil end
        if i==6 and value~='' and value~='npc' and value~='questlog' and value~='book' and value~='turnin' and value~='npc-m' and value~='quest-m' and value~='book-m' and value~='turnin-m' then return nil end
        output[#output+1]=string.char(#value)..value
        -- Reserve the longest source tag for both manual and automatic playback.
        size=size+1+(i==6 and 8 or #value)
    end
    if size>255 then return nil end
    return table.concat(output)
end
local function evidenceMetadata(metadata)
    if type(metadata)~='table' or not metadata.SpeakerKind then return nil end
    local scoped=scopedMetadata(metadata);if not scoped then return nil end
    local function choice(value,allowed) return value=='' or allowed[value]==true end
    local id=metadata.QuestId or ''
    if type(id)~='string' or (id~='' and (not id:match('^[1-9]%d*$') or not tonumber(id) or tonumber(id)>2147483647)) then return nil end
    local level=metadata.QuestLevel
    if level~=nil and (type(level)~='number' or level~=math.floor(level) or level < -1 or level>9999) then return nil end
    local values={id,level~=nil and string.format('%d',level) or '',metadata.QuestStage or '',metadata.RaceSource or '',metadata.SexSource or '',metadata.IdentityConflict and '1' or '',metadata.SpeakerKind,metadata.QuestLevelSource or ''}
    local sources={unit=true,model=true,['npc-table']=true,observed=true,manual=true,legacy=true,unknown=true,document=true}
    if not choice(values[3],{detail=true,progress=true,complete=true,gossip=true,greeting=true,questlog=true,book=true}) or
        not choice(values[4],sources) or not choice(values[5],sources) or
        not choice(values[7],{npc=true,narrator=true}) or not choice(values[8],{['quest-api']=true,['quest-log']=true,unknown=true}) then return nil end
    if metadata.IdentityConflict~=nil and type(metadata.IdentityConflict)~='boolean' then return nil end
    local output={scoped};local size=12+#scoped
    for i,value in ipairs(values) do
        if type(value)~='string' or #value>({10,4,10,16,16,1,8,16})[i] or value:find('[%z\1-\31\127]') then return nil end
        output[#output+1]=string.char(#value)..value;size=size+1+#value
    end
    -- Keep manual and automatic metadata equally representable at the size boundary.
    size=size+8-#(metadata.Source or '')
    if size>255 then return nil end
    return table.concat(output)
end
local function dialogue(text, force, metadata)
    local parts=split(text)
    if #parts>64 then return nil end
    local encodedMetadata=evidenceMetadata(metadata)
    local metadataKind=6
    if not encodedMetadata then encodedMetadata=scopedMetadata(metadata);metadataKind=encodedMetadata and 5 or 4 end
    if not encodedMetadata then encodedMetadata=metadataPayload(metadata) end
    -- Completion-based cooldown belongs to the assistant, not a permanent addon ID.
    serial=serial+1;local id=serial
    local a,b=1.0,0.0
    for i=1,#text do a=(a+text:byte(i))%65521;b=(b+a)%65521 end
    local head=session..number(id,4)
    local tail=number(#text,2)..number(b*65536+a,4)
    local frames={}
    for i,payload in ipairs(parts) do frames[i]=head..string.char(i-1,#parts)..tail..payload end
    frames[0]=settingsCommand()
    frames[-1]=head..encodedMetadata
    return {kind=2,frames=frames,index=0,id=id,identity=encodedMetadata..text,metadataKind=metadataKind}
end

local function pack(kind, payload)
    local data = "QA" .. string.char(kind, #payload) .. payload
    local a, b = 0, 0
    for i=1,#data do a=(a+data:byte(i))%255; b=(b+a)%255 end
    return data .. string.char(a,b)
end

local function pulse(names, nextStep, failed)
    local handles, valid = {}, true
    for _, name in ipairs(names) do
        local ok, played, handle = pcall(PlaySoundFile, base .. name .. ".wav", "Master")
        diagnostics.calls=diagnostics.calls+1
        if not ok or not played or type(handle)~="number" then valid=false; break end
        handles[#handles+1]=handle
    end
    C_Timer.After(0, function()
        for _,handle in ipairs(handles) do if not pcall(StopSound,handle) then valid=false end end
        -- Handles have spent one frame alive and are now released; no idle frame is needed.
        if valid then nextStep() else failed() end
    end)
end

pump = function()
    if sending then return end
    if not pending then pending=table.remove(queue,1) end
    if not pending then return end
    if not transport.TryBegin() then
        if not waiting then
            waiting=true
            C_Timer.After(0.05,function() waiting=false; pump() end)
        end
        return
    end
    local item=pending; pending=nil; sending=true
    activeItem=item
    local data, index = pack(item.index==-1 and (item.metadataKind or 4) or item.index==0 and 3 or item.kind,item.frames[item.index]), 0
    local function finish()
        diagnostics.frames=diagnostics.frames+1
        if not item.cancelled and item.index<#item.frames then item.index=item.index==0 and -1 or item.index==-1 and 1 or item.index+1;pending=item end
        sending=false;activeItem=nil; transport.Finish(); pump()
    end
    local function failed() diagnostics.failures=diagnostics.failures+1;print("冒险有声：发送失败，已清空发送队列");pending=nil;queue={};sending=false;activeItem=nil;transport.Finish() end
    local function batch()
        if index==#data*2 then pulse({"S"},finish,failed); return end
        local names={}
        for _=1,8 do
            if index==#data*2 then break end
            local byte=data:byte(math.floor(index/2)+1)
            local value=index%2==0 and math.floor(byte/16) or byte%16
            names[#names+1]=string.format("H%X",value); index=index+1
        end
        pulse(names,batch,failed)
    end
    pulse({"B"},batch,failed)
end

local function replace(item)
    if activeItem then activeItem.cancelled=true end
    queue={}
    pending=item;pump()
end
local function reject(reason)
    if #queue+(activeItem and 1 or 0)+(pending and 1 or 0)>=16 then
        print("冒险有声：发送队列已满，本次未发送");return
    end
    queue[#queue+1]={kind=1,frames={reason},index=1};pump()
end
QVRPlayback={}
function QVRPlayback.ScopeAvailable(metadata) return scopedMetadata(metadata)~=nil end
function QVRPlayback.Diagnostic()
    return "VA SEND bytes="..diagnostics.bytes.." calls="..diagnostics.calls.." frames="..diagnostics.frames.." failed="..diagnostics.failures.." busy="..tostring(sending).." queued="..#queue
end
local function playResolved(text,force,metadata)
    if type(text)~="string" or #text==0 or #text>4096 then return false,"EMPTY OR TOO LONG" end
    local marked={};for key,value in pairs(metadata or {}) do marked[key]=value end
    local source=marked.Source or "npc"
    if source=="npc" and questCompletion then source="turnin" end
    if force then source=source=="questlog" and "quest-m" or source.."-m" end
    marked.Source=source;metadata=marked
    local valid,item=pcall(dialogue,text,force,metadata)
    if not valid or not item then return false,"INVALID TEXT" end
    if force then generation=generation+1 end
    if not force and activeItem and not activeItem.cancelled and item.identity==activeItem.identity then return true end
    if not force then
        if pending and pending.identity==item.identity then return true end
        for _,queued in ipairs(queue) do if queued.identity==item.identity then return true end end
    end
    if #queue+(activeItem and 1 or 0)+(pending and 1 or 0)>=16 then return false,"QUEUE FULL" end
    queue[#queue+1]=item;pump();return true
end
local bookActive=false
function QVRPlayback.Play(text,force,metadata)
    diagnostics.bytes=type(text)=="string" and #text or 0
    if metadata and metadata.Source=="npc" and questCompletion then
        local captured={};for key,value in pairs(metadata) do captured[key]=value end
        captured.Source="turnin";metadata=captured
    end
    if metadata and metadata.Source=="book" and type(text)=="string" and #text>0 and #text<=4096 then QVRPlayback.Stop() end
    bookActive=metadata and metadata.Source=="book" or false
    -- Freeze the reward page before clicking Complete replaces its NPC/UI state.
    if metadata and metadata.Source=="turnin" then return playResolved(text,force,metadata) end
    if not QVRIdentity or not metadata or not metadata._identity then return playResolved(text,force,metadata) end
    if type(text)~="string" or #text==0 or #text>4096 then return false,"EMPTY OR TOO LONG" end
    if force then generation=generation+1 end
    local ticket=generation
    QVRIdentity.Resolve(metadata,function(resolved)
        if ticket~=generation then return end
        resolved.Source=metadata.Source
        local ok,why=playResolved(text,force,resolved)
        if not ok then print("冒险有声：",why) end
    end)
    return true
end
function QVRPlayback.Stop()
    bookActive=false
    generation=generation+1
    replace({kind=3,frames={"stop"},index=1})
end
function QVRPlayback.Settings(provider,rate,queued,volume)
    if (provider~="system" and provider~="tencent") or type(rate)~="number" or
        rate<6 or rate>20 or rate~=math.floor(rate) then return false end
    if queued~=nil and type(queued)~="boolean" then return false end
    if volume~=nil and (type(volume)~="number" or volume<0 or volume>200 or volume~=math.floor(volume)) then return false end
    local previousProvider,previousRate,_,previousQueued,previousVolume=preferences()
    if queued==nil then queued=previousQueued end
    if volume==nil then volume=previousVolume end
    if provider==previousProvider and rate==previousRate and queued==previousQueued and volume==previousVolume then return true end
    if type(QuestVoiceResourceDB)~="table" then QuestVoiceResourceDB={} end
    QuestVoiceResourceDB.provider=provider;QuestVoiceResourceDB.rate=rate
    QuestVoiceResourceDB.queue=queued
    QuestVoiceResourceDB.volume=volume
    -- Already captured dialogues retain their settings and complete their tails.
    local item={kind=3,frames={settingsCommand()},index=1}
    local last=queue[#queue]
    if last and last.kind==3 and last.frames[1]~="stop" then queue[#queue]=item
    elseif #queue+(activeItem and 1 or 0)+(pending and 1 or 0)<16 then queue[#queue+1]=item end
    pump();return true
end
QVRPlayback.Preferences=preferences

local serviceTypes={vendor=true,trainer=true,banker=true,auctioneer=true,taxi=true,binder=true,
    battlemaster=true,healer=true,petition=true,tabard=true,unlearn=true,workorder=true}
local function serviceOption(option)
    if type(option)~="table" then return false end
    if type(option.type)=="string" and serviceTypes[option.type:lower()] then return true end
    local icon=option.icon
    for kind in pairs(serviceTypes) do
        local path="Interface\\GossipFrame\\"..kind.."GossipIcon"
        if type(icon)=="string" and (icon:lower()==path:lower() or icon:lower()==path:lower()..".blp") then return true end
        if type(icon)=="number" and type(GetFileIDFromPath)=="function" then
            local ok,id=pcall(GetFileIDFromPath,path)
            if not ok or not id or id==0 then ok,id=pcall(GetFileIDFromPath,path..".blp") end
            if ok and id and id>0 and icon==id then return true end
        end
    end
    return false
end
local directionLabels={['专业训练师']=true,['职业训练师']=true,['邮箱']=true,['拍卖行']=true,
    ['旅店']=true,['公会注册员']=true,['蝙蝠管理员']=true,['飞艇管理员']=true,
    ['兽栏管理员']=true,['武器大师']=true,['战场军官']=true,['银行']=true,
    ['狮鹫管理员']=true,['双足飞龙管理员']=true,['飞行管理员']=true}
local function directionLabel(text)
    if type(text)~='string' then return nil end
    text=text:gsub('|c%x%x%x%x%x%x%x%x',''):gsub('|r',''):match('^%s*(.-)%s*$')
    return directionLabels[text] and text or nil
end
local function functionalGreeting()
    -- Guards use ordinary gossip options. Require multiple distinct, exact service labels.
    local labels,count={},0
    local function direction(text)
        local key=directionLabel(text)
        if key and not labels[key] then labels[key]=true;count=count+1 end
        return count>=2
    end
    if C_GossipInfo and type(C_GossipInfo.GetOptions)=="function" then
        local ok,options=pcall(C_GossipInfo.GetOptions)
        if ok and type(options)=="table" then
            for _,option in pairs(options) do
                if serviceOption(option) or (type(option)=='table' and direction(option.name)) then return true end
            end
        end
    end
    if type(GetGossipOptions)=="function" then
        local options={pcall(GetGossipOptions)}
        if options[1] then
            for i=3,#options,2 do
                if (type(options[i])=="string" and serviceTypes[options[i]:lower()]) or direction(options[i-1]) then return true end
            end
        end
    end
    return false
end

local frame=CreateFrame("Frame")
for _,event in ipairs({"GOSSIP_SHOW","QUEST_GREETING","QUEST_DETAIL","QUEST_PROGRESS","QUEST_COMPLETE","GOSSIP_CLOSED","QUEST_FINISHED","ITEM_TEXT_READY","ITEM_TEXT_CLOSED"}) do frame:RegisterEvent(event) end
frame:SetScript("OnEvent",function(_, event)
    if event=="QUEST_COMPLETE" then questCompletion=true
    elseif event=="QUEST_DETAIL" or event=="QUEST_PROGRESS" or event=="GOSSIP_SHOW" or event=="QUEST_GREETING" or event=="QUEST_FINISHED" or event=="GOSSIP_CLOSED" then questCompletion=false end
    if (event=="ITEM_TEXT_READY" or event=="ITEM_TEXT_CLOSED") and bookActive then QVRPlayback.Stop() end
    if event=="ITEM_TEXT_CLOSED" then generation=generation+1;return end
    generation=generation+1
    local ticket=generation
    if event=="QUEST_COMPLETE" then
        local _,_,automatic=preferences()
        if automatic then
            local payload,_,metadata=transport.ReadRaw()
            if payload then
                metadata=metadata or {};metadata.Source="turnin"
                local ok,why=QVRPlayback.Play(payload,false,metadata)
                if not ok then print("冒险有声：",why) end
                return
            end
        end
    end
    -- Closing the window is not cancellation of an already captured dialogue.
    C_Timer.After(0.15,function()
        if ticket~=generation then return end
        local _,_,automatic=preferences()
        if not automatic then return end
        -- Only automatic greetings are filtered; quest pages and manual play bypass this.
        if event=="GOSSIP_SHOW" and not (QuestVoiceResourceDB and QuestVoiceResourceDB.functionalNpcAuto==true) and functionalGreeting() then return end
        local payload, reason,metadata
        if event=="ITEM_TEXT_READY" and QVRBookText then payload,reason,metadata=QVRBookText.Read()
        else payload,reason,metadata=transport.ReadRaw() end
        if payload then
            local ok,why=QVRPlayback.Play(payload,false,metadata)
            if not ok then print("冒险有声：",why=="QUEUE FULL" and "发送队列已满，本次未发送" or "文字格式异常，本次未发送") end
        elseif reason and reason:find("TOO LONG",1,true) then
            print("QVR AUTO TOO LONG - 4096 UTF-8 bytes maximum")
            reject("too-long")
        elseif reason and reason~="NO VISIBLE DIALOG" then
            print("QVR AUTO",reason); reject("unavailable-text")
        end
        pump()
    end)
end)
QVR_AUTO_VERSION = 4
