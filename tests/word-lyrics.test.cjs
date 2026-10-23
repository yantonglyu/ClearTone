const test=require('node:test'),assert=require('node:assert/strict');
const {parseYrc,normalize,load}=require('../service/word-lyrics.cjs');

test('YRC uses absolute milliseconds, preserves word text and skips credit metadata',()=>{
 const raw='{"t":0,"c":[{"tx":"Credits"}]}\n[12000,2300](12000,500,0)First (12500,1800,0)word!\r\n';
 assert.deepEqual(parseYrc(raw),{source:'netease-yrc',lines:[{start:12,duration:2.3,text:'First word!',words:[{start:12,duration:.5,text:'First '},{start:12.5,duration:1.8,text:'word!'}]}]});
});
test('Japanese text, supplementary Unicode and literal parentheses stay intact',()=>{
 const timeline=parseYrc('[300,600](300,200,0)あ(500,200,0)😀(700,200,0)(声)');
 assert.equal(timeline.lines[0].text,'あ😀(声)');assert.equal(timeline.lines[0].words.length,3);assert.equal(timeline.lines[0].words[2].text,'(声)');
});
test('Zero duration punctuation is a real boundary rather than an estimated interval',()=>{
 const words=parseYrc('[1000,1000](1000,1000,0)La(2000,0,0)!').lines[0].words;
 assert.equal(words[1].start,2);assert.equal(words[1].duration,0);
});
test('Malformed, relative, backward and out-of-line timestamps do not appear precise',()=>{
 for(const raw of ['[1000,1000](0,100,0)a','[1000,1000](1500,100,0)a(1400,100,0)b','[1000,1000](1900,200,0)a','[1000,1000]prefix(1000,100,0)a','[0,0](0,0,0)a','[1,2](9007199254740992,1,0)a'])assert.equal(parseYrc(raw).lines.length,0,raw);
});
test('Line order is chronological and empty/unavailable YRC stays empty',()=>{
 assert.deepEqual(parseYrc('[2000,100](2000,100,0)b\n[1000,100](1000,100,0)a').lines.map(l=>l.start),[1,2]);
 for(const raw of [null,undefined,'','[00:01]only line lyrics','x'.repeat(2*1024*1024+1)])assert.equal(parseYrc(raw).lines.length,0);
});
test('Legacy lyrics and YRC-aligned translations remain separate',()=>{
 const payload=normalize({lrc:{lyric:'[00:10]old'},tlyric:{lyric:'[00:10]译'},romalrc:{lyric:'[00:10]roma'},yrc:{lyric:'[11000,1000](11000,1000,0)new'},ytlrc:{lyric:'[00:11]译'},yromalrc:{lyric:'[00:11]roma'}});
 assert.equal(payload.lrc,'[00:10]old');assert.equal(payload.translation,'[00:10]译');assert.equal(payload.romanization,'[00:10]roma');assert.equal(payload.wordTranslation,'[00:11]译');assert.equal(payload.wordRomanization,'[00:11]roma');assert.equal(payload.wordTimeline.lines[0].start,11);
});
test('Public new endpoint is used once when it supplies normal LRC, including no YRC',async()=>{
 let calls=0;const result=await load('123',async(name,p)=>{calls++;assert.equal(name,'lyric_new');assert.deepEqual(p,{id:'123'});return {code:200,lrc:{lyric:'[00:01]test'}};});
 assert.equal(calls,1);assert.equal(result.lrc,'[00:01]test');assert.deepEqual(result.wordTimeline.lines,[]);
});
test('A rejected word endpoint falls back to the existing lyric route',async()=>{
 const calls=[];const result=await load('123',async name=>{calls.push(name);if(name==='lyric_new')throw Error('provider rejected');return {lrc:{lyric:'legacy'},tlyric:{lyric:'translation'}};});
 assert.deepEqual(calls,['lyric_new','lyric']);assert.equal(result.lrc,'legacy');assert.equal(result.translation,'translation');assert.deepEqual(result.wordTimeline.lines,[]);
});
test('Missing legacy fields are recovered without discarding available word data',async()=>{
 const result=await load('123',async name=>name==='lyric_new'?{code:200,yrc:{lyric:'[1000,100](1000,100,0)a'}}:{lrc:{lyric:'line'},romalrc:{lyric:'roman'}});
 assert.equal(result.lrc,'line');assert.equal(result.romanization,'roman');assert.equal(result.wordTimeline.lines.length,1);
});
test('When both endpoints fail the original caller receives failure, not fake empty lyrics',async()=>{
 await assert.rejects(()=>load('123',async()=>{throw Error('offline')}),/offline/);
});
