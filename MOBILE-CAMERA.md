# Cámara móvil y grabación manual — implementación para prueba

## Qué cambia

La evaluación habitual conserva sus rutas, identidad Firebase, navegación, comandos y captura de pantalla de Windows. La cámara móvil es una función opcional. No se analiza la imagen automáticamente ni se captura audio. La cámara de Google Meet sigue independiente.

1. El docente genera el QR en la tarjeta de cámara móvil de un estudiante ya conectado.
2. En la aplicación Windows actualizada, el estudiante pulsa **Vincular celular**. También se puede compartir privadamente el enlace individual desde el panel sin actualizar la app antigua.
3. El QR dura 5 minutos y se reclama una sola vez. La página móvil usa una identidad anónima propia; nunca recibe el token de Windows ni credenciales docentes. Una recarga en la misma sesión de navegador puede reanudar el vínculo.
4. El teléfono explica el tratamiento de datos y solicita consentimiento y acceso a la cámara. El docente pulsa **Ver cámara**. El video usa WebRTC; Firebase transporta solamente señalización y eventos.
5. **Grabar** en el panel solicita primero que el móvil muestre su indicador y lo confirme. Solo entonces se inicia MediaRecorder sobre el video recibido. **Detener grabación** finaliza el clip y lo envía al backend privado. No hay búfer ni grabación anterior a ese clic.
6. El clip se cierra si se interrumpe la supervisión, el docente pierde conexión, vence su reserva de visualización o termina la evaluación. No se reanuda automáticamente. Límite de seguridad por clip: 15 min o aproximadamente 20 MB (25 MB de máximo en el servidor). Si se alcanza, se guarda lo recibido y se avisa; el docente puede iniciar otro clip.
7. Si falla la subida, el clip permanece en memoria del panel con **Reintentar**, **Descargar copia local** y **Descartar**. Cambiar de sesión o salir desde la interfaz se bloquea mientras haya grabaciones o clips pendientes. Cerrar forzosamente el navegador puede perder una copia no guardada. La copia descargada queda bajo custodia docente y no se borra automáticamente.

## Arquitectura y permisos

- `mobileSessions/<sesión>/pairs/<token>`: vínculo creado/reclamado por Functions; duración máxima 24 h desde su creación. Regenerar un QR revoca el anterior.
- `links/<uidWindows>`: invitación que únicamente puede leer esa app y los docentes.
- `status/<token>`: heartbeat de 5 s, visibilidad, foco, cámara y confirmación del aviso de grabación. A los 20 s sin señal, el panel alerta. No se afirma conocer qué aplicación abrió el alumno.
- `rtc/<token>`: oferta, respuesta, candidatos ICE y reserva exclusiva de un panel durante 25 s, renovada cada 5 s. Solo los participantes autorizados pueden leer la señalización. Los candidatos de intentos anteriores se ignoran.
- `events/<token>`: transiciones técnicas con hora del servidor, separadas de los eventos Windows. No se agregan eventos por cada heartbeat.
- `recordings` y `clips`: inicio manual confirmado y metadatos privados. El backend verifica Firebase Auth y `admins/<uid>` en cada subida/lectura. Ninguna URL pública persistente se almacena en RTDB.
- `mobileMembers`: índice escrito solo por servidor; permite al teléfono leer exclusivamente los indicadores de cierre de su evaluación/estudiante.

El video en directo es WebRTC entre móvil y docente (a través de TURN si la red lo requiere). Los clips usan un bucket privado dedicado. La visualización de un clip requiere cuenta docente y genera una URL firmada de hasta 5 minutos; tratala como un enlace sensible mientras esté vigente.

Al llegar a las 24 h del vínculo se rechazan lecturas móviles, nuevas subidas y reproducción de clips. Una función horaria elimina datos de cámara y objetos de almacenamiento: la eliminación física puede demorarse hasta una hora adicional, o más si el servicio falla. Monitorear esa función. No se modifica la retención de los clips de pantalla existentes en Drive.

## Despliegue controlado (NO ejecutado por esta implementación)

Primero probar en un proyecto Firebase de ensayo con cuentas y estudiantes ficticios. No cambiar la versión distribuida ni `main` hasta validar el flujo completo.

Requisitos nuevos:

- Firebase Functions de segunda generación, Cloud Scheduler y Storage con facturación habilitada. El tráfico de video también puede generar costos de TURN. No se contrató ni habilitó ningún servicio desde este cambio.
- Servidor TURN compatible con credenciales temporales HMAC (por ejemplo coturn con `use-auth-secret` y `static-auth-secret`). Configurar TLS y UDP/TCP según la red. No colocar el secreto compartido en archivos públicos ni RTDB.
- Bucket dedicado para clips, con acceso uniforme y **prevención de acceso público aplicada**. No reutilizar un bucket compartido con reglas desconocidas. La cuenta de servicio de Functions necesita administrar objetos de ese bucket y firmar URLs (`iam.serviceAccounts.signBlob`, normalmente mediante Service Account Token Creator sobre sí misma). Configurar un ciclo de vida como respaldo de la limpieza, según política institucional; revisar soft delete/versionado si se exige eliminación física en un plazo preciso.

Configuración:

1. Instalar dependencias con `npm ci` en la raíz y `npm install` en `functions/`. Node 22 para backend; Java 21 para emulador RTDB; .NET 8 para compilar Windows.
2. Definir el secreto con `firebase functions:secrets:set MOBILE_TURN_SECRET --project <proyecto>`.
3. Configurar los parámetros `MOBILE_TURN_URLS` (URLs separadas por coma) y `MOBILE_CLIP_BUCKET` (nombre del bucket privado) durante el despliegue. En `mobile-config.js` figura `us-central1`; mantener la región coherente con las funciones y la URL de subida.
4. En un ensayo alojado en otro origen, agregar su origen exacto a `allowedOrigins` en `functions/index.js`. Configurar `firebase-config.js` con el proyecto de ensayo. No poner credenciales administrativas allí.
5. Ejecutar `npm run test:mobile` y `npm run test:rules`. Compilar la app con el workflow Windows.
6. Desplegar las reglas RTDB y únicamente el codebase nuevo: `firebase deploy --only database,functions:mobile-camera --project <proyecto>`.
7. `storage.rules` documenta denegación de acceso directo; NO se agregó a `firebase.json` para evitar reemplazar reglas de otros usos del bucket existente. Si el bucket dedicado se incorpora a Firebase Storage, desplegar esa regla explícitamente sobre el destino dedicado. Antes de subir clips comprobar que accesos anónimos/directos fallan y que la URL firmada docente funciona.
8. Publicar los archivos web en el origen de ensayo. El QR se genera localmente con la copia MIT de QRCode.js; no se envía el enlace individual a un generador de QR externo.
9. Distribuir primero a un solo equipo el artefacto Windows de prueba. La versión/contrato 0.9 permanece deliberadamente compatible con el receptor de clips de pantalla existente, que valida exactamente esa versión. No reemplazar el ZIP estable en Drive ni publicar una release automáticamente.

## Prueba de aceptación con dos dispositivos reales

- Iniciar una evaluación habitual sin QR: comprobar conexión Windows, Moodle, comandos y clips de pantalla existentes.
- QR correcto, vencido, regenerado, reutilizado desde otro teléfono; recarga del teléfono autorizado.
- Chrome Android y Safari iPhone, cámara frontal/trasera, permisos denegados, pantalla bloqueada, cambio de aplicación, Wi-Fi cortado, cambio Wi-Fi/datos móviles. Incluir teléfonos en redes diferentes y una prueba que obligue a usar TURN.
- Ver que no se guarda ningún video al solo mirar. Grabar 10 s, detener, reproducir en el panel y comprobar identidad, tiempos y ausencia de audio. El móvil debe mostrar el aviso antes de que empiece la captura.
- Cortar red de docente y celular durante grabación; finalizar estudiante y sesión. Ver cierre del clip, aviso y ausencia de reinicio automático.
- Dos paneles intentando abrir la misma cámara: solo uno obtiene la reserva. Cerrar uno y comprobar que el otro puede abrirla después de vencer la reserva.
- Forzar error de subida: conservar clip, reintentar/descargar y evitar salida accidental. Verificar acceso no docente denegado y limpieza de clips/eventos vencidos con datos de ensayo.
- Escalar gradualmente el número de cámaras: cada cámara implica una conexión WebRTC, decodificación y ancho de banda en el equipo docente. No se declara capacidad de aula completa sin medirla.

## Recuperación

Estado estable: commit `07d5d76a804e1c7111dd2eaba74ecd198d84ca5d`, rama `backup/pre-mobile-camera-2026-09-27`.

El ZIP estable de Drive y la release v0.9.0 no se modifican. Esta implementación se mantiene en una rama separada y PR de revisión.

Si ya se probó en producción: detener y guardar cualquier grabación móvil; restaurar los archivos web y `database.rules.json` de la rama de respaldo y redistribuir la aplicación estable si correspondiera. Volver a desplegar esas reglas. Desactivar los endpoints móviles y el servicio TURN después de cerrar las sesiones; conservar la limpieza programada hasta eliminar los datos temporales según el plazo informado. Verificar una evaluación habitual. No restaurar una copia de datos de RTDB sobre datos de evaluaciones actuales: el respaldo de código no es una copia de la base de datos ni de la configuración de consola.
