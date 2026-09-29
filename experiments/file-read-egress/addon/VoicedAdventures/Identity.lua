-- Model results are valid only for the captured interaction and client build.
QVRIdentity={}
local current,model=nil,nil
local raceNames={human="人类",orc="兽人",dwarf="矮人",nightelf="暗夜精灵",undead="亡灵",tauren="牛头人",gnome="侏儒",troll="巨魔",["forever.tianyi"]="天裔"}
local function read(fn,...)
    if type(fn)~="function" then return nil end
    local ok,value=pcall(fn,...);if ok then return value end
end
local function copy(t)
    local result={};for k,v in pairs(t or {}) do result[k]=v end;return result
end
local function integer(value,minimum,maximum)
    return type(value)=='number' and value==math.floor(value) and value>=minimum and value<=maximum
end
function QVRIdentity.QuestContext(id,stage,base,knownLevel)
    local result=copy(base)
    result.QuestId='';result.QuestLevel=nil;result.QuestLevelSource='';result.QuestStage=stage or ''
    if stage~='detail' and stage~='progress' and stage~='complete' and stage~='questlog' then return result end
    if not integer(id,1,2147483647) then return result end
    result.QuestId=string.format('%d',id)
    local level=knownLevel
    if not integer(level,-1,9999) then
        local index=C_QuestLog and read(C_QuestLog.GetLogIndexForQuestID,id)
        local info=integer(index,1,10000) and read(C_QuestLog.GetInfo,index)
        if type(info)=='table' and info.questID==id and not info.isHeader then level=info.level end
        if not integer(level,-1,9999) and type(GetQuestLogTitle)=='function' then
            local count=read(GetNumQuestLogEntries)
            if integer(count,0,10000) then
                for i=1,count do
                    local ok,_,candidate,_,header,_,_,_,quest=pcall(GetQuestLogTitle,i)
                    if ok and not header and quest==id then level=candidate;break end
                end
            end
        end
    end
    if integer(level,-1,9999) then result.QuestLevel=level;result.QuestLevelSource='quest-log' end
    return result
end
local function conflicts(a,b)
    if not b then return false end
    if b.IdentityConflict then return true end
    for _,key in ipairs({'SpeakerRace','SpeakerSex'}) do
        if a[key] and a[key]~='' and b[key] and b[key]~='' and a[key]~=b[key] then return true end
    end
    return false
end
local function scope()
    local _,build=GetBuildInfo()
    return tostring(build)..":"..tostring(read(GetRealmName) or ""),tonumber(build)
end
local function store()
    if type(QuestVoiceResourceDB)~="table" then QuestVoiceResourceDB={} end
    local key=scope()
    if type(QuestVoiceResourceDB.identity)~="table" then QuestVoiceResourceDB.identity={} end
    local db=QuestVoiceResourceDB.identity
    if type(db[key])~="table" then db[key]={} end
    return db[key]
end
local function valid(s)
    return current==s and read(UnitGUID,s.unit)==s.guid
end
local function enrich(s)
    local result=copy(s.metadata)
    local db=store()
    if conflicts(result,db.npcs and db.npcs[result.NpcId]) then result.IdentityConflict=true end
    -- Immediate turn-in and timeout snapshots can play before verification finishes.
    if not s.done and result.SpeakerKind~='narrator' then
        result.RaceSource='legacy';result.SexSource='legacy'
    end
    result._identity=s
    return result
end
local function finish(s)
    if not valid(s) then return end
    s.done=true
    if s.metadata.NpcId then
        local db=store();db.npcs=db.npcs or {}
        local old=db.npcs[s.metadata.NpcId] or {}
        if conflicts(s.metadata,old) then s.metadata.IdentityConflict=true end
        for k,v in pairs(s.metadata) do if v~='' then old[k]=v end end
        db.npcs[s.metadata.NpcId]=old
    end
    if type(s.quest)=="number" and s.quest>0 and s.phase and s.metadata.NpcId then
        local db=store();local key=tostring(s.quest)
        if type(db[key])~="table" then db[key]={} end
        db[key][s.phase]=copy(s.metadata)
    end
    local waiting=s.waiting;s.waiting={}
    for _,callback in ipairs(waiting) do callback(enrich(s)) end
end
local function poll(s)
    if not valid(s) then return end
    if s.metadata.SpeakerKind=='narrator' then s.reason='document';finish(s);return end
    if s.metadata.SpeakerRace then
        local _,build=scope()
        local file=s.modelReady and model and read(model.GetModelFileID,model)
        local models=QVRIdentityModels
        local mapped=models and models.Build==build and models.Models and raceNames[models.Models[file]]
        if mapped and mapped~=s.metadata.SpeakerRace then s.metadata.IdentityConflict=true end
        s.reason=s.reason or 'api';finish(s);return
    end
    if not s.modelReady then s.reason='model-unavailable';finish(s);return end
    local file=model and read(model.GetModelFileID,model)
    s.file=file
    local _,build=scope()
    local models=QVRIdentityModels
    if type(file)=="number" and file>0 then
        if not s.metadata.SpeakerRace and models and models.Build==build then
            s.metadata.SpeakerRace=raceNames[models.Models[file]]
            if s.metadata.SpeakerRace then s.metadata.RaceSource='model' end
        end
        s.modelMs=math.floor((GetTime()-s.started)*1000+.5)
        s.reason=(not models or models.Build~=build) and 'build-mismatch' or nil
        if not s.reason then s.reason=s.metadata.SpeakerRace and 'mapped' or 'unmapped' end
        finish(s)
    elseif GetTime()-s.started>=1 then s.reason='model-timeout';finish(s)
    else C_Timer.After(.05,function() poll(s) end) end
end
function QVRIdentity.Diagnostic()
    local s=current
    if not s or not valid(s) then return 'VA ID no-active-dialogue' end
    local _,build=scope()
    return 'VA ID npc='..tostring(s.metadata.NpcId or '?')..' build='..tostring(build)..
        ' file='..tostring(s.file or 0)..' reason='..tostring(s.reason or 'pending')..
        ' race='..tostring(s.metadata.SpeakerRace or 'unknown')..' sex='..tostring(s.metadata.SpeakerSex)..
        ' sentRace='..tostring(s.sentRace or 'not-sent')..' sentMs='..tostring(s.sentMs or -1)..
        ' modelMs='..tostring(s.modelMs or -1)
end
function QVRIdentity.Metadata(unit,base)
    local result=copy(base)
    local s=current
    if s and s.unit==unit and valid(s) then
        for k,v in pairs(enrich(s)) do result[k]=v end
        result._identity=s
    end
    return result
end
function QVRIdentity.Resolve(metadata,callback)
    local s=metadata and metadata._identity
    if not s then callback(metadata);return end
    if not valid(s) then return end
    local delivered=false
    local function resolved(value)
        if delivered or not valid(s) then return end
        delivered=true
        local result=copy(metadata)
        for k,v in pairs(value) do result[k]=v end
        s.sentRace=result.SpeakerRace or 'unknown'
        s.sentMs=math.floor((GetTime()-s.started)*1000+.5)
        callback(result)
    end
    if s.done then resolved(enrich(s));return end
    -- Share the existing event debounce budget; model loading must not stall speech.
    local remaining=.15-(GetTime()-s.started)
    if remaining<=0 then resolved(enrich(s));return end
    s.waiting[#s.waiting+1]=resolved
    C_Timer.After(remaining,function() resolved(enrich(s)) end)
end
function QVRIdentity.Quest(id)
    if type(id)~="number" or id<=0 then return nil end
    local db=store();local entry=db[tostring(id)]
    if entry and entry.detail then
        local result=copy(entry.detail)
        -- Old saved observations have no provenance and cannot become verified by reload.
        result.RaceSource=result.RaceSource or 'legacy';result.SexSource=result.SexSource or 'legacy'
        result.SpeakerKind=result.SpeakerKind or 'npc'
        if conflicts(result,db.npcs and db.npcs[result.NpcId]) then result.IdentityConflict=true end
        return result
    end
    local data=QVRQuestSpeakers;local _,build=scope()
    if not data or data.Build~=build or data.Realm~=read(GetRealmName) then return nil end
    local known=data.Quests[id];if not known then return nil end
    local result=copy(known);result.Source='questlog';result.SpeakerKind='npc'
    result.RaceSource='legacy';result.SexSource='legacy'
    local function merge(observed)
        if not observed or observed.NpcId~=result.NpcId then return end
        if conflicts(result,observed) then result.IdentityConflict=true end
        for _,key in ipairs({'SpeakerRace','SpeakerSex'}) do
            if observed[key] and observed[key]~='' then
                result[key]=observed[key]
                local provenance=key=='SpeakerRace' and 'RaceSource' or 'SexSource'
                result[provenance]=observed[provenance] or 'legacy'
            end
        end
    end
    -- Reuse observations only for the mapped giver, never the current target or recipient.
    for key,questEntry in pairs(db) do
        if key~='npcs' and type(questEntry)=='table' then
            for _,phase in ipairs({'progress','complete','detail'}) do merge(questEntry[phase]) end
        end
    end
    merge(db.npcs and db.npcs[result.NpcId])
    local races=QVRLocalNpcRaces
    if not result.SpeakerRace and races and races.Build==build and races.Realm==data.Realm then
        result.SpeakerRace=raceNames[races.Npcs[tonumber(result.NpcId)]]
        result.RaceSource='legacy'
    end
    return result
end
local phases={QUEST_DETAIL="detail",QUEST_PROGRESS="progress",QUEST_COMPLETE="complete"}
local frame=CreateFrame("Frame")
for _,event in ipairs({"GOSSIP_SHOW","QUEST_GREETING","QUEST_DETAIL","QUEST_PROGRESS","QUEST_COMPLETE","GOSSIP_CLOSED","QUEST_FINISHED"}) do frame:RegisterEvent(event) end
frame:SetScript("OnEvent",function(_,event)
    if event=="GOSSIP_CLOSED" then
        if current and current.event=="GOSSIP_SHOW" then current=nil end
        return
    elseif event=="QUEST_FINISHED" then
        if current and current.event~="GOSSIP_SHOW" then current=nil end
        return
    end
    current=nil
    local unit="questnpc"
    local name=read(UnitName,unit)
    if type(name)~="string" or name=="" then unit="npc" end
    local guid=read(UnitGUID,unit)
    if type(guid)~="string" then return end
    local sex=read(UnitSex,unit)
    local race=read(UnitRace,unit);if race=="" then race=nil end
    local stage=phases[event] or (event=='GOSSIP_SHOW' and 'gossip' or 'greeting')
    local id=phases[event] and read(GetQuestID) or nil
    local s={unit=unit,guid=guid,event=event,started=GetTime(),waiting={},quest=id,phase=phases[event],metadata={
        SpeakerName=read(UnitName,unit),NpcId=guid:match("^Creature%-%d+%-%d+%-%d+%-%d+%-(%d+)%-"),
        SpeakerRace=race,SpeakerSex=sex==2 and "male" or sex==3 and "female" or "",Source="npc",
        RaceSource=race and 'unit' or '',SexSource=(sex==2 or sex==3) and 'unit' or '',SpeakerKind='npc'}}
    s.metadata=QVRIdentity.QuestContext(id,stage,s.metadata)
    if guid:match('^GameObject%-') then
        s.metadata.SpeakerKind='narrator';s.metadata.SpeakerRace=nil;s.metadata.SpeakerSex=''
        s.metadata.RaceSource='document';s.metadata.SexSource='document'
        current=s;finish(s);return
    end
    current=s
    local supplemental=QVRLocalNpcRaces
    local _,build=scope()
    local identities=QVRIdentityModels
    if identities and identities.Build==build and identities.Npcs then
        local mapped=raceNames[identities.Npcs[tonumber(s.metadata.NpcId)]]
        if race and mapped and race~=mapped then s.metadata.IdentityConflict=true end
        if not race then
            s.metadata.SpeakerRace=mapped
            if mapped then s.reason='client-npc-table';s.metadata.RaceSource='npc-table' end
        end
    end
    if not s.metadata.SpeakerRace and supplemental and supplemental.Build==build and supplemental.Realm==read(GetRealmName) then
        s.metadata.SpeakerRace=raceNames[supplemental.Npcs[tonumber(s.metadata.NpcId)]]
        if s.metadata.SpeakerRace then s.reason='local-npc-table';s.metadata.RaceSource='legacy' end
    end
    if not model then
        local ok,result=pcall(CreateFrame,"PlayerModel");if ok then model=result end
        if model then model:SetSize(1,1);model:SetPoint("CENTER");model:SetAlpha(0);model:Show() end
    end
    if model then
        local cleared=model.ClearModel and pcall(model.ClearModel,model)
        local assigned=cleared and pcall(model.SetUnit,model,unit)
        s.modelReady=assigned==true
    end
    C_Timer.After(.05,function() poll(s) end)
end)
