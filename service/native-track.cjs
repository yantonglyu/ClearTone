'use strict';

// A queue/search result already has display metadata. Refresh only its service-local
// media URLs: song_detail can return 404 even when this account has a playable URL.
module.exports=async function nativeTrack(id,metadata,{call,song}){
 if(typeof metadata?.name==='string'&&metadata.name.trim()&&metadata.name!=='暂不可用的歌曲'){
  return song({id,name:metadata.name,ar:[{name:typeof metadata.artist==='string'?metadata.artist:''}],
   al:{picUrl:typeof metadata.cover==='string'?metadata.cover:''},
   dt:Number.isFinite(metadata.fullDuration)&&metadata.fullDuration>0?metadata.fullDuration*1000:0});
 }
 const result=await call('song_detail',{ids:id});
 const track=result.songs?.find(t=>String(t.id)===id);
 if(!track){const e=Error('歌曲详情暂不可用，请从搜索或歌单重新添加。');e.publicMessage=e.message;e.status=404;throw e;}
 return song(track);
};
