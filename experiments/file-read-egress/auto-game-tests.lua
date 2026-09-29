local ok, err = xpcall(function()
local tasks, handlers, paths, live = {}, {}, {}, {}
local now, body, shown = 0, 'first dialogue', true
SlashCmdList = {}
time = function() return 1790300000 end
print = function() end
CreateFrame = function() return { RegisterEvent=function() end, SetScript=function(_,_,fn) handlers[#handlers+1]=fn end } end
GossipFrame = { IsVisible=function() return shown end }
QuestFrame = { IsVisible=function() return shown end }
C_GossipInfo = { GetText=function() return body end }
GetQuestText = function() return body end
C_Timer = { After=function(delay,fn) tasks[#tasks+1]={at=now+math.max(delay,0.001),fn=fn} end }
PlaySoundFile = function(path)
    paths[#paths+1]=path:match('([^\\]+)%.wav$'); live[#paths]=true
    local n=0; for _ in pairs(live) do n=n+1 end; assert(n<=8,'bounded handles')
    return true,#paths
end
StopSound = function(handle) assert(live[handle],'only own handles'); live[handle]=nil end
local function event(name) for _,fn in ipairs(handlers) do fn(nil,name) end end
local function step()
    table.sort(tasks,function(a,b) return a.at<b.at end)
    local t=table.remove(tasks,1); if not t then return false end
    now=t.at; t.fn(); return true
end
local function drain() local n=0; while step() do n=n+1; assert(n<3000,'bounded timer work') end end
local function frames(includeControls)
    local out,current={},nil
    for _,p in ipairs(paths) do
        if p=='B' then assert(not current,'no overlapping frames'); current={}
        elseif p=='S' then
            assert(current and #current%2==0,'end requires frame')
            local bytes={}; for i=1,#current,2 do bytes[#bytes+1]=current[i]*16+current[i+1] end
            assert(bytes[1]==81 and bytes[2]==65 and #bytes==bytes[4]+6,'QA envelope')
            local a,b=0,0; for i=1,#bytes-2 do a=(a+bytes[i])%255;b=(b+a)%255 end
            assert(bytes[#bytes-1]==a and bytes[#bytes]==b,'checksum')
            local text='';for i=5,#bytes-2 do text=text..string.char(bytes[i]) end
            local part={kind=bytes[3],text=text,raw=bytes}
            if part.kind==2 then
                part.id=text:sub(1,12);part.index=text:byte(13);part.count=text:byte(14)
                part.meta=text:sub(15,20);part.text=text:sub(21)
                assert(#part.text<=235,'bounded segment')
            end
            if includeControls or (part.kind~=3 and part.kind~=4 and part.kind~=5 and part.kind~=6) then out[#out+1]=part end;current=nil
        else assert(current,'payload requires start');current[#current+1]=tonumber(p:sub(2),16) end
    end
    assert(not current and next(live)==nil,'complete and handles stopped');return out
end
dofile('experiments/file-read-egress/addon/VoicedAdventures/Main.lua')
dofile('experiments/file-read-egress/addon/VoicedAdventures/Auto.lua')
assert(QVRPlayback and QVRPlayback.Play and QVRPlayback.Stop and QVRPlayback.Settings,'playback API')
assert(QVR_AUTO_VERSION==4,'metadata protocol version')
event('GOSSIP_SHOW');event('GOSSIP_SHOW');drain()
assert(#paths>0,'automatic gossip must emit without a command')
local f=frames();assert(#f==1 and f[1].text==body,'debounced current text')
paths={};body='quest';event('QUEST_DETAIL');drain();f=frames();assert(#f==1 and f[1].text=='quest')
paths={};body='closed';event('GOSSIP_SHOW');shown=false;event('GOSSIP_CLOSED');drain();assert(#paths==0,'no stale closed text')
shown=true;body='A';event('GOSSIP_SHOW')
while #paths<3 do assert(step()) end
body='B';event('GOSSIP_SHOW');body='C';event('GOSSIP_SHOW');drain()
f=frames();assert(#f==2 and f[1].text=='A' and f[2].text=='C','newest pending only')
paths={};body=string.rep('x',200);event('GOSSIP_SHOW');drain();f=frames()
assert(#f==1 and f[1].text==body,'short dialogue stays one cloud request')
paths={};body=string.rep('x',256);event('GOSSIP_SHOW');drain();f=frames()
assert(#f>1 and f[1].kind==2,'long dialogue must be segmented, not rejected')
local joined='';for i,p in ipairs(f) do assert(p.index==i-1 and p.count==#f and p.id==f[1].id and p.meta==f[1].meta,'consistent segments');joined=joined..p.text end
assert(joined==body,'lossless long ASCII')
local sameId=f[1].id;paths={};event('GOSSIP_SHOW');drain();assert(frames()[1].id~=sameId,'reopen gets fresh identity; desktop owns timed cooldown')
paths={};assert(QVRPlayback.Play(body,true));drain();assert(frames()[1].id~=sameId,'manual replay has fresh identity')
local all=frames(true);assert(all[1].kind==3 and all[1].text=='settings:system:10:interrupt:100','settings precede dialogue')
for _,length in ipairs({4095,4096}) do
    paths={};body=string.rep('x',length);assert(QVRPlayback.Play(body,true));drain();f=frames();joined=''
    for _,p in ipairs(f) do joined=joined..p.text end
    assert(joined==body,'inclusive UTF-8 byte budget')
end
assert(not QVRPlayback.Play(string.rep('x',4097),true),'manual rejects overflow')
assert(not QVRPlayback.Settings('other',10) and not QVRPlayback.Settings('system',5) and not QVRPlayback.Settings('system',21) and not QVRPlayback.Settings('system',6.5),'settings bounds')
paths={};assert(QVRPlayback.Settings('tencent',6));drain();assert(frames(true)[1].text=='settings:tencent:6:interrupt:100')
paths={};assert(QVRPlayback.Settings('system',20));drain();assert(frames(true)[1].text=='settings:system:20:interrupt:100')
paths={};assert(QVRPlayback.Settings('system',20,true));drain();assert(frames(true)[1].text=='settings:system:20:queue:100')
paths={};assert(QVRPlayback.Settings('system',20,false));drain();assert(frames(true)[1].text=='settings:system:20:interrupt:100')
assert(QVRPlayback.Settings('system',10));drain()
paths={};assert(QVRPlayback.Settings('system',10));drain();assert(#frames(true)==0,'unchanged settings do not interrupt a dialogue')
paths={};body=string.rep('stop.',200);assert(QVRPlayback.Play(body,true))
while #paths<3 do assert(step()) end
QVRPlayback.Stop();drain();all=frames(true)
assert(#all==2 and all[2].kind==3 and all[2].text=='stop','stop finishes frame and drops tail')
paths={};body='last-frame';assert(QVRPlayback.Play(body,true))
local starts=0
while starts<3 do assert(step());starts=0;for _,p in ipairs(paths) do if p=='B' then starts=starts+1 end end end
QVRPlayback.Stop();drain();all=frames(true)
assert(#all==4 and all[3].kind==2 and all[4].text=='stop','stop during final frame still emitted')
paths={};body=string.rep('cancel tail.',80);assert(QVRPlayback.Play(body,true));starts=0
while starts<3 do assert(step());starts=0;for _,p in ipairs(paths) do if p=='B' then starts=starts+1 end end end
assert(QVRPlayback.Settings('tencent',12));drain();all=frames(true)
joined='';for _,part in ipairs(all) do if part.kind==2 then joined=joined..part.text end end
assert(joined==body and all[#all].text=='settings:tencent:12:interrupt:100','settings preserves long text tail, then updates subsequent dialogue')
assert(QVRPlayback.Settings('system',10));drain()
paths={};body='manual wins pending auto event';event('GOSSIP_SHOW');assert(QVRPlayback.Play(body,true));drain()
assert(#frames()==1,'pending automatic capture cannot interrupt manual replay')
paths={};QuestVoiceResourceDB.auto=false;event('GOSSIP_SHOW');drain();assert(#paths==0,'auto-off')
assert(QVRPlayback.Play(body,true));drain();assert(#frames()==1,'manual with auto-off');QuestVoiceResourceDB.auto=true
paths={};body=string.rep('x',4097);event('GOSSIP_SHOW');drain();f=frames()
assert(#f==1 and f[1].kind==1 and f[1].text=='too-long','explicit technical bound')
paths={};body=string.rep('abc.',200);event('GOSSIP_SHOW')
while #paths<3 do assert(step()) end
shown=false;event('GOSSIP_CLOSED');drain();f=frames();joined='';for _,p in ipairs(f) do joined=joined..p.text end
assert(joined==body,'closure keeps complete captured dialogue');shown=true
paths={};body=string.rep('repeat.',80);event('GOSSIP_SHOW')
while #paths<3 do assert(step()) end
event('GOSSIP_SHOW');drain();f=frames()
for i,p in ipairs(f) do assert(p.index==i-1 and p.count==#f,'duplicate event must not restart incomplete dialogue') end
paths={};body='still gossip';event('GOSSIP_SHOW');event('QUEST_FINISHED');drain();f=frames()
assert(#f==1 and f[1].text==body,'unrelated quest closure must not lose gossip')
local function plain(payload)
    if payload:byte(1)~=255 then return payload end
    local chars,i={},2
    while i<=#payload do
        local cp=payload:byte(i)+256*payload:byte(i+1);i=i+2
        if cp>=55296 and cp<=56319 then
            local low=payload:byte(i)+256*payload:byte(i+1);i=i+2
            assert(low>=56320 and low<=57343,'valid surrogate pair');cp=65536+(cp-55296)*1024+low-56320
        else assert(cp<56320 or cp>57343,'no isolated surrogate') end
        chars[#chars+1]=utf8.char(cp)
    end
    return table.concat(chars)
end
for _,extra in ipairs({0,1,2}) do
    paths={};body=string.rep(utf8.char(20013),1365)..string.rep('x',extra)
    local accepted=QVRPlayback.Play(body,true)
    if extra==2 then assert(not accepted and #paths==0,'4097 Unicode bytes rejected')
    else
        assert(accepted);drain();joined=''
        for _,p in ipairs(frames()) do joined=joined..plain(p.text) end
        assert(joined==body,'4095/4096 Unicode bytes preserved')
    end
end
paths={};body=string.rep(utf8.char(20013,25991,128512)..' text.\n',60)..'END';event('QUEST_DETAIL');drain();f=frames();joined=''
for i,p in ipairs(f) do assert(p.index==i-1 and p.count==#f,'mixed text indices');joined=joined..plain(p.text) end
assert(joined==body,'lossless mixed UTF-8 and surrogate boundaries')
local fixtureBody,fixtureFrames=body,f
paths={}
local unitCalls={}
UnitName=function(unit) unitCalls[#unitCalls+1]=unit;return '中文 NPC' end
UnitGUID=function(unit) unitCalls[#unitCalls+1]=unit;return 'Creature-0-1-2-3-456-abcdef' end
UnitRace=function(unit) unitCalls[#unitCalls+1]=unit;return '人类' end
UnitSex=function(unit) unitCalls[#unitCalls+1]=unit;return 3 end
body='metadata';event('GOSSIP_SHOW');drain();all=frames(true)
assert(all[2].kind==4 and all[2].text:sub(1,12)==all[3].id,'metadata before text with exact ID')
local fields,cursor={},13
for i=1,6 do local size=all[2].text:byte(cursor);fields[i]=all[2].text:sub(cursor+1,cursor+size);cursor=cursor+size+1 end
assert(fields[1]=='中文 NPC' and fields[2]=='456' and fields[3]=='人类' and fields[4]=='female' and fields[6]=='npc','speaker metadata fields')
local metadataFixture=all[2].raw
for _,unit in ipairs(unitCalls) do assert(unit=='npc' or unit=='questnpc','never inspect target/player') end
UnitName=function(unit) if unit=='questnpc' then return '任务说话人' end end
UnitGUID=function(unit) if unit=='questnpc' then return 'Creature-0-1-2-3-789-abcdef' end end
UnitRace=function(unit) if unit=='questnpc' then return '亡灵' end end
UnitSex=function(unit) if unit=='questnpc' then return 2 end end
local _,_,speaker=QVRTransport.ReadRaw()
assert(speaker.SpeakerName=='任务说话人' and speaker.NpcId=='789' and speaker.SpeakerRace=='亡灵' and speaker.SpeakerSex=='male','questnpc-only dialogue identity')
UnitName=function(unit) return unit=='questnpc' and '任务说话人' or '其他单位' end
UnitGUID=function(unit) if unit=='npc' then return 'Creature-0-1-2-3-999-abcdef' end end
UnitRace=function(unit) if unit=='npc' then return '人类' end end
_,_,speaker=QVRTransport.ReadRaw()
assert(speaker.SpeakerName=='任务说话人' and not speaker.NpcId and not speaker.SpeakerRace,'never mix fields from different unit tokens')
UnitName=function(unit) if unit=='questnpc' then error('unsupported token') end;assert(unit=='npc');return '备用对话人' end
_,_,speaker=QVRTransport.ReadRaw()
assert(speaker.SpeakerName=='备用对话人' and speaker.NpcId=='999','guarded npc fallback')
UnitName=function() error('unavailable') end;UnitSex=function() return 1 end
paths={};body='unknown';event('GOSSIP_SHOW');drain();all=frames(true);assert(all[2].text:byte(13)==0,'guarded unavailable NPC')
paths={};local long=string.rep('old.',100)
assert(QVRPlayback.Play(long,true));while #paths<3 do assert(step()) end
assert(QVRPlayback.Play('next',true));drain();f=frames();joined=''
for _,p in ipairs(f) do joined=joined..p.text end
assert(joined==long..'next','FIFO preserves already captured long tail')
paths={};assert(QVRPlayback.Play('active',true))
for i=1,15 do assert(QVRPlayback.Play('queued'..i,true)) end
local accepted,why=QVRPlayback.Play('overflow',true);assert(not accepted and why=='QUEUE FULL','bounded queue refuses explicitly')
QVRPlayback.Stop();drain()
paths={};local resolve
QVRIdentity={Resolve=function(metadata,callback) resolve=callback end}
assert(QVRPlayback.Play('pending identity',true,{_identity={},Source='npc'}));assert(#paths==0,'identity waits before enqueue')
QVRPlayback.Stop();drain();paths={};resolve({Source='npc',SpeakerSex='female'})
drain();assert(#paths==0,'stop cancels waiting identity')
assert(QVRPlayback.Play('resolved identity',true,{_identity={},Source='npc'}))
resolve({Source='npc',SpeakerSex='female',SpeakerRace='亡灵'});drain()
assert(frames()[1].text=='resolved identity','resolved identity reaches transport')
paths={};assert(QVRPlayback.Play('old identity',true,{_identity={}}));local old=resolve
assert(QVRPlayback.Play('new identity',true,{_identity={}}));old({Source='npc'});resolve({Source='npc'});drain()
assert(#frames()==1 and frames()[1].text=='new identity','new manual replay supersedes pending identity')
QVRIdentity=nil
QuestVoiceResourceDB.auto=true;QuestVoiceResourceDB.functionalNpcAuto=false
C_GossipInfo.GetOptions=function() return {{type='vendor'}} end
paths={};body='merchant greeting';event('GOSSIP_SHOW');drain();assert(#paths==0,'service greeting default muted')
assert(QVRPlayback.Play(body,true));drain();assert(#frames()>0,'manual service playback allowed')
paths={};body='merchant quest';event('QUEST_DETAIL');drain();assert(#frames()>0,'service quest detail allowed')
paths={};QuestVoiceResourceDB.functionalNpcAuto=true;body='opted in';event('GOSSIP_SHOW');drain();assert(#frames()>0,'service opt in')
QuestVoiceResourceDB.functionalNpcAuto=false
C_GossipInfo.GetOptions=function() return {{type='gossip'}} end
paths={};body='story gossip';event('GOSSIP_SHOW');drain();assert(#frames()>0,'ordinary story still automatic')
C_GossipInfo.GetOptions=nil;GetGossipOptions=function() return 'Train','trainer' end
paths={};body='legacy trainer';event('GOSSIP_SHOW');drain();assert(#paths==0,'legacy trainer filtered')
GetGossipOptions=nil;GetFileIDFromPath=function(path) if path:lower():find('bankergossipicon',1,true) then return 999123 end end
C_GossipInfo.GetOptions=function() return {{icon=999123}} end
paths={};body='modern bank';event('GOSSIP_SHOW');drain();assert(#paths==0,'modern service icon filtered')
C_GossipInfo.GetOptions=function() error('unavailable') end
paths={};body='unknown service classification';event('GOSSIP_SHOW');drain();assert(#frames()>0,'failed classification does not suppress story')
C_GossipInfo.GetOptions=nil;GetFileIDFromPath=nil
local directions={{name='专业训练师',type='gossip'},{name='邮箱',type='gossip'},{name='拍卖行',type='gossip'}}
C_GossipInfo.GetOptions=function() return directions end
paths={};body='你在找什么？';event('GOSSIP_SHOW');drain();assert(#paths==0,'guard directions filtered despite generic gossip icons')
assert(QVRPlayback.Play(body,true));drain();assert(#frames()>0,'guard manual playback allowed')
paths={};body='guard quest body';event('QUEST_DETAIL');drain();assert(#frames()>0,'guard quest body allowed')
QuestVoiceResourceDB.functionalNpcAuto=true
paths={};body='guard opted in';event('GOSSIP_SHOW');drain();assert(#frames()>0,'guard opt in allowed')
QuestVoiceResourceDB.functionalNpcAuto=false
C_GossipInfo.GetOptions=function() return {{name='我要去拍卖行调查。'},{name='邮箱'}} end
paths={};body='story mentioning services';event('GOSSIP_SHOW');drain();assert(#frames()>0,'service keywords alone must not suppress story')
C_GossipInfo.GetOptions=nil;GetGossipOptions=function() return '银行','gossip','旅店','gossip' end
paths={};body='legacy guard';event('GOSSIP_SHOW');drain();assert(#paths==0,'legacy guard directions filtered')
GetGossipOptions=nil
ItemTextFrame={IsVisible=function() return true end}
local bookPage=1
ItemTextGetText=function() return '<HTML><BODY><P>Book page '..bookPage..'</P></BODY></HTML>' end
ItemTextGetItem=function() return 'Tablet' end
ItemTextGetPage=function() return bookPage end
dofile('experiments/file-read-egress/addon/VoicedAdventures/BookText.lua')
QuestVoiceResourceDB.auto=true;QuestVoiceResourceDB.queue=true
paths={};event('ITEM_TEXT_READY');drain()
f=frames();assert(#f==1 and f[1].text=='Book page 1','book current page auto reading')
local all=frames(true)
assert(all[1].kind==3 and all[1].text=='stop','book cancels earlier narration before page even in queue mode')
local bookMetadata
for _,p in ipairs(all) do if p.kind==4 then bookMetadata=p.text end end
assert(bookMetadata and bookMetadata:sub(-4)=='book' and bookMetadata:find('Tablet',1,true),'book metadata has source and title')
bookPage=2;paths={};event('ITEM_TEXT_READY');drain();f=frames()
assert(#f==1 and f[1].text=='Book page 2','next page replaces previous page')
paths={};event('ITEM_TEXT_CLOSED');drain();all=frames(true)
assert(#all==1 and all[1].text=='stop','closing active book stops narration')
paths={};event('ITEM_TEXT_READY');drain();assert(#frames()==1,'reopening the same book page must receive a fresh dialogue')
paths={};event('ITEM_TEXT_CLOSED');drain();paths={}
event('ITEM_TEXT_READY');event('ITEM_TEXT_CLOSED');drain();assert(#frames()==0,'close cancels pending first-page callback even before playback begins')
QuestVoiceResourceDB.auto=false;paths={};event('ITEM_TEXT_READY');drain()
assert(#paths==0,'disabled automatic book reading')
local bookText,_,bookMeta=QVRBookText.Read()
assert(QVRPlayback.Play(bookText,true,bookMeta));drain();assert(#frames()==1,'manual reading works with auto disabled')
paths={};event('ITEM_TEXT_READY');event('ITEM_TEXT_CLOSED');drain()
assert(#frames()==0,'closed book cannot be read by deferred callback')
QuestVoiceResourceDB.auto=true;ItemTextFrame=nil;shown=true
GetRewardText=function() return 'reward speech' end
paths={};event('QUEST_COMPLETE');body='followup quest';event('QUEST_DETAIL');drain()
f=frames();assert(#f==2 and f[1].text=='reward speech' and f[2].text=='followup quest','rapid complete and next quest retain both pages')
local turninMetadata=false
for _,p in ipairs(frames(true)) do if p.kind==4 and p.text:sub(-6)=='turnin' then turninMetadata=true end end
assert(turninMetadata,'completion page carries protected playback intent')
paths={};assert(QVRPlayback.Play('manual sample',true,{Source='npc'}));drain()
local manualMetadata=false
for _,p in ipairs(frames(true)) do if p.kind==4 and p.text:sub(-5)=='npc-m' then manualMetadata=true end end
assert(manualMetadata,'manual replay bypass marker survives metadata transport')
for _,volume in ipairs({0,90,200}) do
    paths={};assert(QVRPlayback.Settings('system',10,false,volume));drain()
    local _,_,_,_,saved=QVRPlayback.Preferences()
    assert(saved==volume and frames(true)[1].text=='settings:system:10:interrupt:'..volume,'volume saved and transported')
end
assert(not QVRPlayback.Settings('system',10,false,201) and not QVRPlayback.Settings('system',10,false,-1) and not QVRPlayback.Settings('system',10,false,0.5),'invalid volume rejected')
GetRealmName=function() return 'Classic Beta PvE' end
GetBuildInfo=function() return 'version','70009' end
UnitName=function(unit) assert(unit=='player');return 'Alice' end
local scopedMeta={SpeakerName='NPC',NpcId='123',SpeakerRace='Human',SpeakerSex='male',QuestTitle='Quest',Source='npc'}
assert(QVRPlayback.ScopeAvailable and QVRPlayback.ScopeAvailable(scopedMeta),'v5 scope availability')
paths={};assert(QVRPlayback.Play('Hello Alice!',true,scopedMeta));drain()
local scopeFixture
for _,p in ipairs(frames(true)) do if p.kind==5 then scopeFixture=p.raw;assert(p.text:find('Classic Beta PvE',1,true) and p.text:find('70009',1,true) and p.text:sub(-5)=='Alice','v5 real scope fields') end end
assert(scopeFixture,'live scope sends v5 metadata without external data')
local evidence={SpeakerName='NPC',NpcId='123',SpeakerRace='人类',SpeakerSex='male',QuestTitle='Quest',Source='npc',
    QuestId='91743',QuestLevel=12,QuestStage='detail',RaceSource='unit',SexSource='unit',IdentityConflict=true,SpeakerKind='npc',QuestLevelSource='quest-log'}
paths={};assert(QVRPlayback.Play('Evidence sample',true,evidence));drain()
local evidenceFixture
for _,p in ipairs(frames(true)) do if p.kind==6 then evidenceFixture=p.raw end end
assert(evidenceFixture,'v6 transports quest metadata and provenance')
GetRealmName=function() return '' end
assert(not QVRPlayback.ScopeAvailable(scopedMeta),'missing realm cannot claim scope');paths={};assert(QVRPlayback.Play('fallback cloud',true,scopedMeta));drain()
local fallback=false;for _,p in ipairs(frames(true)) do if p.kind==4 then fallback=true end;assert(p.kind~=5,'wrong realm stays v4') end;assert(fallback)
GetRealmName=function() return 'Classic Beta PvE' end
scopedMeta.QuestTitle=string.rep('x',97);assert(not QVRPlayback.ScopeAvailable(scopedMeta),'v5 refuses oversized title without truncating')
scopedMeta.QuestTitle=string.rep('x',96);scopedMeta.SpeakerName=string.rep('n',64);scopedMeta.SpeakerRace=string.rep('r',40)
UnitName=function() return string.rep('p',72) end
assert(not QVRPlayback.ScopeAvailable(scopedMeta),'v5 total payload bound')
paths={};assert(QVRPlayback.Play('full unchanged dialogue',true,scopedMeta));drain();assert(frames()[1].text=='full unchanged dialogue','v5 fallback never truncates speech')
for _,p in ipairs(frames(true)) do assert(p.kind~=5,'oversized scope emits old v4') end
if arg and arg[1]=='fixture' then
    body,f=fixtureBody,fixtureFrames
    io.write((body:gsub('.',function(c)return string.format('%02X',c:byte())end))..'\n')
    for _,p in ipairs(f) do for _,b in ipairs(p.raw) do io.write(string.format('%02X',b)) end;io.write('\n') end
    io.write('M');for _,b in ipairs(metadataFixture) do io.write(string.format('%02X',b)) end;io.write('\n')
    io.write('V');for _,b in ipairs(scopeFixture) do io.write(string.format('%02X',b)) end;io.write('\n')
    io.write('E');for _,b in ipairs(evidenceFixture) do io.write(string.format('%02X',b)) end;io.write('\n')
else io.write('PASS automatic NPC events, lossless long/Unicode segments, dedupe, markers, closure and bounds\n') end
end, debug.traceback)
if not ok then io.stderr:write(err..'\n');os.exit(1)end
