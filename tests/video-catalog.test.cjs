const test=require('node:test'),assert=require('node:assert/strict');
const mv=require('../service/video-catalog.cjs');
test('MV keeps independent identity and resolves fresh audio/video from one source',async()=>{
 const calls=[];const r=await mv({id:'123'},{call:async(n,p)=>{calls.push([n,p]);return n==='mv_detail'?{data:{name:'Live',artistName:'Artist',duration:100000,cover:'http://p.music.126.net/a.jpg'}}:{data:{url:'http://vod.music.126.net/v.mp4'}};}});
 assert.equal(r.track.netEaseId,'');assert.equal(r.track.netEaseMvId,'123');assert.equal(r.track.fullDuration,100);assert.equal(r.track.url,r.track.videoUrl);assert(r.track.url.startsWith('https:'));assert.equal(calls.length,2);
});
test('MV failures are explicit and do not become empty playable queue entries',async()=>{
 for(const url of [undefined,'file:///C:/secret','javascript:alert(1)'])await assert.rejects(()=>mv({id:'1'},{call:async n=>n==='mv_detail'?{data:{name:'x'}}:{data:{url}}}),e=>!!e.publicMessage);
 await assert.rejects(()=>mv({id:'../1'},{call:()=>{throw Error('must not call')}}),e=>!!e.publicMessage);
});
test('A missing optional detail API does not block available MV media',async()=>{
 const r=await mv({id:'1',metadata:{name:'Known title',artist:'Known singer',fullDuration:120}},{call:async n=>{if(n==='mv_detail')throw Error('404');return {data:{url:'https://vod.music.126.net/a.mp4'}};}});
 assert.equal(r.track.name,'Known title');assert.equal(r.track.artist,'Known singer');assert.equal(r.track.fullDuration,120);
 await assert.rejects(()=>mv({id:'1'},{call:async n=>n==='mv_detail'?{}:{data:{url:null,msg:'版权方要求，当前资源暂时无法使用'}}}),e=>e.publicMessage.includes('版权方'));
});
