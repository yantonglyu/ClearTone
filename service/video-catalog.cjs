'use strict';
function fail(message){const e=Error(message);e.publicMessage=message;return e;}
module.exports=async function mvTrack(input,{call}){
 const id=String(input.id||'');if(!/^\d{1,20}$/.test(id))throw fail('MV 信息不完整，请重新打开歌手页面。');
 // Known catalogue metadata remains useful when the optional detail API fails.
 const [detail,media]=await Promise.all([call('mv_detail',{mvid:id}).catch(()=>({})),call('mv_url',{id,r:720})]);
 const info=detail.data||input.metadata||{},src=media.data?.url;
 if(!src)throw fail(media.data?.msg||'当前账号暂时无法播放这支 MV。');
 let url;try{url=new URL(src);}catch{throw fail('这支 MV 暂未提供有效的播放地址。');}
 if(!['http:','https:'].includes(url.protocol)||url.username||url.password)throw fail('MV 地址不可用。');
 url.protocol='https:';
 return {track:{netEaseMvId:id,netEaseId:'',name:info.name||'MV',artist:info.artistName||info.artist||(info.artists||[]).map(a=>a.name).join(' / '),cover:String(info.cover||'').replace(/^http:/,'https:'),fullDuration:Number(info.duration||0)/1000||Number(info.fullDuration)||0,url:url.href,videoUrl:url.href,lrc:''}};
};
