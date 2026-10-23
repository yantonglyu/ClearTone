// Opt-in native integration check. Run with a dedicated data folder, never real data.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const [mode,bundleArg,dataArg]=process.argv.slice(2);
if(!bundleArg||!dataArg)throw Error('Usage: node tests/installed-smoke.cjs prepare|check <installed-root> <new-test-data>');
const bundle=path.resolve(bundleArg),dir=path.resolve(dataArg);
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
if(mode==='prepare'){
 if(fs.existsSync(dir))throw Error('Refusing to overwrite an existing data folder');
 fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,'.cleartone-test'),'synthetic-only');
 const frames=44100*12,buf=Buffer.alloc(44+frames*2);buf.write('RIFF');buf.writeUInt32LE(buf.length-8,4);buf.write('WAVEfmt ',8);buf.writeUInt32LE(16,16);buf.writeUInt16LE(1,20);buf.writeUInt16LE(1,22);buf.writeUInt32LE(44100,24);buf.writeUInt32LE(88200,28);buf.writeUInt16LE(2,32);buf.writeUInt16LE(16,34);buf.write('data',36);buf.writeUInt32LE(frames*2,40);for(let i=0;i<frames;i++)buf.writeInt16LE(Math.round(3000*Math.sin(2*Math.PI*440*i/44100)),44+i*2);fs.writeFileSync(path.join(dir,'tone.wav'),buf);
 fs.writeFileSync(path.join(dir,'state.json'),JSON.stringify({TotalSeconds:3600,Volume:0,Motion:true,Queue:[{Name:'Local playback check',Artist:'Synthetic audio',Url:path.join(dir,'tone.wav'),Cover:path.resolve(__dirname,'../src/ClearTone/Assets/ClearTone-logo-master.png'),Lrc:'[00:00]Local playback\n[00:02]Lyrics follow\n[00:04]Focus continues\n[00:06]ClearTone',NetEaseId:''}]}));process.exit();
}
if(mode!=='check'||!fs.existsSync(path.join(dir,'.cleartone-test')))throw Error('Only a marked synthetic test directory is allowed');
const read=()=>JSON.parse(fs.readFileSync(path.join(dir,'diagnostics.json')));
async function wait(fn){for(let i=0;i<180;i++){try{const d=read();if(fn(d))return d;}catch{}await sleep(200);}throw Error('Timed out waiting for native diagnostics');}
async function cmd(action,extra={}){const id=Date.now().toString();fs.writeFileSync(path.join(dir,'control.json'),JSON.stringify({action,id,...extra}));return wait(d=>d.testCommandId===id);}
(async()=>{
 await wait(d=>d.ready&&d.lyrics===4&&d.rate===44100);assert(!read().accountLogged);await cmd('study-finish');await cmd('load-first');await cmd('navigate',{page:'now'});
 await cmd('play');await wait(d=>d.playing&&d.position>1);
 await cmd('lyric-drag');assert(read().lyricManual);await cmd('lyric-follow');assert(!read().lyricManual);
 await cmd('pause');const position=read().position;await sleep(1500);assert(Math.abs(read().position-position)<.2);
 const heard=read().heardSeconds;assert(heard>1&&heard<8);await sleep(1500);assert(Math.abs(read().heardSeconds-heard)<.05);
 await cmd('seek',{seconds:8});assert(Math.abs(read().heardSeconds-heard)<.05);
 await cmd('play');await sleep(1200);await cmd('pause');assert(read().heardSeconds>heard+.5&&read().heardSeconds<heard+3);
 const before=read().totalSeconds;await cmd('study');await sleep(2500);await cmd('study');assert(read().totalSeconds>before+1);
 const rested=read();assert(rested.studyResting&&!rested.studyRunning);assert.equal(rested.studyDisplay,'00:00:00');
 const phases=new Set();for(let i=0;i<12;i++){await sleep(250);phases.add(read().studyOpacity);}
 if(read().visible)assert(phases.has(.5)&&phases.has(1),'Rest clock blinks without disappearing');
 assert(read().restSeconds>2);if(read().visible)assert(read().studyDisplay!=='00:00:00');
 assert.equal(read().totalSeconds,rested.totalSeconds);assert.equal(read().studySeconds,rested.studySeconds);
 await cmd('study');assert(read().studyRunning&&!read().studyResting);assert.equal(read().studyOpacity,1);assert.equal(read().restSeconds,0);assert(read().studySeconds>=rested.studySeconds);
 await cmd('seek',{seconds:0});await cmd('play');await wait(d=>d.playing);await cmd('study');const restDuringPlayback=read();await sleep(1200);assert(read().position>restDuringPlayback.position+.5);assert.equal(read().totalSeconds,restDuringPlayback.totalSeconds);
 await cmd('study-finish');assert.equal(read().studyDisplay,'00:00:00');assert.equal(read().studyOpacity,1);assert.equal(read().restSeconds,0);assert(!read().studyResting&&!read().studyRunning);
 await cmd('pause');assert.equal(JSON.parse(fs.readFileSync(path.join(dir,'state.json'))).TotalSeconds,read().totalSeconds);
 assert(!fs.existsSync(path.join(dir,'account.dpapi')));
 console.log(JSON.stringify({passed:true,checks:['signed out','synthetic playback','44100 Hz decode','lyric browsing/follow','pause','study checkpoint','rest clock and blink','rest excluded from saved total','resume preserves study session','playback continues during rest','finish clears clocks','rendered listening time excludes pause and seek'],final:read()}));
})().catch(e=>{console.error(e.message);process.exitCode=1});
