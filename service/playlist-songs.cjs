'use strict';
// Playlist metadata can contain songs that song_detail omits. Never discard it.
module.exports=async function playlistSongs(playlist,call){
 const embedded=Array.isArray(playlist.tracks)?playlist.tracks:[];
 const ids=Array.isArray(playlist.trackIds)&&playlist.trackIds.length
  ?playlist.trackIds.map(t=>String(t.id)):embedded.map(t=>String(t.id));
 const songs=new Map(embedded.filter(t=>t&&t.id!=null&&t.name).map(t=>[String(t.id),t]));
 const missing=[...new Set(ids.filter(id=>!songs.has(id)))];
 for(let i=0;i<missing.length;i+=500){
  const result=await call('song_detail',{ids:missing.slice(i,i+500).join(',')});
  for(const song of result.songs||[])if(song&&song.id!=null&&song.name)songs.set(String(song.id),song);
 }
 // Preserve unavailable entries and order rather than silently shortening a playlist.
 return ids.map(id=>songs.get(id)||{id,name:'暂不可用的歌曲',ar:[],al:{},dt:0});
};
