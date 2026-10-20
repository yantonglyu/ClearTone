const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const create=require('../service/listening-sync.cjs');
const input=()=>({sessionId:require('node:crypto').randomBytes(16).toString('hex'),accountId:'7',songId:'42',sourceId:'9',seconds:12.9,startedAt:Date.now()-20000});
const good={code:200,details:{startplay:{code:200},play:{code:200}}};
test('concurrent duplicate, restart and account isolation never repeat an external report',async()=>{
 const directory=fs.mkdtempSync(path.join(os.tmpdir(),'cleartone-listening-'));let calls=0,release;
 const context={directory,requireLogin:async()=>{},getProfile:()=>({id:7}),call:async(name,p)=>{calls++;assert.equal(name,'scrobble');assert.equal(p.time,12);await new Promise(r=>release=r);return good;}};
 try{
  const sync=create(context),record=input(),first=sync.report(record);await new Promise(r=>setImmediate(r));
  assert.equal((await sync.report(record)).state,'pending');release();assert.equal((await first).state,'sent');
  assert.equal((await create(context).report(record)).state,'sent');assert.equal(calls,1);
  assert.equal((await sync.report({...input(),accountId:'8'})).state,'account-mismatch');assert.equal(calls,1);
  await assert.rejects(()=>sync.report({...input(),seconds:1000}));assert.equal(calls,1);
 }finally{fs.rmSync(directory,{recursive:true,force:true});}
});
test('ambiguous upstream timeout and crash claim are never retried',async()=>{
 const directory=fs.mkdtempSync(path.join(os.tmpdir(),'cleartone-listening-'));let calls=0;
 const context={directory,requireLogin:async()=>{},getProfile:()=>({id:7}),call:async()=>{calls++;throw Error('response lost');}};
 try{
  const r=input();assert.equal((await create(context).report(r)).state,'uncertain');assert.equal((await create(context).report(r)).state,'uncertain');assert.equal(calls,1);
  const crash=input();fs.writeFileSync(path.join(directory,'listening-ledger.json'),JSON.stringify({['7:'+crash.sessionId]:{state:'sending',seconds:12}}));
  assert.equal((await create(context).report(crash)).state,'uncertain');assert.equal(calls,1);
 }finally{fs.rmSync(directory,{recursive:true,force:true});}
});

test('damaged ledger disables reporting without stopping the service',async()=>{
 const directory=fs.mkdtempSync(path.join(os.tmpdir(),'cleartone-listening-'));let calls=0;
 try{
  fs.writeFileSync(path.join(directory,'listening-ledger.json'),'{broken');
  const sync=create({directory,requireLogin:async()=>{},getProfile:()=>({id:7}),call:async()=>{calls++;return good;}});
  await assert.rejects(()=>sync.report(input()),/ledger unavailable/);assert.equal(calls,0);
 }finally{fs.rmSync(directory,{recursive:true,force:true});}
});
