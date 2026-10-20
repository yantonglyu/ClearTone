'use strict';
const http=require('node:http'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {execFileSync}=require('node:child_process');
const {Readable}=require('node:stream');
const {inspect:inspectAudio}=require('./audio-format.cjs');
const songLibrary=require('./song-library.cjs');
const formatCache=new Map();
async function audioInfo(id){
 const media=await resolveMedia(id);if(!media.url)return {sampleRate:null,format:null};
 const cached=formatCache.get(id);if(cached&&cached.url===media.url)return cached.info;
 const response=await fetch(media.url.replace(/^http:/,'https:'),{headers:{Range:'bytes=0-131071'},signal:AbortSignal.timeout(12000)});
 if(!response.ok)throw Error('Audio header unavailable');
 const reader=response.body.getReader(),parts=[];let size=0;
 try{while(size<131072){const {value,done}=await reader.read();if(done)break;const chunk=Buffer.from(value).subarray(0,131072-size);parts.push(chunk);size+=chunk.length;}}finally{await reader.cancel();}
 const info=inspectAudio(Buffer.concat(parts))||{sampleRate:null,format:null};formatCache.set(id,{url:media.url,info});if(formatCache.size>64)formatCache.delete(formatCache.keys().next().value);return info;
}
const port=Number(process.env.CLEARTONE_PORT||18754);
const origin=process.env.CLEARTONE_ORIGIN||'http://127.0.0.1:18754';
const base='http://127.0.0.1:'+port;
const accountDir=process.env.CLEARTONE_DATA||__dirname;
fs.mkdirSync(accountDir,{recursive:true});
const analysis=require('./analysis-jobs.cjs')({directory:accountDir,resolveMedia});
const cookieFile=path.join(accountDir,'account.dpapi');
const token=/^[a-f0-9]{64}$/.test(process.env.CLEARTONE_MEDIA_TOKEN||'')?process.env.CLEARTONE_MEDIA_TOKEN:crypto.randomBytes(32).toString('hex');
const lib=path.dirname(require.resolve('@neteasecloudmusicapienhanced/api/package.json'));
const anonymous=path.join(require('node:os').tmpdir(),'anonymous_token');
if(!fs.existsSync(anonymous))fs.writeFileSync(anonymous,'',{flag:'wx'});
const request=require(path.join(lib,'util/request'));
const modules={};
for(const name of ['login_qr_key','login_qr_create','login_qr_check','login_status','user_playlist','playlist_detail','song_detail','song_url_v1','lyric','recommend_songs','recommend_resource','likelist','like','playlist_tracks','scrobble'])modules[name]=require(path.join(lib,'module',name));
let cookies={},profile=null,qr=null,generation=0;
const media=new Map();
function dpapi(bytes,mode){return Buffer.from(execFileSync('powershell.exe',['-NoProfile','-File',path.join(__dirname,'dpapi.ps1'),'-Mode',mode],{input:Buffer.from(bytes).toString('base64'),encoding:'utf8',windowsHide:true,timeout:10000,stdio:['pipe','pipe','pipe']}).trim(),'base64')}
try{if(fs.existsSync(cookieFile))cookies=JSON.parse(dpapi(fs.readFileSync(cookieFile),'Unprotect').toString())}catch(_){console.warn('Saved account could not be opened; scan again.');}
function save(){const encrypted=dpapi(Buffer.from(JSON.stringify(cookies)),'Protect');fs.writeFileSync(cookieFile+'.tmp',encrypted);fs.renameSync(cookieFile+'.tmp',cookieFile)}
function mergeCookies(list){for(const raw of list||[]){const first=String(raw).split(';')[0],at=first.indexOf('=');if(at>0)cookies[first.slice(0,at)]=first.slice(at+1)}}
async function call(name,params={}){const gen=generation;const result=await modules[name]({...params,cookie:{...cookies},timeout:12000,randomCNIP:false,unblock:'false'},request);if(gen!==generation)throw Error('Account changed');mergeCookies(result.cookie);return result.body}
function identifier(value){if(!/^\d{1,20}$/.test(String(value)))throw Error('Invalid ID');return String(value)}
function safeImage(src){try{const u=new URL(src);return u.protocol==='https:'||u.protocol==='http:'?u.href.replace(/^http:/,'https:'):''}catch(_){return ''}}
async function status(){if(!cookies.MUSIC_U){profile=null;return {loggedIn:false}}const result=await call('login_status');const data=result.data||result;if(!data.profile?.userId){profile=null;cookies={};return {loggedIn:false,expired:true}}profile={id:data.profile.userId,name:data.profile.nickname,avatar:safeImage(data.profile.avatarUrl)};return {loggedIn:true,profile}}
async function requireLogin(){if(!profile){const s=await status();if(!s.loggedIn){const e=Error('请先扫码登录网易云。');e.status=401;throw e}}}
const listeningSync=require('./listening-sync.cjs')({directory:accountDir,call,requireLogin,getProfile:()=>profile});
function song(item){return {name:item.name,artist:(item.ar||item.artists||[]).map(a=>a.name).join(' / '),cover:safeImage((item.al||item.album)?.picUrl),url:base+'/media?id='+item.id+'&token='+token,lrc:base+'/lyrics?id='+item.id+'&token='+token,type:'netease',netEaseId:String(item.id),fullDuration:Number(item.dt||item.duration||0)/1000}}
async function resolveMedia(id){const existing=media.get(id);if(existing&&existing.expires>Date.now())return existing;const body=await call('song_url_v1',{id,level:'standard',crypto:'eapi'});const data=body.data?.[0];if(!data)throw Error('音源暂不可用');const result={url:data.url,trial:!!data.freeTrialInfo,trialSeconds:data.freeTrialInfo?(data.freeTrialInfo.end-data.freeTrialInfo.start):null,available:!!data.url,expires:Date.now()+Math.max(10,Math.min(Number(data.expi)||120,120)-5)*1000};media.set(id,result);return result}
function reply(res,status,body,type='application/json; charset=utf-8'){res.writeHead(status,{'Content-Type':type,'Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(type.startsWith('application/json')?JSON.stringify(body):body)}
async function handle(req,res){
 if(req.headers.host!=='127.0.0.1:'+port)return reply(res,403,{error:'Local host required'});
 const url=new URL(req.url,base);
 if(url.pathname==='/health')return reply(res,200,{service:'cleartone-service-v1'});
 if(req.headers.origin===origin){res.setHeader('Access-Control-Allow-Origin',origin);res.setHeader('Vary','Origin')}
 if(req.method==='OPTIONS'){if(req.headers.origin!==origin)return reply(res,403,{});res.setHeader('Access-Control-Allow-Headers','Content-Type, X-ClearTone-Client, Range');res.setHeader('Access-Control-Allow-Methods','GET, POST, OPTIONS');return reply(res,204,'','text/plain')}
 const asset=['/media','/lyrics'].includes(url.pathname);
 if(asset){if(url.searchParams.get('token')!==token||!['GET','HEAD'].includes(req.method))return reply(res,403,{error:'Invalid media token'});}
 else if(req.method!=='POST'||req.headers.origin!==origin||req.headers['x-cleartone-client']!=='1')return reply(res,403,{error:'App request required'});
 try{
  if(asset){await requireLogin();const id=identifier(url.searchParams.get('id'));if(url.pathname==='/lyrics'){const body=await call('lyric',{id});return reply(res,200,body.lrc?.lyric||'','text/plain; charset=utf-8')}
   const data=await resolveMedia(id);if(!data.url)return reply(res,403,{error:'当前账号无可用音源，请在网易云查看权限。'});
   const target=new URL(data.url);if(!['http:','https:'].includes(target.protocol))throw Error('Invalid media URL');
   const controller=new AbortController();res.once('close',()=>controller.abort());
   const timeout=setTimeout(()=>controller.abort(),15000);let upstream;
   try{upstream=await fetch(target.href.replace(/^http:/,'https:'),{method:req.method,headers:req.headers.range?{Range:req.headers.range}:{},signal:controller.signal});}finally{clearTimeout(timeout);}
   const headers={'Cache-Control':'no-store','X-Content-Type-Options':'nosniff'};
   for(const key of ['content-type','content-length','content-range','accept-ranges'])if(upstream.headers.has(key))headers[key]=upstream.headers.get(key);
   res.writeHead(upstream.status,headers);if(!upstream.body||req.method==='HEAD')return res.end();
   const stream=Readable.fromWeb(upstream.body);stream.on('error',()=>res.destroy());stream.pipe(res);return;
  }
  let text='';for await(const chunk of req){text+=chunk;if(text.length>8192)throw Error('Request too large')}
  const input=text?JSON.parse(text):{};
  if(songLibrary.routes.has(url.pathname))return reply(res,200,await songLibrary.handle(url.pathname,input,{call,requireLogin,getProfile:()=>profile}));
  switch(url.pathname){
   case '/api/status':return reply(res,200,await status());
   case '/api/listening-report':return reply(res,200,await listeningSync.report(input));
   case '/api/qr':{
    const result=await call('login_qr_key');const key=result.data?.unikey||result.unikey;if(!key)throw Error('二维码暂不可用，请稍后重试');
    const image=await call('login_qr_create',{key,qrimg:true});qr={key,expires:Date.now()+180000};return reply(res,200,{image:image.data.qrimg,expires:qr.expires});
   }
   case '/api/check':{
    if(!qr||Date.now()>qr.expires)return reply(res,200,{code:800});const current=qr;const result=await call('login_qr_check',{key:current.key});
    if(qr!==current)return reply(res,200,{code:800});
    if(result.code===803){const s=await status();if(s.loggedIn){save();qr=null;return reply(res,200,{code:803,...s})}}
    return reply(res,200,{code:result.code||800});
   }
   case '/api/logout':generation++;cookies={};profile=null;qr=null;media.clear();if(fs.existsSync(cookieFile))fs.unlinkSync(cookieFile);return reply(res,200,{loggedIn:false});
   case '/api/playlists':{
    await requireLogin();const body=await call('user_playlist',{uid:profile.id,limit:100,offset:Math.max(0,Number(input.offset)||0)});
    return reply(res,200,{items:(body.playlist||[]).map(p=>({id:String(p.id),name:p.name,cover:safeImage(p.coverImgUrl),count:p.trackCount,owned:p.creator?.userId===profile.id})),more:!!body.more});
   }
   case '/api/playlist':{
    await requireLogin();const id=identifier(input.id);const detail=await call('playlist_detail',{id});const pl=detail.playlist;if(!pl)throw Error('歌单暂不可用');
    const ids=(pl.trackIds||[]).map(t=>String(t.id));const songs=[];
    for(let i=0;i<ids.length;i+=500){const data=await call('song_detail',{ids:ids.slice(i,i+500).join(',')});songs.push(...(data.songs||[]))}
    return reply(res,200,{name:pl.name,tracks:songs.map(song)});
   }
   case '/api/daily':{await requireLogin();const body=await call('recommend_songs');const tracks=body.data?.dailySongs||body.recommend;if(!Array.isArray(tracks))throw Error('Daily recommendations unavailable');return reply(res,200,{name:'每日推荐',tracks:tracks.map(song)})}
   case '/api/recommendations':{await requireLogin();const body=await call('recommend_resource');if(!Array.isArray(body.recommend))throw Error('Recommendations unavailable');return reply(res,200,{items:body.recommend.map(p=>({id:String(p.id),name:p.name,cover:safeImage(p.picUrl||p.coverImgUrl),count:p.trackCount,reason:p.copywriter||'为你推荐'})),more:false})}
   case '/api/lyrics':{const body=await call('lyric',{id:identifier(input.id)});return reply(res,200,{lrc:body.lrc?.lyric||'',translation:body.tlyric?.lyric||'',romanization:body.romalrc?.lyric||''})}
   case '/api/native-track':{await requireLogin();const id=identifier(input.id);const result=await call('song_detail',{ids:id});if(!result.songs?.length)throw Error('Song unavailable');return reply(res,200,{track:song(result.songs[0])})}
   case '/api/track':{await requireLogin();const data=await resolveMedia(identifier(input.id));return reply(res,200,{trial:data.trial,trialSeconds:data.trialSeconds,available:data.available})}
   case '/api/analysis':{await requireLogin();return reply(res,200,analysis.status(identifier(input.id),input.queue===true,Number(input.duration)||0))}
   case '/api/audio-info':{await requireLogin();return reply(res,200,await audioInfo(identifier(input.id)))}
   default:return reply(res,404,{error:'Unknown route'});
  }
 }catch(e){return reply(res,e.status||502,{error:e.publicMessage||(e.status===401?e.message:'网易云请求暂未完成，请稍后重试。')})}
}
const server=http.createServer((req,res)=>{handle(req,res).catch(()=>{if(!res.headersSent)reply(res,500,{error:'本地音乐服务暂不可用'});else res.end()})});
server.requestTimeout=30000;server.headersTimeout=10000;
server.listen(port,'127.0.0.1',()=>console.log('ClearTone music service ready at '+base));
