'use strict';
const {parentPort}=require('node:worker_threads');
const path=require('node:path'),kuromoji=require('kuromoji'),kana=require('wanakana');
const ready=new Promise((resolve,reject)=>kuromoji.builder({dicPath:path.join(path.dirname(require.resolve('kuromoji/package.json')),'dict')}).build((e,t)=>e?reject(e):resolve(t)));
parentPort.on('message',async({id,lines})=>{
 try{
  const tokenizer=await ready;
  const result=lines.map(text=>{
   let at=0;const tokens=[];
   for(const t of tokenizer.tokenize(text)){
    const start=text.indexOf(t.surface_form,at);
    if(start>at)tokens.push({text:text.slice(at,start),reading:'',roman:''});
    const reading=t.reading?kana.toHiragana(t.reading):kana.isKana(t.surface_form)?kana.toHiragana(t.surface_form):'';
    tokens.push({text:t.surface_form,reading,roman:reading?kana.toRomaji(t.pos==='助詞'&&t.pronunciation?kana.toHiragana(t.pronunciation):reading):'',uncertain:!t.reading&&/[\u3400-\u9fff]/.test(t.surface_form)});
    at=(start<0?at:start)+t.surface_form.length;
   }
   if(at<text.length)tokens.push({text:text.slice(at),reading:'',roman:''});
   return {text,tokens};
  });
  parentPort.postMessage({id,lines:result});
 }catch(e){parentPort.postMessage({id,error:e.message});}
});
