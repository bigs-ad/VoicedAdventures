table.getn=table.getn or function(t) return #t end
math.mod=math.mod or math.fmod
local callbacks, paths, widgets, created = {}, {}, {}, {}
local function widget()
    local w={scripts={}}
    function w:SetScript(name,fn) self.scripts[name]=fn end
    function w:GetValue() return self.value end
    function w:SetWidth(value) self.width=value end
    function w:SetHeight(value) self.height=value end
    function w:SetPoint(...) self.point={...} end
    setmetatable(w,{__index=function(_,name)
        if name=="CreateFontString" or name=="CreateTexture" then return widget end
        if name=="IsVisible" then return function() return false end end
        return function() end
    end})
    return w
end
local first=true
local modelPath, modelFails, modelCleared, modelShown=nil,false,0,false
CreateFrame=function(kind,name,parent)
    if kind=="PlayerModel" then
        local w=widget()
        w.ClearModel=function() modelCleared=modelCleared+1 end
        w.SetUnit=function(_,unit) assert(unit=="npc");if modelFails then error("unavailable") end end
        w.GetModel=function() return modelPath end
        w.Show=function() modelShown=true end
        return w
    end
    local w=widget()
    w.parent=parent;w.kind=kind;created[#created+1]=w
    if name then widgets[name]=w end
    if first then first=false;w.SetScript=function(_,name,fn) callbacks[name]=fn end end
    return w
end
getglobal=function() return widget() end
UISpecialFrames={};UIParent=widget()
GossipFrame=widget();QuestFrame=widget();QuestLogFrame=widget();ItemTextFrame=widget()
time=function() return 1700000000 end
GetTime=function() return 123 end
UnitName=function() return "Test NPC" end
UnitRace=function() return "亡灵" end
UnitSex=function() return 2 end
GetTitleText=function() return "Test quest" end
GetQuestText=function() return string.rep("这是一段任务文本。",30) end
PlaySoundFile=function(path) paths[#paths+1]=path end
VATurtleFileTransport={Update=function(queue)
    if #queue>0 then paths[#paths+1]=table.remove(queue,1) end
    return false
end}
dofile("experiments/turtle-compat/PacketTransport.lua")
SlashCmdList={}
local chatMessages={}
DEFAULT_CHAT_FRAME={AddMessage=function(_,text) chatMessages[#chatMessages+1]=text end}
dofile("experiments/turtle-compat/Addon.lua")
assert(SLASH_VOICEDADVENTURES1=='/va' and SlashCmdList.VOICEDADVENTURES,'new command registered')
assert(not SLASH_QVRUI1 and not SLASH_QUESTVOICERESOURCE1,'no legacy command aliases')
event="PLAYER_LOGIN";callbacks.OnEvent()
assert(string.find(chatMessages[#chatMessages],"/va",1,true),'login shows new command')
local settingsShown=false
widgets.VATurtleSettings.Show=function() settingsShown=true end
SlashCmdList.VOICEDADVENTURES('  ');assert(settingsShown,'slash opens settings')
local strips=0
for _,w in ipairs(created) do
    if w.width==104 and w.height==32 then
        strips=strips+1;assert(w.point[1]=='BOTTOMRIGHT' and w.point[3]=='TOPRIGHT','Turtle strip above owner')
        local buttons=0
        for _,b in ipairs(created) do if b.parent==w and b.kind=='Button' then
            buttons=buttons+1;assert(b.height==26 and (b.width==36 or b.width==26),'Turtle strip button sizes')
        end end
        assert(buttons==3,'three Turtle icon buttons')
    end
end
assert(strips==4,'all Turtle dialogue toolbars constructed')
event="QUEST_DETAIL";callbacks.OnEvent()
for i=1,1000 do arg1=0.03;callbacks.OnUpdate() end
assert(#paths>0 and QuestVoiceResourceDB.turtle.speakers["Test quest"][3]=="亡灵")
local count=#paths
event="QUEST_DETAIL";callbacks.OnEvent()
for i=1,1000 do arg1=0.03;callbacks.OnUpdate() end
assert(#paths==count,"same open dialogue should not be resent")
UnitRace=function() return nil end
modelPath="Character\\Dwarf\\Male\\DwarfMale.m2"
GetTitleText=function() return "Dwarf quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Dwarf quest"][3]=="矮人","missing NPC race must resolve dwarf model")
assert(modelCleared>0 and not modelShown,"model must reset without showing a visible overlay")
modelPath={file="Character\\Dwarf\\Male\\DwarfMale.m2"}
GetTitleText=function() return "Table model quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Table model quest"][3]=="矮人","structured model result must resolve explicit dwarf path")
modelPath={model={path="Character/Scourge/Female/ScourgeFemale.m2"}}
GetTitleText=function() return "Nested model quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Nested model quest"][3]=="亡灵","nested model data must resolve explicit path")
modelPath={a="Character/Dwarf/Male/DwarfMale.m2",b="Character/Human/Male/HumanMale.m2"}
GetTitleText=function() return "Ambiguous model quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Ambiguous model quest"][3]=="","conflicting model paths must not guess")
modelPath={};modelPath.self=modelPath
GetTitleText=function() return "Cyclic model quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Cyclic model quest"][3]=="","cyclic table without a model remains unknown")
modelFails=true
GetTitleText=function() return "Unknown quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Unknown quest"][3]=="","failed model must not reuse previous dwarf")
modelFails=false;modelPath="Creature\\Bear\\Bear.m2"
GetTitleText=function() return "Creature quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Creature quest"][3]=="","unknown creature must not be guessed")
UnitSex=function() return nil end
modelPath="Character/Scourge/Female/ScourgeFemale.m2"
GetTitleText=function() return "Undead female quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Undead female quest"][3]=="亡灵" and QuestVoiceResourceDB.turtle.speakers["Undead female quest"][4]=="female","model identifies undead female without API race or sex")
UnitRace=function() return "人类" end
GetTitleText=function() return "API race quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["API race quest"][3]=="人类","model fallback must not override valid API race")
UnitRace=function() return nil end
UnitSex=function() return 2 end
modelPath=nil;modelFails=true
UnitName=function() return "巴尔林·霜锤" end
GetTitleText=function() return "Verified dwarf quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Verified dwarf quest"][3]=="矮人","verified Turtle NPC must resolve with both race and model unavailable")
UnitName=function() return "欧文·萨德" end
GetTitleText=function() return "Verified undead quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Verified undead quest"][3]=="亡灵" and QuestVoiceResourceDB.turtle.speakers["Verified undead quest"][4]=="male","user-confirmed undead male has exact-name fallback")
UnitName=function() return "另一个霜锤" end
GetTitleText=function() return "Unverified name quest" end
event="QUEST_DETAIL";callbacks.OnEvent()
assert(QuestVoiceResourceDB.turtle.speakers["Unverified name quest"][3]=="","name fragments must not guess a race")
local bookPackets={}
VATurtlePackets.Encode=function(_,payload) bookPackets[#bookPackets+1]=payload;return {'book-packet'} end
ItemTextFrame={IsVisible=function() return true end}
local page=1
ItemTextGetText=function() return '<P>Tablet page '..page..'</P>' end
ItemTextGetItem=function() return 'Tablet' end
ItemTextGetPage=function() return page end
dofile('experiments/file-read-egress/addon/VoicedAdventures/BookText.lua')
event='ITEM_TEXT_READY';callbacks.OnEvent()
assert(bookPackets[1]:byte(3)==3 and bookPackets[1]:sub(5,8)=='stop','book page replaces previous playback')
local metadataPacket
for _,packet in ipairs(bookPackets) do if packet:byte(3)==4 then metadataPacket=packet end end
assert(metadataPacket and metadataPacket:find('Tablet',1,true) and metadataPacket:find('book',1,true),'Turtle book title and source')
assert(not metadataPacket:find('霜锤',1,true),'Turtle book never uses old NPC identity')
bookPackets={};page=2;event='ITEM_TEXT_READY';callbacks.OnEvent()
assert(#bookPackets>0,'Turtle next page sends')
bookPackets={};event='ITEM_TEXT_CLOSED';callbacks.OnEvent()
assert(#bookPackets==1 and bookPackets[1]:sub(5,8)=='stop','Turtle closing book stops')
QuestVoiceResourceDB.turtle.auto=false;bookPackets={};event='ITEM_TEXT_READY';callbacks.OnEvent()
assert(#bookPackets==0,'Turtle books honor automatic setting')
QuestVoiceResourceDB.turtle.auto=true;ItemTextFrame=nil
GetRewardText=function() return 'reward speech' end
GetQuestText=function() return 'followup quest' end
local emitted={};VATurtleFileTransport.Update=function(queue) while #queue>0 do emitted[#emitted+1]=table.remove(queue,1) end;return false end
arg1=0.03;callbacks.OnUpdate();emitted={};bookPackets={}
event='QUEST_COMPLETE';callbacks.OnEvent();event='QUEST_DETAIL';callbacks.OnEvent()
local expected=#bookPackets;callbacks.OnUpdate()
assert(#emitted==expected,'following quest must not discard pending reward transport packets')
local turnin=false
for _,packet in ipairs(bookPackets) do if packet:byte(3)==4 and packet:find('turnin',1,true) then turnin=true end end
assert(turnin,'Turtle reward metadata carries completion intent')
assert(widgets.VATurtleVolume,'Turtle volume slider exists')
for _,volume in ipairs({0,90,200}) do
    bookPackets={};this=widgets.VATurtleVolume;this.value=volume;this.scripts.OnValueChanged();this=nil
    callbacks.OnUpdate()
    assert(QuestVoiceResourceDB.turtle.volume==volume,'Turtle volume saved')
    local sent=false
    for _,packet in ipairs(bookPackets) do if packet:byte(3)==3 and packet:find('settings:system:12:interrupt:'..volume,1,true) then sent=true end end
    assert(sent,'Turtle volume command sent without stop')
end
print('PASS Turtle identity, book, completion intent, retained queue and game volume')
