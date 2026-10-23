'use strict';
// Synthetic files only. No downloads, account reads, audio-device output or cleanup.
// Usage: node tests/dsd-fixture.cjs <new-workspace-directory> <ffmpeg-executable> [seconds]
// Container fields were checked against FFmpeg n7.1's upstream demuxers:
// https://github.com/FFmpeg/FFmpeg/blob/n7.1/libavformat/dsfdec.c
// https://github.com/FFmpeg/FFmpeg/blob/n7.1/libavformat/iff.c
// Silence/bit order: https://github.com/FFmpeg/FFmpeg/blob/n7.1/libavcodec/dsddec.c
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {execFileSync}=require('node:child_process');
const DSD_RATE=2822400,PCM_RATE=176400,CHANNELS=2,BLOCK=4096;
const sources=[
 'https://github.com/FFmpeg/FFmpeg/blob/n7.1/libavformat/dsfdec.c',
 'https://github.com/FFmpeg/FFmpeg/blob/n7.1/libavformat/iff.c',
 'https://github.com/FFmpeg/FFmpeg/blob/n7.1/libavcodec/dsddec.c'
];
function u32be(value){const b=Buffer.alloc(4);b.writeUInt32BE(value);return b;}
function u16be(value){const b=Buffer.alloc(2);b.writeUInt16BE(value);return b;}
function chunk(id,payload){const head=Buffer.alloc(12);head.write(id,0,4,'ascii');head.writeBigUInt64BE(BigInt(payload.length),4);return Buffer.concat([head,payload,payload.length%2?Buffer.alloc(1):Buffer.alloc(0)]);}
function makeDsf(samples){
 const bytes=samples/8,blocks=Math.ceil(bytes/BLOCK),data=Buffer.alloc(blocks*BLOCK*CHANNELS);
 // DSF channel blocks are planar and least-significant bit first. Padding is zero.
 for(let block=0;block<blocks;block++)for(let channel=0;channel<CHANNELS;channel++){
  const start=(block*CHANNELS+channel)*BLOCK,length=Math.min(BLOCK,bytes-block*BLOCK);
  data.fill(0x96,start,start+length);
 }
 const header=Buffer.alloc(92);header.write('DSD ',0,'ascii');header.writeBigUInt64LE(28n,4);header.writeBigUInt64LE(BigInt(header.length+data.length),12);header.writeBigUInt64LE(0n,20);
 header.write('fmt ',28,'ascii');header.writeBigUInt64LE(52n,32);header.writeUInt32LE(1,40);header.writeUInt32LE(0,44);
 header.writeUInt32LE(2,48);header.writeUInt32LE(CHANNELS,52);header.writeUInt32LE(DSD_RATE,56);header.writeUInt32LE(1,60);
 header.writeBigUInt64LE(BigInt(samples),64);header.writeUInt32LE(BLOCK,72);header.writeUInt32LE(0,76);
 header.write('data',80,'ascii');header.writeBigUInt64LE(BigInt(data.length+12),84);
 return Buffer.concat([header,data]);
}
function makeDff(samples){
 // DSDIFF uses big-endian chunk lengths (excluding their 12-byte header),
 // byte-interleaved channels and most-significant bit first sample order.
 const channels=Buffer.concat([u16be(CHANNELS),Buffer.from('SLFTSRGT','ascii')]);
 const compression=Buffer.concat([Buffer.from('DSD ','ascii'),Buffer.from([14]),Buffer.from('not compressed','ascii')]);
 const properties=Buffer.concat([Buffer.from('SND ','ascii'),chunk('FS  ',u32be(DSD_RATE)),chunk('CHNL',channels),chunk('CMPR',compression)]);
 const data=Buffer.alloc(samples/8*CHANNELS,0x69);
 const payload=Buffer.concat([Buffer.from('DSD ','ascii'),chunk('FVER',u32be(0x01050000)),chunk('PROP',properties),chunk('DSD ',data)]);
 return chunk('FRM8',payload);
}
function inspectDsf(bytes){
 assert.equal(bytes.toString('ascii',0,4),'DSD ');assert.equal(Number(bytes.readBigUInt64LE(12)),bytes.length);
 assert.equal(bytes.toString('ascii',28,32),'fmt ');assert.equal(Number(bytes.readBigUInt64LE(32)),52);
 assert.equal(bytes.readUInt32LE(40),1);assert.equal(bytes.readUInt32LE(44),0);assert.equal(bytes.readUInt32LE(60),1);
 assert.equal(bytes.toString('ascii',80,84),'data');assert.equal(Number(bytes.readBigUInt64LE(84))+80,bytes.length);
 const samples=Number(bytes.readBigUInt64LE(64)),sampleRate=bytes.readUInt32LE(56),channels=bytes.readUInt32LE(52);
 return {format:'DSF',codec:'dsd_lsbf_planar',dsdRate:sampleRate,channels,samplesPerChannel:samples,duration:samples/sampleRate,blockBytes:bytes.readUInt32LE(72),fileBytes:bytes.length};
}
function inspectDff(bytes){
 assert.equal(bytes.toString('ascii',0,4),'FRM8');assert.equal(Number(bytes.readBigUInt64BE(4))+12,bytes.length);assert.equal(bytes.toString('ascii',12,16),'DSD ');
 let sampleRate=0,channels=0,dataBytes=0;
 function walk(start,end){for(let at=start;at+12<=end;){const id=bytes.toString('ascii',at,at+4),size=Number(bytes.readBigUInt64BE(at+4)),data=at+12;assert.ok(data+size<=end);
  if(id==='PROP'){assert.equal(bytes.toString('ascii',data,data+4),'SND ');walk(data+4,data+size);}
  if(id==='FS  ')sampleRate=bytes.readUInt32BE(data);
  if(id==='CHNL'){channels=bytes.readUInt16BE(data);assert.equal(bytes.toString('ascii',data+2,data+10),'SLFTSRGT');}
  if(id==='CMPR')assert.equal(bytes.toString('ascii',data,data+4),'DSD ');
  if(id==='DSD ')dataBytes=size;
  at=data+size+(size%2);
 }}
 walk(16,bytes.length);assert.ok(sampleRate>0&&channels>0&&dataBytes>0);
 return {format:'DSDIFF',codec:'dsd_msbf',dsdRate:sampleRate,channels,samplesPerChannel:dataBytes*8/channels,duration:dataBytes*8/channels/sampleRate,fileBytes:bytes.length};
}
function inspectWave(file){
 const bytes=fs.readFileSync(file);assert.equal(bytes.toString('ascii',0,4),'RIFF');assert.equal(bytes.toString('ascii',8,12),'WAVE');
 let format,pcm;
 for(let at=12;at+8<=bytes.length;){const id=bytes.toString('ascii',at,at+4),size=bytes.readUInt32LE(at+4),data=at+8;assert.ok(data+size<=bytes.length);
  if(id==='fmt '){const tag=bytes.readUInt16LE(data);format={sampleRate:bytes.readUInt32LE(data+4),channels:bytes.readUInt16LE(data+2),bits:bytes.readUInt16LE(data+14),encoding:tag===0xfffe?bytes.readUInt16LE(data+24):tag};}
  if(id==='data')pcm=bytes.subarray(data,data+size);at=data+size+(size%2);
 }
 assert.ok(format&&pcm);assert.equal(format.encoding,3,'IEEE float PCM expected');assert.equal(format.bits,32);assert.equal(format.channels,CHANNELS);assert.equal(format.sampleRate,PCM_RATE);assert.equal(pcm.length%8,0);
 let peak=0,energy=0,settledPeak=0,settledEnergy=0,settledCount=0;
 const settlingBytes=Math.ceil(.05*format.sampleRate)*8;
 for(let at=0;at<pcm.length;at+=4){const value=pcm.readFloatLE(at);assert.ok(Number.isFinite(value),'PCM samples must be finite');peak=Math.max(peak,Math.abs(value));energy+=value*value;if(at>=settlingBytes){settledPeak=Math.max(settledPeak,Math.abs(value));settledEnergy+=value*value;settledCount++;}}
 return {...format,frames:pcm.length/8,duration:pcm.length/8/format.sampleRate,dataBytes:pcm.length,peak,rms:Math.sqrt(energy/(pcm.length/4)),settlingSeconds:.05,settledPeak,settledRms:Math.sqrt(settledEnergy/Math.max(1,settledCount))};
}
function inside(root,candidate){const relative=path.relative(root,candidate);return relative.length>0&&!relative.startsWith('..'+path.sep)&&relative!=='..'&&!path.isAbsolute(relative);}
function run(directory,ffmpeg,seconds=2){
 assert.ok(directory&&ffmpeg,'Usage: node tests/dsd-fixture.cjs <new-workspace-directory> <ffmpeg-executable> [seconds]');
 assert.ok(Number.isFinite(seconds)&&seconds>=.1&&seconds<=10,'Choose a short duration from 0.1 to 10 seconds');
 const root=fs.realpathSync(path.join(__dirname,'..')),out=path.resolve(directory),parent=fs.realpathSync(path.dirname(out));
 assert.ok(inside(root,out)&& (parent===root||inside(root,parent)),'Fixture output must be inside the ClearTone workspace');
 assert.ok(!fs.existsSync(out),'Use a new directory so no existing file can be replaced');
 ffmpeg=fs.realpathSync(ffmpeg);
 const version=execFileSync(ffmpeg,['-version'],{windowsHide:true,encoding:'utf8',timeout:10000,maxBuffer:1024*1024}).split(/\r?\n/)[0];
 fs.mkdirSync(out);fs.writeFileSync(path.join(out,'.cleartone-dsd-fixture'),'synthetic-silence-only\n',{flag:'wx'});
 const samples=Math.round(seconds*DSD_RATE/16)*16,expectedFrames=samples/16,result={passed:false,ffmpegVersion:version,synthetic:true,sources,expected:{dsdRate:DSD_RATE,channels:CHANNELS,samplesPerChannel:samples,duration:samples/DSD_RATE,pcmSampleRate:PCM_RATE,pcmBits:32,pcmEncoding:'IEEE float'},files:[]};
 for(const [ext,make,inspect] of [['dsf',makeDsf,inspectDsf],['dff',makeDff,inspectDff]]){
  const file=path.join(out,'silence-dsd64.'+ext),wav=path.join(out,'silence-'+ext+'-176400-f32.wav'),bytes=make(samples);
  fs.writeFileSync(file,bytes,{flag:'wx'});const container=inspect(bytes);
  const args=['-nostdin','-hide_banner','-loglevel','info','-protocol_whitelist','file,pipe','-i',file,'-map','0:a:0','-vn','-sn','-dn','-map_metadata','-1','-ar',String(PCM_RATE),'-acodec','pcm_f32le','-f','wav','-n',wav];
  execFileSync(ffmpeg,args,{windowsHide:true,encoding:'utf8',timeout:30000,maxBuffer:2*1024*1024,stdio:['ignore','pipe','pipe']});
  const pcm=inspectWave(wav);assert.ok(Math.abs(pcm.frames-expectedFrames)<=1,'Decoded sample count differs from container duration');
  // Decoder/filter initialization can create a tiny transient. Report it separately
  // instead of claiming every sample is exactly zero or silently deleting samples.
  assert.ok(pcm.peak<.01&&pcm.settledPeak<.000001,'Expected near-zero PCM after filter settling');
  // Read-only stream inspection gives demuxer/decoder evidence in addition to our header parser.
  let mediaEvidence='';try{execFileSync(ffmpeg,['-nostdin','-hide_banner','-i',file],{windowsHide:true,encoding:'utf8',timeout:10000,maxBuffer:1024*1024,stdio:['ignore','pipe','pipe']});}catch(error){mediaEvidence=String(error.stderr||'').split(/\r?\n/).filter(line=>/Input #|Duration:|Stream #/.test(line)).map(line=>line.trim()).join('\n');}
  assert.ok(mediaEvidence.includes(container.codec),'FFmpeg did not identify the expected DSD encoding');
  result.files.push({file:path.basename(file),wave:path.basename(wav),container,pcm,mediaEvidence});
 }
 result.passed=true;fs.writeFileSync(path.join(out,'validation.json'),JSON.stringify(result,null,2)+'\n',{flag:'wx'});return result;
}
if(require.main===module){try{console.log(JSON.stringify(run(process.argv[2],process.argv[3],process.argv[4]===undefined?2:Number(process.argv[4])),null,2));}catch(error){console.error(error.message);if(error.stderr)console.error(String(error.stderr));process.exitCode=1;}}
module.exports={makeDsf,makeDff,inspectDsf,inspectDff,inspectWave,run};
