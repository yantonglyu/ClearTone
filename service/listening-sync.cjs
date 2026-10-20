'use strict';
const fs=require('node:fs'),path=require('node:path');
module.exports=function createListeningSync({directory,call,requireLogin,getProfile}){
 const file=path.join(directory,'listening-ledger.json');
 let ledger={},storageError=false;
 try{ledger=fs.existsSync(file)?JSON.parse(fs.readFileSync(file,'utf8')):{};if(!ledger||typeof ledger!=='object'||Array.isArray(ledger))throw Error('Invalid ledger');}
 catch{ledger={};storageError=true;}
 for(const row of Object.values(ledger))if(row.state==='sending')row.state='uncertain';
 function save(){fs.writeFileSync(file+'.tmp',JSON.stringify(ledger));fs.renameSync(file+'.tmp',file);}
 return {async report(input){
  if(storageError)throw Error('Listening ledger unavailable');
  await requireLogin();
  const {sessionId,accountId,songId,sourceId,seconds,startedAt}=input;
  if(!/^[a-f0-9]{32}$/.test(sessionId)||!/^\d{1,20}$/.test(songId)||!/^\d{1,20}$/.test(accountId)||
     !/^\d{0,20}$/.test(sourceId)||!Number.isFinite(seconds)||seconds<1||seconds>86400||
     !Number.isFinite(startedAt)||startedAt>Date.now()+60000||seconds>(Date.now()-startedAt)/1000+2)throw Error('Invalid listening record');
  if(String(getProfile().id)!==accountId)return {state:'account-mismatch'};
  const key=accountId+':'+sessionId;
  if(ledger[key])return {state:ledger[key].state==='sending'?'pending':ledger[key].state,seconds:ledger[key].seconds};
  // Persist the claim BEFORE an external write. Timeouts are not safe to resend:
  // NetEase exposes no verified idempotency key for this endpoint.
  const row={songId,seconds:Math.floor(seconds),state:'sending',at:Date.now()};ledger[key]=row;save();
  try{
   const result=await call('scrobble',{id:songId,sourceid:sourceId||songId,time:row.seconds});
   row.state=result.code===200&&result.details?.startplay?.code===200&&result.details?.play?.code===200?'sent':'rejected';
  }catch{row.state='uncertain';}
  save();return {state:row.state,seconds:row.seconds};
 }};
};
