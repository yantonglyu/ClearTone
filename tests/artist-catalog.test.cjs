'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const create=require('../service/artist-catalog.cjs');
test('artist MV catalog preserves video identity and pagination separately from songs',async()=>{
 const calls=[];const api=create({safeImage:s=>s||'',call:async(name,args)=>{calls.push({name,args});return {mvs:[{id:123,name:'Stage',artistName:'Liella!',imgurl:'https://example.org/cover.jpg',duration:95000}],hasMore:true};}});
 const page=await api.artistMvs({id:'456',offset:40});
 assert.deepEqual(calls,[{name:'artist_mv',args:{id:'456',offset:40,limit:40}}]);
 assert.equal(page.tracks[0].netEaseMvId,'123');assert.equal(page.tracks[0].netEaseId,'');assert.equal(page.tracks[0].artistId,'456');assert.equal(page.tracks[0].fullDuration,95);assert.equal(page.more,true);assert.equal(page.nextOffset,41);
});
test('artist MV catalog rejects invalid ids and tolerates an empty catalog',async()=>{
 let calls=0;const api=create({safeImage:s=>s||'',call:async()=>{calls++;return {};}});
 await assert.rejects(api.artistMvs({id:'../123'}));assert.equal(calls,0);
 const page=await api.artistMvs({id:'123'});assert.deepEqual(page.tracks,[]);assert.equal(page.more,false);
});
