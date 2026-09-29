local shown=true
ItemTextFrame={IsVisible=function() return shown end}
local raw='<HTML><BODY><H1>风蚀石板</H1><P>你好，|cffffffff勇士|r。<BR/>带上 &lt;石板&gt; &amp; 信件。</P><P>|Ticon|t第二段。</P></BODY></HTML>'
ItemTextGetText=function() return raw end
ItemTextGetItem=function() return '风蚀石板' end
ItemTextGetPage=function() return 2 end
dofile('experiments/file-read-egress/addon/VoicedAdventures/BookText.lua')
local text,err,meta=QVRBookText.Read()
assert(text=='风蚀石板\n你好，勇士。\n带上 <石板> & 信件。\n\n第二段。',text)
assert(meta.Source=='book' and meta.SpeakerRace=='' and meta.SpeakerSex=='' and meta.NpcId=='')
assert(meta.QuestTitle=='风蚀石板 · 第2页')
shown=false;assert(QVRBookText.Read()==nil)
shown=true;raw='';assert(QVRBookText.Read()==nil)
raw=string.rep('a',4097);assert(QVRBookText.Read()==nil)
raw='第3页';ItemTextGetPage=function() return 3 end
local nextText,_,nextMeta=QVRBookText.Read()
assert(nextText=='第3页' and nextMeta.QuestTitle~=meta.QuestTitle)
ItemTextGetText=function() error('not ready') end
assert(QVRBookText.Read()==nil)
print('PASS book text: page metadata, isolation, markup, hidden, empty, bounds, unavailable')
