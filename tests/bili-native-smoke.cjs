const fs=require('fs'),crypto=require('crypto'),assert=require('assert/strict');
const d=process.argv[2],sleep=t=>new Promise(r=>setTimeout(r,t)),read=()=>JSON.parse(fs.readFileSync(d+'/diagnostics.json'));
async function wait(fn,n=90){for(let i=0;i<n*10;i++){try{let x=read();if(fn(x))return x}catch{}await sleep(100)}throw Error(JSON.stringify(read()))}
async function cmd(action,extra={}){const id=crypto.randomUUID();fs.writeFileSync(d+'/control.json',JSON.stringify({id,action,...extra}));return wait(x=>x.testCommandId===id)}
(async()=>{
 await cmd('expand-player');await cmd('load-index',{index:3});await cmd('play');await wait(x=>x.videoFrames>20&&x.duration>9000);
 const seeks=[];
 for(const seconds of [300,3600,60]){
  const before=read().videoFrames;await cmd('seek',{seconds});const samples=[];
  for(let i=0;i<12;i++){await sleep(1000);const x=read();assert(!x.videoFailed,'decoder must remain healthy');samples.push({at:i+1,drift:x.presentationPosition-x.videoPosition,frames:x.videoFrames});}
  console.log({seconds,samples});
  assert(samples[3].frames-before>10,'indexed seek must resume actual decoded frames within four seconds');
  assert(samples.slice(-4).every(x=>Math.abs(x.drift)<.45),'video must converge and remain synchronized');
  assert(samples.at(-1).frames-before>100,'video must continuously decode after seeking');seeks.push({seconds,samples});
 }
 await cmd('video-snapshot');await cmd('layout-snapshot');fs.copyFileSync(d+'/layout.png',d+'/bili-layout.png');await cmd('pause');
 assert.equal(read().totalSeconds,0);assert.equal(read().syncSeconds,0);
 fs.writeFileSync(d+'/bili-native-result.json',JSON.stringify({passed:true,seeks},null,2));
 console.log('PASS real Bili native DASH streaming, 3 indexed seeks, decoded frames and sustained sync');
})().catch(e=>{console.error(e);process.exitCode=1});
