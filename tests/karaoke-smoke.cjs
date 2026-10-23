// Native-only acceptance test. Refuses ordinary data directories and never enables an account.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const directory=path.resolve(process.argv[2]||'');
if(!fs.existsSync(path.join(directory,'.cleartone-test')))throw Error('Synthetic test marker required');
if(fs.existsSync(path.join(directory,'account.dpapi')))throw Error('Karaoke smoke must not use an account');
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms)),read=()=>JSON.parse(fs.readFileSync(path.join(directory,'diagnostics.json')));
async function wait(predicate,label='diagnostics'){for(let i=0;i<200;i++){try{const value=read();if(predicate(value))return value;}catch{}await sleep(150);}throw Error('Karaoke test timed out: '+label);}
async function command(action,extra={}){const id=crypto.randomUUID();fs.writeFileSync(path.join(directory,'control.json'),JSON.stringify({id,action,...extra}));return wait(value=>value.testCommandId===id,action);}
const elements=value=>[...new Intl.Segmenter('ja',{granularity:'grapheme'}).segment(value)].map(s=>s.segment);
(async()=>{
 await wait(value=>value.ready&&value.lyrics>=2);await command('study-finish');await command('pause');const total=read().totalSeconds;
 await command('load-first');await command('pause');await command('navigate',{page:'practice'});await wait(value=>value.practiceReady>=2);
 const state=JSON.parse(fs.readFileSync(path.join(directory,'state.json'))),track=state.Queue[0];
 const lines=track.Lrc.split('\n').flatMap(line=>{const match=line.match(/^\[(\d+):(\d+(?:\.\d+)?)\](.+)$/);return match?[{start:Number(match[1])*60+Number(match[2]),text:match[3]}]:[];});
 assert(lines.length>=2,'Fixture needs at least two LRC lines');
 const timeline={source:'native-test-yrc',lines:lines.map((line,index)=>{const duration=Math.max(.5,(lines[index+1]?.start??line.start+2)-line.start),chars=elements(line.text);return {...line,duration,words:chars.map((text,at)=>({text,start:line.start+duration*at/chars.length,duration:duration/chars.length}))};})};
 const first=timeline.lines[0],count=elements(first.text).length;
 await command('practice-word-timeline',{timeline});await wait(value=>value.practiceKaraokeExact,'exact YRC accepted');
 await command('seek',{seconds:first.start+first.duration*.25});await wait(value=>value.practiceSelected===0&&Math.abs(value.practiceKaraokePosition-count*.25)<.08,'first quarter progress');
 const paused=read().practiceKaraokePosition;await sleep(1300);assert(Math.abs(read().practiceKaraokePosition-paused)<.02,'Paused audio freezes karaoke');
 await command('seek',{seconds:first.start+first.duration*.8});await wait(value=>Math.abs(value.practiceKaraokePosition-count*.8)<.08,'forward seek');
 await command('seek',{seconds:first.start+first.duration*.1});await wait(value=>Math.abs(value.practiceKaraokePosition-count*.1)<.08,'backward seek');
 const transitions=read().practiceTransitions,animated=read().shellMotionEnabled;
 await command('seek',{seconds:timeline.lines[1].start+.1});await wait(value=>value.practiceSelected===1&&!value.practiceTransitionRunning,'next sentence settled');
 if(animated)assert(read().practiceTransitions>transitions,'Next sentence has one native transition');
 await command('practice-select',{index:0});assert(read().practiceLoop);await command('practice-toggle-loop');assert(!read().practiceLoop&&read().practiceFollow&&read().playing);
 await wait(value=>value.practiceSelected>=1&&value.playing,'continuous following after loop');
 await command('navigate',{page:'library'});const hidden=read().practiceKaraokePosition;await sleep(1300);assert.equal(read().practiceKaraokePosition,hidden,'Offscreen karaoke does not repaint');assert(!read().practiceTransitionRunning,'Offscreen transition is stopped');
 await command('pause');await command('navigate',{page:'practice'});await command('practice-word-timeline',{timeline:{source:'',lines:[]}});
 await command('seek',{seconds:first.start+first.duration*.5});await wait(value=>value.practiceSelected===0&&!value.practiceKaraokeExact&&value.practiceKaraokePosition>0,'LRC estimated fallback');
 assert.equal(read().totalSeconds,total,'Karaoke does not alter study history');assert(!read().studyRunning);assert(!fs.existsSync(path.join(directory,'account.dpapi')));
 const result={passed:true,checks:['source word timing','paused progress stable','forward/backward seeks','native line transition','loop resumes following','hidden page has no karaoke updates','estimated LRC fallback','study total preserved'],final:read()};
 fs.writeFileSync(path.join(directory,'karaoke-test-result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
})().catch(error=>{console.error(error);process.exitCode=1;});
