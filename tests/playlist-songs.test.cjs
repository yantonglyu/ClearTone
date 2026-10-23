const test=require('node:test'),assert=require('node:assert/strict');
const load=require('../service/playlist-songs.cjs');
test('all 70 embedded songs survive without the lossy detail request',async()=>{
 const tracks=Array.from({length:70},(_,i)=>({id:i+1,name:'song '+i}));
 const result=await load({trackIds:tracks.map(t=>({id:t.id})),tracks},()=>{throw Error('unnecessary lookup')});
 assert.deepEqual(result,tracks);
});
test('only missing metadata is fetched, playlist order wins over response order',async()=>{
 const result=await load({trackIds:[{id:3},{id:1},{id:2}],tracks:[{id:1,name:'original'}]},async(name,p)=>{
  assert.equal(name,'song_detail');assert.equal(p.ids,'3,2');return {songs:[{id:2,name:'two'},{id:3,name:'three'}]};
 });
 assert.deepEqual(result.map(t=>t.id),[3,1,2]);assert.equal(result[1].name,'original');
});
test('unavailable IDs remain visible and an absent ID list uses embedded order',async()=>{
 const result=await load({trackIds:[{id:1},{id:9}],tracks:[{id:1,name:'one'}]},async()=>({songs:[]}));
 assert.equal(result.length,2);assert.equal(result[1].name,'暂不可用的歌曲');
 assert.deepEqual(await load({tracks:[{id:1,name:'one'}]},()=>{throw Error('unused')}),[{id:1,name:'one'}]);
});
