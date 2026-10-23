const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const directory=path.resolve(process.argv[2]||'');
if(!fs.existsSync(path.join(directory,'.cleartone-test'))||fs.existsSync(path.join(directory,'account.dpapi')))throw Error('Account-free synthetic profile required');
const sleep=ms=>new Promise(r=>setTimeout(r,ms)),read=()=>JSON.parse(fs.readFileSync(path.join(directory,'diagnostics.json')));
async function wait(test){for(let n=0;n<400;n++){try{const d=read();if(test(d))return d;}catch{}await sleep(150);}throw Error('Native audio test timed out');}
async function send(action,extra={}){const id=crypto.randomUUID();fs.writeFileSync(path.join(directory,'control.json'),JSON.stringify({id,action,...extra}));return wait(d=>d.testCommandId===id);}
(async()=>{
 await wait(d=>d.ready);await send('study-finish');await send('pause');const total=read().totalSeconds;
 const formats=[];
 for(const index of [1,2]){const d=await send('load-index',{index});assert.equal(d.rate,176400);assert(Math.abs(d.duration-2)<.01);assert(!d.playing);assert.equal(d.notice,'');formats.push({index,rate:d.rate,duration:d.duration,format:d.audioFormat});}
 const cache=path.join(directory,'decoded-audio');const files=fs.readdirSync(cache).filter(x=>x.endsWith('.wav'));assert.equal(files.length,2);
 await send('load-index',{index:1});assert.equal(fs.readdirSync(cache).filter(x=>x.endsWith('.wav')).length,2);
 await send('load-first');const media=await send('system-media-test');assert.equal(media.systemMediaTestResult,'passed');assert(!media.playing);assert(media.heardSeconds<1.5,'SMTC pause does not over-report listening');assert.equal(media.totalSeconds,total);
 const result={passed:true,formats,dsdCacheReused:true,systemMediaControl:media.systemMediaTestResult,studyUnchanged:true};fs.writeFileSync(path.join(directory,'hifi-native-result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
})().catch(e=>{console.error(e);process.exitCode=1;});
