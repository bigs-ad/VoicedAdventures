-- Lua 5.0 compatible, bounded transport. Each request uses a fresh path.
VATurtlePackets = {}
local base = "Interface\\AddOns\\VoicedAdventures\\Sounds\\"
local function checksum(value)
    local a, b = 1, 0
    for i = 1, string.len(value) do
        a = math.mod(a + string.byte(value, i), 65521)
        b = math.mod(b + a, 65521)
    end
    return b * 65536 + a
end
function VATurtlePackets.Encode(id, payload)
    if type(id) ~= "string" or string.len(id) ~= 16 or string.find(id, "[^0-9A-F]") then error("packet id") end
    if type(payload) ~= "string" or string.len(payload) < 1 or string.len(payload) > 8192 then error("packet length") end
    local count = math.ceil(string.len(payload) / 48)
    local sum = checksum(payload)
    local paths = {}
    for index = 1, count do
        local hex = ""
        local part = string.sub(payload, (index - 1) * 48 + 1, index * 48)
        for offset = 1, string.len(part) do hex = hex .. string.format("%02X", string.byte(part, offset)) end
        table.insert(paths, base .. string.format("VA1_%s_%03X_%03X_%08X_", id, index, count, sum) .. hex .. ".wav")
    end
    return paths
end
