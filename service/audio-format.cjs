'use strict';
function inspect(b){
 if(b.length<16)return null;
 if(b.toString('ascii',0,4)==='RIFF'&&b.toString('ascii',8,12)==='WAVE'){
  for(let p=12;p+16<=b.length;){const size=b.readUInt32LE(p+4);if(b.toString('ascii',p,p+4)==='fmt '&&size>=16&&p+24<=b.length)return {format:'WAV',sampleRate:b.readUInt32LE(p+12)};p+=8+size+(size%2);}
 }
 if(b.toString('ascii',0,4)==='fLaC'&&b.length>=42)return {format:'FLAC',sampleRate:(b[18]<<12)|(b[19]<<4)|(b[20]>>4)};
 if(b.toString('ascii',4,8)==='ftyp'){
  const p=b.indexOf(Buffer.from('mp4a'));if(p>=4&&p+32<=b.length){const sampleRate=b.readUInt32BE(p+28)>>>16;if(sampleRate>=8000&&sampleRate<=192000)return {format:'AAC/MP4',sampleRate};}return null;
 }
 let start=0;
 if(b.toString('ascii',0,3)==='ID3')start=10+((b[6]&127)<<21)+((b[7]&127)<<14)+((b[8]&127)<<7)+(b[9]&127);
 for(let p=start;p+4<b.length;p++){
  if(b[p]!==255||(b[p+1]&224)!==224)continue;
  const v=(b[p+1]>>3)&3,layer=(b[p+1]>>1)&3,index=(b[p+2]>>2)&3,bitrate=b[p+2]>>4;
  if(v!==1&&layer===1&&index!==3&&bitrate>0&&bitrate<15){const sampleRate=[44100,48000,32000][index]/(v===3?1:v===2?2:4),kbps=(v===3?[0,32,40,48,56,64,80,96,112,128,160,192,224,256,320]:[0,8,16,24,32,40,48,56,64,80,96,112,128,144,160])[bitrate],next=p+Math.floor((v===3?144:72)*kbps*1000/sampleRate)+((b[p+2]>>1)&1);if(next+3<b.length&&b[next]===255&&b[next+1]===b[p+1]&&((b[next+2]>>2)&3)===index)return {format:'MP3',sampleRate};}
  if((b[p+1]&246)===240&&p+7<b.length){const rate=[96000,88200,64000,48000,44100,32000,24000,22050,16000,12000,11025,8000][(b[p+2]>>2)&15],length=((b[p+3]&3)<<11)|(b[p+4]<<3)|(b[p+5]>>5),next=p+length;if(rate&&length>=7&&next+3<b.length&&b[next]===255&&(b[next+1]&246)===240)return {format:'AAC',sampleRate:rate};}
 }
 return null;
}
module.exports={inspect};
