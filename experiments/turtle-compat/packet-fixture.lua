math.mod = math.mod or math.fmod
table.getn = table.getn or function(t) return #t end
dofile("experiments/turtle-compat/PacketTransport.lua")
local text = "NPC\n" .. string.rep("任务文本，重复对话也应完整传输。", 80)
local paths = VATurtlePackets.Encode("0000000000000001", text)
for _, path in ipairs(paths) do
    assert(string.len(path)<240)
    print(path)
end
assert(not pcall(VATurtlePackets.Encode,"bad",text))
assert(not pcall(VATurtlePackets.Encode,"0000000000000001",string.rep("x",8193)))
