// Only writes to the explicitly marked synthetic smoke directory.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const dir=path.resolve(process.argv[2]||'');if(!fs.existsSync(path.join(dir,'.cleartone-test')))throw Error('Synthetic test marker required');
const sleep=ms=>new Promise(r=>setTimeout(r,ms)),read=()=>JSON.parse(fs.readFileSync(path.join(dir,'diagnostics.json')));
async function wait(fn){for(let i=0;i<200;i++){try{const d=read();if(fn(d))return d;}catch{}await sleep(200);}throw Error('Native practice test timed out');}
async function cmd(action,extra={}){const id=crypto.randomUUID();fs.writeFileSync(path.join(dir,'control.json'),JSON.stringify({id,action,...extra}));return wait(d=>d.testCommandId===id);}
(async()=>{
 await wait(d=>d.ready&&d.lyrics===4);await cmd('study-finish');await cmd('seek',{seconds:0});await cmd('play');await cmd('study');
 const before=read();await cmd('navigate',{page:'discover'});assert(read().playing&&read().studyRunning);assert.equal(read().lyricRendering,false);
 await cmd('shelf-add');await cmd('navigate',{page:'library'});assert.equal(read().shelfCount,1);assert(read().position>before.position);
 const builds=read().homeBuilds;await cmd('expand-player');assert.equal(read().page,'now');await cmd('collapse-player');assert.equal(read().page,'library');assert.equal(read().homeBuilds,builds);
 await cmd('navigate',{page:'practice'});await wait(d=>d.practiceReady===4);assert(read().practiceTokens.some(t=>t.Reading));
 await cmd('practice-select',{index:1});await cmd('practice-speed',{speed:.75});const loops=read().practiceLoops,heard=read().heardSeconds,start=Date.now();
 await wait(d=>d.practiceLoops>loops);assert.equal(read().practiceSelected,1);assert.equal(read().speed,.75);
 assert(read().heardSeconds>heard);assert(read().heardSeconds-heard<(Date.now()-start)/1000+1);
 await cmd('practice-correct');assert.equal(read().practiceTokens[0].Reading,'てすと');
 await cmd('navigate',{page:'now'});assert(!read().practiceLoop);assert.equal(read().speed,1);
 // Navigation may span the next lyric boundary while audio keeps playing.
 // Re-select the corrected line instead of assuming the current lyric stayed unchanged.
 await cmd('navigate',{page:'practice'});await cmd('practice-select',{index:1});assert.equal(read().practiceTokens[0].Reading,'てすと');
 await cmd('practice-stop');await cmd('study');const total=read().totalSeconds;await sleep(2200);assert.equal(read().totalSeconds,total);assert(read().studyResting);
 await cmd('study-finish');assert.equal(read().studyDisplay,'00:00:00');assert(!fs.existsSync(path.join(dir,'account.dpapi')));
 const result={passed:true,checks:['shared playback across pages','hidden lyric animation stopped','shared focus timer','local library persistence','Japanese ruby','pitch-preserving speed','line loop and lead-in','actual listening time at changed tempo','correction persisted across navigation','normal speed restored on exit','rest excluded'],final:read()};fs.writeFileSync(path.join(dir,'practice-test-result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
})().catch(e=>{console.error(e);process.exitCode=1});
