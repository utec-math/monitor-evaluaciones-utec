// Runs real browser WebRTC and MediaRecorder with a deterministic fake Firebase transport.
// Firebase authorization is tested separately against the RTDB emulator.
const { chromium } = require('playwright');
const http = require('http');
const fs = require('fs');
const path = require('path');
const assert = require('assert/strict');
const root = path.resolve(__dirname, '..');
const state = { sessions: { TEST: { config: { active: true }, clients: { student: { uid: 'student', name: 'Estudiante de prueba', state: 'locked' } } } }, mobileSessions: {} };
const read = key => key.split('/').reduce((o, p) => o?.[p], state) ?? null;
function write(key, value) { const parts = key.split('/'); let node = state; for (const p of parts.slice(0, -1)) node = node[p] ||= {}; node[parts.at(-1)] = value; }
const fake = `
export const initializeApp = options => ({options});
export const getAuth = () => ({currentUser:{uid:location.pathname.includes('harness')?'teacher':'phone',getIdToken:async()=> 'test-only-token'}});
export const getDatabase = () => ({});
export const browserSessionPersistence = {};
export const setPersistence = async () => {};
export const signInAnonymously = async () => ({});
export const ref = (db,path) => path;
const snapshot = value => ({val:()=>value,exists:()=>value!==null});
const rpc = async (action,path,value) => (await fetch('/fake',{method:'POST',body:JSON.stringify({action,path,value})})).json();
export const get = async path => snapshot(await rpc('get',path));
export const set = async (path,value) => rpc('set',path,value);
export const update = async (path,value) => rpc('update',path,value);
export const serverTimestamp = () => Date.now();
export function onValue(path,cb,error){let stopped=false,last=Symbol();const poll=async()=>{try{const v=await rpc('get',path);const json=JSON.stringify(v);if(!stopped&&json!==last){last=json;cb(snapshot(v));}}catch(e){error?.(e);}};poll();const timer=setInterval(poll,80);return()=>{stopped=true;clearInterval(timer);};}
export function onChildAdded(path,cb,error){const seen=new Set();return onValue(path,s=>Object.entries(s.val()||{}).forEach(([key,value])=>{if(!seen.has(key)){seen.add(key);cb(snapshot(value));}}),error);}
export const onDisconnect = () => ({set:async()=>{},update:async()=>{}});
export const runTransaction = async(path,fn)=>{const old=await rpc('get',path),value=fn(old);if(value===undefined)return {committed:false};await rpc('set',path,value);return {committed:true,snapshot:snapshot(value)};};
export const push = async(path,value)=>set(path+'/'+Math.random().toString(36).slice(2),value);
`;
const harness = `<!doctype html><html lang="es"><head><meta charset="utf-8"><link rel="stylesheet" href="mobile/mobile.css"></head><body><div id="panels" class="mobile-panels"></div><script src="vendor/qrcode.min.js"></script><script type="module">
import {createMobilePanel} from './mobile/teacher.js';import {initializeApp,getDatabase,getAuth} from './mobile/firebase.js';
const app=initializeApp({projectId:'test-project'});window.panel=createMobilePanel({app,db:getDatabase(app),auth:getAuth(app),container:document.getElementById('panels'),beep:()=>{},soundEnabled:()=>false});
window.panel.setContext({session:'TEST',active:true,clients:{student:{uid:'student',name:'Estudiante de prueba',state:'locked'}}});
window.closeEvaluation=()=>window.panel.setContext({session:'TEST',active:false,clients:{student:{uid:'student',name:'Estudiante de prueba',state:'locked'}}});
</script></body></html>`;
const server = http.createServer(async(req,res)=>{
  const pathname = new URL(req.url, 'http://local').pathname;
  if(pathname==='/fake'){
    let body='';for await(const part of req)body+=part;
    const {action,path:key,value}=JSON.parse(body);let result=null;
    if(action==='get')result=key==='.info/connected'?true:key==='.info/serverTimeOffset'?0:read(key);
    if(action==='set')write(key,value);
    if(action==='update')for(const [child,next] of Object.entries(value))write(key?key+'/'+child:child,next);
    res.setHeader('Content-Type','application/json');res.end(JSON.stringify(result));return;
  }
  if(pathname==='/mobile/firebase.js'){res.setHeader('Content-Type','text/javascript');res.end(fake);return;}
  if(pathname==='/harness.html'){res.setHeader('Content-Type','text/html');res.end(harness);return;}
  const target=path.resolve(root,'.'+pathname);
  if(!target.startsWith(root+path.sep)||!fs.existsSync(target)){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',target.endsWith('.js')?'text/javascript':target.endsWith('.css')?'text/css':'text/html');res.end(fs.readFileSync(target));
});
(async()=>{
  console.log('Starting browser media smoke test');
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const origin=`http://127.0.0.1:${server.address().port}`;
  const browser=await chromium.launch({headless:true,timeout:20000,...(process.env.BROWSER_PATH?{executablePath:process.env.BROWSER_PATH}:{}),args:['--use-fake-ui-for-media-stream','--use-fake-device-for-media-stream','--autoplay-policy=no-user-gesture-required']});
  try {
    const context=await browser.newContext({permissions:['camera'],acceptDownloads:true,viewport:{width:1050,height:850}});
    const errors=[];
    const teacher=await context.newPage(); teacher.on('pageerror',e=>errors.push(e.message));
    await teacher.goto(origin+'/harness.html');
    console.log('Teacher page loaded');
    await teacher.getByRole('button',{name:'Generar QR',exact:true}).click();
    await teacher.waitForSelector('.mobile-qr img');
    console.log('QR generated');
    await new Promise(r=>setTimeout(r,400));
    assert.equal(await teacher.locator('.mobile-qr img').count(),1,'QR must survive live updates');
    const phone=await context.newPage();phone.on('pageerror',e=>errors.push(e.message));
    const token=read('mobileSessions/TEST/links/student').token;
    await phone.goto(origin+'/celular.html#session=TEST&token='+token);
    await phone.locator('#consent').check();
    await phone.getByRole('button',{name:'Activar cámara y compartir'}).click();
    await phone.waitForSelector('#stopCamera:not([hidden])');
    console.log('Phone camera started');
    await teacher.getByRole('button',{name:'Ver cámara',exact:true}).click();
    await teacher.waitForFunction(()=>document.querySelector('.mobile-state')?.textContent.includes('Cámara en directo'),{},{timeout:15000});
    console.log('Real WebRTC connected');
    await teacher.waitForFunction(()=>{const s=document.querySelector('.mobile-panel video')?.srcObject;return !!s?.getVideoTracks?.().some(t=>t.readyState==='live');},{timeout:10000});
    const miniWidth=await teacher.locator('.mobile-panel').evaluate(el=>el.getBoundingClientRect().width);
    assert.ok(miniWidth<=220,'the default teacher camera card must stay a thumbnail');
    await teacher.getByRole('button',{name:'Ampliar imagen'}).click();
    assert.ok((await teacher.locator('.mobile-panel').evaluate(el=>el.getBoundingClientRect().width))>miniWidth,'the teacher can enlarge the live card');
    await teacher.getByRole('button',{name:'Reducir imagen'}).click();
    assert.ok((await teacher.locator('.mobile-panel').evaluate(el=>el.getBoundingClientRect().width))<=220,'the card returns to thumbnail size');
    // Keep the recording assertion deterministic: the preceding step verifies
    // the real mobile->teacher WebRTC path; this local camera track supplies
    // stable frames for MediaRecorder while the same teacher controls remain active.
    await teacher.evaluate(async()=>{const video=document.querySelector('.mobile-panel video');const canvas=document.createElement('canvas');canvas.width=640;canvas.height=480;const ctx=canvas.getContext('2d');let n=0;window.testFrames=setInterval(()=>{ctx.fillStyle=n++%2?'#067':'#a30';ctx.fillRect(0,0,640,480);},100);video.srcObject=canvas.captureStream(10);await video.play();});
    assert.equal(await teacher.locator('.mobile-clip').count(),0,'watching must not create a clip');
    assert.equal(await phone.locator('#recordingNotice').count(),0,'there is no recording-start alert on the phone');
    await teacher.getByRole('button',{name:'Grabar',exact:true}).click().catch(async e=>{console.log('Record disabled:',await teacher.locator('.mobile-state').textContent(),'message:',await teacher.locator('.mobile-message').textContent(),'status:',read('mobileSessions/TEST/status/pair-test'));throw e;});
    await teacher.waitForSelector('.mobile-recording:not([hidden])').catch(async e=>{console.log('Teacher:',await teacher.locator('.mobile-message').textContent(),'status:',read('mobileSessions/TEST/status/'+token));throw e;});
    assert.equal(await phone.locator('#recordingNotice').count(),0,'recording does not add an alert on the phone');
    await new Promise(r=>setTimeout(r,1500));
    await teacher.getByRole('button',{name:'Detener grabación',exact:true}).click();
    await teacher.getByRole('button',{name:'Descargar clip'}).waitFor({timeout:8000});
    const downloadEvent=teacher.waitForEvent('download');
    await teacher.getByRole('button',{name:'Descargar clip'}).click();
    const downloaded=await downloadEvent;
    assert.ok(fs.statSync(await downloaded.path()).size>0,'teacher download must contain video data');
    write('sessions/TEST/config/active',false);await teacher.evaluate(()=>window.closeEvaluation());
    await phone.waitForSelector('#stopCamera[hidden]', { state: 'attached' });
    assert.deepEqual(errors,[]);
    console.log('PASS browser: QR persistence, real WebRTC, resizable thumbnails, no automatic capture, quiet phone, manual local download and session close.');
  } finally {await browser.close();server.close();}
})().catch(e=>{console.error(e);server.close();process.exitCode=1;});

