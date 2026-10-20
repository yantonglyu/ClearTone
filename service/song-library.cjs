'use strict';
const routes=new Set(['/api/liked','/api/like','/api/collect']);
function fail(message,status=502){const e=Error(message);e.status=status;e.publicMessage=message;throw e;}
function id(value){if(!/^\d{1,20}$/.test(String(value)))fail('歌曲或歌单编号无效。',400);return String(value);}
function success(body){const result=body?.body||body;if(Number(result?.code)!==200)fail('网易云未确认保存成功，请稍后重试。');return result;}
module.exports={routes,async handle(route,input,{call,requireLogin,getProfile}){
 await requireLogin();const song=id(input.id);
 if(route==='/api/liked'){const body=success(await call('likelist',{uid:getProfile().id}));if(!Array.isArray(body.ids))fail('暂时无法读取喜欢状态。');return {id:song,liked:body.ids.some(x=>String(x)===song)};}
 if(route==='/api/like'){
  if(typeof input.liked!=='boolean')fail('喜欢状态无效。',400);
  success(await call('like',{id:song,like:input.liked?'true':'false'}));return {id:song,liked:input.liked};
 }
 const playlist=id(input.playlistId),detail=success(await call('playlist_detail',{id:playlist})),pl=detail.playlist;
 if(!pl||String(pl.creator?.userId)!==String(getProfile().id))fail('只能添加到你自己创建的歌单。',403);
 if(pl.trackIds?.some(t=>String(t.id)===song))return {id:song,playlistId:playlist,already:true};
 success(await call('playlist_tracks',{op:'add',pid:playlist,tracks:song}));
 return {id:song,playlistId:playlist,already:false};
}};
