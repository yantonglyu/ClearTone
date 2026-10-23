'use strict';

// NetEase YRC stores absolute millisecond timestamps, including each token's
// timestamp. Keep those boundaries, never distribute a line over its characters.
const MAX_TEXT = 2 * 1024 * 1024;
const MAX_TIME = 24 * 60 * 60 * 1000;
const MAX_LINES = 4000;
const MAX_WORDS = 20000;
function lyricText(body, key) {
 const text = body?.[key]?.lyric;
 return typeof text === 'string' && text.length <= MAX_TEXT ? text : '';
}
function validTime(value) { return Number.isSafeInteger(value) && value >= 0 && value <= MAX_TIME; }
function parseYrc(raw) {
 const lines = [];
 if (typeof raw !== 'string' || raw.length > MAX_TEXT) return { source: 'netease-yrc', lines };
 let totalWords = 0;
 for (const line of raw.split(/\r?\n/).slice(0, MAX_LINES)) {
  // JSON credit/author metadata is not timed singing and is intentionally skipped.
  const header = /^\[(\d+),(\d+)\](.*)$/.exec(line);
  if (!header) continue;
  const start = Number(header[1]), duration = Number(header[2]), end = start + duration;
  if (!validTime(start) || !validTime(duration) || !validTime(end) || duration === 0) continue;
  const content = header[3], markers = [...content.matchAll(/\((\d+),(\d+),(-?\d+)\)/g)];
  if (!markers.length || markers[0].index !== 0 || totalWords + markers.length > MAX_WORDS) continue;
  const words = [];
  let previousStart = start, invalid = false;
  for (let i = 0; i < markers.length; i++) {
   const marker = markers[i], at = Number(marker[1]), length = Number(marker[2]);
   const text = content.slice(marker.index + marker[0].length, i + 1 < markers.length ? markers[i + 1].index : content.length);
   // Do not repair malformed or relative timestamps into apparently precise data.
   if (!validTime(at) || !validTime(length) || at < previousStart || at + length > end) { invalid = true; break; }
   previousStart = at;
   if (text.length) words.push({ start: at / 1000, duration: length / 1000, text });
  }
  if (invalid || !words.length) continue;
  totalWords += words.length;
  lines.push({ start: start / 1000, duration: duration / 1000, text: words.map(word => word.text).join(''), words });
 }
 lines.sort((a, b) => a.start - b.start);
 return { source: 'netease-yrc', lines };
}
function normalize(body) {
 const yrc = lyricText(body, 'yrc');
 return {
  lrc: lyricText(body, 'lrc'),
  translation: lyricText(body, 'tlyric'),
  romanization: lyricText(body, 'romalrc'),
  yrc,
  wordTranslation: lyricText(body, 'ytlrc'),
  wordRomanization: lyricText(body, 'yromalrc'),
  wordTimeline: parseYrc(yrc)
 };
}
async function load(id, call) {
 let body;
 try {
  body = await call('lyric_new', { id });
  if (!body || (body.code != null && Number(body.code) !== 200)) throw Error('Word lyrics unavailable');
 } catch {
  // Older endpoints remain useful when a provider rejects or removes the new one.
  return normalize(await call('lyric', { id }));
 }
 if (typeof body.lrc?.lyric !== 'string') {
  try {
   const legacy = await call('lyric', { id });
   for (const key of ['lrc', 'tlyric', 'romalrc']) if (typeof body[key]?.lyric !== 'string') body[key] = legacy[key];
  } catch { /* Preserve available YRC when the optional legacy lookup fails. */ }
 }
 return normalize(body);
}
module.exports = { load, normalize, parseYrc };
