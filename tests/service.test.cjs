const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path'),{spawn}=require('node:child_process');
const library=require('../service/song-library.cjs');
test('library validates input, ownership and duplicate collection',async()=>{
 const context={requireLogin:async()=>{},getProfile:()=>({id:7}),call:async(name)=>name==='playlist_detail'?{code:200,playlist:{creator:{userId:7},trackIds:[{id:42}]}}:{code:200,ids:[42]}};
 assert.equal((await library.handle('/api/liked',{id:'42'},context)).liked,true);
 assert.equal((await library.handle('/api/collect',{id:'42',playlistId:'9'},context)).already,true);
 await assert.rejects(()=>library.handle('/api/like',{id:'42',liked:'yes'},context));
 await assert.rejects(()=>library.handle('/api/liked',{id:'bad'},context));
 const foreign={...context,call:async()=>({code:200,playlist:{creator:{userId:8}}})};
 await assert.rejects(()=>library.handle('/api/collect',{id:'42',playlistId:'9'},foreign));
});
test('service starts independently, keeps login empty and rejects browser misuse',async()=>{
 const directory=fs.mkdtempSync(path.join(os.tmpdir(),'cleartone-test-'));
 const net=require('node:net'),port=await new Promise(resolve=>{const s=net.createServer();s.listen(0,'127.0.0.1',()=>{const p=s.address().port;s.close(()=>resolve(p));});});
 const origin='http://127.0.0.1:'+port;
 const child=spawn(process.execPath,[path.resolve(__dirname,'../service/server.cjs')],{windowsHide:true,stdio:'ignore',env:{...process.env,CLEARTONE_PORT:String(port),CLEARTONE_ORIGIN:origin,CLEARTONE_DATA:directory}});
 try{
  let ready=false;for(let i=0;i<100;i++){try{const r=await fetch(origin+'/health');assert.equal((await r.json()).service,'cleartone-service-v1');ready=true;break;}catch{await new Promise(r=>setTimeout(r,100));}}
  assert(ready);
  const call=(route,headers={})=>fetch(origin+route,{method:'POST',headers:{'Content-Type':'application/json',...headers},body:'{}'});
  assert.equal((await call('/api/status')).status,403);
  assert.equal((await call('/api/status',{Origin:'https://example.com','X-ClearTone-Client':'1'})).status,403);
  const status=await call('/api/status',{Origin:origin,'X-ClearTone-Client':'1'});assert.equal(status.status,200);assert.equal((await status.json()).loggedIn,false);
  assert.equal((await fetch(origin+'/media?id=42&token=invalid')).status,403);
  assert(!fs.existsSync(path.join(directory,'account.dpapi')));
 }finally{if(child.exitCode===null&&child.signalCode===null){const stopped=new Promise(resolve=>child.once('exit',resolve));child.kill();await stopped;}fs.rmSync(directory,{recursive:true,force:true});}
});
