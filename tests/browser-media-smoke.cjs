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
export const getFunctions = () => ({});
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
export const httpsCallable = (functions,name) => async data => ({data:await rpc('call',name,data)});
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
    if(action==='update')write(key,{...read(key),...value});
    if(action==='call'){
      if(key==='createMobilePair'){
        const pair={studentUid:'student',claimedBy:'',createdAt:Date.now(),inviteExpiresAt:Date.now()+300000,expiresAt:Date.now()+86400000,revoked:false};
        write('mobileSessions/TEST/pairs/pair-test',pair);write('mobileSessions/TEST/links/student',{token:'pair-test',inviteExpiresAt:pair.inviteExpiresAt});
        result={token:'pair-test',inviteExpiresAt:pair.inviteExpiresAt};
      }
      if(key==='claimMobilePair'){const pair=read('mobileSessions/TEST/pairs/pair-test');pair.claimedBy='phone';result={studentUid:'student',name:'Estudiante de prueba',expiresAt:pair.expiresAt};}
      if(key==='mobileIceServers')result={iceServers:[]};
    }
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
    const context=await browser.newContext({permissions:['camera'],viewport:{width:1050,height:850}});
    const errors=[];let uploaded=0;
    await context.route('https://us-central1-test-project.cloudfunctions.net/uploadMobileClip?**', async route=>{
      if(route.request().method()==='OPTIONS'){await route.fulfill({status:204,headers:{'access-control-allow-origin':'*','access-control-allow-headers':'authorization,content-type'}});return;}
      uploaded++; assert.ok(route.request().postDataBuffer().length>0);
      await route.fulfill({status:200,contentType:'application/json',body:'{"ok":true}',headers:{'access-control-allow-origin':'*'}});
    });
    const teacher=await context.newPage(); teacher.on('pageerror',e=>errors.push(e.message));
    await teacher.goto(origin+'/harness.html');
    console.log('Teacher page loaded');
    await teacher.getByRole('button',{name:'Generar QR',exact:true}).click();
    await teacher.waitForSelector('.mobile-qr img');
    console.log('QR generated');
    await new Promise(r=>setTimeout(r,400));
    assert.equal(await teacher.locator('.mobile-qr img').count(),1,'QR must survive live updates');
    const phone=await context.newPage();phone.on('pageerror',e=>errors.push(e.message));
    await phone.goto(origin+'/celular.html#session=TEST&token=pair-test');
    await phone.locator('#consent').check();
    await phone.getByRole('button',{name:'Activar cámara y compartir'}).click();
    await phone.waitForSelector('#stopCamera:not([hidden])');
    console.log('Phone camera started');
    await teacher.getByRole('button',{name:'Ver cámara',exact:true}).click();
    await teacher.waitForFunction(()=>document.querySelector('.mobile-state')?.textContent.includes('Cámara en directo'),{},{timeout:15000});
    console.log('Real WebRTC connected');
    await teacher.waitForFunction(()=>{const s=document.querySelector('.mobile-panel video')?.srcObject;return !!s?.getVideoTracks?.().some(t=>t.readyState==='live');},{timeout:10000});
    // Keep the recording assertion deterministic: the preceding step verifies
    // the real mobile->teacher WebRTC path; this local camera track supplies
    // stable frames for MediaRecorder while the same teacher controls remain active.
    await teacher.evaluate(async()=>{const video=document.querySelector('.mobile-panel video');const local=await navigator.mediaDevices.getUserMedia({audio:false,video:true});video.srcObject=local;await video.play();});
    assert.equal(uploaded,0,'watching must not create an upload');
    assert.equal(await phone.locator('#recordingNotice').isVisible(),false);
    await teacher.getByRole('button',{name:'Grabar',exact:true}).click().catch(async e=>{console.log('Record disabled:',await teacher.locator('.mobile-state').textContent(),'message:',await teacher.locator('.mobile-message').textContent(),'status:',read('mobileSessions/TEST/status/pair-test'));throw e;});
    await phone.waitForSelector('#recordingNotice:not([hidden])',{timeout:8000}).catch(async e=>{console.log('Phone:',await phone.locator('#mobileStatus').textContent(),'noticeHidden:',await phone.locator('#recordingNotice').getAttribute('hidden'),'phoneStatus:',read('mobileSessions/TEST/status/pair-test'),'recording:',read('mobileSessions/TEST/rtc/pair-test/recording'),'viewer:',read('mobileSessions/TEST/rtc/pair-test/viewer'));throw e;});
    await teacher.waitForSelector('.mobile-recording:not([hidden])').catch(async e=>{console.log('Teacher:',await teacher.locator('.mobile-message').textContent(),'status:',read('mobileSessions/TEST/status/pair-test'));throw e;});
    await new Promise(r=>setTimeout(r,1500));
    await teacher.getByRole('button',{name:'Detener grabación',exact:true}).click();
    await teacher.waitForFunction(()=>document.querySelector('.mobile-message')?.textContent.includes('Clip guardado'),{timeout:8000}).catch(async e=>{console.log('After stop:',await teacher.locator('.mobile-message').textContent(),'uploaded:',uploaded,'clips:',await teacher.locator('.mobile-clip').count());throw e;});
    assert.equal(uploaded,1);
    await phone.waitForSelector('#recordingNotice[hidden]', { state: 'attached' });
    await teacher.getByRole('button',{name:'Grabar',exact:true}).click();
    await teacher.waitForSelector('.mobile-recording:not([hidden])');
    await new Promise(r=>setTimeout(r,1000));
    write('sessions/TEST/config/active',false);await teacher.evaluate(()=>window.closeEvaluation());
    await phone.waitForSelector('#stopCamera[hidden]', { state: 'attached' });
    await teacher.waitForFunction(()=>document.querySelector('.mobile-message')?.textContent.includes('Clip guardado'));
    await new Promise(r=>setTimeout(r,300));
    assert.equal(uploaded,2,'closing the evaluation must finalize the manual clip');
    assert.deepEqual(errors,[]);
    console.log('PASS browser: QR persistence, real WebRTC, no automatic capture, recording indicator, manual clip upload and session-close finalization.');
  } finally {await browser.close();server.close();}
})().catch(e=>{console.error(e);server.close();process.exitCode=1;});
