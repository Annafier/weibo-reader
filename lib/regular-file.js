"use strict";
// Prevent a replaced FIFO/device/symlink from blocking the single-threaded engine.
// A descriptor is checked again after open; O_NONBLOCK also closes the stat/open race.
const fs=require('node:fs'),path=require('node:path');
function list(folder,pattern){
 if(!fs.existsSync(folder))return [];
 const stat=fs.lstatSync(folder);if(!stat.isDirectory()||stat.isSymbolicLink())throw Object.assign(Error('本地来源目录不是可核实的普通目录'),{code:'LOCAL_SOURCE_INVALID'});
 return fs.readdirSync(folder,{withFileTypes:true}).filter(e=>pattern.test(e.name)).map(e=>{if(!e.isFile()||e.isSymbolicLink())throw Object.assign(Error('本地来源不是可读取的普通文件：'+e.name),{code:'LOCAL_SOURCE_INVALID'});return require('node:path').join(folder,e.name);});
}
function open(file,{maxBytes=Infinity}={}){
 const fail=()=>Object.assign(Error('本地来源不是可读取的普通文件'),{code:'LOCAL_SOURCE_INVALID'});
 const before=fs.lstatSync(file,{bigint:true});if(!before.isFile()||before.isSymbolicLink()||Number(before.size)>maxBytes)throw fail();
 let fd;
 try{
  fd=fs.openSync(file,fs.constants.O_RDONLY|fs.constants.O_NOFOLLOW|fs.constants.O_NONBLOCK);
  const opened=fs.fstatSync(fd,{bigint:true});
  if(!opened.isFile()||opened.dev!==before.dev||opened.ino!==before.ino||Number(opened.size)>maxBytes)throw fail();
  return fd;
 }catch(error){if(fd!=null)fs.closeSync(fd);if(error.code==='ELOOP')throw fail();throw error;}
}
function relPath(from,to){
 // Stored evidence references are POSIX-style ids; several validators match them
 // against forward-slash patterns, so separators are normalized at the producer.
 return path.relative(from,to).split(path.sep).join('/');
}
function fsync(fd){
 // FlushFileBuffers requires a writable handle, so a read-only descriptor
 // cannot be flushed on Windows; POSIX fsync accepts it. EPERM there is expected.
 try{return fs.fsyncSync(fd);}catch(error){if(process.platform!=='win32'||error.code!=='EPERM')throw error;}
}
function syncDirectory(directory){
 // Durability of the parent entry itself. Windows cannot open directories for
 // reading, and rename there is already metadata-committed by the filesystem.
 if(process.platform==='win32')return;
 const fd=fs.openSync(directory,fs.constants.O_RDONLY|fs.constants.O_DIRECTORY|fs.constants.O_NOFOLLOW);
 try{fs.fsyncSync(fd);}finally{fs.closeSync(fd);}
}
function read(file,{maxBytes=Infinity}={}){
 const fail=()=>Object.assign(Error('本地来源不是可读取的普通文件'),{code:'LOCAL_SOURCE_INVALID'}),fd=open(file,{maxBytes});
 try{
  if(!Number.isFinite(maxBytes))return fs.readFileSync(fd);
  const chunks=[];let size=0;for(;;){const chunk=Buffer.allocUnsafe(Math.min(65536,maxBytes-size+1)),n=fs.readSync(fd,chunk,0,chunk.length,null);if(!n)break;size+=n;if(size>maxBytes)throw fail();chunks.push(chunk.subarray(0,n));}return Buffer.concat(chunks,size);
 }finally{if(fd!=null)fs.closeSync(fd);}
}
module.exports={read,list,open,fsync,syncDirectory,relPath};
