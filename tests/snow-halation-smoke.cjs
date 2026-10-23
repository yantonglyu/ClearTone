const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const dir=path.resolve(process.argv[2]||'');if(!fs.existsSync(path.join(dir,'.cleartone-test')))throw Error('Isolated profile required');
const sleep=ms=>new Promise(r=>setTimeout(r,ms)),read=()=>JSON.parse(fs.readFileSync(path.join(dir,'diagnostics.json')));
async function wait(fn){for(let i=0;i<200;i++){try{const d=read();if(fn(d))return d;}catch{}await sleep(100);}throw Error('Easter egg native test timeout');}
async function cmd(action,extra={}){const id=crypto.randomUUID();fs.writeFileSync(path.join(dir,'control.json'),JSON.stringify({id,action,...extra}));return wait(d=>d.testCommandId===id);}
(async()=>{
 await wait(d=>d.ready&&d.duration>250);await cmd('expand-player');await cmd('seek',{seconds:189});await cmd('play');
 await wait(d=>d.eggBursts===1&&d.eggOpen);await sleep(2500);await cmd('pause');await sleep(2000);
 const frozen=read().eggMotion;await sleep(1500);assert.equal(read().eggMotion,frozen);assert.equal(read().eggBurst,0);assert.equal(read().eggTimerRunning,false);
 await cmd('navigate',{page:'practice'});assert(read().eggOpen);await cmd('navigate',{page:'library'});assert(read().eggOpen);
 await cmd('settings');await sleep(1000);assert(read().eggOpen);await cmd('close-dialog');
 await cmd('seek',{seconds:100});await wait(d=>!d.eggOpen&&!d.eggTimerRunning);
 await cmd('seek',{seconds:210});await wait(d=>d.eggOpen);assert.equal(read().eggBursts,1);
 await cmd('seek',{seconds:185});await cmd('play');await wait(d=>d.eggBursts===2);
 await cmd('seek',{seconds:253.9});await sleep(2000);await wait(d=>!d.eggOpen);
 await cmd('seek',{seconds:210});await wait(d=>d.eggOpen);await cmd('load-index',{index:1});await wait(d=>!d.eggOpen&&!d.eggTimerRunning);
 assert.equal(read().totalSeconds,0);assert.equal(read().accountLogged,false);assert.equal(read().syncSeconds,0);
 await cmd('load-index',{index:0});await cmd('seek',{seconds:208});await cmd('play');await cmd('expand-player');await wait(d=>d.eggOpen);
 fs.writeFileSync(path.join(dir,'snow-result.json'),JSON.stringify({passed:true,checks:['trigger','pause','all pages','dialog','seek in/out','rewind','fade out','switch','study/account isolation'],final:read()},null,2));console.log('Snow Halation native lifecycle checks passed');
})().catch(e=>{console.error(e);process.exitCode=1});
