-- Legacy client entry point. PacketTransport.lua is bundled before this file.
QVR_AUTO_VERSION = 4
VA_TURTLE_VERSION = 1
local queue, last, sequence, elapsed = {}, nil, 0, 0
local bookActive=false
local lastAt=0
local settingsDirty=false
local frame = CreateFrame("Frame")
local function db()
    if type(QuestVoiceResourceDB) ~= "table" then QuestVoiceResourceDB = {} end
    if type(QuestVoiceResourceDB.turtle) ~= "table" then QuestVoiceResourceDB.turtle = {} end
    local d = QuestVoiceResourceDB.turtle
    if d.auto == nil then d.auto = true end
    if d.rate == nil then d.rate = 12 end
    if d.queue == nil then d.queue = false end
    if type(d.volume)~="number" or d.volume<0 or d.volume>200 or d.volume~=math.floor(d.volume) then d.volume=100 end
    return d
end
local function le(value, width)
    local result = ""
    for i = 1, width do result = result .. string.char(math.mod(value, 256)); value = math.floor(value / 256) end
    return result
end
local function sum(text)
    local a, b = 1, 0
    for i = 1, string.len(text) do a = math.mod(a + string.byte(text, i), 65521); b = math.mod(b + a, 65521) end
    return b * 65536 + a
end
local function send(kind, data)
    local value = "QA" .. string.char(kind, string.len(data)) .. data
    local a, b = 0, 0
    for i = 1, string.len(value) do a = math.mod(a + string.byte(value, i), 255); b = math.mod(b + a, 255) end
    value = value .. string.char(a, b)
    sequence = sequence + 1
    local id = string.format("%08X%08X", time(), sequence)
    local paths = VATurtlePackets.Encode(id, value)
    for _, path in ipairs(paths) do table.insert(queue, path) end
end
local function settings()
    local d = db()
    settingsDirty=false
    send(3, "settings:system:" .. d.rate .. ":" .. (d.queue and "queue" or "interrupt") .. ":" .. d.volume)
end
local function clip(text, limit)
    text = text or ""
    if string.len(text) <= limit then return text end
    local n = limit + 1
    while n > 1 and string.byte(text, n) >= 128 and string.byte(text, n) < 192 do n = n - 1 end
    return string.sub(text, 1, n - 1)
end
local identityModel
-- Exact Turtle NPC identity confirmed in game; never match name fragments.
local verifiedNpcs={["巴尔林·霜锤"]={"矮人","male"},["欧文·萨德"]={"亡灵","male"}}
local function knownIdentity(name,race,sex)
    local known=verifiedNpcs[name]
    if race=="" and known and (sex=="" or sex==known[2]) then return known[1],known[2] end
    return race,sex
end
local modelRaces={human="人类",dwarf="矮人",gnome="侏儒",nightelf="暗夜精灵",orc="兽人",tauren="牛头人",troll="巨魔",scourge="亡灵",undead="亡灵"}
local function readModelResult(value)
    local seen,trace={},{}
    local count,found,conflict,limited=0,nil,false,false
    local function visit(item,depth,key)
        count=count+1
        if count>64 then limited=true;return end
        if table.getn(trace)<16 then
            local description=type(item)=="string" and string.sub(item,1,160) or tostring(item)
            table.insert(trace,tostring(key).."="..type(item)..":"..description)
        end
        if type(item)=="string" then
            local path=string.lower(string.gsub(item,"\\","/"))
            local _,_,race,sex=string.find(path,"^character/([^/]+)/([^/]+)/")
            if modelRaces[race] and (sex=="male" or sex=="female") then
                if found and (found[1]~=modelRaces[race] or found[2]~=sex) then conflict=true end
                found={modelRaces[race],sex,path}
            end
        elseif type(item)=="table" and not seen[item] then
            seen[item]=true
            if depth>=3 then limited=true;return end
            for childKey,child in pairs(item) do visit(child,depth+1,childKey);if count>64 then break end end
        end
    end
    visit(value,0,"result")
    if found and not conflict and not limited then return found[1],found[2],found[3] end
    return "","","model-data: "..table.concat(trace,"; ")
end
local function modelIdentity()
    local ok, path = pcall(function()
        if not identityModel then
            identityModel=CreateFrame("PlayerModel",nil,UIParent)
            identityModel:SetWidth(1);identityModel:SetHeight(1)
            identityModel:SetAlpha(0);identityModel:Hide()
        end
        -- Clear before every lookup; a failed SetUnit must never reuse the previous NPC.
        identityModel:ClearModel();identityModel:SetUnit("npc")
        return {identityModel:GetModel()}
    end)
    if not ok then return "", "", "model-error: "..tostring(path) end
    return readModelResult(path)
end
local function metadata(source)
    local name, race, sex = "", "", ""
    if source ~= "questlog" then
        name = UnitName("npc") or ""
        if UnitRace then race = UnitRace("npc") or "" end
        if UnitSex then local value = UnitSex("npc"); if value == 2 then sex = "male" elseif value == 3 then sex = "female" end end
        local path=""
        if race=="" then
            local modelSex
            race,modelSex,path=modelIdentity()
            if sex=="" then sex=modelSex end
        end
        race,sex=knownIdentity(name,race,sex)
        if ExportFile then pcall(ExportFile,"VoicedAdventures_identity",name.."\n"..race.."\n"..sex.."\n"..path) end
    end
    local title = ""
    if source == "questlog" and GetQuestLogSelection and GetQuestLogTitle then title = GetQuestLogTitle(GetQuestLogSelection()) or ""
    elseif GetTitleText then title = GetTitleText() or "" end
    return {clip(name,64), "", clip(race,40), sex, clip(title,96), source}
end
local function speak(text, source, manual, supplied)
    if type(text) ~= "string" or text == "" then return end
    if not manual and not db().auto then return end
    local originalFields = supplied or metadata(source)
    local fields={};for i,value in ipairs(originalFields) do fields[i]=value end
    if source == "questlog" and db().speakers and db().speakers[fields[5]] then
        for i,value in ipairs(db().speakers[fields[5]]) do fields[i]=value end
    end
    fields[3],fields[4]=knownIdentity(fields[1],fields[3],fields[4])
    local key = fields[1] .. "\n" .. fields[5] .. "\n" .. text
    if not manual and last==key and GetTime()-lastAt<0.5 then return end
    lastAt=GetTime()
    last = key
    bookActive=source=="book"
    if table.getn(queue)>2048 then DEFAULT_CHAT_FRAME:AddMessage("冒险有声：发送队列已满，请稍后再试。");return end
    if source=="book" then queue={};send(3,"stop") end
    fields[6]=manual and (source=="questlog" and "quest-m" or source.."-m") or source
    settings()
    if string.len(text)>4096 then send(1,"too-long"); return end
    local id = le(time(),4) .. le(math.floor(GetTime()*1000),4) .. le(sequence+1,4)
    local meta = id
    for _, field in ipairs(fields) do meta = meta .. string.char(string.len(field)) .. field end
    send(4,meta)
    local parts, offset = {}, 1
    while offset <= string.len(text) do
        local part = clip(string.sub(text,offset),180)
        table.insert(parts,part); offset = offset + string.len(part)
    end
    for i, part in ipairs(parts) do
        send(2,id .. string.char(i-1,table.getn(parts)) .. le(string.len(text),2) .. le(sum(text),4) .. part)
    end
end
local function current()
    if ItemTextFrame and ItemTextFrame:IsVisible() and QVRBookText then
        local text,reason,meta=QVRBookText.Read()
        if text then return text,"book",{meta.SpeakerName,"","","",clip(meta.QuestTitle,96),"book"} end
        return nil,"book"
    end
    if QuestFrame and QuestFrame:IsVisible() then
        if QuestFrameRewardPanel and QuestFrameRewardPanel:IsVisible() then return GetRewardText(), "turnin" end
        if QuestFrameProgressPanel and QuestFrameProgressPanel:IsVisible() then return GetProgressText(), "npc" end
        return GetQuestText(), "npc"
    end
    if GossipFrame and GossipFrame:IsVisible() then return GetGossipText(), "npc" end
    if QuestLogFrame and QuestLogFrame:IsVisible() and GetQuestLogQuestText then return GetQuestLogQuestText(), "questlog" end
end
local function play()
    local ok, problem = pcall(function()
        local text, source, fields = current()
        if type(text) ~= "string" or text == "" then
            DEFAULT_CHAT_FRAME:AddMessage("冒险有声：当前窗口没有可朗读文字。")
            return
        end
        speak(text,source,true,fields)
    end)
    if not ok then DEFAULT_CHAT_FRAME:AddMessage("冒险有声：" .. tostring(problem)) end
end
local function stop() bookActive=false;queue={};send(3,"stop") end
local function functional()
    if not GetGossipOptions then return false end
    local options={GetGossipOptions()}
    for i=2,table.getn(options),2 do if options[i] ~= "gossip" then return true end end
    return false
end
local settingsFrame
local function toggleSettings()
    if settingsFrame:IsVisible() then settingsFrame:Hide() else settingsFrame:Show() end
end
local function button(parent, text, x, callback)
    local b=CreateFrame("Button",nil,parent,"UIPanelButtonTemplate")
    b:SetWidth(52);b:SetHeight(24);b:SetPoint("LEFT",parent,"LEFT",x,0);b:SetText(text);b:SetScript("OnClick",callback)
    return b
end
local bars={}
local function stripIcon(parent,kind,x,callback,tip)
    local b=CreateFrame("Button",nil,parent)
    local primary=kind=="play"
    b:SetWidth(primary and 36 or 26);b:SetHeight(26);b:SetPoint("LEFT",parent,"LEFT",x,0)
    if primary then
        b:SetBackdrop({bgFile="Interface\\Tooltips\\UI-Tooltip-Background",edgeFile="Interface\\Tooltips\\UI-Tooltip-Border",edgeSize=8,insets={left=2,right=2,top=2,bottom=2}})
        b:SetBackdropColor(0.48,0.035,0.035,1);b:SetBackdropBorderColor(0.78,0.60,0.27,1)
    end
    local icon=b:CreateTexture(nil,"ARTWORK")
    local size=primary and 22 or kind=="stop" and 9 or 15
    icon:SetWidth(size);icon:SetHeight(size);icon:SetPoint("CENTER",b,"CENTER",0,0)
    if kind=="stop" then icon:SetTexture(1,1,1)
    else icon:SetTexture(primary and "Interface\\Buttons\\UI-SpellbookIcon-NextPage-Up" or "Interface\\Buttons\\UI-OptionsButton") end
    local function reset()
        icon:SetVertexColor(primary and 1 or 0.86,primary and 0.88 or 0.77,0.48,1)
        if primary then b:SetBackdropColor(0.48,0.035,0.035,1) end
    end
    reset();b:SetScript("OnClick",callback)
    b:SetScript("OnEnter",function()
        icon:SetVertexColor(1,0.88,0.58,1)
        if primary then b:SetBackdropColor(0.65,0.065,0.045,1) end
        if GameTooltip then GameTooltip:SetOwner(b,"ANCHOR_RIGHT");GameTooltip:SetText(tip);GameTooltip:Show() end
    end)
    local function leave() reset();if GameTooltip and GameTooltip:IsOwned(b) then GameTooltip:Hide() end end
    b:SetScript("OnLeave",leave);b:SetScript("OnHide",leave)
end
local function toolbar(parent)
    if not parent then return end
    local bar=CreateFrame("Frame",nil,parent)
    bar:SetWidth(104);bar:SetHeight(32);bar:SetPoint("BOTTOMRIGHT",parent,"TOPRIGHT",-40,3)
    bar:SetClampedToScreen(true)
    bar:SetBackdrop({bgFile="Interface\\Tooltips\\UI-Tooltip-Background",edgeFile="Interface\\Tooltips\\UI-Tooltip-Border",edgeSize=8,insets={left=2,right=2,top=2,bottom=2}})
    bar:SetBackdropColor(0.075,0.08,0.085,0.96);bar:SetBackdropBorderColor(0.39,0.35,0.25,1)
    stripIcon(bar,"play",5,play,"朗读 / 重新朗读");stripIcon(bar,"stop",44,stop,"停止朗读");stripIcon(bar,"settings",73,toggleSettings,"语音设置")
    table.insert(bars,bar)
end
local function initialize()
    if settingsFrame then return end
    settingsFrame=CreateFrame("Frame","VATurtleSettings",UIParent)
    settingsFrame:SetWidth(300);settingsFrame:SetHeight(274);settingsFrame:SetPoint("CENTER",UIParent,"CENTER",0,0)
    settingsFrame:SetBackdrop({bgFile="Interface\\DialogFrame\\UI-DialogBox-Background",edgeFile="Interface\\DialogFrame\\UI-DialogBox-Border",tile=true,tileSize=32,edgeSize=32,insets={left=8,right=8,top=8,bottom=8}})
    settingsFrame:SetFrameStrata("DIALOG");settingsFrame:EnableMouse(true)
    local title=settingsFrame:CreateFontString(nil,"OVERLAY","GameFontNormalLarge")
    title:SetPoint("TOP",settingsFrame,"TOP",0,-18);title:SetText("冒险有声")
    local auto=CreateFrame("CheckButton",nil,settingsFrame,"UICheckButtonTemplate")
    auto:SetWidth(24);auto:SetHeight(24);auto:SetPoint("TOPLEFT",settingsFrame,"TOPLEFT",20,-52)
    local label=auto:CreateFontString(nil,"OVERLAY","GameFontNormal");label:SetPoint("LEFT",auto,"RIGHT",3,0);label:SetText("自动朗读任务对话")
    auto:SetScript("OnClick",function() db().auto=this:GetChecked() and true or false; if not db().auto then stop() end end)
    local slider=CreateFrame("Slider","VATurtleRate",settingsFrame,"OptionsSliderTemplate")
    slider:SetWidth(244);slider:SetHeight(16);slider:SetPoint("TOPLEFT",settingsFrame,"TOPLEFT",28,-108)
    slider:SetMinMaxValues(6,20);slider:SetValueStep(1)
    getglobal("VATurtleRateLow"):SetText("0.6");getglobal("VATurtleRateHigh"):SetText("2.0")
    slider:SetScript("OnValueChanged",function() db().rate=math.floor(this:GetValue()+0.5);getglobal("VATurtleRateText"):SetText("语速："..db().rate/10);settings() end)
    local volume=CreateFrame("Slider","VATurtleVolume",settingsFrame,"OptionsSliderTemplate")
    volume:SetWidth(244);volume:SetHeight(16);volume:SetPoint("TOPLEFT",settingsFrame,"TOPLEFT",28,-172)
    volume:SetMinMaxValues(0,200);volume:SetValueStep(5)
    getglobal("VATurtleVolumeLow"):SetText("0%");getglobal("VATurtleVolumeHigh"):SetText("200%")
    volume:SetScript("OnValueChanged",function()
        local value=math.max(0,math.min(200,math.floor(this:GetValue()+0.5)))
        if db().volume~=value then db().volume=value;settingsDirty=true end
        getglobal("VATurtleVolumeText"):SetText("播报音量："..value.."%")
    end)
    local hint=settingsFrame:CreateFontString(nil,"OVERLAY","GameFontNormalSmall")
    hint:SetPoint("BOTTOM",settingsFrame,"BOTTOM",0,48);hint:SetText("没有声音时，请确认桌面助手已打开。")
    local close=button(settingsFrame,"关闭",124,function() settingsFrame:Hide() end)
    close:ClearAllPoints();close:SetPoint("BOTTOM",settingsFrame,"BOTTOM",0,16)
    settingsFrame:SetScript("OnShow",function() auto:SetChecked(db().auto);slider:SetValue(db().rate);volume:SetValue(db().volume) end)
    settingsFrame:Hide();table.insert(UISpecialFrames,"VATurtleSettings")
    toolbar(GossipFrame);toolbar(QuestFrame);toolbar(QuestLogFrame);toolbar(ItemTextFrame)
end
SLASH_VOICEDADVENTURES1="/va"
SlashCmdList.VOICEDADVENTURES=function(command)
    command=string.lower((string.gsub(command or "","^%s*(.-)%s*$","%1")))
    if command=="" then
        if not settingsFrame then initialize() end
        settingsFrame:Show()
    elseif command=="play" then play()
    elseif command=="stop" then stop()
    else DEFAULT_CHAT_FRAME:AddMessage("冒险有声：/va 打开设置；/va play 播放；/va stop 停止。") end
end
frame:RegisterEvent("PLAYER_LOGIN");frame:RegisterEvent("GOSSIP_SHOW");frame:RegisterEvent("GOSSIP_CLOSED")
frame:RegisterEvent("QUEST_DETAIL");frame:RegisterEvent("QUEST_PROGRESS");frame:RegisterEvent("QUEST_COMPLETE");frame:RegisterEvent("QUEST_FINISHED")
frame:RegisterEvent("ITEM_TEXT_READY");frame:RegisterEvent("ITEM_TEXT_CLOSED")
frame:SetScript("OnEvent",function()
    if event=="PLAYER_LOGIN" then initialize();settings();DEFAULT_CHAT_FRAME:AddMessage("冒险有声：输入 /va 打开语音设置。如果没有声音，请确认桌面助手是否已打开。")
    elseif event=="ITEM_TEXT_CLOSED" then if bookActive then stop() end;last=nil
    elseif event=="ITEM_TEXT_READY" then
        if bookActive then stop() end
        last=nil
        if QVRBookText then
            local text,reason,meta=QVRBookText.Read()
            if text then speak(text,"book",false,{meta.SpeakerName,"","","",clip(meta.QuestTitle,96),"book"})
            elseif reason then DEFAULT_CHAT_FRAME:AddMessage("冒险有声："..reason) end
        end
    elseif event=="GOSSIP_CLOSED" or event=="QUEST_FINISHED" then last=nil
    elseif event=="GOSSIP_SHOW" then if not functional() then speak(GetGossipText(),"npc",false) end
    elseif event=="QUEST_DETAIL" then
        local fields=metadata("npc");db().speakers=db().speakers or {}; if fields[5]~="" then db().speakers[fields[5]]=fields end
        speak(GetQuestText(),"npc",false)
    elseif event=="QUEST_PROGRESS" then speak(GetProgressText(),"npc",false)
    elseif event=="QUEST_COMPLETE" then speak(GetRewardText(),"turnin",false) end
end)
frame:SetScript("OnUpdate",function()
    elapsed=elapsed+(arg1 or 0)
    if elapsed<0.025 then return end
    elapsed=0
    if settingsDirty and table.getn(queue)==0 then settings() end
    if VATurtleFileTransport.Update(queue) then queue={};settings();last=nil end
    for _, bar in ipairs(bars) do
        if settingsFrame:IsVisible() then bar:Hide() else bar:Show() end
    end
end)
