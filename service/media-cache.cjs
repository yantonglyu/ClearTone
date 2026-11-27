'use strict';
// URL metadata only. Removing an entry never interrupts an active response stream.
module.exports=function createMediaCache({limit=128,now=Date.now}={}){
 const entries=new Map();
 function prune(){const time=now();for(const [id,value] of entries)if(value.expires<=time)entries.delete(id);}
 return {
  get(id){prune();const value=entries.get(id);if(value){entries.delete(id);entries.set(id,value);}return value;},
  set(id,value){prune();entries.delete(id);if(value.expires>now())entries.set(id,value);while(entries.size>limit)entries.delete(entries.keys().next().value);},
  clear(){entries.clear();},prune,
  get size(){return entries.size;}
 };
};
