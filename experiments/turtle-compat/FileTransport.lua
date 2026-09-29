VATurtleFileTransport = {}
local session, sequence, pending = nil, 0, nil
local client = string.format("%08X%08X", time(), math.floor(GetTime()*1000))
local function read(name)
    local ok, value = pcall(ImportFile, name)
    if ok and type(value) == "string" then return value end
end
function VATurtleFileTransport.Update(queue)
    if type(ImportFile) ~= "function" or type(ExportFile) ~= "function" then return false end
    local token = read("VoicedAdventures_session")
    if not token or string.len(token) ~= 32 or string.find(token, "[^0-9a-f]") then return false end
    if token ~= session then
        session, sequence, pending = token, 0, nil
        return true
    end
    if pending then
        if read("VoicedAdventures_ack") ~= session .. ":" .. client .. ":" .. sequence then return false end
        pending = nil
    end
    if table.getn(queue) == 0 then return false end
    local path = table.remove(queue, 1)
    sequence = sequence + 1
    local payload = "VA_FILE_2\n" .. session .. "\n" .. client .. "\n" .. sequence .. "\n" .. path .. "\nEND\n"
    if pcall(ExportFile, "VoicedAdventures_outbox", payload) then pending = true
    else sequence = sequence - 1; table.insert(queue, 1, path) end
    return false
end
