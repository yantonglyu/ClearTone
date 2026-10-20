const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..'),lock=require('../service/package-lock.json');
const inventory=[];
for(const [folder,pkg] of Object.entries(lock.packages)){
 if(!folder)continue;
 const directory=path.join(root,'service',folder),meta=JSON.parse(fs.readFileSync(path.join(directory,'package.json'),'utf8'));
 const files=fs.readdirSync(directory).filter(f=>/^(licen[cs]e|copying|notice)(\.|$)/i.test(f));
 inventory.push({name:meta.name,version:pkg.version,license:meta.license||pkg.license||'See package license',source:pkg.resolved,licenseFiles:files});
}
fs.writeFileSync(path.join(root,'licenses/npm-inventory.json'),JSON.stringify(inventory,null,2)+'\n');
console.log('Recorded '+inventory.length+' dependency packages.');
