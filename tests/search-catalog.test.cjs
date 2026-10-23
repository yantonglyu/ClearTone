const test=require('node:test'),assert=require('node:assert/strict');
const create=require('../service/search-catalog.cjs');
const song=s=>({name:s.name,netEaseId:String(s.id)});
const offline=()=>{throw Error('Unexpected external request')};
test('NetEase songs retain source playback resolution and paginate',async()=>{
 const catalog=create({song,fetch:offline,call:async(name,p)=>{
  assert.equal(name,'cloudsearch');assert.deepEqual(p,{keywords:'Liella',type:1,limit:40,offset:40});
  return {result:{songs:[{id:42,name:'song'}],songCount:90}};
 }});
 const result=await catalog.search({query:' Liella ',offset:40});
 assert.deepEqual(result.tracks,[{name:'song',netEaseId:'42'}]);assert.equal(result.more,true);assert.equal(result.nextOffset,80);
});
test('NetEase artist search has an artist result, not song keyword results',async()=>{
 const catalog=create({song,call:async(name,p)=>{
  assert.equal(p.type,100);return {result:{artists:[{id:7,name:'歌手',picUrl:'http://p1.music.126.net/a.jpg',alias:['Singer']}],artistCount:1}};
 }});
 const r=await catalog.search({query:'歌手',kind:'artists',source:'netease'});
 assert.deepEqual(r.tracks,[]);assert.deepEqual(r.artists,[{id:'7',name:'歌手',cover:'https://p1.music.126.net/a.jpg',source:'netease',description:'Singer'}]);assert.equal(r.more,false);
});
test('Artist songs use the artist ID with pagination and upstream more state',async()=>{
 const catalog=create({song,call:async(name,p)=>{
  assert.equal(name,'artist_songs');assert.deepEqual(p,{id:'7',limit:40,offset:80,order:'hot'});
  return {songs:[{id:9,name:'next'}],more:true};
 }});
 const r=await catalog.artistTracks({id:'7',source:'netease',offset:80});
 assert.equal(r.more,true);assert.equal(r.tracks[0].netEaseId,'9');assert.equal(r.nextOffset,120);
});
test('Bilibili video results normalize highlight markup, UP names and source IDs',async()=>{
 const catalog=create({song,call:offline,fetch:async(url,options)=>{
  assert.equal(url.origin,'https://api.bilibili.com');assert.equal(url.searchParams.get('page'),'2');assert.equal(url.searchParams.get('keyword'),'Liella');assert.ok(options.signal);
  return {ok:true,status:200,json:async()=>({code:0,data:{result:[{bvid:'BV1RNnJ6AEEm',title:'<em class="keyword">Song</em> &amp; &#26085;',author:'Liella &quot;Live&quot;',pic:'//i0.hdslb.com/a.jpg',duration:'1:02:03'},{bvid:'invalid',title:'invalid'}],numResults:38,numPages:2}})};
 }});
 const r=await catalog.search({query:'Liella',source:'bilibili',offset:20});
 assert.equal(r.tracks.length,1);assert.deepEqual(r.tracks[0],{name:'Song & 日',artist:'Liella "Live"',cover:'https://i0.hdslb.com/a.jpg',url:'',lrc:'',type:'bilibili',netEaseId:'',bvid:'BV1RNnJ6AEEm',biliPage:1,biliCid:0,fullDuration:3723});assert.equal(r.more,false);assert.equal(r.nextOffset,40);
});
test('Bilibili author search and author video browser use distinct official endpoints',async()=>{
 const catalog=create({song,call:offline,fetch:async url=>{
  if(url.pathname.endsWith('/type')){
   assert.equal(url.searchParams.get('search_type'),'bili_user');
   return {ok:true,status:200,json:async()=>({code:0,data:{result:[{mid:123,uname:'UP 主',upic:'//i0.hdslb.com/a.jpg',usign:'原唱'}],numPages:1,numResults:1}})};
  }
  assert.equal(url.pathname,'/x/space/arc/search');assert.equal(url.searchParams.get('mid'),'123');assert.equal(url.searchParams.get('pn'),'2');
  return {ok:true,status:200,json:async()=>({code:0,data:{list:{vlist:[{bvid:'BV1RNnJ6AEEm',title:'video',author:'UP 主',length:'04:10'}]},page:{count:45}}})};
 }});
 const author=(await catalog.search({query:'UP 主',source:'bilibili',kind:'artists'})).artists[0];
 assert.equal(author.id,'123');const r=await catalog.artistTracks({...author,offset:20});assert.equal(r.tracks[0].fullDuration,250);assert.equal(r.more,true);
});
test('Bilibili restrictions are reported without request retries or bypasses',async()=>{
 let requests=0;const catalog=create({song,call:offline,fetch:async()=>{requests++;return {ok:false,status:412}}});
 await assert.rejects(()=>catalog.search({query:'x',source:'bilibili'}),e=>e.status===502&&e.publicMessage.includes('限制'));
 assert.equal(requests,1);
});
test('Bilibili nonzero API code and failed network return readable public errors',async()=>{
 const rejected=create({song,call:offline,fetch:async()=>({ok:true,status:200,json:async()=>({code:-352,message:'blocked'})})});
 await assert.rejects(()=>rejected.search({query:'x',source:'bilibili'}),e=>!!e.publicMessage);
 const disconnected=create({song,call:offline,fetch:async()=>{throw Error('network')}});
 await assert.rejects(()=>disconnected.search({query:'x',source:'bilibili'}),e=>e.publicMessage.includes('连接'));
});
test('Empty search makes no upstream request, local files are not exposed through service',async()=>{
 const catalog=create({song,call:offline,fetch:offline});
 assert.equal((await catalog.search({query:' '})).tracks.length,0);
 await assert.rejects(()=>catalog.search({query:'private',source:'local'}),e=>e.status===400);
 await assert.rejects(()=>catalog.artistTracks({source:'netease',id:'../../private'}),e=>e.status===400);
});
test('Offsets and queries are bounded',async()=>{
 let calls=0;const catalog=create({song,call:async(name,p)=>{calls++;assert.equal(p.offset,10000);assert.equal(p.keywords.length,100);return {result:{songs:[],songCount:0}}}});
 assert.equal((await catalog.search({query:'x'.repeat(200),offset:Infinity})).more,false);assert.equal(calls,1);
});
