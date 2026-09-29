local ok,err=xpcall(function()
local events,frames,hooks={},{},{}
local selected,header,questText=1,false,'  |cffffffffQuest|r |Hquest:1|hlink|h |Ticon|t\r\nBody  '
local plays,stops,changes={},{},{}
local labels,messages={},{}
local function widget(name)
    local w={name=name,scripts={},shown=true}
    function w:SetScript(event,fn) self.scripts[event]=fn end
    function w:HookScript(event,fn)
        local previous=self.scripts[event]
        self.scripts[event]=function(...) if previous then previous(...) end;fn(...) end
    end
    function w:RegisterEvent(event) events[event]=self end
    function w:SetText(text) self.text=text end
    function w:SetChecked(value) self.checked=value end
    function w:GetChecked() return self.checked end
    function w:SetValue(value) self.value=value;if self.scripts.OnValueChanged then self.scripts.OnValueChanged(self,value) end end
    function w:GetValue() return self.value end
    function w:Show() self.shown=true end
    function w:Hide() self.shown=false end
    function w:IsShown() return self.shown end
    w.IsVisible=w.IsShown
    function w:CreateFontString() local font=widget();labels[#labels+1]=font;return font end
    function w:CreateTexture() local t=widget();self.icon=t;return t end
    function w:SetTexture(path) self.texture=path end
    function w:SetVertexColor() end
    function w:SetBackdropBorderColor() end
    function w:SetClampedToScreen(value) self.clamped=value end
    for _,method in ipairs({'SetSize','SetPoint','ClearAllPoints','SetFrameStrata','SetMovable','EnableMouse','RegisterForDrag','StartMoving','StopMovingOrSizing','SetMinMaxValues','SetValueStep','SetObeyStepOnDrag','SetBackdrop','SetBackdropColor','SetEnabled'}) do w[method]=function() end end
    function w:SetSize(width,height) self.width=width;self.height=height end
    function w:SetParent(parent) self.parent=parent end
    function w:SetPoint(...) self.point={...} end
    function w:SetFrameStrata(strata) self.strata=strata end
    if name then _G[name]=w;frames[name]=w end
    return w
end
UIParent=widget();GossipFrame=widget();QuestFrame=widget();QuestLogFrame=widget();LWDialogFrame=widget();LWDialogFrame:Hide()
CreateFrame=function(_,name,parent,template) local w=widget(name);w.template=template;return w end
local registered,opened,registrations=nil,nil,0
if arg and arg[1]=='legacy' then
    InterfaceOptions_AddCategory=function(frame) registered=frame;registrations=registrations+1 end
    InterfaceOptionsFrame_OpenToCategory=function(frame) opened=frame;frame:Show() end
else
    Settings={RegisterCanvasLayoutCategory=function(frame,name) registered=frame;assert(name=='冒险有声');return {GetID=function() return 777 end} end,
        RegisterAddOnCategory=function(category) assert(category:GetID()==777);registrations=registrations+1 end,
        OpenToCategory=function(id) opened=id;registered:Show() end}
end
SlashCmdList={};print=function(...) local parts={...};for i,v in ipairs(parts) do parts[i]=tostring(v) end;messages[#messages+1]=table.concat(parts,' ') end
QVRTransport={ReadRaw=function() return 'NPC text' end,Compact=function(text) return text end}
QVRPlayback={Play=function(text,force,metadata) plays[#plays+1]={text=text,force=force,metadata=metadata};return true end,
    Stop=function() stops[#stops+1]=true end,
    Settings=function(provider,rate,queued,volume) changes[#changes+1]={provider,rate,queued,volume};QuestVoiceResourceDB.provider=provider;QuestVoiceResourceDB.rate=rate;if queued~=nil then QuestVoiceResourceDB.queue=queued end;if volume~=nil then QuestVoiceResourceDB.volume=volume end;return true end,
    Preferences=function()
        local prefs=QuestVoiceResourceDB or {}
        return prefs.provider or 'system',prefs.rate or 10,prefs.auto~=false,prefs.queue==true,prefs.volume or 100
    end}
GetQuestLogSelection=function() return selected end
GetQuestLogTitle=function() return 'Quest',1,1,header end
GetQuestLogQuestText=function() return questText,'Objectives' end
QuestLog_Update=function() end
hooksecurefunc=function(name,fn) hooks[name]=fn end
dofile('experiments/file-read-egress/addon/VoicedAdventures/Controls.lua')
assert(SLASH_VOICEDADVENTURES1=='/va' and SlashCmdList.VOICEDADVENTURES,'new settings command')
assert(not SLASH_QVRUI1 and not SLASH_QUESTVOICERESOURCE1 and not SlashCmdList.QVRUI and not SlashCmdList.QUESTVOICERESOURCE,'old commands removed')
assert(registered==QVRControlsPanel and registrations==1,'registered in Blizzard addon settings')
assert(QVRControlsPanel.template==nil,'settings is not a separate floating window')
events.PLAYER_LOGIN.scripts.OnEvent(nil,'PLAYER_LOGIN')
assert(not QVRNPCCoverage and not QVRQuestCoverage and not QVRBookCoverage,'toolbars contain only playback controls, without coverage badges')
local reminder='如果没有声音，请确认桌面助手是否已打开。'
local found=false;for _,font in ipairs(labels) do if font.text==reminder then found=true end end
assert(found,'settings has permanent assistant reminder')
assert(messages[#messages]:find(reminder,1,true),'login displays assistant reminder without claiming connection status')
assert(QuestVoiceResourceDB.provider=='system' and QuestVoiceResourceDB.rate==10 and QuestVoiceResourceDB.auto==true,'safe persisted defaults')
SlashCmdList.VOICEDADVENTURES('');assert(QVRControlsPanel:IsShown(),'slash opens panel')
local beforePlay,beforeStop=#plays,#stops
SlashCmdList.VOICEDADVENTURES(' PLAY ');assert(#plays==beforePlay+1 and plays[#plays].force,'slash manual play')
SlashCmdList.VOICEDADVENTURES(' stop ');assert(#stops==beforeStop+1,'slash stop')
plays[#plays]=nil;stops[#stops]=nil
assert(opened==(Settings and 777 or QVRControlsPanel),'slash opens registered category')
assert(QVRNPCBar.width==104 and QVRQuestBar.width==120,'NPC strip and quest toolbar dimensions')
assert(QVRNPCPlay.width==36 and QVRNPCStop.width==26 and QVRNPCSettings.width==26,'NPC primary and secondary hit targets')
assert(QVRNPCSettings.icon.texture=='Interface\\Buttons\\UI-OptionsButton','native settings icon')
QVRNPCSettings.scripts.OnClick();assert(opened==(Settings and 777 or QVRControlsPanel),'toolbar opens addon settings')
QVRQueueWait.scripts.OnClick();assert(QuestVoiceResourceDB.queue and QVRQueueWait:GetChecked() and not QVRQueueInterrupt:GetChecked(),'FIFO control')
QVRQueueInterrupt.scripts.OnClick();assert(not QuestVoiceResourceDB.queue and QVRQueueInterrupt:GetChecked(),'interrupt control')
assert(QVRControlsPanel.clamped and QVRNPCBar.clamped and QVRQuestBar.clamped,'controls stay within screen')
QVRNPCPlay.scripts.OnClick();assert(plays[#plays].text=='NPC text' and plays[#plays].force,'manual NPC fresh replay')
GameTooltip={SetOwner=function() end,SetText=function(_,text) GameTooltip.text=text end,Show=function() end,Hide=function() end}
QVRNPCPlay.scripts.OnEnter(QVRNPCPlay);assert(GameTooltip.text:find(reminder,1,true),'NPC play tooltip has reminder')
QVRQuestPlay.scripts.OnEnter(QVRQuestPlay);assert(GameTooltip.text:find(reminder,1,true),'quest play tooltip has reminder')
local tooltipOwner,tooltipHidden=QVRNPCSettings,false
GameTooltip.IsOwned=function(_,owner) return owner==tooltipOwner end
GameTooltip.Hide=function() tooltipHidden=true end
QVRNPCSettings.scripts.OnHide();assert(tooltipHidden,'hidden toolbar button clears its tooltip')
tooltipHidden=false;tooltipOwner=QVRQuestPlay
QVRNPCSettings.scripts.OnHide();assert(not tooltipHidden,'hidden NPC button preserves other tooltips')
QVRQuestPlay.scripts.OnClick();assert(plays[#plays].text=='Quest link \nBody\n\nObjectives','selected quest cleanup and objective inclusion')
assert(plays[#plays].metadata.Source=='questlog' and plays[#plays].metadata.QuestTitle=='Quest' and not plays[#plays].metadata.SpeakerName,'questlog does not inherit current NPC')
GetQuestLogTitle=function() return 'Quest',1,1,header,false,false,0,423 end
QVRIdentity={Quest=function(id) assert(id==423);return {SpeakerName='Giver',SpeakerSex='female',NpcId='250686',Source='npc'} end}
QVRQuestPlay.scripts.OnClick();assert(plays[#plays].metadata.Source=='questlog' and plays[#plays].metadata.NpcId=='250686','legacy quest id uses recorded giver')
assert(plays[#plays].metadata.QuestId=='423' and plays[#plays].metadata.QuestLevel==1 and plays[#plays].metadata.QuestStage=='questlog','legacy log transmits ID and quest level')
QVRIdentity=nil
local n=#plays;selected=0;QVRQuestPlay.scripts.OnClick();assert(#plays==n,'no selection')
selected=1;header=true;QVRQuestPlay.scripts.OnClick();assert(#plays==n,'header not quest')
header=false;questText=string.rep('x',4097);QVRQuestPlay.scripts.OnClick();assert(#plays==n,'no truncation')
questText='';QVRQuestPlay.scripts.OnClick();assert(#plays==n,'missing detail body not a readable quest')
events.QUEST_LOG_UPDATE.scripts.OnEvent(nil,'QUEST_LOG_UPDATE');if hooks.QuestLog_Update then hooks.QuestLog_Update() end
assert(#plays==n,'browsing log never auto speaks')
QVRNPCStop.scripts.OnClick();assert(#stops==1,'stop control')
assert(not QVRProviderTencent and not QVRProviderSystem,'source belongs only in desktop assistant')
QVRRateSlider:SetValue(14);assert(changes[#changes][2]==14,'rate control')
for _,volume in ipairs({0,90,200}) do
    local priorStops=#stops
    QVRVolumeSlider:SetValue(volume)
    assert(changes[#changes][4]==volume and QuestVoiceResourceDB.volume==volume and #stops==priorStops,'volume control persists without stopping speech')
end
assert(changes[#changes][1]=='system','rate retains legacy wire field')
QVRAutoCheck:SetChecked(false);QVRAutoCheck.scripts.OnClick(QVRAutoCheck);assert(QuestVoiceResourceDB.auto==false,'auto persisted')
-- Forever uses the modern quest map; its UI may load after this addon.
QVRControlsPanel:Hide()
QuestLogFrame=nil;GetQuestLogSelection=nil;GetQuestLogTitle=nil;QuestLog_Update=nil
local questID,logIndex,infoQuestID=501,7,501
C_QuestLog={GetSelectedQuest=function() return questID end,
    GetLogIndexForQuestID=function(id) assert(id==501);return logIndex end,
    GetInfo=function(index) assert(index==7);return {title='Modern quest',questID=infoQuestID,isHeader=header} end}
GetQuestLogQuestText=function(index) assert(index==7,'explicit selected log index');return 'Modern body','Modern objectives' end
WorldMapFrame=widget();QuestMapFrame=widget();QuestMapFrame.DetailsFrame=widget();QuestMapFrame.DetailsFrame.questID=501
QuestMapFrame_ShowQuestDetails=function() end;QuestMapFrame_CloseQuestDetails=function() end
assert(events.ADDON_LOADED,'late-loaded quest UI event')
events.ADDON_LOADED.scripts.OnEvent(nil,'ADDON_LOADED','Blizzard_QuestUI')
assert(registrations==1,'settings category never duplicated on addon events')
assert(QVRQuestBar:IsShown(),'modern details expose controls without legacy frame')
assert(QVRQuestBar.parent==QuestMapFrame.DetailsFrame and not QVRQuestBar.strata,'quest toolbar inherits detail window stacking instead of DIALOG strata')
assert(QVRQuestBar.point[2]==WorldMapFrame and QVRQuestBar.point[3]=='TOPRIGHT' and QVRQuestBar.point[4]>=6,'quest toolbar starts beyond outer map border, not inset details')
assert(not QVRNPCBar:IsShown(),'quest map suppresses NPC toolbar behind it')
n=#plays;QVRQuestPlay.scripts.OnClick();assert(#plays==n+1 and plays[#plays].text=='Modern body\n\nModern objectives','modern selected quest')
QVRIdentity={Quest=function(id) assert(id==501);return {SpeakerName='Modern giver',SpeakerSex='female',NpcId='251362',Source='npc'} end}
QVRQuestPlay.scripts.OnClick();assert(plays[#plays].metadata.Source=='questlog' and plays[#plays].metadata.NpcId=='251362','modern quest id uses recorded giver')
assert(plays[#plays].metadata.QuestId=='501' and plays[#plays].metadata.QuestLevel==nil,'modern missing level remains unknown')
QVRIdentity=nil
n=#plays;questID=0;QVRQuestPlay.scripts.OnClick();assert(#plays==n,'modern missing selection rejects stale text')
questID=501;logIndex=nil;QVRQuestPlay.scripts.OnClick();assert(#plays==n,'modern missing log entry')
logIndex=7;header=true;QVRQuestPlay.scripts.OnClick();assert(#plays==n,'modern header rejected');header=false
infoQuestID=502;QVRQuestPlay.scripts.OnClick();assert(#plays==n,'mismatched modern entry rejected');infoQuestID=501
QuestMapFrame.DetailsFrame.questID=502;QVRQuestPlay.scripts.OnClick();assert(#plays==n,'mismatched visible detail rejected');QuestMapFrame.DetailsFrame.questID=501
assert(QuestMapFrame.DetailsFrame.scripts.OnHide and WorldMapFrame.scripts.OnHide,'late-loaded frame hooks installed')
QuestMapFrame.DetailsFrame:Hide();QuestMapFrame.DetailsFrame.scripts.OnHide();assert(not QVRQuestBar:IsShown(),'details close hides controls')
assert(not QVRNPCBar:IsShown(),'map without details still suppresses NPC toolbar')
assert(hooks.QuestMapFrame_ShowQuestDetails and hooks.QuestMapFrame_CloseQuestDetails,'late-loaded quest functions hooked')
QuestMapFrame.DetailsFrame:Show();hooks.QuestMapFrame_ShowQuestDetails();assert(QVRQuestBar:IsShown() and #plays==n,'browse shows controls without speaking')
QVRControlsPanel:Hide()
WorldMapFrame:Hide();WorldMapFrame.scripts.OnHide()
assert(not QVRNPCBar:IsShown(),'visible quest map still suppresses NPC toolbar')
QuestMapFrame:Hide();QuestMapFrame.scripts.OnHide()
assert(QVRNPCBar:IsShown(),'closing map restores NPC toolbar')
assert(QVRNPCBar.parent==GossipFrame and not QVRNPCBar.strata,'NPC toolbar inherits its window stacking')
QuestLogFrame=widget();events.ADDON_LOADED.scripts.OnEvent(nil,'ADDON_LOADED')
assert(not QVRNPCBar:IsShown() and QVRQuestBar:IsShown(),'legacy log suppresses NPC bar but keeps quest controls')
assert(QVRQuestBar.parent==QuestLogFrame,'quest toolbar reparents to legacy owner')
QuestLogFrame:Hide();QuestLogFrame.scripts.OnHide()
assert(QVRNPCBar:IsShown(),'closing legacy log restores NPC toolbar')
SettingsPanel=widget();SettingsPanel:Hide()
InterfaceOptionsFrame=widget();InterfaceOptionsFrame:Hide()
events.ADDON_LOADED.scripts.OnEvent(nil,'ADDON_LOADED','Blizzard_Settings')
assert(QVRNPCBar:IsShown(),'NPC toolbar visible before settings')
for _,settings in ipairs({SettingsPanel,InterfaceOptionsFrame,QVRControlsPanel}) do
    settings:Show()
    if settings.scripts.OnShow then settings.scripts.OnShow(settings) end
    events.QUEST_LOG_UPDATE.scripts.OnEvent(nil,'QUEST_LOG_UPDATE')
    assert(not QVRNPCBar:IsShown() and not QVRQuestBar:IsShown(),'settings suppress both toolbars despite game events')
    settings:Hide()
    if settings.scripts.OnHide then settings.scripts.OnHide(settings) end
    assert(QVRNPCBar:IsShown(),'closing settings restores visible NPC toolbar')
end
QVRControlsPanel.scripts.OnShow()
assert(not QVRFunctionalNpcCheck:GetChecked(),'functional greetings default off')
QVRFunctionalNpcCheck:SetChecked(true);QVRFunctionalNpcCheck.scripts.OnClick(QVRFunctionalNpcCheck)
assert(QuestVoiceResourceDB.functionalNpcAuto==true,'functional greeting setting persisted')
QVRControlsPanel.scripts.OnShow();assert(QVRFunctionalNpcCheck:GetChecked(),'functional greeting setting restored')
QVRFunctionalNpcCheck:SetChecked(false);QVRFunctionalNpcCheck.scripts.OnClick(QVRFunctionalNpcCheck)
assert(QuestVoiceResourceDB.functionalNpcAuto==false,'functional greeting setting can be disabled')
ItemTextFrame=widget();ItemTextGetText=function() return '<P>Tablet text</P>' end
ItemTextGetItem=function() return 'Tablet' end
dofile('experiments/file-read-egress/addon/VoicedAdventures/BookText.lua')
QVRControlsPanel:Hide();events.ADDON_LOADED.scripts.OnEvent(nil,'ADDON_LOADED')
assert(QVRBookBar:IsShown() and not QVRNPCBar:IsShown() and not QVRQuestBar:IsShown(),'book toolbar excludes other toolbars')
assert(QVRBookBar.parent==ItemTextFrame and not QVRBookBar.strata,'book toolbar inherits owner layering')
QVRBookPlay.scripts.OnClick();assert(plays[#plays].text=='Tablet text' and plays[#plays].metadata.Source=='book','book button reads its owner not last NPC')
QVRBookStop.scripts.OnClick();assert(#stops>0)
ItemTextFrame:Hide();ItemTextFrame.scripts.OnHide();assert(not QVRBookBar:IsShown(),'hidden book hides toolbar')
GossipFrame:Hide();QuestFrame:Show()
GetBuildInfo=function()return '12.1.0','69933','',120100 end
events.QUEST_DETAIL.scripts.OnEvent(nil,'QUEST_DETAIL')
assert(QVRNPCBar:IsShown() and QVRNPCBar.parent==QuestFrame,'retail NPC controls keep owner visibility')
assert(QVRNPCBar.point[1]=='BOTTOMRIGHT' and QVRNPCBar.point[2]==QuestFrame and QVRNPCBar.point[3]=='TOPRIGHT' and QVRNPCBar.point[5]>0,'retail controls sit outside above title, not behind model sidebar')
GetBuildInfo=function()return '1.60.1','70009','',16001 end
events.QUEST_DETAIL.scripts.OnEvent(nil,'QUEST_DETAIL')
assert(QVRNPCBar.point[1]=='BOTTOMRIGHT' and QVRNPCBar.point[3]=='TOPRIGHT','beta shares approved upper-edge strip')
GetBuildInfo=function()return '12.1.0','69933','',120100 end
QuestFrame.GetBottom=function()return 80 end
dofile('experiments/file-read-egress/addon/VoicedAdventures/Controls.lua')
assert(QVRNPCBar.width==104 and QVRNPCBar.height==32,'compact retail strip dimensions')
assert(QVRNPCPlay.width==36 and QVRNPCStop.width==26 and QVRNPCSettings.width==26,'primary play is larger than secondary controls')
events.QUEST_DETAIL.scripts.OnEvent(nil,'QUEST_DETAIL')
assert(QVRNPCBar.point[1]=='BOTTOMRIGHT' and QVRNPCBar.point[3]=='TOPRIGHT','strip remains above title regardless of bottom space')
QuestFrame.GetBottom=function()return 12 end
events.QUEST_DETAIL.scripts.OnEvent(nil,'QUEST_DETAIL')
assert(QVRNPCBar.point[1]=='BOTTOMRIGHT' and QVRNPCBar.point[3]=='TOPRIGHT','strip moves above when bottom space insufficient')
n=#plays;QVRNPCPlay.scripts.OnClick();assert(#plays==n+1,'compact play remains functional')
QVRNPCStop.scripts.OnClick();assert(#stops>0,'compact stop remains functional')
io.write('PASS selected quest, settings visibility, cleanup, replay, rate, automatic and book controls\n')
end,debug.traceback)
if not ok then io.stderr:write(err..'\n');os.exit(1) end
