-- Shared with the legacy client; keep this file compatible with Lua 5.0.
QVRBookText = {}
local function clean(text)
    if type(text) ~= "string" then return nil end
    text=string.gsub(text,"|c%x%x%x%x%x%x%x%x","")
    text=string.gsub(text,"|r","")
    text=string.gsub(text,"|H.-|h(.-)|h","%1")
    text=string.gsub(text,"|T.-|t","")
    text=string.gsub(text,"|A.-|a","")
    text=string.gsub(text,"<[Bb][Rr]%s*/?>","\n")
    text=string.gsub(text,"</[Pp]>","\n\n")
    text=string.gsub(text,"</[Hh]%d>","\n")
    text=string.gsub(text,"<[^>]*>","")
    local entities={nbsp=" ",quot='"',apos="'",lt="<",gt=">",amp="&"}
    text=string.gsub(text,"&(%a+);",function(key) return entities[key] or ("&"..key..";") end)
    text=string.gsub(text,"\r","")
    text=string.gsub(text,"[%z\1-\8\11\12\14-\31\127]","")
    text=string.gsub(text,"^%s+","");text=string.gsub(text,"%s+$","")
    return text
end
QVRBookText.Clean=clean
function QVRBookText.Read()
    if not ItemTextFrame or not ItemTextFrame.IsVisible or not ItemTextFrame:IsVisible() then return nil,"请先打开书籍或物品正文" end
    if type(ItemTextGetText)~="function" then return nil,"当前无法读取物品正文" end
    local ok,text=pcall(ItemTextGetText)
    if not ok then return nil,"暂时无法读取物品正文" end
    text=clean(text)
    if not text or text=="" then return nil,"当前页没有可朗读文字" end
    if string.len(text)>4096 then return nil,"当前页文字过长，本次未发送" end
    local title="书籍 / 物品"
    if type(ItemTextGetItem)=="function" then
        local valid,value=pcall(ItemTextGetItem)
        if valid and type(value)=="string" and value~="" then title=clean(value) end
    end
    title=string.gsub(title,"[\n\t]"," ")
    if type(ItemTextGetPage)=="function" then
        local valid,page=pcall(ItemTextGetPage)
        if valid and type(page)=="number" and page>=1 then title=title.." · 第"..math.floor(page).."页" end
    end
    return text,nil,{SpeakerName="书籍旁白",NpcId="",SpeakerRace="",SpeakerSex="",QuestTitle=title,Source="book",
        SpeakerKind="narrator",RaceSource="document",SexSource="document",QuestStage="book",QuestId=""}
end
