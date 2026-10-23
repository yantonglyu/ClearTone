const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const root=path.resolve(process.argv[2]||'artifacts/queue-live-check'), sleep=ms=>new Promise(r=>setTimeout(r,ms));
if(!fs.existsSync(path.join(root,'.cleartone-test')))throw Error('Isolated test profile required');
const read=()=>JSON.parse(fs.readFileSync(path.join(root,'diagnostics.json'),'utf8'));
async function send(action,extra={}){const id=crypto.randomUUID();fs.writeFileSync(path.join(root,'control.json'),JSON.stringify({id,action,...extra}));for(let n=0;n<600;n++){await sleep(100);let d=read();if(d.testCommandId===id)return d;}throw Error('timeout '+action);}
async function waitLyrics(){for(let n=0;n<300;n++){let d=read();if(d.lyrics>0&&!d.notice)return;await sleep(100);}throw Error('Lyrics not ready');}
(async()=>{for(let cycle=0;cycle<3;cycle++){
 await send('load-index',{index:2});await waitLyrics();await send('navigate',{page:'practice'});await sleep(1000);
 await send('expand-player');await send('load-index',{index:1});await waitLyrics();await send('seek',{seconds:300});await sleep(600);
 await send('load-index',{index:3});await waitLyrics();await send('navigate',{page:'practice'});await sleep(1200);
 await send('expand-player');await send('cycle-reading');await sleep(1000);await send('lyric-drag');await send('lyric-follow');
 console.log(JSON.stringify({cycle,alive:true,lyrics:read().lyrics,notice:read().notice}));
 }
 await send('load-index',{index:1});await sleep(2000);await send('seek',{seconds:60});await send('expand-player');
 const d=read();if(d.playing||d.totalSeconds!==0)throw Error('unexpected playback/study');fs.writeFileSync(path.join(root,'crash-regression-result.json'),JSON.stringify({passed:true,cycles:3,final:d},null,2));
})().catch(e=>{console.error(e);process.exitCode=1});


