local succeeded, failure = xpcall(function()
local messages, timers, calls = {}, {}, {}
print = function(...) local a = {...}; for i,v in ipairs(a) do a[i] = tostring(v) end; messages[#messages+1] = table.concat(a, " ") end
SlashCmdList = {}
C_Timer = {After = function(delay, fn) timers[#timers+1] = {delay,fn} end}
PlaySoundFile = function(path, channel) calls[#calls+1] = path; assert(channel == "Master"); return true, #calls end
local ok, err = pcall(dofile, "experiments/file-read-egress/addon/VoicedAdventures/Main.lua")
assert(ok, err)
assert(#calls == 0 and #timers == 0, "must not autostart")
QVRDiagnosticCommand("test")
assert(#timers == 14, "14 resource requests")
QVRDiagnosticCommand("test")
assert(#timers == 14, "no reentry")
for i,timer in ipairs(timers) do
    assert(timer[1] == i * 0.75, "bounded ordered schedule")
    timer[2]()
end
assert(#calls == 14)
local sequence = {"A","B","A","S","A","B","S"}
for i,name in ipairs(sequence) do
    assert(calls[i] == "Interface\\AddOns\\VoicedAdventures\\Sounds\\" .. name .. ".wav")
    assert(calls[i+7] == "Interface\\AddOns\\VoicedAdventures\\Sounds\\fresh" .. i .. ".wav")
end
local before = #timers
QVRDiagnosticCommand("unknown")
assert(#timers == before, "unknown command must not issue sound")
timers = {}
PlaySoundFile = function() error("simulated resource error") end
QVRDiagnosticCommand("test")
for _,timer in ipairs(timers) do timer[2]() end
assert(messages[#messages]:find("DONE"), "errors must not leave run stuck")
timers = {}
PlaySoundFile = nil
QVRDiagnosticCommand("test")
assert(#timers == 0, "missing API must not start")
io.write("PASS resource sequence, timings, no autostart, reentry, failure recovery and API guard\n")
end, debug.traceback)
if not succeeded then io.stderr:write(failure .. "\n"); os.exit(1) end
