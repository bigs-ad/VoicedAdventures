local running = false
local runId = 0
local base = "Interface\\AddOns\\VoicedAdventures\\Sounds\\"
local sequence = {"A", "B", "A", "S", "A", "B", "S"}

local function test()
    if running then print("QVR BUSY"); return end
    if type(PlaySoundFile) ~= "function" or not C_Timer or type(C_Timer.After) ~= "function" then
        print("QVR unavailable API"); return
    end
    running = true
    runId = runId + 1
    local currentRun = runId
    print("QVR BEGIN", currentRun)
    for i = 1, 14 do
        local index = i
        local file = index <= 7 and sequence[index] or ("fresh" .. (index - 7))
        C_Timer.After(index * 0.75, function()
            local ok, played, handle = pcall(PlaySoundFile, base .. file .. ".wav", "Master")
            print("QVR", currentRun, index, file, ok, played, handle)
            if index == 14 then running = false; print("QVR DONE", currentRun) end
        end)
    end
end

local function longTest()
    if running then print("QVR BUSY"); return end
    if type(PlaySoundFile) ~= "function" or type(StopSound) ~= "function"
        or not C_Timer or type(C_Timer.After) ~= "function" then
        print("QVR unavailable API"); return
    end
    running = true
    runId = runId + 1
    local currentRun = runId
    print("QVR LONG BEGIN", currentRun)
    for i = 1, 9 do
        local index = i
        local file = index <= 3 and "A" or (index <= 6 and "B" or "S")
        C_Timer.After(index * 1.5, function()
            local ok, played, handle = pcall(PlaySoundFile, base .. file .. ".wav", "Master")
            print("QVR LONG", currentRun, index, file, ok, played, handle)
            C_Timer.After(0.25, function()
                if ok and played and type(handle) == "number" then
                    local stopped, result = pcall(StopSound, handle)
                    print("QVR STOP", currentRun, index, stopped, result)
                end
                if index == 9 then running = false; print("QVR LONG DONE", currentRun) end
            end)
        end)
    end
end

local function encodeFrame(payload)
    local frame = "QV" .. string.char(#payload) .. payload
    local a, b = 0, 0
    for i = 1, #frame do a = (a + frame:byte(i)) % 255; b = (b + a) % 255 end
    return frame .. string.char(a, b)
end

local function compactText(text)
    local output = {string.char(255)}
    local i = 1
    local function word(value)
        output[#output + 1] = string.char(value % 256, math.floor(value / 256))
    end
    while i <= #text do
        local lead = text:byte(i)
        local count, value, minimum
        if lead < 128 then count, value, minimum = 1, lead, 0
        elseif lead >= 194 and lead <= 223 then count, value, minimum = 2, lead - 192, 128
        elseif lead >= 224 and lead <= 239 then count, value, minimum = 3, lead - 224, 2048
        elseif lead >= 240 and lead <= 244 then count, value, minimum = 4, lead - 240, 65536
        else error("Invalid UTF-8") end
        for j = 1, count - 1 do
            local nextByte = text:byte(i + j)
            if not nextByte or nextByte < 128 or nextByte > 191 then error("Invalid UTF-8") end
            value = value * 64 + nextByte - 128
        end
        if value < minimum or value > 1114111 or (value >= 55296 and value <= 57343) then error("Invalid UTF-8") end
        if value < 65536 then word(value)
        else value = value - 65536; word(55296 + math.floor(value / 1024)); word(56320 + value % 1024) end
        i = i + count
    end
    local encoded = table.concat(output)
    return #encoded < #text and encoded or text
end

local lastNpcEvent
if type(CreateFrame) == "function" then
    local npcEvents = CreateFrame("Frame")
    for _, event in ipairs({"GOSSIP_SHOW", "QUEST_GREETING", "QUEST_DETAIL", "QUEST_PROGRESS", "QUEST_COMPLETE", "GOSSIP_CLOSED", "QUEST_FINISHED"}) do npcEvents:RegisterEvent(event) end
    npcEvents:SetScript("OnEvent", function(_, event)
        if event == "GOSSIP_CLOSED" then
            if lastNpcEvent == "GOSSIP_SHOW" then lastNpcEvent = nil end
        elseif event == "QUEST_FINISHED" then
            if lastNpcEvent ~= "GOSSIP_SHOW" then lastNpcEvent = nil end
        else lastNpcEvent = event end
    end)
end

local function npcMetadata()
    local function read(fn,unit)
        if type(fn)~="function" then return nil end
        local ok,value=pcall(fn,unit)
        if ok then return value end
    end
    local unit,name="questnpc",read(UnitName,"questnpc")
    if type(name)~="string" or name=="" then unit,name="npc",read(UnitName,"npc") end
    if type(name)~="string" or name=="" then name=nil end
    local guid=read(UnitGUID,unit)
    local sex=read(UnitSex,unit)
    local title
    if lastNpcEvent~="GOSSIP_SHOW" and type(GetTitleText)=="function" then
        local ok,value=pcall(GetTitleText);if ok then title=value end
    end
    local metadata={SpeakerName=name,NpcId=type(guid)=="string" and guid:match("^Creature%-%d+%-%d+%-%d+%-%d+%-(%d+)%-") or nil,
        SpeakerRace=read(UnitRace,unit),SpeakerSex=sex==2 and "male" or sex==3 and "female" or "",QuestTitle=title,Source="npc"}
    metadata.SpeakerKind='npc';metadata.RaceSource=metadata.SpeakerRace and metadata.SpeakerRace~='' and 'unit' or ''
    metadata.SexSource=metadata.SpeakerSex~='' and 'unit' or ''
    if type(guid)=='string' and guid:match('^GameObject%-') then
        metadata.SpeakerKind='narrator';metadata.SpeakerRace=nil;metadata.SpeakerSex=''
        metadata.RaceSource='document';metadata.SexSource='document'
    end
    local stage=({QUEST_DETAIL='detail',QUEST_PROGRESS='progress',QUEST_COMPLETE='complete',GOSSIP_SHOW='gossip',QUEST_GREETING='greeting'})[lastNpcEvent]
    if QVRIdentity and QVRIdentity.QuestContext then metadata=QVRIdentity.QuestContext(read(GetQuestID),stage,metadata) end
    return QVRIdentity and QVRIdentity.Metadata(unit,metadata) or metadata
end
local function rawNpcText()
    local function visible(frame) return frame and frame:IsVisible() end
    local anchor
    if lastNpcEvent == "GOSSIP_SHOW" then anchor = GossipFrame else anchor = QuestFrame end
    if not lastNpcEvent or not (visible(anchor) or visible(LWDialogFrame)) then return nil, "NO VISIBLE DIALOG" end
    local getters = {
        GOSSIP_SHOW = C_GossipInfo and C_GossipInfo.GetText or GetGossipText,
        QUEST_GREETING = GetGreetingText, QUEST_DETAIL = GetQuestText,
        QUEST_PROGRESS = GetProgressText, QUEST_COMPLETE = GetRewardText,
    }
    local getter = getters[lastNpcEvent]
    if type(getter) ~= "function" then return nil, "UNAVAILABLE API" end
    local ok, text = pcall(getter)
    if not ok or type(text) ~= "string" then return nil, "UNAVAILABLE TEXT" end
    text = text:gsub("|c%x%x%x%x%x%x%x%x", ""):gsub("|r", "")
        :gsub("|H.-|h(.-)|h", "%1"):gsub("|T.-|t", ""):gsub("|A.-|a", "")
        :gsub("||", "|"):gsub("\r", ""):gsub("^%s+", ""):gsub("%s+$", "")
    if text == "" then return nil, "EMPTY TEXT" end
    if #text > 4096 then return nil, "TOO LONG" end
    local encodedOK, encoded = pcall(compactText, text)
    if not encodedOK then return nil, "INVALID TEXT" end
    return text,nil,npcMetadata()
end

local function npcText()
    local text, reason = rawNpcText()
    if not text then return nil, reason end
    local encoded = compactText(text)
    if #encoded > 255 then return nil, "TOO LONG " .. #encoded .. " / 255" end
    return encoded
end

QVRTransport = {
    Read = npcText,
    ReadRaw = rawNpcText,
    Compact = compactText,
    TryBegin = function() if running then return false end; running = true; return true end,
    Finish = function() running = false end,
}

local function textTest(fast, hex, repeated, npc)
    if running then print("QVR BUSY"); return end
    if type(PlaySoundFile) ~= "function" or type(StopSound) ~= "function"
        or not C_Timer or type(C_Timer.After) ~= "function" then
        print("QVR unavailable API"); return
    end
    -- UTF-8 payload followed by Fletcher-16; this experiment never reads chat.
    local payload = string.char(228, 189, 160, 229, 165, 189)
    if npc then
        local reason
        payload, reason = npcText()
        if not payload then print("QVR NPC", reason); return end
    end
    local frame = encodeFrame(payload)
    if repeated then
        payload = string.char(229, 139, 135, 229, 163, 171, 239, 188, 140, 232, 175, 183, 229, 137, 141, 229, 190, 128, 230, 157, 145, 229, 143, 163, 229, 175, 187, 230, 137, 190, 230, 150, 165, 229, 128, 153, 239, 188, 140, 229, 184, 166, 229, 155, 158, 230, 182, 136, 230, 129, 175, 227, 128, 130)
        frame = ""
        for i = 1, 3 do frame = frame .. encodeFrame(tostring(i) .. ":" .. payload) end
    end
    running = true
    runId = runId + 1
    local currentRun = runId
    local index = 0
    local halfInterval = fast and 0.05 or 0.25
    local width = hex and 4 or 1
    local count = #frame * 8 / width
    local label = npc and "QVR NPC" or (repeated and "QVR REPEAT" or (hex and "QVR HEX" or (fast and "QVR FAST" or "QVR TEXT")))
    print(label .. " BEGIN", currentRun, count)
    local function sendNext()
        index = index + 1
        local offset = (index - 1) * width
        local byte = frame:byte(math.floor(offset / 8) + 1)
        local value = math.floor(byte / 2 ^ (8 - width - (offset % 8))) % (2 ^ width)
        local file = hex and string.format("H%X", value) or (value == 0 and "B" or "S")
        local ok, played, handle = pcall(PlaySoundFile, base .. file .. ".wav", "Master")
        if not ok or not played or type(handle) ~= "number" then
            running = false; print(label .. " FAILED", currentRun, index); return
        end
        C_Timer.After(halfInterval, function()
            local stopped = pcall(StopSound, handle)
            if not stopped then
                running = false; print(label .. " STOP FAILED", currentRun, index); return
            end
            if index == count then
                running = false; print(label .. " DONE", currentRun, index)
            else
                C_Timer.After(halfInterval, sendNext)
            end
        end)
    end
    C_Timer.After(halfInterval * 2, sendNext)
end

local function batchBenchmark(npc)
    if running then print("QVR BUSY"); return end
    if type(PlaySoundFile) ~= "function" or type(StopSound) ~= "function"
        or not C_Timer or type(C_Timer.After) ~= "function" then print("QVR unavailable API"); return end
    local frame = ""
    if npc then
        local payload, reason = npcText()
        if not payload then print("QVR NPCBURST", reason); return end
        frame = encodeFrame(payload)
    else
        for n = 1, 3 do frame = frame .. encodeFrame("ID:" .. (100000 + n)) end
    end
    local label = npc and "QVR NPCBURST" or "QVR IDBENCH"
    local batchSize = npc and 8 or 4
    running = true
    runId = runId + 1
    local currentRun, index = runId, 0
    print(label .. " BEGIN", currentRun, #frame * 2)
    local function batch()
        local handles, failed = {}, false
        for _ = 1, batchSize do
            if index == #frame * 2 then break end
            local byte = frame:byte(math.floor(index / 2) + 1)
            local value = index % 2 == 0 and math.floor(byte / 16) or byte % 16
            index = index + 1
            local ok, played, handle = pcall(PlaySoundFile, base .. string.format("H%X.wav", value), "Master")
            if not ok or not played or type(handle) ~= "number" then failed = true; break end
            handles[#handles + 1] = handle
        end
        C_Timer.After(0, function()
            for _, handle in ipairs(handles) do
                if not pcall(StopSound, handle) then failed = true end
            end
            if failed then running = false; print(label .. " FAILED", currentRun, index)
            elseif index == #frame * 2 then running = false; print(label .. " DONE", currentRun, index)
            else C_Timer.After(0, batch) end
        end)
    end
    C_Timer.After(0.1, batch)
end

QVRDiagnosticCommand = function(command)
    if command and command:match("^%s*play%s*$") then
        local ok,err=pcall(function()
            if not QVRPlayback then print("VA PLAY unavailable");return end
            local text,reason,metadata=QVRTransport.ReadRaw()
            if not text then print("VA READ "..tostring(reason));return end
            print("VA READ bytes="..#text)
            local accepted,why=QVRPlayback.Play(text,true,metadata)
            print("VA PLAY "..tostring(accepted).." "..tostring(why or ""))
            C_Timer.After(5,function() print(QVRPlayback.Diagnostic()) end)
        end)
        if not ok then print("VA ERROR "..tostring(err)) end
    elseif command and command:match("^%s*diag%s*$") then print(QVRPlayback and QVRPlayback.Diagnostic and QVRPlayback.Diagnostic() or "VA SEND unavailable")
    elseif command and command:match("^%s*identity%s*$") then print(QVRIdentity.Diagnostic())
    elseif command and command:match("^%s*test%s*$") then test()
    elseif command and command:match("^%s*long%s*$") then longTest()
    elseif command and command:match("^%s*text%s*$") then textTest()
    elseif command and command:match("^%s*fast%s*$") then textTest(true)
    elseif command and command:match("^%s*hex%s*$") then textTest(true, true)
    elseif command and command:match("^%s*repeat%s*$") then textTest(true, true, true)
    elseif command and command:match("^%s*npc%s*$") then textTest(true, true, false, true)
    elseif command and command:match("^%s*idbench%s*$") then batchBenchmark()
    elseif command and command:match("^%s*npcburst%s*$") then batchBenchmark(true)
    else print("冒险有声：/va 打开设置；/va play 播放；/va stop 停止。") end
end
