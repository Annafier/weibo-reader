'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os');
const P=require('../lib/pair-report');
const uidA='1234567890',uidB='9876543210',third='1111111111';
const POST_B1='80000000000008801',POST_C1='80000000000008802',POST_A1='80000000000008803',POST_C2='80000000000008804';
const save=(f,v)=>{fs.mkdirSync(path.dirname(f),{recursive:true});fs.writeFileSync(f,JSON.stringify(v));};
const user=uid=>({idstr:uid,screen_name:uid===uidA?'甲':uid===uidB?'乙':'丙'});
const comment=(id,author,target,{replyTo=null,day=1,hour=10}={})=>({
 idstr:id,text_raw:'正文 '+id,created_at:`2026-11-${String(day).padStart(2,'0')}T${String(hour).padStart(2,'0')}:30:00+08:00`,
 user:user(author),status:{idstr:target.postId,text_raw:'原微博 '+target.postId,user:user(target.author)},
 ...(replyTo?{reply_comment:{idstr:replyTo.id,text_raw:'被回复 '+replyTo.id,created_at:`2026-11-${String(day).padStart(2,'0')}T09:00:00+08:00`,user:user(replyTo.author)}}:{}),
});
function workspace(){
 const base=fs.mkdtempSync(path.join(os.tmpdir(),'weibo-pair-test-')),dirA=path.join(base,'data/user-comments',uidA),dirB=path.join(base,'data/user-comments',uidB);
 save(path.join(base,'data/.layout-v2'),'2');
 save(path.join(base,'data/user-comments/.account_names.json'),{[uidA]:{nickname:'甲',folder:uidA},[uidB]:{nickname:'乙',folder:uidB}});
 // 甲的发出：评论乙的原微博；回复乙的评论（原微博属于丙）；一条与乙无关的评论。
 save(path.join(dirA,'cache','sent_c3_m0.json'),{comments:[
  comment('301',uidA,{postId:POST_B1,author:uidB},{hour:10}),
  comment('302',uidA,{postId:POST_C1,author:third},{replyTo:{id:'9001',author:uidB},hour:11}),
  comment('305',uidA,{postId:POST_C2,author:third},{hour:12}),
 ],next_cursor_str:'0'});
 // 甲的收到：乙评论甲的原微博（与乙目录里的同一条，用于跨目录去重）。
 save(path.join(dirA,'cache','received_c2_m0.json'),{comments:[comment('303',uidB,{postId:POST_A1,author:uidA},{hour:12})],next_cursor_str:'0'});
 // 乙的发出：同一条 303（去重）+ 回复甲的评论 301。
 save(path.join(dirB,'cache','sent_c2_m0.json'),{comments:[
  comment('303',uidB,{postId:POST_A1,author:uidA},{hour:12}),
  comment('304',uidB,{postId:POST_B1,author:uidB},{replyTo:{id:'301',author:uidA},day:2,hour:9}),
 ],next_cursor_str:'0'});
 // 乙的收到：丙评论乙的原微博——与甲乙无关，不得计入。
 save(path.join(dirB,'cache','received_c1_m0.json'),{comments:[comment('306',third,{postId:POST_B1,author:uidB})],next_cursor_str:'0'});
 return base;
}
test('pair interactions: extraction, cross-dir dedupe and stats',()=>{
 const base=workspace();
 try{
  const {rows,nicknames}=P.collectPair(base,uidA,uidB);
  const ids=rows.map(r=>r.row.recordId).sort();
  assert.deepEqual(ids,['301','302','303','304'],'only A/B-involved rows survive, cross-directory duplicates merge');
  assert.equal(nicknames.a,'甲');assert.equal(nicknames.b,'乙');
  const stats=P.computeStats(rows,uidA);
  assert.equal(stats.total,4);assert.equal(stats.aSent,2);assert.equal(stats.bSent,2);
  assert.equal(stats.posts.size,3);assert.equal(stats.days.size,2);
  assert.equal(stats.roundTrips,1,'one a→b alternation');
  assert.ok(Number.isFinite(stats.medianGapMinutes)&&stats.medianGapMinutes>0);
  assert.equal(stats.peakDay.day,'2026-11-01');assert.equal(stats.peakHour.hour,10);
 }finally{fs.rmSync(base,{recursive:true,force:true});}
});
test('pair report: renders dashboard HTML, reuses one file per pair and rejects invalid pairs',()=>{
 const base=workspace();
 try{
  const first=P.generatePairReport(base,uidA,uidB);
  assert.equal(path.basename(first.file),'1234567890_9876543210.html');
  const html=fs.readFileSync(first.file,'utf8');
  for(const marker of ['数据看板','每天互动量','时段分布','甲','乙','命中互动','<svg'])assert.ok(html.includes(marker),'missing marker');
  assert.ok(html.includes('weibo.com/9876543210/'+POST_B1),'root post links present');
  const second=P.generatePairReport(base,uidB,uidA);
  assert.equal(path.basename(second.file),path.basename(first.file),'pair key is order-independent');
  assert.equal(second.stats.total,4);
  assert.throws(()=>P.generatePairReport(base,uidA,uidA),/同一个账号/);
  assert.throws(()=>P.generatePairReport(base,uidA,'abc'),/UID 无效/);
  // 缺少本地资料的账号必须明确报错，而不是产出空档案。
  assert.throws(()=>P.generatePairReport(base,uidA,'5555512345'),/本地资料缺少账号/);
  fs.rmSync(path.join(base,'data/user-comments',uidA,'cache'),{recursive:true,force:true});
  const regenerated=P.generatePairReport(base,uidA,uidB);
  assert.equal(regenerated.stats.total,2,'missing own archive degrades to the other side only');
 }finally{fs.rmSync(base,{recursive:true,force:true});}
});
test('runPairCommand prints the dossier path and summary',()=>{
 const base=workspace();
 try{
  const logs=[];const original=console.log;console.log=m=>logs.push(String(m));
  try{P.runPairCommand([uidA,uidB],base);}finally{console.log=original;}
  const output=logs.join('\n');
  assert.match(output,/定向互动档案已生成/);
  assert.match(output,/pair\/1234567890_9876543210\.html/);
  assert.match(output,/命中互动 4 条/);
 }finally{fs.rmSync(base,{recursive:true,force:true});}
});
