'use strict';

// Search results carry source identifiers only. Playback still resolves through the
// existing account-aware NetEase service or the Bilibili audio loader.
const PAGE_SIZE = { netease: 40, bilibili: 20 };
const AGENT = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36';
function failure(message, status = 502) { const e = Error(message); e.status = status; e.publicMessage = message; return e; }
function text(value) {
 return String(value ?? '').replace(/<[^>]*>/g, '').replace(/&(?:amp|lt|gt|quot|apos|#39|#\d+|#x[0-9a-f]+);/gi, entity => {
  const named = { '&amp;': '&', '&lt;': '<', '&gt;': '>', '&quot;': '"', '&apos;': "'", '&#39;': "'" };
  if (named[entity.toLowerCase()]) return named[entity.toLowerCase()];
  const value = entity[2].toLowerCase() === 'x' ? parseInt(entity.slice(3, -1), 16) : Number(entity.slice(2, -1));
  return value > 0 && value <= 0x10ffff ? String.fromCodePoint(value) : '';
 });
}
function cover(value) { try { const u = new URL(String(value || '').replace(/^\/\//, 'https://')); return ['http:', 'https:'].includes(u.protocol) && !u.username && !u.password ? u.href.replace(/^http:/, 'https:') : ''; } catch { return ''; } }
function duration(value) {
 if (typeof value === 'number') return Math.max(0, value);
 const parts = String(value || '').split(':');
 return parts.length <= 3 && parts.every(p => /^\d+$/.test(p)) ? parts.reduce((n, p) => n * 60 + Number(p), 0) : 0;
}
function offset(value) { return Math.max(0, Math.min(10000, Math.floor(Number(value) || 0))); }
function source(value) { const s = value || 'netease'; if (!Object.hasOwn(PAGE_SIZE, s)) throw failure('请选择网易云或 B 站搜索。', 400); return s; }
function identifier(value) { if (!/^\d{1,20}$/.test(String(value || ''))) throw failure('作者信息不完整，请重新搜索。', 400); return String(value); }
function video(item) {
 if (!/^BV[0-9A-Za-z]{10}$/.test(item?.bvid || '')) return null;
 return { name: text(item.title), artist: text(item.author), cover: cover(item.pic), url: '', lrc: '', type: 'bilibili', netEaseId: '', bvid: item.bvid, biliPage: 1, biliCid: 0, fullDuration: duration(item.duration || item.length) };
}
function result(s, kind, start, limit, tracks = [], artists = [], total = 0, more = false) {
 return { source: s, kind, tracks, artists, total, offset: start, nextOffset: start + limit, more };
}
module.exports = function searchCatalog({ call, song, fetch: request = globalThis.fetch }) {
 async function bili(path, params) {
  const url = new URL('https://api.bilibili.com/' + path);
  for (const [key, value] of Object.entries(params)) url.searchParams.set(key, String(value));
  let response;
  try { response = await request(url, { headers: { 'User-Agent': AGENT, Referer: 'https://search.bilibili.com/' }, signal: AbortSignal.timeout(12000) }); }
  catch { throw failure('B 站搜索暂时无法连接，请稍后重试。'); }
  if ([403, 412, 429].includes(response.status)) throw failure('B 站暂时限制了这次搜索，请稍后重试，也可以直接粘贴视频链接。');
  if (!response.ok) throw failure('B 站搜索暂不可用，请稍后重试。');
  let body;
  try { body = await response.json(); } catch { throw failure('B 站没有返回可读取的搜索结果，请稍后重试。'); }
  if (body.code !== 0 || !body.data) throw failure('B 站暂时未提供这组搜索结果，请稍后重试。');
  return body.data;
 }
 async function search(input = {}) {
  const s = source(input.source), kind = input.kind === 'artists' ? 'artists' : 'songs';
  const query = String(input.query || '').trim().slice(0, 100), start = offset(input.offset), limit = PAGE_SIZE[s];
  if (!query) return result(s, kind, start, limit);
  if (s === 'netease') {
   const body = await call('cloudsearch', { keywords: query, type: kind === 'artists' ? 100 : 1, limit, offset: start });
   const data = body.result || {}, items = Array.isArray(data[kind === 'artists' ? 'artists' : 'songs']) ? data[kind === 'artists' ? 'artists' : 'songs'] : [];
   const total = Math.max(0, Number(data[kind === 'artists' ? 'artistCount' : 'songCount']) || 0);
   const artists = kind === 'artists' ? items.map(a => ({ id: String(a.id), name: text(a.name), cover: cover(a.picUrl || a.img1v1Url), source: s, description: (a.alias || []).map(text).join(' / ') })) : [];
   return result(s, kind, start, limit, kind === 'songs' ? items.map(song) : [], artists, total, start + items.length < total);
  }
  const page = Math.floor(start / limit) + 1;
  const data = await bili('x/web-interface/search/type', { search_type: kind === 'artists' ? 'bili_user' : 'video', keyword: query, page, page_size: limit });
  const items = Array.isArray(data.result) ? data.result : [], total = Math.max(0, Number(data.numResults) || 0);
  const artists = kind === 'artists' ? items.filter(a => /^\d+$/.test(String(a.mid))).map(a => ({ id: String(a.mid), name: text(a.uname), cover: cover(a.upic), source: s, description: text(a.usign) })) : [];
  return result(s, kind, (page - 1) * limit, limit, kind === 'songs' ? items.map(video).filter(Boolean) : [], artists, total, page < Number(data.numPages || 0));
 }
 async function artistTracks(input = {}) {
  const s = source(input.source), id = identifier(input.id), start = offset(input.offset), limit = PAGE_SIZE[s];
  if (s === 'netease') {
   const body = await call('artist_songs', { id, limit, offset: start, order: 'hot' });
   const items = Array.isArray(body.songs) ? body.songs : [];
   const total = Math.max(0, Number(body.total) || 0);
   return result(s, 'songs', start, limit, items.map(song), [], total, typeof body.more === 'boolean' ? body.more : start + items.length < total);
  }
  const page = Math.floor(start / limit) + 1;
  const data = await bili('x/space/arc/search', { mid: id, pn: page, ps: limit, order: 'pubdate' });
  const items = Array.isArray(data.list?.vlist) ? data.list.vlist : [], total = Math.max(0, Number(data.page?.count) || 0);
  return result(s, 'songs', (page - 1) * limit, limit, items.map(video).filter(Boolean), [], total, page * limit < total);
 }
 return { search, artistTracks };
};
