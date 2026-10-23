const test=require('node:test'),assert=require('node:assert/strict');
const refresh=require('../service/native-track.cjs');
test('known queue metadata plays independently of the detail endpoint, with newly issued media URLs',async()=>{
 const metadata={name:'Holiday∞Holiday',artist:'スリーズブーケ',cover:'https://example.com/cover.jpg',fullDuration:198,url:'stale-token'};
 let detailCalls=0;
 const track=await refresh('2056955316',metadata,{call:async()=>{detailCalls++;throw {status:404};},song:s=>({id:s.id,name:s.name,artist:s.ar[0].name,cover:s.al.picUrl,fullDuration:s.dt/1000,url:'new-session-token'})});
 assert.equal(detailCalls,0);assert.equal(track.id,'2056955316');assert.equal(track.name,metadata.name);assert.equal(track.artist,metadata.artist);assert.equal(track.cover,metadata.cover);assert.equal(track.fullDuration,198);assert.equal(track.url,'new-session-token');
});
test('ID-only callers still resolve details and choose the requested ID',async()=>{
 let called;const track=await refresh('42',undefined,{call:async(name,args)=>{called={name,args};return {songs:[{id:7,name:'other'},{id:42,name:'requested'}]};},song:s=>s});
 assert.deepEqual(called,{name:'song_detail',args:{ids:'42'}});assert.equal(track.name,'requested');
});
test('missing metadata cannot turn an unavailable ID into a false successful detail lookup',async()=>{
 for(const metadata of [undefined,{}, {name:''},{name:'暂不可用的歌曲'}])await assert.rejects(()=>refresh('42',metadata,{call:async()=>({songs:[]}),song:s=>s}),e=>e.status===404&&e.publicMessage.includes('详情'));
});
test('ID-only upstream failure is propagated, not reported as success',async()=>{
 const error={status:401};await assert.rejects(()=>refresh('42',null,{call:async()=>{throw error},song:s=>s}),e=>e===error);
});
