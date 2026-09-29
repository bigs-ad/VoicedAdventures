local ok,err=xpcall(function()
local root='experiments/file-read-egress/addon/VoicedAdventures/'
local now,tasks,handlers=0,{},{}
local guid,sex,file,race,quest='Creature-0-1-2-3-250686-01',3,997378,nil,423
local model
GetTime=function() return now end
GetBuildInfo=function() return '1.60.1','70009' end
GetRealmName=function() return 'test' end
UnitGUID=function(u) if u=='npc' then return guid end end
UnitName=function(u) if u=='npc' then return 'npc' end end
UnitSex=function() return sex end
UnitRace=function() return race end
GetQuestID=function() return quest end
QuestVoiceResourceDB={}
QVRIdentityModels={Build=70009,Models={[997378]='undead',[7478494]='forever.tianyi'}}
C_Timer={After=function(d,fn) tasks[#tasks+1]={at=now+d,fn=fn} end}
CreateFrame=function(kind)
    if kind=='PlayerModel' then
        model={SetSize=function()end,SetPoint=function()end,SetAlpha=function()end,Show=function()end,
            ClearModel=function()end,SetUnit=function()end,GetModelFileID=function()return file end,SetScript=function()end}
        return model
    end
    return {RegisterEvent=function()end,SetScript=function(_,_,fn) handlers[#handlers+1]=fn end}
end
local function event(e) for _,fn in ipairs(handlers) do fn(nil,e) end end
local function advance(seconds)
    local finish=now+seconds;local n=0
    while true do
        table.sort(tasks,function(a,b)return a.at<b.at end)
        if not tasks[1] or tasks[1].at>finish then break end
        local t=table.remove(tasks,1);now=t.at;t.fn();n=n+1;assert(n<500,'bounded timers')
    end
    now=finish
end
dofile(root..'Identity.lua')
event('QUEST_DETAIL');advance(.1)
local m=QVRIdentity.Metadata('npc',{Source='npc'})
local got;QVRIdentity.Resolve(m,function(v) got=v end)
assert(got.SpeakerRace=='亡灵' and got.SpeakerSex=='female','observed female undead')
assert(got.RaceSource=='model' and got.SexSource=='unit' and got.SpeakerKind=='npc','model and unit provenance retained')
assert(got.QuestId=='423' and got.QuestStage=='detail' and got.QuestLevel==nil,'real quest ID with unresolved level')
local diagnostic=QVRIdentity.Diagnostic()
assert(diagnostic:find('file=997378',1,true) and diagnostic:find('sentRace=亡灵',1,true),'diagnostic captures model and sent identity')
local saved=QVRIdentity.Quest(423)
assert(saved.SpeakerRace=='亡灵' and saved.NpcId=='250686','quest giver persisted')
guid='Creature-0-1-2-3-251362-02';file=7478494
event('QUEST_COMPLETE');advance(.1)
assert(QVRIdentity.Quest(423).NpcId=='250686','recipient does not overwrite giver')
assert(QVRIdentity.Metadata('npc',{}).SpeakerRace=='天裔','tianyi category')
assert(QVRIdentity.Quest(999)==nil,'unseen quest stays unknown')
file=0;quest=100;event('QUEST_DETAIL');got=nil
QVRIdentity.Resolve(QVRIdentity.Metadata('npc',{}),function(v)got=v end)
assert(not got,'wait for model')
event('QUEST_FINISHED');file=997378;advance(1.2);assert(not got,'closed event rejects late result')
file=0;event('GOSSIP_SHOW');got=nil
QVRIdentity.Resolve(QVRIdentity.Metadata('npc',{}),function(v)got=v end)
advance(.15);assert(got and got.SpeakerSex=='female','slow model must not hold playback beyond existing 150ms event debounce')
advance(1.1);assert(got and got.SpeakerRace==nil and got.SpeakerSex=='female','timeout retains real sex')
file=997378;event('GOSSIP_SHOW');got=nil
QVRIdentity.Resolve(QVRIdentity.Metadata('npc',{}),function(v)got=v end)
guid='Creature-0-1-2-3-999-03';advance(.2);assert(not got,'changed guid rejects callback')
GetBuildInfo=function() return 'other','12345' end
event('QUEST_DETAIL');advance(.1)
assert(QVRIdentity.Metadata('npc',{}).SpeakerRace==nil,'wrong build no model inference')
assert(QVRIdentity.Quest(423)==nil,'quest observations scoped by client version')
race='人类';event('QUEST_DETAIL');advance(.1)
assert(QVRIdentity.Metadata('npc',{}).SpeakerRace=='人类','API race works without model table')
race=nil;sex=1;file=997378
GetBuildInfo=function() return '1.60.1','70009' end
event('GOSSIP_SHOW');advance(.1)
assert(QVRIdentity.Metadata('npc',{}).SpeakerSex=='','unknown sex is not inferred from model')
sex=2;file=0;event('GOSSIP_SHOW');got=nil
QVRIdentity.Resolve(QVRIdentity.Metadata('npc',{}),function(v)got=v end)
advance(.4);file=997378;advance(.1)
assert(got and got.SpeakerRace==nil and got.SpeakerSex=='male','slow model cannot delay or change already released male snapshot')
assert(QVRIdentity.Metadata('npc',{}).SpeakerRace=='亡灵','background model classification continues after playback release')
diagnostic=QVRIdentity.Diagnostic()
assert(diagnostic:find('sentRace=unknown',1,true) and diagnostic:find('race=亡灵',1,true),'diagnostic distinguishes late model from released identity')
file=123456;event('GOSSIP_SHOW');advance(.1)
QVRIdentity.Resolve(QVRIdentity.Metadata('npc',{}),function()end)
diagnostic=QVRIdentity.Diagnostic()
assert(diagnostic:find('file=123456',1,true) and diagnostic:find('reason=unmapped',1,true),'diagnostic exposes unmapped model without guessing race')
file=0;quest=424;event('QUEST_DETAIL');advance(.2)
local releasedAt=now;got=nil;local releases=0
QVRIdentity.Resolve(QVRIdentity.Metadata('npc',{}),function(v)got=v;releases=releases+1 end)
assert(got and now==releasedAt,'normal auto dispatch does not add model waiting')
file=997378;advance(.2)
assert(releases==1 and not got.SpeakerRace and QVRIdentity.Quest(424).SpeakerRace=='亡灵','late result records quest but never changes or repeats released speech')
file=0;event('GOSSIP_SHOW');got=nil
QVRIdentity.Resolve(QVRIdentity.Metadata('npc',{}),function(v)got=v end)
model.GetModelFileID=function() error('not ready') end
advance(1.1);assert(got and got.SpeakerSex=='male','model errors fall back without killing playback')
model.GetModelFileID=function() return 997378 end
local count=0;file=0;event('GOSSIP_SHOW')
QVRIdentity.Resolve(QVRIdentity.Metadata('npc',{}),function() count=count+1 end)
advance(1.1);assert(count==1,'exactly one result')
event('GOSSIP_SHOW');event('QUEST_FINISHED');advance(.1)
assert(QVRIdentity.Metadata('npc',{}).SpeakerRace=='亡灵','unrelated quest close preserves current gossip identity')
event('QUEST_DETAIL');event('GOSSIP_CLOSED');advance(.1)
assert(QVRIdentity.Metadata('npc',{}).SpeakerRace=='亡灵','unrelated gossip close preserves current quest identity')
event('QUEST_FINISHED')
assert(QVRIdentity.Diagnostic()=='VA ID no-active-dialogue','closed dialogue does not expose stale identity')
model.ClearModel=function() error('clear failed') end
model.SetUnit=function() error('load failed') end
guid='Creature-0-1-2-3-555-01';event('GOSSIP_SHOW');got=nil
QVRIdentity.Resolve(QVRIdentity.Metadata('npc',{}),function(v)got=v end)
advance(1.1);assert(got and not got.SpeakerRace,'failed model assignment never inherits previous model')
print('identity tests passed')
model.ClearModel=function()end;model.SetUnit=function()end
model.GetModelFileID=function()return 959310 end
QVRLocalNpcRaces={Build=70009,Realm='test',Npcs={[2121]='undead'}}
guid='Creature-0-1-2-3-2121-01';sex=2;race=nil
event('GOSSIP_SHOW');advance(.2)
got=nil;QVRIdentity.Resolve(QVRIdentity.Metadata('npc',{}),function(v)got=v end)
assert(got.SpeakerRace=='亡灵' and got.SpeakerSex=='male','optional NPC mapping with live male sex')
assert(got.RaceSource=='legacy','heuristic fallback is not verified evidence')
assert(QVRIdentity.Diagnostic():find('reason=local-npc-table',1,true),'diagnostic identifies heuristic source')
race='人类';event('GOSSIP_SHOW');advance(.2)
assert(QVRIdentity.Metadata('npc',{}).SpeakerRace=='人类','live API overrides optional NPC table')
race=nil;sex=1;event('GOSSIP_SHOW');advance(.2)
assert(QVRIdentity.Metadata('npc',{}).SpeakerSex=='','NPC race table never infers sex')
QVRLocalNpcRaces.Realm='another';event('GOSSIP_SHOW');advance(.2)
assert(not QVRIdentity.Metadata('npc',{}).SpeakerRace,'optional table realm isolation')
QVRLocalNpcRaces.Realm='test';QVRLocalNpcRaces.Build=123;event('GOSSIP_SHOW');advance(.2)
assert(not QVRIdentity.Metadata('npc',{}).SpeakerRace,'optional table build isolation')
QVRLocalNpcRaces=nil;event('GOSSIP_SHOW');advance(.2)
assert(not QVRIdentity.Metadata('npc',{}).SpeakerRace,'no optional data means unchanged fallback')
print('optional NPC mapping tests passed')
dofile(root..'NpcRaceFallback.lua')
local total=0;for _ in pairs(QVRLocalNpcRaces.Npcs) do total=total+1 end
assert(total==0 and QVRLocalNpcRaces.Build==0,'release contains no third-party NPC database')
dofile(root..'QuestSpeakers.lua')
assert(next(QVRQuestSpeakers.Quests)==nil,'release contains no third-party quest database')
dofile(root..'IdentityModels.lua')
assert(next(QVRIdentityModels.Models)==nil and QVRIdentityModels.Build==0,'release contains no client-derived model database')
QuestVoiceResourceDB={};race=nil;event('GOSSIP_SHOW');advance(.2)
assert(not QVRIdentity.Metadata('npc',{}).SpeakerRace and not QVRIdentity.Quest(477),'empty databases safely preserve unknown identity')
-- Synthetic fixtures exercise optional fallback behavior without shipping source data.
QVRLocalNpcRaces={Build=70009,Realm='Classic Beta PvE',Npcs={[2121]='undead'}}
QVRQuestSpeakers={Build=70009,Realm='Classic Beta PvE',Quests={[477]={NpcId='2121'}}}
GetRealmName=function()return QVRLocalNpcRaces.Realm end
sex=2;event('GOSSIP_SHOW');advance(.2)
assert(QVRIdentity.Metadata('npc',{}).SpeakerRace=='亡灵','bundled fallback resolves live male NPC')
local history=QuestVoiceResourceDB
handlers={};dofile(root..'Identity.lua')
local fromLog=QVRIdentity.Quest(477)
assert(fromLog.NpcId=='2121' and fromLog.SpeakerRace=='亡灵' and fromLog.SpeakerSex=='male','fresh session uses persisted NPC identity without reopening quest')
QuestVoiceResourceDB={}
fromLog=QVRIdentity.Quest(477)
assert(fromLog.NpcId=='2121' and fromLog.SpeakerRace=='亡灵' and not fromLog.SpeakerSex,'static race must not guess unobserved sex')
QuestVoiceResourceDB=history
local db=QuestVoiceResourceDB.identity['70009:Classic Beta PvE']
db['477']={complete={NpcId='999',SpeakerRace='人类',SpeakerSex='female'}}
assert(QVRIdentity.Quest(477).NpcId=='2121','recipient never replaces mapped giver')
db['477'].detail={NpcId='999',SpeakerRace='人类',SpeakerSex='female'}
assert(QVRIdentity.Quest(477).NpcId=='999','observed quest giver overrides static mapping')
GetRealmName=function()return 'other' end
assert(not QVRIdentity.Quest(477),'static lookup scoped to realm')
print('quest speaker fallback and reload tests passed')
GetBuildInfo=function() return '12.1.0','69933' end
QVRLocalNpcRaces=nil
QVRIdentityModels={Build=69933,Models={},Npcs={[245394]='human'}}
guid='Creature-0-1-2-3-245394-01';race=nil;sex=3
event('GOSSIP_SHOW');advance(.2)
assert(QVRIdentity.Metadata('npc',{}).SpeakerRace=='人类','retail exact NPC mapping precedes ambiguous model')
assert(QVRIdentity.Metadata('npc',{}).SpeakerSex=='female','retail NPC lookup preserves live sex')
QVRIdentityModels.Build=70009;event('GOSSIP_SHOW');advance(.2)
assert(not QVRIdentity.Metadata('npc',{}).SpeakerRace,'retail lookup rejects foreign build')
QVRIdentityModels.Build=69933;race='矮人';event('GOSSIP_SHOW');advance(.2)
assert(QVRIdentity.Metadata('npc',{}).SpeakerRace=='矮人','live API remains authoritative')
assert(QVRIdentity.Metadata('npc',{}).IdentityConflict,'contradictory client mapping is retained as conflict')
race=nil;guid='Creature-0-1-2-3-999999-01';event('GOSSIP_SHOW');advance(.2)
assert(not QVRIdentity.Metadata('npc',{}).SpeakerRace,'unknown retail NPC remains unknown')
print('retail exact NPC identity tests passed')
local infoId=77
C_QuestLog={GetLogIndexForQuestID=function(id) return id==77 and 2 or nil end,
 GetInfo=function(index) return {questID=infoId,level=19,minLevel=10,isHeader=false} end}
local context=QVRIdentity.QuestContext(77,'detail',{})
assert(context.QuestId=='77' and context.QuestLevel==19 and context.QuestLevelSource=='quest-log','level comes from exact quest log ID, not minLevel')
infoId=78;context=QVRIdentity.QuestContext(77,'detail',{})
assert(context.QuestLevel==nil,'wrong quest cannot lend its level')
context=QVRIdentity.QuestContext(77,'gossip',{})
assert(context.QuestId=='' and context.QuestLevel==nil,'gossip does not inherit stale quest')
C_QuestLog=nil;GetNumQuestLogEntries=function()return 2 end
GetQuestLogTitle=function(index) return 'same title',index==1 and 5 or -1,0,false,false,false,0,index==1 and 78 or 77 end
context=QVRIdentity.QuestContext(77,'complete',{})
assert(context.QuestLevel==-1 and context.QuestStage=='complete','legacy log preserves scaling and stage by ID')
context=QVRIdentity.QuestContext(nil,'detail',{QuestId='old',QuestLevel=20,QuestLevelSource='quest-log'})
assert(context.QuestId=='' and context.QuestLevel==nil and context.QuestLevelSource=='','missing current quest clears previous metadata')
print('quest context evidence tests passed')

guid='Creature-0-1-2-3-888888-01';race='人类';sex=2
event('QUEST_COMPLETE');advance(.2)
QuestVoiceResourceDB.identity['69933:other'].npcs['888888'].IdentityConflict=true
event('QUEST_COMPLETE')
local immediate=QVRIdentity.Metadata('npc',{})
assert(immediate.IdentityConflict,'immediate completion snapshot preserves persisted conflict')
assert(immediate.RaceSource=='legacy' and immediate.SexSource=='legacy','pending snapshot cannot claim completed identity verification')
advance(.2)
assert(QVRIdentity.Metadata('npc',{}).IdentityConflict,'completed snapshot retains conflict')
guid='GameObject-0-1-2-3-123-01';event('QUEST_DETAIL');advance(.1)
local document=QVRIdentity.Metadata('npc',{})
assert(document.SpeakerKind=='narrator' and document.RaceSource=='document' and document.SpeakerRace==nil and document.SpeakerSex=='','quest object is explicit narrator not previous NPC identity')
end,debug.traceback)
if not ok then io.stderr:write(err..'\n');os.exit(1) end
