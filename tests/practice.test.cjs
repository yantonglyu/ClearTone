const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path');
test('background Japanese readings preserve text, cache and validate input',async()=>{
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'cleartone-readings-')),practice=require('../service/practice.cjs')(dir);
 try{
  const lines=['君の空','夢を見る','明日へ歩こう','Hello 世界！','ひらがな カタカナ'];
  const result=await practice.annotate(lines);
  for(let i=0;i<lines.length;i++)assert.equal(result.lines[i].tokens.map(t=>t.text).join(''),lines[i]);
  assert.equal(result.lines[0].tokens.map(t=>t.reading).join(''),'きみのそら');
  assert.equal(result.lines[1].tokens.map(t=>t.reading).join(''),'ゆめをみる');
  assert.equal(result.lines[0].tokens[0].roman,'kimi');
  practice.close();assert.deepEqual(await practice.annotate(lines),result);
  await assert.rejects(()=>practice.annotate(['x'.repeat(1001)]));
  console.log(JSON.stringify(result));
 }finally{practice.close();await fs.rm(dir,{recursive:true,force:true});}
});
