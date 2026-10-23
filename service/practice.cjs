'use strict';
const {Worker}=require('node:worker_threads'),path=require('node:path'),fs=require('node:fs/promises'),crypto=require('node:crypto');
module.exports=directory=>{
 let worker,id=0,idle;const pending=new Map(),inflight=new Map();
 function stop(){clearTimeout(idle);const old=worker;worker=null;if(old)old.terminate();}
 function start(){if(worker)return;const current=worker=new Worker(path.join(__dirname,'practice-worker.cjs'));current.on('message',m=>{const p=pending.get(m.id);if(!p)return;clearTimeout(p.timer);pending.delete(m.id);m.error?p.reject(Error(m.error)):p.resolve(m.lines);if(!pending.size){clearTimeout(idle);idle=setTimeout(stop,60000);idle.unref();}});current.on('error',e=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(e);}pending.clear();if(worker===current)stop();});}
 async function annotate(lines){
  if(!Array.isArray(lines)||lines.length>500||lines.some(x=>typeof x!=='string'||x.length>1000)||lines.join('').length>40000)throw Error('Invalid lyric text');
  const key=crypto.createHash('sha256').update('kuromoji-0.1.2-v2\n'+JSON.stringify(lines)).digest('hex'),folder=path.join(directory,'readings'),file=path.join(folder,key+'.json');
  try{return JSON.parse(await fs.readFile(file,'utf8'));}catch{}
  if(inflight.has(key))return inflight.get(key);
  const task=(async()=>{start();clearTimeout(idle);const job=++id;const result=await new Promise((resolve,reject)=>{const timer=setTimeout(()=>{pending.delete(job);reject(Error('Reading analysis timed out'));if(!pending.size)stop();},45000);pending.set(job,{resolve,reject,timer});worker.postMessage({id:job,lines});});const out={version:1,lines:result};await fs.mkdir(folder,{recursive:true});await fs.writeFile(file+'.tmp',JSON.stringify(out));await fs.rename(file+'.tmp',file);return out;})();
  inflight.set(key,task);try{return await task;}finally{inflight.delete(key);}
 }
 return {annotate,close:stop};
};
