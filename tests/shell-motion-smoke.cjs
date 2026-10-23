const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const dir=path.resolve(process.argv[2]||'');
if(!fs.existsSync(path.join(dir,'.cleartone-test')))throw Error('Synthetic marker required');
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const read=()=>JSON.parse(fs.readFileSync(path.join(dir,'diagnostics.json')));
async function wait(fn){for(let n=0;n<200;n++){try{const d=read();if(fn(d))return d;}catch{}await sleep(100);}throw Error('Motion test timeout');}
async function cmd(action,extra={}){const id=crypto.randomUUID();fs.writeFileSync(path.join(dir,'control.json'),JSON.stringify({id,action,...extra}));return wait(d=>d.testCommandId===id&&!d.navigating);}
(async()=>{
 await wait(d=>d.ready);await cmd('study-finish');await cmd('load-first');await cmd('play');await cmd('study');
 await cmd('navigate',{page:'library'});const before=read();
 for(let i=0;i<3;i++){
  const builds=read().homeBuilds;
  assert.equal((await cmd('expand-player')).page,'now');
  assert.equal((await cmd('collapse-player')).page,'library');
  assert.equal(read().homeBuilds,builds);
 }
 await cmd('navigation-burst');assert.equal(read().page,'discover');
 await wait(d=>d.trailStars===0);const after=read();
 assert(after.playing&&after.studyRunning);assert(after.heardSeconds>before.heardSeconds);
 if(before.shellMotionEnabled)assert(after.shellTransitions>=before.shellTransitions+6);
 await cmd('pause');await cmd('study-finish');
 const result={passed:true,animated:before.shellMotionEnabled,transitions:after.shellTransitions-before.shellTransitions,trailStarsAfter:after.trailStars,checks:['expand/collapse','preserved library view','latest rapid navigation wins','playback continues','timer continues','particle cleanup']};
 fs.writeFileSync(path.join(dir,'motion-test-result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
})().catch(e=>{console.error(e);process.exitCode=1});
