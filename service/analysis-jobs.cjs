'use strict';
const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process');
const VERSION=4;
// Owns a single local background queue. No credentials or playback URLs enter Python.
module.exports=function createAnalysis({directory,resolveMedia}){
 const cache=path.join(directory,'analysis-cache');fs.mkdirSync(cache,{recursive:true});
 const jobs=new Map();let active=false;
 const file=id=>path.join(cache,id+'-v'+VERSION+'.json');
 function read(id){try{const data=JSON.parse(fs.readFileSync(file(id),'utf8'));if(data.version===VERSION)return data;}catch(_){}return null;}
 async function run(job){
  const input=path.join(cache,job.id+'.audio'),output=file(job.id)+'.tmp';
  try{
   job.state='running';const config=JSON.parse(fs.readFileSync(path.join(__dirname,'analysis-config.json'),'utf8'));
   if(!fs.existsSync(config.python))throw Error('Runtime unavailable');
   const media=await resolveMedia(job.id);if(!media.url||media.trial)throw Error('Full licensed audio required');
   const u=new URL(media.url);if(!['https:','http:'].includes(u.protocol))throw Error('Invalid audio URL');
   const response=await fetch(u.href.replace(/^http:/,'https:'),{signal:AbortSignal.timeout(120000)});if(!response.ok)throw Error('Audio unavailable');
   const fd=fs.openSync(input,'w');let size=0;try{for await(const chunk of response.body){size+=chunk.length;if(size>80*1024*1024)throw Error('Audio too large');fs.writeSync(fd,chunk);}}finally{fs.closeSync(fd);}
   await new Promise((resolve,reject)=>{
    const child=spawn(config.python,[path.join(__dirname,'../analysis/analyze-song.py'),input,output,String(job.duration),path.join(cache,job.id+'-v3.json')],{windowsHide:true,env:{...process.env,TORCH_HOME:config.models,OMP_NUM_THREADS:'4',MKL_NUM_THREADS:'4'}});
    // Keep only a short local diagnostic; never print account/session data.
    let error='';child.stderr.on('data',b=>{error=(error+b.toString()).slice(-4000)});child.stdout.on('data',b=>{const stage=b.toString().trim();if(['beats','vocals','pitch'].includes(stage))job.stage=stage;});
    try{require('node:os').setPriority(child.pid,require('node:os').constants.priority.PRIORITY_BELOW_NORMAL)}catch(_){}
    const timeout=setTimeout(()=>{child.kill();reject(Error('Analysis timeout'));},30*60*1000);
    child.on('error',e=>{clearTimeout(timeout);reject(e)});child.on('exit',code=>{clearTimeout(timeout);code===0?resolve():reject(Error(error||'Worker failed'))});
   });
   const result=JSON.parse(fs.readFileSync(output,'utf8'));if(result.version!==VERSION||!Array.isArray(result.pitch)||!Array.isArray(result.beats))throw Error('Invalid analysis');
   fs.renameSync(output,file(job.id));job.state='ready';
  }catch(e){job.state='failed';fs.writeFileSync(path.join(cache,job.id+'.error.txt'),String(e.message).slice(-4000));}
  finally{for(const f of [input,output,output+'.partial',output+'.partial-writing'])try{fs.unlinkSync(f)}catch(_){}active=false;pump();}
 }
 function pump(){if(active)return;const next=[...jobs.values()].find(j=>j.state==='queued');if(next){active=true;run(next);}}
 return {status(id,queue=false,duration=0){
  const data=read(id);if(data&&(!duration||Math.abs(data.duration-duration)<3))return {state:'ready',timeline:data};
  if(!fs.existsSync(path.join(__dirname,'analysis-config.json')))return {state:'disabled'};
  let fallback=null;try{fallback=JSON.parse(fs.readFileSync(path.join(cache,id+'-v3.json'),'utf8'));if(duration&&Math.abs(fallback.duration-duration)>=3)fallback=null;}catch(_){}
  let job=jobs.get(id);
  if(queue&&(!job||job.state==='failed'||job.state==='ready')){
   if(!Number.isFinite(duration)||duration<10||duration>1200)return {state:'unavailable'};
   if([...jobs.values()].filter(j=>['queued','running'].includes(j.state)).length>=8)return {state:'unavailable'};
   job={id,duration,state:'queued'};jobs.set(id,job);pump();
  }
  if(job?.state==='running')try{const partial=JSON.parse(fs.readFileSync(file(id)+'.tmp.partial','utf8'));if(fallback&&!partial.beats.length){partial.beats=fallback.beats;partial.bars=fallback.bars;partial.tempo=fallback.tempo;}fallback=partial}catch(_){}
  return {state:job?.state==='ready'?'idle':job?.state||'idle',stage:job?.stage,timeline:fallback};
 }};
};
