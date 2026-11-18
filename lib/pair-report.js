'use strict';
// 定向互动档案：从两位已归档账号的本地资料里提取双方互动，生成一份带数据看板的
// 离线 HTML 报告。纯本地计算，0 credits；补齐未归档部分仍走常规获取流程。
const fs=require('node:fs'),path=require('node:path');
const {loadIndependentArchive}=require('./completeness');
const ESC=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const UID_RE=/^[1-9]\d{4,19}$/;
const MAX_LIST_ROWS=5000;

function pairKey(uidA,uidB){return [uidA,uidB].sort().join('_');}

function involvement(row,other){
 // 一条评论“涉及对方”的三种形态：评论对方的原微博、回复对方的评论、引用对方的评论。
 return row.postAuthorUid===other||row.replyAuthorUid===other||row.replyRecord&&row.replyRecord.authorUid===other;
}

function nicknameOf(index,uid,records){
 const entry=index[uid];
 if(entry&&entry.nickname)return entry.nickname;
 for(const kind of ['sent','received'])for(const entry of (records[kind]||[]).values()){
  const row=entry.row;
  if(row.authorUid===uid&&row.author)return row.author;
  if(row.postAuthorUid===uid&&row.postAuthor)return row.postAuthor;
  if(row.replyAuthorUid===uid&&row.replyAuthor)return row.replyAuthor;
 }
 return '';
}

function collectPair(workspace,uidA,uidB){
 if(!UID_RE.test(uidA)||!UID_RE.test(uidB))throw Error('互动双方的 UID 无效');
 if(uidA===uidB)throw Error('互动双方不能是同一个账号');
 const storage=require('./storage'),p=storage.storagePaths(workspace),index=storage.indexRead(p.users);
 const dirOf=uid=>{const entry=index[uid],folder=entry&&entry.folder;if(!folder||folder==='.reader')throw Error('本地资料缺少账号 '+uid+'，请先获取该用户的互动数据');return path.join(p.users,folder);};
 const dirs=[[dirOf(uidA),uidA,uidB],[dirOf(uidB),uidB,uidA]];
 const picked=new Map(),records={a:new Map(),b:new Map()},warnings=[];
 for(const [dir,self,other] of dirs){
  for(const kind of ['sent','received']){
   const archive=loadIndependentArchive(dir,kind,self);
   if(archive.warnings.length)warnings.push(...archive.warnings);
   for(const [id,entry] of archive.records){
    records[self===uidA?'a':'b'].set(id,entry);
    const row=entry.row,author=row.authorUid||self;
    if(author!==self&&author!==other)continue;
    if(!involvement(row,author===self?other:self))continue;
    const previous=picked.get(id);
    if(previous&&previous.row.content)continue;
    picked.set(id,{...entry,row:{...row,authorUid:author}});
   }
  }
 }
 const rows=[...picked.values()].map(entry=>({row:entry.row,side:entry.row.authorUid===uidA?'a':'b'})).sort((x,y)=>(x.row.ms||0)-(y.row.ms||0));
 const nicknames={a:nicknameOf(index,uidA,records),b:nicknameOf(index,uidB,records)};
 return {rows,nicknames,warnings};
}

function median(values){
 if(!values.length)return null;
 const sorted=[...values].sort((x,y)=>x-y),mid=Math.floor(sorted.length/2);
 return sorted.length%2?sorted[mid]:Math.round((sorted[mid-1]+sorted[mid])/2);
}

function computeStats(rows,uidA){
 const stats={total:rows.length,aSent:0,bSent:0,posts:new Map(),days:new Map(),hours:new Map(),roundTrips:0,gaps:[],peakDay:null,peakHour:null,range:null};
 let previousSide=null,previousMs=null;
 for(const {row,side} of rows){
  if(side==='a')stats.aSent++;else stats.bSent++;
  if(row.postId&&row.postAuthorUid)if(!stats.posts.has(row.postId))stats.posts.set(row.postId,{authorUid:row.postAuthorUid,author:row.postAuthor||'',text:row.postText||'',count:1});
   else stats.posts.get(row.postId).count++;
  if(Number.isFinite(row.ms)){
   const date=new Date(row.ms),day=date.getFullYear()+'-'+String(date.getMonth()+1).padStart(2,'0')+'-'+String(date.getDate()).padStart(2,'0'),hour=date.getHours();
   stats.days.set(day,(stats.days.get(day)||0)+1);
   stats.hours.set(hour,(stats.hours.get(hour)||0)+1);
   if(!stats.range||row.ms<stats.range.from)stats.range={from:row.ms,to:stats.range?stats.range.to:row.ms};
   if(row.ms>(stats.range?.to||0))stats.range.to=row.ms;
   if(previousSide&&side!==previousSide&&previousMs!=null&&row.ms>=previousMs)stats.gaps.push(row.ms-previousMs);
  }
  previousSide=side;previousMs=Number.isFinite(row.ms)?row.ms:previousMs;
 }
 stats.roundTrips=stats.gaps.length;
 stats.medianGapMinutes=median(stats.gaps.map(ms=>Math.round(ms/60000)));
 delete stats.gaps;
 for(const [day,count] of stats.days)if(!stats.peakDay||count>stats.peakDay.count)stats.peakDay={day,count};
 for(const [hour,count] of stats.hours)if(!stats.peakHour||count>stats.peakHour.count)stats.peakHour={hour,count};
 return stats;
}

function barChart(title,note,buckets,peakKey){
 const width=920,left=42,bottom=30,gap=4,max=Math.max(1,...buckets.map(b=>b.count));
 const bar=b=>{const w=(width-left-12)/buckets.length-gap,h=Math.round((b.count/max)*(150)),x=left+b.index*((width-left-12)/buckets.length)+gap/2,y=170-h,highlight=peakKey!=null&&b.key===peakKey;
  return `<rect x="${x.toFixed(1)}" y="${y}" width="${((width-left-12)/buckets.length-gap).toFixed(1)}" height="${Math.max(h,b.count?2:0)}" rx="2" fill="${highlight?'#ff6a3d':'#b03a24'}"><title>${ESC(b.label)}：${b.count} 条</title></rect>`+
   (b.count?`<text x="${(x+((width-left-12)/buckets.length-gap)/2).toFixed(1)}" y="${y-4}" font-size="9" fill="${highlight?'#ff6a3d':'#8a5a4a'}" text-anchor="middle">${b.count}</text>`:'')+
   (b.tick?`<text x="${(x+((width-left-12)/buckets.length-gap)/2).toFixed(1)}" y="186" font-size="10" fill="#9a8b85" text-anchor="middle">${ESC(b.tick)}</text>`:'');};
 return `<section class="chart"><h3>${ESC(title)}${note?`<span class="chart-note">${ESC(note)}</span>`:''}</h3><svg viewBox="0 0 ${width} 196" role="img">${buckets.map(bar).join('')}<line x1="${left}" y1="170" x2="${width-8}" y2="170" stroke="#3a2f2b"/></svg></section>`;
}

function renderPairReport(workspace,uidA,uidB){
 const {rows,nicknames,warnings}=collectPair(workspace,uidA,uidB),stats=computeStats(rows,uidA);
 const labelA=nicknames.a||uidA,labelB=nicknames.b||uidB;
 const nameA=ESC(labelA),nameB=ESC(labelB);
 const generated=new Date().toISOString().replace('T',' ').slice(0,19);
 const dayKeys=[...stats.days.keys()].sort(),hourBuckets=Array.from({length:24},(_,h)=>({key:h,index:h,count:stats.hours.get(h)||0,label:h+' 时',tick:h%6===0?h+'时':''}));
 const dayBuckets=dayKeys.slice(-60).map((day,index)=>({key:day,index,count:stats.days.get(day),label:day,tick:index%5===0?day.slice(5):''}));
 const range=stats.range?new Date(stats.range.from).toISOString().slice(0,10)+' — '+new Date(stats.range.to).toISOString().slice(0,10):'—';
 const groups=new Map();
 for(const {row,side} of rows){
  const key=row.postId||'other';if(!groups.has(key))groups.set(key,{postId:row.postId||'',postAuthorUid:row.postAuthorUid||'',postAuthor:row.postAuthor||'',postText:row.postText||'',items:[]});
  groups.get(key).items.push({row,side});
 }
 let listed=0;
 const groupHtml=[...groups.values()].map(group=>{
  const link=group.postAuthorUid&&/^[1-9]\d{4,19}$/.test(group.postId)?`<a href="https://weibo.com/${ESC(group.postAuthorUid)}/${ESC(group.postId)}" target="_blank" rel="noreferrer">weibo.com ↗</a> <a href="https://weibo.cn/detail/${ESC(group.postId)}" target="_blank" rel="noreferrer">weibo.cn ↗</a>`:'';
  const items=group.items.slice(0,MAX_LIST_ROWS).map(({row,side})=>{
   listed++;
   const who=side==='a'?nameA:nameB,other=side==='a'?nameB:nameA;
   const reply=row.replyAuthor?`<span class="reply-to">回复 ${ESC(row.replyAuthor===labelA?labelA:row.replyAuthor===labelB?labelB:row.replyAuthor)}：</span>`:'';
   return `<div class="item side-${side}" data-text="${ESC((row.author||'')+' '+(row.content||'')+' '+(row.replyAuthor||''))}"><span class="badge">${side==='a'?'A':'B'}</span><span class="who">${who}</span><span class="time">${ESC(row.time||'')}</span><span class="content">${reply}${ESC(row.content||'')}</span></div>`;
  }).join('');
  return `<details class="thread" open><summary><span class="thread-author">${group.postAuthor?ESC(group.postAuthor):'原微博'}</span><span class="thread-text">${ESC((group.postText||'').slice(0,140))}</span><span class="thread-meta">${link}<span class="count">${group.items.length} 条</span></span></summary><div class="items">${items}</div></details>`;
 }).join('');
 const truncated=rows.length>listed?`<p class="notice">互动条数超过展示上限 ${MAX_LIST_ROWS} 条，仅渲染最近部分；完整数据仍保存在双方本地资料中。</p>`:'';
 const empty=rows.length?'':'<p class="notice">本地资料中没有命中这两人的互动。可以先在双方的用户页「获取数据」拉取发出和收到的评论（覆盖对方原微博或回复对方的记录），再重新生成。</p>';
 const warningHtml=warnings.length?`<p class="notice">部分本地档案读取告警：${ESC(warnings.slice(0,4).join('；'))}</p>`:'';
 const html=`<!doctype html>
<html lang="zh-CN"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>互动档案 · ${nameA} ⇄ ${nameB}</title><style>
:root{color-scheme:dark}body{background:#0d0d10;color:#e8e2de;font:14px/1.6 "PingFang SC","Microsoft YaHei",sans-serif;margin:0;padding:28px 34px 60px}
a{color:#ff7a4d;text-decoration:none}a:hover{text-decoration:underline}
h1{font-size:22px;margin:4px 0 2px}.pair-a{color:#ff7a4d}.pair-b{color:#6aa6ff}.arrow{color:#8a8078;margin:0 8px}
.meta{color:#9a8b85;font-size:12px;margin-bottom:18px}
.cards{display:flex;flex-wrap:wrap;gap:10px;margin:14px 0 22px}
.card{background:#17161a;border:1px solid #2a2523;border-radius:8px;padding:10px 16px;min-width:130px}.card b{display:block;font-size:20px;margin-top:2px}.card span{color:#9a8b85;font-size:12px}
.chart{background:#141317;border:1px solid #2a2523;border-radius:10px;padding:14px 18px;margin:14px 0}
.chart h3{margin:2px 0 10px;font-size:14px}.chart-note{color:#9a8b85;font-weight:400;font-size:12px;margin-left:10px}
.tools{display:flex;gap:8px;align-items:center;margin:16px 0 10px;flex-wrap:wrap}
.tools input,.tools select{background:#17161a;border:1px solid #2a2523;color:#e8e2de;border-radius:6px;padding:6px 10px;font-size:13px}
.thread{background:#141317;border:1px solid #2a2523;border-radius:10px;margin:10px 0;overflow:hidden}
.thread summary{cursor:pointer;padding:10px 14px;display:flex;gap:10px;align-items:baseline;flex-wrap:wrap}
.thread-author{color:#6aa6ff;font-weight:600}.thread-text{color:#b8ada6;flex:1;min-width:200px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.thread-meta{color:#9a8b85;font-size:12px;display:flex;gap:8px;align-items:center}
.items{border-top:1px solid #2a2523}.item{display:flex;gap:10px;padding:8px 14px;border-bottom:1px solid #1c1a1e;align-items:baseline}
.badge{font-size:11px;font-weight:700;border-radius:4px;padding:1px 6px}.side-a .badge{background:#3d221a;color:#ff7a4d}.side-b .badge{background:#1a2a3d;color:#6aa6ff}
.who{font-weight:600;min-width:80px}.time{color:#9a8b85;font-size:12px;min-width:130px}.content{flex:1}.reply-to{color:#8a8078}
.notice{color:#c9a227;background:#1c1a14;border:1px solid #3a3222;border-radius:8px;padding:10px 14px}
@media print{body{background:#fff;color:#111}.card,.chart,.thread{background:#fff;border-color:#ddd;color:#111}.card span,.chart-note,.meta,.time,.thread-meta,.thread-text{color:#555}}
</style></head><body>
<h1><span class="pair-a">${nameA}</span><span class="arrow">⇄</span><span class="pair-b">${nameB}</span></h1>
<div class="meta">定向互动档案 · UID ${ESC(uidA)} / ${ESC(uidB)} · 记录时间：${ESC(range)} · 生成于 ${ESC(generated)} · 本地离线生成 0 credits</div>
${warningHtml}${empty}
<div class="cards">
 <div class="card"><span>命中互动</span><b>${stats.total}</b></div>
 <div class="card"><span>${nameA} 发出</span><b>${stats.aSent}</b></div>
 <div class="card"><span>${nameB} 发出</span><b>${stats.bSent}</b></div>
 <div class="card"><span>涉及帖子</span><b>${stats.posts.size}</b></div>
</div>
<h2 style="font-size:16px;margin:18px 0 4px">数据看板</h2>
<div class="cards">
 <div class="card"><span>互动总条数</span><b>${stats.total}</b></div>
 <div class="card"><span>活跃天数</span><b>${stats.days.size}</b></div>
 <div class="card"><span>往返 · 中位间隔</span><b>${stats.roundTrips} 次${stats.medianGapMinutes!=null?' · '+stats.medianGapMinutes+' 分':''}</b></div>
 <div class="card"><span>峰值日 / 时段</span><b>${stats.peakDay?stats.peakDay.day.slice(5)+' · '+stats.peakDay.count+' 条':'—'} / ${stats.peakHour?stats.peakHour.hour+' 点 · '+stats.peakHour.count+' 条':'—'}</b></div>
</div>
${dayBuckets.length?barChart('每天互动量',dayKeys.length>60?'仅显示最近 60 天 · 峰值 '+stats.peakDay.day+'（'+stats.peakDay.count+' 条）':'共 '+dayKeys.length+' 天'+(stats.peakDay?' · 峰值 '+stats.peakDay.day+'（'+stats.peakDay.count+' 条）':''),dayBuckets,stats.peakDay&&stats.peakDay.day):''}
${stats.total?barChart('时段分布（0-23 点）',stats.peakHour?'24 小时 · 峰值 '+stats.peakHour.hour+' 点（'+stats.peakHour.count+' 条）':'',hourBuckets,stats.peakHour&&stats.peakHour.hour):''}
<div class="tools"><input id="pair-filter" type="search" placeholder="搜索作者或关键词"><select id="pair-side"><option value="">全部互动</option><option value="a">${ESC(labelA)} 发出</option><option value="b">${ESC(labelB)} 发出</option></select><button id="pair-clear" type="button">清空筛选</button><span class="chart-note" id="pair-count"></span><button id="pair-expand" type="button">全部展开</button><button id="pair-collapse" type="button">全部收起</button></div>
${truncated}${groupHtml}
<script>
(function(){
 var filter=document.getElementById('pair-filter'),side=document.getElementById('pair-side'),count=document.getElementById('pair-count');
 function apply(){var term=filter.value.trim().toLowerCase(),pick=side.value,total=0,shown=0;
  document.querySelectorAll('.thread').forEach(function(thread){var visible=0;
   thread.querySelectorAll('.item').forEach(function(item){var ok=(!term||item.dataset.text.indexOf(term)>=0)&&(!pick||item.classList.contains('side-'+pick));item.style.display=ok?'':'none';if(ok)visible++;});
   total+=thread.querySelectorAll('.item').length;shown+=visible;thread.style.display=visible?'':'none';});
  count.textContent='显示 '+shown+' / '+total+' 条';}
 filter.addEventListener('input',apply);side.addEventListener('change',apply);
 document.getElementById('pair-clear').addEventListener('click',function(){filter.value='';side.value='';apply();});
 document.getElementById('pair-expand').addEventListener('click',function(){document.querySelectorAll('.thread').forEach(function(t){t.open=true;});});
 document.getElementById('pair-collapse').addEventListener('click',function(){document.querySelectorAll('.thread').forEach(function(t){t.open=false;});});
 apply();
})();
</script>
</body></html>`;
 return {html,stats,nicknames,warnings};
}

function pairReportFile(workspace,uidA,uidB){
 const file=path.join(workspace,'output','pair',pairKey(uidA,uidB)+'.html');
 fs.mkdirSync(path.dirname(file),{recursive:true});
 return file;
}

function generatePairReport(workspace,uidA,uidB){
 const {html,stats,nicknames,warnings}=renderPairReport(workspace,uidA,uidB),file=pairReportFile(workspace,uidA,uidB);
 require('./durable-write').atomicBytes(file,Buffer.from(html));
 return {file,stats,nicknames,warnings};
}

async function runPairCommand(uids,workspace){
 const [uidA,uidB]=uids;
 if(!uidA||!uidB||uids.length!==2||uids.some(u=>!UID_RE.test(u)))throw Error('用法：node weibo_cli.js pair <UID-A> <UID-B> —— 两位账号均需已获取过互动数据');
 const {file,stats,nicknames}=generatePairReport(workspace||require('./config').BASE,uidA,uidB);
 console.log('定向互动档案已生成（本地离线，0 credits）：'+file);
 console.log(`命中互动 ${stats.total} 条（${nicknames.a||uidA} 发出 ${stats.aSent} · ${nicknames.b||uidB} 发出 ${stats.bSent}），涉及 ${stats.posts.size} 条原微博。`);
 require('./terminal-report').reportShortcut(file,{});
}

module.exports={collectPair,computeStats,renderPairReport,generatePairReport,pairReportFile,runPairCommand,pairKey};
