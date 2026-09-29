-- SavedVariables contain only nonsecret playback preferences.
if not QVRPlayback or not CreateFrame then return end
local playback=QVRPlayback
local assistantReminder="如果没有声音，请确认桌面助手是否已打开。"
local function db()
    if type(QuestVoiceResourceDB)~="table" then QuestVoiceResourceDB={} end
    local provider,rate,automatic,queued,volume=playback.Preferences()
    QuestVoiceResourceDB.provider=provider;QuestVoiceResourceDB.rate=rate
    QuestVoiceResourceDB.auto=automatic
    QuestVoiceResourceDB.queue=queued==true
    QuestVoiceResourceDB.volume=volume or 100
    QuestVoiceResourceDB.functionalNpcAuto=QuestVoiceResourceDB.functionalNpcAuto==true
    return QuestVoiceResourceDB
end
local function clean(text)
    if type(text)~="string" then return nil end
    return text:gsub("|c%x%x%x%x%x%x%x%x", ""):gsub("|r", "")
        :gsub("|H.-|h(.-)|h", "%1"):gsub("|T.-|t", ""):gsub("|A.-|a", "")
        :gsub("||", "|"):gsub("\r", ""):gsub("^%s+", ""):gsub("%s+$", "")
end
local function selectedQuest()
    if type(GetQuestLogQuestText)~="function" then return nil,"当前无法读取任务日志" end
    local modern=C_QuestLog and type(C_QuestLog.GetSelectedQuest)=="function" and
        type(C_QuestLog.GetLogIndexForQuestID)=="function" and type(C_QuestLog.GetInfo)=="function"
    local ok,index,titleOK,title,isHeader,questID,level
    if modern then
        local selectedOK
        selectedOK,questID=pcall(C_QuestLog.GetSelectedQuest)
        if not selectedOK or type(questID)~="number" or questID<=0 then return nil,"请先选中一项任务" end
        local details=QuestMapFrame and QuestMapFrame.DetailsFrame
        if details and details.questID and details.questID~=questID then return nil,"请重新打开要朗读的任务详情" end
        ok,index=pcall(C_QuestLog.GetLogIndexForQuestID,questID)
        if ok and type(index)=="number" and index>=1 and index==math.floor(index) then
            local infoOK,info=pcall(C_QuestLog.GetInfo,index)
            titleOK=infoOK and type(info)=="table" and info.questID==questID
            if titleOK then title,isHeader,level=info.title,info.isHeader,info.level end
        end
    else
        if type(GetQuestLogSelection)~="function" or type(GetQuestLogTitle)~="function" then return nil,"当前无法读取任务日志" end
        ok,index=pcall(GetQuestLogSelection)
        if ok and type(index)=="number" and index>=1 and index==math.floor(index) then
            local group,collapsed,complete,frequency
            titleOK,title,level,group,isHeader,collapsed,complete,frequency,questID=pcall(GetQuestLogTitle,index)
        end
    end
    if not ok or type(index)~="number" or index<1 or index~=math.floor(index) then return nil,"请先选中一项任务" end
    if not titleOK or type(title)~="string" or title=="" or isHeader then return nil,"请选择具体任务，而不是分类标题" end
    local textOK,description,objectives=pcall(GetQuestLogQuestText,index)
    description=clean(description);objectives=clean(objectives)
    if not textOK or not description or description=="" then return nil,"暂时无法读取任务详情" end
    local text=description..(objectives and objectives~="" and ("\n\n"..objectives) or "")
    if #text>4096 then return nil,"任务文字过长，本次未发送" end
    if not pcall(QVRTransport.Compact,text) then return nil,"任务文字格式异常，本次未发送" end
    local metadata=QVRIdentity and QVRIdentity.Quest(questID) or {}
    metadata=metadata or {};metadata.Source="questlog";metadata.QuestTitle=clean(title)
    metadata.SpeakerKind=metadata.SpeakerKind or 'npc'
    metadata.QuestId=type(questID)=='number' and questID>0 and questID<=2147483647 and questID==math.floor(questID) and string.format('%d',questID) or ''
    metadata.QuestStage='questlog';metadata.QuestLevel=nil;metadata.QuestLevelSource=''
    if metadata.QuestId~='' and type(level)=='number' and level==math.floor(level) and level>=-1 and level<=9999 then metadata.QuestLevel=level;metadata.QuestLevelSource='quest-log' end
    metadata.RaceSource=metadata.RaceSource or 'legacy';metadata.SexSource=metadata.SexSource or 'legacy'
    return text,nil,metadata
end
local function play(getter)
    local text,reason,metadata=getter()
    if not text then
        local messages={
            ["NO VISIBLE DIALOG"]="请先打开对话窗口",
            ["UNAVAILABLE API"]="当前无法读取对话",
            ["UNAVAILABLE TEXT"]="暂时无法读取对话文字",
            ["EMPTY TEXT"]="当前没有可朗读的文字",
            ["TOO LONG"]="对话文字过长，本次未发送",
            ["INVALID TEXT"]="对话文字格式异常，本次未发送",
        }
        print("冒险有声：",messages[reason] or reason or "暂时无法读取文字");return
    end
    local ok,why=playback.Play(text,true,metadata)
    if not ok then print("冒险有声：",why=="QUEUE FULL" and "发送队列已满，本次未发送" or why=="EMPTY OR TOO LONG" and "文字为空或过长，本次未发送" or "文字格式异常，本次未发送") end
end
local function label(parent,text,x,y)
    local font=parent:CreateFontString(nil,"OVERLAY","GameFontNormal")
    font:SetPoint("TOPLEFT",parent,"TOPLEFT",x,y);font:SetText(text)
    return font
end
local function button(parent,name,text,width,action,tip)
    local b=CreateFrame("Button",name,parent,"UIPanelButtonTemplate")
    b:SetSize(width,24);b:SetText(text);b:SetScript("OnClick",action)
    b:SetScript("OnEnter",function(self)
        if GameTooltip then GameTooltip:SetOwner(self,"ANCHOR_RIGHT");GameTooltip:SetText(tip or text);GameTooltip:Show() end
    end)
    b:SetScript("OnLeave",function() if GameTooltip then GameTooltip:Hide() end end)
    return b
end
local panel=CreateFrame("Frame","QVRControlsPanel",UIParent)
panel.name="冒险有声"
panel:SetSize(580,494)
panel:SetClampedToScreen(true)
label(panel,"冒险有声",18,-16)
label(panel,"音源与 API 请在桌面助手中配置",18,-50)
local syncing=false
local refresh
local rateLabel=label(panel,"语速：1.0 倍",18,-101)
local slider=CreateFrame("Slider","QVRRateSlider",panel,"OptionsSliderTemplate")
slider:SetSize(260,16);slider:SetPoint("TOPLEFT",panel,"TOPLEFT",28,-130)
slider:SetMinMaxValues(6,20);slider:SetValueStep(1)
if slider.SetObeyStepOnDrag then slider:SetObeyStepOnDrag(true) end
if QVRRateSliderLow then QVRRateSliderLow:SetText("0.6 倍") end
if QVRRateSliderHigh then QVRRateSliderHigh:SetText("2.0 倍") end
slider:SetScript("OnValueChanged",function(_,value)
    if syncing then return end
    local rate=math.max(6,math.min(20,math.floor(value+0.5)))
    local prefs=db()
    if prefs.rate~=rate then playback.Settings(prefs.provider,rate) end
    rateLabel:SetText(string.format("语速：%.1f 倍",rate/10))
end)
local automatic=CreateFrame("CheckButton","QVRAutoCheck",panel,"UICheckButtonTemplate")
local volumeLabel=label(panel,"播报音量：100%",18,-165)
local volumeSlider=CreateFrame("Slider","QVRVolumeSlider",panel,"OptionsSliderTemplate")
volumeSlider:SetSize(260,16);volumeSlider:SetPoint("TOPLEFT",panel,"TOPLEFT",28,-194)
volumeSlider:SetMinMaxValues(0,200);volumeSlider:SetValueStep(5)
if volumeSlider.SetObeyStepOnDrag then volumeSlider:SetObeyStepOnDrag(true) end
if QVRVolumeSliderLow then QVRVolumeSliderLow:SetText("0%") end
if QVRVolumeSliderHigh then QVRVolumeSliderHigh:SetText("200%") end
volumeSlider:SetScript("OnValueChanged",function(_,value)
    if syncing then return end
    local volume=math.max(0,math.min(200,math.floor(value+0.5)))
    local prefs=db()
    if prefs.volume~=volume then playback.Settings(prefs.provider,prefs.rate,prefs.queue,volume) end
    volumeLabel:SetText("播报音量："..volume.."%")
end)
automatic:SetSize(24,24);automatic:SetPoint("TOPLEFT",panel,"TOPLEFT",18,-229)
label(automatic,"自动朗读任务 NPC 对话",28,-5)
automatic:SetScript("OnClick",function(self)
    db().auto=self:GetChecked() and true or false
    if not QuestVoiceResourceDB.auto then playback.Stop() end
end)
local stop=button(panel,"QVRPanelStop","停止",80,playback.Stop)
stop:SetPoint("TOPLEFT",panel,"TOPLEFT",18,-426)
local services=CreateFrame("CheckButton","QVRFunctionalNpcCheck",panel,"UICheckButtonTemplate")
services:SetSize(24,24);services:SetPoint("TOPLEFT",panel,"TOPLEFT",18,-261)
label(services,"自动朗读功能 NPC 对话",28,-5)
services:SetScript("OnClick",function(self) db().functionalNpcAuto=self:GetChecked() and true or false end)
label(panel,"新对话",18,-306)
local function modeButton(name,title,x,queued)
    local b=CreateFrame("CheckButton",name,panel,"UICheckButtonTemplate")
    b:SetSize(24,24);b:SetPoint("TOPLEFT",panel,"TOPLEFT",x,-326)
    label(b,title,28,-5)
    b:SetScript("OnClick",function() local prefs=db();playback.Settings(prefs.provider,prefs.rate,queued);refresh() end)
    return b
end
local interrupt=modeButton("QVRQueueInterrupt","智能打断",18,false)
local queued=modeButton("QVRQueueWait","依次排队",180,true)
local reminder=label(panel,assistantReminder,18,-370)
reminder:SetSize(284,42)
refresh=function()
    local prefs=db();syncing=true
    slider:SetValue(prefs.rate);rateLabel:SetText(string.format("语速：%.1f 倍",prefs.rate/10))
    volumeSlider:SetValue(prefs.volume);volumeLabel:SetText("播报音量："..prefs.volume.."%")
    automatic:SetChecked(prefs.auto);syncing=false
    services:SetChecked(prefs.functionalNpcAuto)
    interrupt:SetChecked(not prefs.queue);queued:SetChecked(prefs.queue)
end
local category,categoryMode
local function registerSettings()
    if categoryMode then return end
    if Settings and type(Settings.RegisterCanvasLayoutCategory)=="function" and type(Settings.RegisterAddOnCategory)=="function" then
        category=Settings.RegisterCanvasLayoutCategory(panel,panel.name)
        Settings.RegisterAddOnCategory(category);categoryMode="modern"
    elseif type(InterfaceOptions_AddCategory)=="function" then
        InterfaceOptions_AddCategory(panel);categoryMode="legacy"
    end
end
local function open()
    registerSettings();refresh()
    if categoryMode=="modern" and type(Settings.OpenToCategory)=="function" then
        Settings.OpenToCategory(category:GetID())
    elseif categoryMode=="legacy" and type(InterfaceOptionsFrame_OpenToCategory)=="function" then
        InterfaceOptionsFrame_OpenToCategory(panel)
    else print("冒险有声：请在游戏设置的插件列表中打开冒险有声。") end
end
panel:SetScript("OnShow",refresh)
panel:Hide()
registerSettings()
SLASH_VOICEDADVENTURES1="/va"
SlashCmdList.VOICEDADVENTURES=function(command)
    command=string.lower((command or ""):match("^%s*(.-)%s*$"))
    if command=="" then open()
    elseif command=="play" then play(QVRTransport.ReadRaw)
    elseif command=="stop" then playback.Stop()
    elseif QVRDiagnosticCommand then QVRDiagnosticCommand(command)
    else print("冒险有声：/va 打开设置；/va play 播放；/va stop 停止。") end
end
local function retailClient()
    if not GetBuildInfo then return false end
    local _,_,_,interface=GetBuildInfo()
    return type(interface)=='number' and interface>=100000
end
local function iconButton(parent,name,kind,action,tip,compact)
    local b=CreateFrame("Button",name,parent,"BackdropTemplate")
    local primary=compact and kind=="play"
    b:SetSize(primary and 36 or compact and 26 or 32,compact and 26 or 32)
    b:SetBackdrop({bgFile="Interface\\Buttons\\WHITE8X8",edgeFile="Interface\\Tooltips\\UI-Tooltip-Border",edgeSize=10,insets={left=2,right=2,top=2,bottom=2}})
    b:SetBackdropColor(0.06,0.06,0.06,0.94);b:SetBackdropBorderColor(0.5,0.43,0.27,1)
    if primary then b:SetBackdropColor(0.48,0.035,0.035,1);b:SetBackdropBorderColor(0.78,0.60,0.27,1)
    elseif compact then b:SetBackdrop(nil) end
    local icon=b:CreateTexture(nil,"ARTWORK")
    local size=compact and (kind=="stop" and 9 or kind=="play" and 22 or 15) or (kind=="stop" and 12 or 20)
    icon:SetSize(size,size);icon:SetPoint("CENTER",b,"CENTER",0,0)
    icon:SetTexture(kind=="play" and "Interface\\Buttons\\UI-SpellbookIcon-NextPage-Up" or kind=="settings" and "Interface\\Buttons\\UI-OptionsButton" or "Interface\\Buttons\\WHITE8X8")
    icon:SetVertexColor(primary and 1 or compact and 0.86 or 1,primary and 0.88 or compact and 0.77 or 0.82,compact and 0.48 or 0.35,1)
    b:SetScript("OnClick",action)
    b:SetScript("OnEnter",function(self)
        if compact then icon:SetVertexColor(1,0.88,0.58,1)
        else b:SetBackdropColor(0.2,0.17,0.1,1);b:SetBackdropBorderColor(1,0.82,0.35,1) end
        if primary then b:SetBackdropColor(0.65,0.065,0.045,1) end
        if GameTooltip then GameTooltip:SetOwner(self,"ANCHOR_RIGHT");GameTooltip:SetText(tip);GameTooltip:Show() end
    end)
    local function resetHover()
        if primary then b:SetBackdropColor(0.48,0.035,0.035,1);icon:SetVertexColor(1,0.88,0.48,1)
        elseif compact then icon:SetVertexColor(0.86,0.77,0.48,1)
        else b:SetBackdropColor(0.06,0.06,0.06,0.94);b:SetBackdropBorderColor(0.5,0.43,0.27,1) end
        if GameTooltip and GameTooltip.IsOwned and GameTooltip:IsOwned(b) then GameTooltip:Hide() end
    end
    b:SetScript("OnLeave",resetHover)
    b:SetScript("OnHide",resetHover)
    return b
end
local function toolbar(name,getter)
    local compact=name=="QVRNPCBar"
    local bar=CreateFrame("Frame",name,UIParent,"BackdropTemplate")
    bar:SetSize(compact and 104 or 120,compact and 32 or 42)
    if compact then
        bar:SetBackdrop({bgFile="Interface\\Buttons\\WHITE8X8",edgeFile="Interface\\Tooltips\\UI-Tooltip-Border",edgeSize=8,insets={left=2,right=2,top=2,bottom=2}})
        bar:SetBackdropColor(0.075,0.08,0.085,0.96);bar:SetBackdropBorderColor(0.39,0.35,0.25,1)
    end
    bar:SetClampedToScreen(true)
    local prefix=name:gsub("Bar$","")
    local start=iconButton(bar,prefix.."Play","play",function()
        local ok,err=pcall(play,getter)
        if not ok then print("冒险有声检查："..tostring(err));return end
        if GetBuildInfo and select(4,GetBuildInfo())==120100 and playback.Diagnostic then
            print(playback.Diagnostic())
            if C_Timer then C_Timer.After(5,function() print(playback.Diagnostic()) end) end
        end
    end,"朗读 / 重新朗读\n"..assistantReminder,compact)
    start:SetPoint("LEFT",bar,"LEFT",5,0)
    local halt=iconButton(bar,prefix.."Stop","stop",playback.Stop,"停止朗读",compact)
    halt:SetPoint("LEFT",start,"RIGHT",compact and 3 or 4,0)
    local settings=iconButton(bar,prefix.."Settings","settings",open,"语音设置",compact)
    settings:SetPoint("LEFT",halt,"RIGHT",compact and 3 or 10,0)
    bar:Hide();return bar
end
local npc=toolbar("QVRNPCBar",QVRTransport.ReadRaw)
local quest=toolbar("QVRQuestBar",selectedQuest)
local book=toolbar("QVRBookBar",function() if QVRBookText then return QVRBookText.Read() end return nil,"无法读取物品正文" end)
local function visible(frame) return frame and frame.IsVisible and frame:IsVisible() end
local function anchor(bar,target,outer)
    if target then
        -- Inherit the owning window's strata and frame-level changes, never float above other windows.
        bar:SetParent(target)
        bar:ClearAllPoints();bar:SetPoint("TOPLEFT",outer or target,"TOPRIGHT",6,-36);bar:Show()
    else bar:Hide() end
end
local function update()
    if visible(SettingsPanel) or visible(InterfaceOptionsFrame) or visible(panel) then
        npc:Hide();quest:Hide();book:Hide();return
    end
    if visible(ItemTextFrame) then anchor(book,ItemTextFrame);npc:Hide();quest:Hide();return end
    book:Hide()
    local target=visible(LWDialogFrame) and LWDialogFrame or visible(GossipFrame) and GossipFrame or visible(QuestFrame) and QuestFrame
    local questUi=visible(WorldMapFrame) or visible(QuestMapFrame) or visible(QuestLogFrame)
    anchor(npc,not questUi and target or nil)
    if not questUi and target then
        npc:ClearAllPoints()
        npc:SetPoint("BOTTOMRIGHT",target,"TOPRIGHT",-40,3)
    end
    local details=QuestMapFrame and QuestMapFrame.DetailsFrame
    local modern=visible(QuestMapFrame) and visible(details) and (not WorldMapFrame or visible(WorldMapFrame))
    anchor(quest,modern and details or visible(QuestLogFrame) and QuestLogFrame,modern and (WorldMapFrame or QuestMapFrame) or nil)
end
local hookedFrames,hookedFunctions={},{}
local function hookFrame(parent)
    if parent and parent.HookScript and not hookedFrames[parent] then
        hookedFrames[parent]=true
        parent:HookScript("OnShow",update);parent:HookScript("OnHide",update)
    end
end
local function installHooks()
    for _,name in ipairs({"GossipFrame","QuestFrame","QuestLogFrame","LWDialogFrame","WorldMapFrame","QuestMapFrame","SettingsPanel","InterfaceOptionsFrame","ItemTextFrame"}) do hookFrame(_G[name]) end
    hookFrame(panel)
    hookFrame(QuestMapFrame and QuestMapFrame.DetailsFrame)
    if type(hooksecurefunc)=="function" then
        for _,name in ipairs({"QuestLog_Update","QuestLog_SetSelection","QuestMapFrame_ShowQuestDetails","QuestMapFrame_CloseQuestDetails"}) do
            if type(_G[name])=="function" and not hookedFunctions[name] then
                hooksecurefunc(name,update);hookedFunctions[name]=true
            end
        end
    end
end
local events=CreateFrame("Frame")
for _,event in ipairs({"ADDON_LOADED","PLAYER_LOGIN","GOSSIP_SHOW","QUEST_GREETING","QUEST_DETAIL","QUEST_PROGRESS","QUEST_COMPLETE","GOSSIP_CLOSED","QUEST_FINISHED","QUEST_LOG_UPDATE","ITEM_TEXT_READY","ITEM_TEXT_CLOSED"}) do events:RegisterEvent(event) end
events:SetScript("OnEvent",function(_,event)
    registerSettings()
    if event=="PLAYER_LOGIN" then
        db();refresh()
        print("冒险有声：输入 /va 打开语音设置。"..assistantReminder)
    end
    installHooks();update()
    if C_Timer then C_Timer.After(0,update) end
end)
-- Repeated after addon loads because modern Quest UI and Lorewalker may load later.
installHooks()
