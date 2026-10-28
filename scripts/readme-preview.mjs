// Local, synthetic-data preview for documentation screenshots. Never reads personal history.
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { resolve, extname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../src/TimeLens.Dashboard/dist/', import.meta.url));
const date = new Date();
const day = `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;
const iso = hour => new Date(`${day}T${String(Math.floor(hour)).padStart(2,'0')}:${String(Math.round((hour%1)*60)).padStart(2,'0')}:00`).toISOString();
const sessions = [
  [9,10.5,'Code.exe','Work','Design system · TimeLens'],
  [10.5,11.25,'firefox.exe','Work','Documentation and research'],
  [11.25,11.5,'','idle','Coffee break'],
  [11.5,12.5,'Figma.exe','Work','Dashboard exploration'],
  [13.5,14.5,'Code.exe','Work','Build and refine'],
  [14.5,15,'firefox.exe','Education','Learning something new'],
  [15,15.5,'Obsidian.exe','Work','Notes and next steps'],
];
const timeline = sessions.map(([startHour,endHour,exeName,type,windowTitle]) => ({startHour,endHour,exeName,type,windowTitle,durationSeconds:(endHour-startHour)*3600}));
const browserSites = [['github.com',1800,8],['developer.mozilla.org',1500,6],['svelte.dev',1200,4]].map(([domain,totalSeconds,visits])=>({domain,totalSeconds,visits,lastVisit:iso(15),keystrokes:null,clicks:null,pages:[{url:`https://${domain}/`,title:'Project research',browser:'Firefox',totalSeconds,sessions:visits,lastSeen:iso(15),keystrokes:null,clicks:null}]}));
const summary = {
  summary:{activeTime:'5h 15m',activeSeconds:18900,idleTime:'15m',idleSeconds:900,focusScore:90,topCategory:'Work',topCategoryTime:'4h 45m',vsYesterday:-25,totalKeystrokes:12840,totalClicks:1462},
  timeline, topApps:[['Code.exe',150,8240,367],['firefox.exe',75,1600,365],['Figma.exe',60,1800,365],['Obsidian.exe',30,1200,365]].map(([name,minutes,keystrokes,clicks])=>({name,minutes,keystrokes,clicks})),
  categories:[{name:'Work',percentage:90,minutes:285},{name:'Education',percentage:10,minutes:30}],
  heatmap:Array.from({length:180},(_,i)=>{const d=new Date(date);d.setDate(d.getDate()-179+i);return{date:`${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`,value:i===179?315:d.getDay()%6===0?0:70+(i*37)%260};}),
  live:{currentApp:'Code.exe',idleMinutes:0,isIdle:false,audibleTab:null,audioActive:false,systemState:'active',pendingIdleReturn:false},
  browserSites,audioSessions:[],webMedia:{playbackSeconds:0,backgroundSeconds:0,pictureInPictureSeconds:0,entries:[]},
};
const settings={theme:'default',offlineWorkEnabled:false,trackBrowser:true,trackInput:true,trackAudio:true,timelineGrouped:true,showTitles:true,timeFormat:'24h',pollIntervalSeconds:3600,defaultView:'today',motionEnabled:false,heatmapDays:180};
const api = {
  '/api/summary':summary, '/api/settings':settings, '/api/release-notice':{pending:false},
  '/api/extension-status':{connected:true,compatible:true,version:'2.0.0',minimumVersion:'2.0.0',coveragePercent:100,focusedBrowserSeconds:4500,attributedSeconds:4500,missingSeconds:0,diagnosticWindowSeconds:4500},
  '/api/browser-hourly':Array.from({length:24},(_,hour)=>({hour,totalSeconds:hour===10?1800:hour===11?900:hour===14?1800:0})),
  '/api/browser-visits':browserSites.map(s=>({domain:s.domain,url:`https://${s.domain}/`,title:'Project research',browser:'Firefox',startedAt:iso(10.5),endedAt:iso(11),activeSeconds:s.totalSeconds})),
  '/api/block/stats':[], '/api/rules':[], '/api/offline':[],
};
const types={'.html':'text/html','.js':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml','.woff2':'font/woff2','.ico':'image/x-icon'};
http.createServer(async(req,res)=>{
  const path = new URL(req.url,'http://localhost').pathname;
  if(path.startsWith('/api/')) {res.setHeader('Content-Type','application/json');res.end(JSON.stringify(api[path]??{}));return;}
  const file=resolve(root,`.${decodeURIComponent(path==='/'?'/index.html':path)}`);
  if(!file.startsWith(resolve(root)+sep)){res.writeHead(403).end();return;}
  try {res.setHeader('Content-Type',types[extname(file)]??'application/octet-stream');res.end(await readFile(file));}
  catch {res.writeHead(404).end('Not found');}
}).listen(47889,'127.0.0.1',()=>console.log('Documentation preview: http://127.0.0.1:47889 — synthetic data only'));
