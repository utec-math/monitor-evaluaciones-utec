const FIREBASE_DB = 'https://preciencia1-default-rtdb.firebaseio.com';

function doPost(e) {
  try {
    const body = JSON.parse(e.postData && e.postData.contents || '{}');
    const idToken = String(body.idToken || '');
    const clientUid = cleanKey(body.clientUid, 160);
    const session = cleanKey(body.session, 60);
    const studentId = cleanKey(body.studentId, 80);
    const fileName = cleanFileName(body.fileName || 'clip.avi');
    const contentType = String(body.contentType || 'video/x-msvideo');
    const data = String(body.dataBase64 || '');
    const triggeredAt = Number(body.triggeredAt || Date.now());
    const reason = String(body.reason || 'evento').slice(0, 250);
    const detail = String(body.detail || '').slice(0, 1000);

    if (!idToken || !clientUid || !session || !studentId || !data) return json({ok:false,error:'Solicitud incompleta'});
    if (data.length > 45 * 1024 * 1024) return json({ok:false,error:'Clip demasiado grande para este receptor'});

    // Esta lectura es también la validación del token: las reglas de Realtime Database
    // sólo permiten que una app anónima lea su propio registro de cliente.
    const client = readClient(session, clientUid, idToken);
    if (!client || client.uid !== clientUid || client.id !== studentId) {
      return json({ok:false,error:'La app no está registrada para esta sesión/estudiante'});
    }
    if (client.app !== 'windows-webview2' || String(client.version || '') !== '0.9') {
      return json({ok:false,error:'Versión de la aplicación no autorizada'});
    }
    if (Date.now() - Number(client.lastSeen || 0) > 120000) {
      return json({ok:false,error:'La conexión de la aplicación no está activa'});
    }
    const config = readConfig(session, idToken);
    if (!config || config.active !== true) return json({ok:false,error:'La sesión está cerrada'});

    const folderId = PropertiesService.getScriptProperties().getProperty('FOLDER_ID');
    if (!folderId) return json({ok:false,error:'El receptor no tiene configurada la carpeta de destino'});
    const root = DriveApp.getFolderById(folderId);
    const sessionFolder = getOrCreateFolder(root, session);
    const studentFolder = getOrCreateFolder(sessionFolder, studentId);
    const bytes = Utilities.base64Decode(data);
    const blob = Utilities.newBlob(bytes, contentType, fileName);
    const file = studentFolder.createFile(blob);
    file.setDescription('Monitor Evaluaciones UTEC\nEvento: ' + reason + '\nDetalle: ' + detail + '\nHora: ' + new Date(triggeredAt).toISOString());

    return json({
      ok: true,
      fileId: file.getId(),
      fileName: file.getName(),
      webViewLink: 'https://drive.google.com/file/d/' + file.getId() + '/view',
      triggeredAt: triggeredAt
    });
  } catch (err) {
    return json({ok:false,error:String(err && err.message || err)});
  }
}

function readClient(session, clientUid, idToken) {
  const url = FIREBASE_DB + '/sessions/' + encodeURIComponent(session) + '/clients/' + encodeURIComponent(clientUid) + '.json?auth=' + encodeURIComponent(idToken);
  const res = UrlFetchApp.fetch(url, {muteHttpExceptions:true});
  if (res.getResponseCode() !== 200) return null;
  return JSON.parse(res.getContentText() || 'null');
}

function readConfig(session, idToken) {
  const url = FIREBASE_DB + '/sessions/' + encodeURIComponent(session) + '/config.json?auth=' + encodeURIComponent(idToken);
  const res = UrlFetchApp.fetch(url, {muteHttpExceptions:true});
  if (res.getResponseCode() !== 200) return null;
  return JSON.parse(res.getContentText() || 'null');
}

function getOrCreateFolder(parent, name) {
  const it = parent.getFoldersByName(name);
  return it.hasNext() ? it.next() : parent.createFolder(name);
}

function cleanKey(value, max) {
  return String(value || '').replace(/[^A-Za-z0-9_-]/g, '-').slice(0, max);
}

function cleanFileName(value) {
  return String(value || 'clip.avi').replace(/[\\/:*?\"<>|]/g, '_').slice(0, 120);
}

function json(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj))
    .setMimeType(ContentService.MimeType.JSON);
}
