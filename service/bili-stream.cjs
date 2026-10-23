'use strict';
const crypto=require('node:crypto');
const {Readable}=require('node:stream');
const AGENT='Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36';
function allowed(value){
 const u=new URL(value);if(u.protocol==='http:')u.protocol='https:';
 const host=u.hostname.toLowerCase();
 if(u.protocol!=='https:'||u.port&&u.port!=='443'||u.username||u.password||!(['.bilivideo.com','.bilivideo.cn','.bilivideo.net'].some(d=>host.endsWith(d))||/^upos-[a-z0-9-]+\.akamaized\.net$/.test(host)))throw Error('Unsupported Bilibili media address');
 return u.href;
}
module.exports=function({base,token,fetch:request=globalThis.fetch}){
 const streams=new Map();
 const put=entry=>{const id=crypto.randomBytes(18).toString('hex');streams.set(id,entry);while(streams.size>64)streams.delete(streams.keys().next().value);return base+'/bili-media?id='+id+'&token='+token;};
 function register(input){
  if(!/^https:\/\/www\.bilibili\.com\/video\/BV[0-9a-zA-Z]{10}\/$/.test(input.referer||''))throw Error('Invalid video reference');
  const add=(value,alternatives)=>{const url=allowed(value),urls=[url];for(const candidate of Array.isArray(alternatives)?alternatives.slice(0,8):[])try{const safe=allowed(candidate);if(!urls.includes(safe))urls.push(safe);}catch{}return put({url,urls,referer:input.referer});};
  const audioUrl=add(input.audioUrl,input.audioUrls);let videoUrl=input.videoUrl?add(input.videoUrl,input.videoUrls):'',videoAdaptive=false;
  // DASH files need their sidx range. A bare fragmented MP4 makes Media Foundation
  // scan each moof on distant seeks, causing dozens of CDN requests and stalls.
  const v=input.video;
  if(videoUrl&&v&&Number.isFinite(v.duration)&&v.duration>0&&v.duration<604800&&/^[a-zA-Z0-9.]+$/.test(v.codecs||'')&&/^\d+-\d+$/.test(v.initialization||'')&&/^\d+-\d+$/.test(v.indexRange||'')&&['bandwidth','width','height'].every(k=>Number.isSafeInteger(v[k])&&v[k]>0)){
   const xmlUrl=videoUrl.replaceAll('&','&amp;');
   const manifest=`<?xml version="1.0" encoding="utf-8"?><MPD xmlns="urn:mpeg:dash:schema:mpd:2011" profiles="urn:mpeg:dash:profile:isoff-on-demand:2011" type="static" minBufferTime="PT2S" mediaPresentationDuration="PT${v.duration}S"><Period duration="PT${v.duration}S"><AdaptationSet mimeType="video/mp4" contentType="video" segmentAlignment="true"><Representation id="video" codecs="${v.codecs}" bandwidth="${v.bandwidth}" width="${v.width}" height="${v.height}"><BaseURL>${xmlUrl}</BaseURL><SegmentBase indexRange="${v.indexRange}"><Initialization range="${v.initialization}"/></SegmentBase></Representation></AdaptationSet></Period></MPD>`;
   videoUrl=put({manifest});videoAdaptive=true;
  }
  return {audioUrl,videoUrl,videoAdaptive};
 }
 async function serve(req,res,id){
  const entry=streams.get(id);if(!entry){res.writeHead(404);return res.end();}
  if(entry.manifest){res.writeHead(200,{'Content-Type':'application/dash+xml','Content-Length':Buffer.byteLength(entry.manifest),'Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});return res.end(req.method==='HEAD'?undefined:entry.manifest);}
  const controller=new AbortController();res.once('close',()=>controller.abort());
  const timeout=setTimeout(()=>controller.abort(),20000);let upstream;
  try{
   const attempts=[entry.url,entry.url,...entry.urls.filter(u=>u!==entry.url).slice(0,2)];
   for(let attempt=0;attempt<attempts.length;attempt++){
    try{
     let url=attempts[attempt];
     for(let redirects=0;redirects<4;redirects++){
      upstream=await request(url,{method:req.method,headers:{'User-Agent':AGENT,Referer:entry.referer,...(req.headers.range?{Range:req.headers.range}:{})},signal:controller.signal,redirect:'manual'});
      if(![301,302,303,307,308].includes(upstream.status))break;
      const location=upstream.headers.get('location');await upstream.body?.cancel();url=allowed(new URL(location,url).href);
     }
     if([403,404,410,429,500,502,503,504].includes(upstream.status)&&attempt+1<attempts.length){await upstream.body?.cancel();continue;}
     if(upstream.ok)entry.url=attempts[attempt];
     break;
    }catch(error){if(controller.signal.aborted||attempt+1===attempts.length)throw error;}
   }
  }catch(error){if(process.env.CLEARTONE_STREAM_TRACE==='1')console.log('bili stream error',req.method,req.headers.range,error.name,error.message);throw error;}finally{clearTimeout(timeout);}
  if(process.env.CLEARTONE_STREAM_TRACE==='1')console.log('bili stream',req.method,req.headers.range,upstream.status,upstream.headers.get('content-range'));
  const headers={'Cache-Control':'no-store','X-Content-Type-Options':'nosniff'};
  for(const name of ['content-type','content-length','content-range','accept-ranges'])if(upstream.headers.has(name))headers[name]=upstream.headers.get(name);
  res.writeHead(upstream.status,headers);if(!upstream.body||req.method==='HEAD')return res.end();
  const stream=Readable.fromWeb(upstream.body);stream.on('error',()=>res.destroy());stream.pipe(res);
 }
 return {register,serve};
};
module.exports.allowed=allowed;
