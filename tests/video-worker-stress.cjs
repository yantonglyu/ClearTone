const fs=require('fs'),crypto=require('crypto'),assert=require('assert/strict');
const dir=process.argv[2],sleep=ms=>new Promise(r=>setTimeout(r,ms));
const read=()=>JSON.parse(fs.readFileSync(dir+'/diagnostics.json'));
async function wait(test,seconds=35){for(let i=0;i<seconds*10;i++){try{let state=read();if(test(state))return state;}catch{}await sleep(100);}throw Error('Timeout '+JSON.stringify(read()));}
async function cmd(action,extra={}){const id=crypto.randomUUID(),start=Date.now();fs.writeFileSync(dir+'/control.json',JSON.stringify({id,action,...extra}));await wait(x=>x.testCommandId===id);return Date.now()-start;}
(async()=>{
 await cmd('expand-player');await cmd('load-index',{index:3});await cmd('play');await wait(s=>s.videoFrames>20);
 await cmd('pause');await sleep(11500);await cmd('play');await wait(s=>s.videoRendering&&!s.videoFailed&&s.videoFrames>30);
 const rapidAck=await cmd('rapid-seek',{positions:[7200,60,8000,120,3600,300]});
 const navigationAck=await cmd('navigate',{page:'library'});const cancelAck=await cmd('load-index',{index:0});
 await wait(s=>s.duration>200&&s.duration<1000&&!s.videoReady&&!s.videoFailed);
 assert(rapidAck<3000&&navigationAck<3000&&cancelAck<5000,'native operations must not block UI control acknowledgments');
 await cmd('play');await wait(s=>s.playing);await sleep(500);await cmd('pause');
 assert.equal(read().totalSeconds,0);assert.equal(read().syncSeconds,0);
 fs.writeFileSync(dir+'/worker-stress-result.json',JSON.stringify({passed:true,rapidAck,navigationAck,cancelAck,pausedResume:true,cancelToMusic:true},null,2));
 console.log('PASS video worker pause/resume, rapid far seeks, immediate navigation and track cancellation', {rapidAck,navigationAck,cancelAck});
 await cmd('expand-player');await cmd('load-index',{index:3});await cmd('play');await wait(s=>s.videoFrames>5);
 await cmd('rapid-seek',{positions:[8000,10,7000,3500]});
 const id=crypto.randomUUID();fs.writeFileSync(dir+'/control.json',JSON.stringify({id,action:'test-close'}));
 console.log('Close command posted during far-seek workload; verify process exit separately.');
})().catch(e=>{console.error(e);process.exitCode=1});

