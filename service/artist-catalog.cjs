'use strict';
// Artist pages use the complete platform catalog, independent of local favorites.
module.exports = function artistCatalog({call, safeImage}) {
 async function artistMvs(input = {}) {
  const id = String(input.id || '');
  if (!/^\d{1,20}$/.test(id)) throw Object.assign(Error('歌手信息不完整，请重新搜索。'), {status:400});
  const offset = Math.max(0, Math.min(100000, Math.floor(Number(input.offset) || 0))), limit=40;
  const body = await call('artist_mv', {id,offset,limit});
  const items = Array.isArray(body.mvs) ? body.mvs : [];
  return {tracks:items.filter(m=>/^\d+$/.test(String(m.id))).map(m=>({name:String(m.name||'MV'),artist:String(m.artistName||(m.artists||[]).map(a=>a.name).join(' / ')||''),artistId:id,
   cover:safeImage(m.imgurl16v9||m.imgurl||m.cover),netEaseMvId:String(m.id),netEaseId:'',url:'',videoUrl:'',lrc:'',fullDuration:Math.max(0,Number(m.duration)||0)/1000})),
   nextOffset:offset+items.length,more:body.hasMore===true||body.more===true};
 }
 return {artistMvs};
};
