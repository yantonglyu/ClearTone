// Opt-in native regression test. Refuses real profile directories.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const directory=path.resolve(process.argv[2]||'');
if(!fs.existsSync(path.join(directory,'.cleartone-test')))throw Error('Synthetic test marker required');
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms)),read=()=>JSON.parse(fs.readFileSync(path.join(directory,'diagnostics.json')));
async function wait(predicate){for(let i=0;i<200;i++){try{const value=read();if(predicate(value))return value;}catch{}await sleep(200);}throw Error('Reading/follow regression timed out');}
async function command(action,extra={}){const id=crypto.randomUUID();fs.writeFileSync(path.join(directory,'control.json'),JSON.stringify({id,action,...extra}));return wait(value=>value.testCommandId===id);}
(async()=>{
 await wait(value=>value.ready);await command('study-finish');const total=read().totalSeconds;
 await command('load-first');await command('navigate',{page:'practice'});await wait(value=>value.practiceReady>=2);
 await command('practice-select',{index:0});assert.equal(read().practiceLoop,true);
 const speed=read().speed;await command('practice-toggle-loop');assert.equal(read().practiceLoop,false);assert.equal(read().practiceFollow,true);assert.equal(read().playing,true);assert.equal(read().speed,speed);
 // With looping disabled, normal playback must move to the next sentence on its own.
 await wait(value=>value.practiceSelected>=1&&value.playing);assert.equal(read().practiceFollow,true);
 await command('pause');await command('navigate',{page:'now'});
 const initial=read().playbackReadingMode;let expected=initial;
 for(let i=0;i<3;i++){expected=(expected+1)%3;await command('cycle-reading');assert.equal(read().playbackReadingMode,expected);}
 assert.equal(read().playbackReadingMode,initial);assert.equal(read().totalSeconds,total);
 assert(!fs.existsSync(path.join(directory,'account.dpapi')));
 const result={passed:true,checks:['stop loop resumes continuous following','stop loop preserves playback and speed','automatic next sentence','off/hiragana/romaji cycle','no study mutation'],final:read()};
 fs.writeFileSync(path.join(directory,'reading-follow-result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
})().catch(error=>{console.error(error);process.exitCode=1;});
