const test=require('node:test'),assert=require('node:assert/strict');
const make=require('../service/bili-stream.cjs');
test('Bilibili domestic and overseas media URLs are allowed, unrelated addresses rejected',()=>{
 for(const u of ['https://upos-sz-mirrorakam.akamaized.net/a','http://upos.bilivideo.com/a'])assert(make.allowed(u).startsWith('https:'));
 for(const u of ['https://evil.com/a','http://127.0.0.1/a','https://upos-a.akamaized.net.evil.com/a','https://user:pass@upos.bilivideo.com/a'])assert.throws(()=>make.allowed(u));
});
test('Registration issues process-local tokens, separate stream IDs and no source disclosure',()=>{
 const service=make({base:'http://127.0.0.1:1',token:'secret'});
 const r=service.register({audioUrl:'https://upos.bilivideo.com/a',videoUrl:'https://upos-sz-mirrorakam.akamaized.net/v',referer:'https://www.bilibili.com/video/BV12WR7BwESW/'});
 assert.notEqual(r.audioUrl,r.videoUrl);assert(!r.videoUrl.includes('akamaized'));assert(r.videoUrl.endsWith('token=secret'));
 assert.throws(()=>service.register({audioUrl:'https://upos.bilivideo.com/a',referer:'https://evil.com/'}));
});
test('Range streams preserve seek metadata and mandatory video headers',async()=>{
 const http=require('node:http');let request;
 const service=make({base:'http://127.0.0.1:1',token:'s',fetch:async(u,o)=>{request={u,o};return new Response(new Uint8Array([1,2,3]),{status:206,headers:{'Content-Range':'bytes 5-7/100','Content-Length':'3','Content-Type':'video/mp4','Accept-Ranges':'bytes'}});}});
 const r=service.register({audioUrl:'https://upos.bilivideo.com/a',referer:'https://www.bilibili.com/video/BV12WR7BwESW/'}),id=new URL(r.audioUrl).searchParams.get('id');
 const server=http.createServer((req,res)=>service.serve(req,res,id));await new Promise(r=>server.listen(0,'127.0.0.1',r));
 try{const response=await fetch('http://127.0.0.1:'+server.address().port,{headers:{Range:'bytes=5-7'}});assert.equal(response.status,206);assert.equal(response.headers.get('content-range'),'bytes 5-7/100');assert.deepEqual([...new Uint8Array(await response.arrayBuffer())],[1,2,3]);assert.equal(request.o.headers.Range,'bytes=5-7');assert.equal(request.o.headers.Referer,'https://www.bilibili.com/video/BV12WR7BwESW/');assert(request.o.headers['User-Agent'].includes('Mozilla'));}finally{server.closeAllConnections();await new Promise(r=>server.close(r));}
});
test('DASH manifests expose initialization and sidx ranges without source URL disclosure',async()=>{
 const http=require('node:http');let fetched=0;
 const service=make({base:'http://127.0.0.1:1',token:'s',fetch:async()=>{fetched++;throw Error('manifest should not fetch media');}});
 const r=service.register({audioUrl:'https://upos.bilivideo.com/a',videoUrl:'https://upos.bilivideo.com/v?private=source',referer:'https://www.bilibili.com/video/BV12WR7BwESW/',video:{duration:9456,codecs:'avc1.640033',bandwidth:756547,width:852,height:480,initialization:'0-937',indexRange:'938-23837'}});
 const id=new URL(r.videoUrl).searchParams.get('id');
 const server=http.createServer((req,res)=>service.serve(req,res,id));await new Promise(r=>server.listen(0,'127.0.0.1',r));
 try{const response=await fetch('http://127.0.0.1:'+server.address().port);assert.equal(response.status,200);assert.equal(response.headers.get('content-type'),'application/dash+xml');const xml=await response.text();assert(xml.includes('indexRange="938-23837"'));assert(xml.includes('range="0-937"'));assert(xml.includes('mediaPresentationDuration="PT9456S"'));assert(xml.includes('&amp;token=s'));assert(!xml.includes('private=source'));assert(!xml.includes('bilivideo'));assert.equal(fetched,0);}finally{server.closeAllConnections();await new Promise(r=>server.close(r));}
});
test('Transient header failures retry before sending a response, then use same-file CDN backup',async()=>{
 const http=require('node:http'),requests=[];
 const service=make({base:'http://127.0.0.1:1',token:'s',fetch:async(url,options)=>{requests.push({url,range:options.headers.Range});if(!url.includes('backup'))throw new TypeError('fetch failed');return new Response(new Uint8Array([4,5]),{status:206,headers:{'Content-Range':'bytes 4-5/10','Content-Length':'2'}});}});
 const r=service.register({audioUrl:'https://upos.bilivideo.com/a',audioUrls:['https://upos.bilivideo.com/a','https://upos-backup.akamaized.net/a','http://127.0.0.1/private'],referer:'https://www.bilibili.com/video/BV12WR7BwESW/'}),id=new URL(r.audioUrl).searchParams.get('id');
 const server=http.createServer((req,res)=>service.serve(req,res,id));await new Promise(r=>server.listen(0,'127.0.0.1',r));
 try{const response=await fetch('http://127.0.0.1:'+server.address().port,{headers:{Range:'bytes=4-5'}});assert.equal(response.status,206);assert.deepEqual([...new Uint8Array(await response.arrayBuffer())],[4,5]);assert.equal(requests.length,3);assert(requests.every(r=>r.range==='bytes=4-5'));assert(requests.at(-1).url.includes('backup'));}finally{server.closeAllConnections();await new Promise(r=>server.close(r));}
});
