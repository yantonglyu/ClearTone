const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const create=require('../service/analysis-jobs.cjs');
function fixture(){const directory=fs.mkdtempSync(path.join(os.tmpdir(),'cleartone-analysis-'));const configFile=path.join(directory,'config.json');fs.writeFileSync(configFile,JSON.stringify({python:process.execPath,models:directory}));return {directory,configFile,script:__filename};}
test('missing analysis script is unavailable before any audio fetch',()=>{
 const f=fixture();let calls=0;try{const jobs=create({...f,script:path.join(f.directory,'missing.py'),resolveMedia:async()=>{calls++;throw Error('unexpected')}});assert.equal(jobs.status('42',true,240).state,'unavailable');assert.equal(calls,0);}finally{fs.rmSync(f.directory,{recursive:true,force:true})}
});
test('a failed worker resolution does not redownload on every UI poll',async()=>{
 const f=fixture();let calls=0;try{const jobs=create({...f,resolveMedia:async()=>{calls++;throw Error('offline')}});jobs.status('42',true,240);await new Promise(r=>setImmediate(r));for(let n=0;n<20;n++)assert.equal(jobs.status('42',true,240).state,'failed');assert.equal(calls,1);}finally{fs.rmSync(f.directory,{recursive:true,force:true})}
});
test('existing analysis remains available even if optional components are absent',()=>{
 const f=fixture();try{const jobs=create({...f,script:'missing',resolveMedia:async()=>{throw Error('unexpected')}});fs.writeFileSync(path.join(f.directory,'analysis-cache','42-v4.json'),JSON.stringify({version:4,duration:240,pitch:[440],beats:[1]}));assert.equal(jobs.status('42',true,240).state,'ready');}finally{fs.rmSync(f.directory,{recursive:true,force:true})}
});
