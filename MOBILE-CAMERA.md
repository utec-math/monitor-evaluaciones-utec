# Cámara móvil opcional — piloto sin facturación

La evaluación habitual conserva la aplicación Windows, la vista docente, sus comandos y los clips de pantalla existentes. La cámara móvil se muestra en miniaturas que el docente puede ampliar. No analiza imágenes, no captura audio y no graba hasta que el docente pulsa **Grabar**. El estudiante acepta antes de compartir la cámara que el docente puede grabar fragmentos durante la evaluación sin avisos adicionales en el celular.

## Qué utiliza

- Páginas estáticas HTTPS para docente y celular. Pueden alojarse en GitHub Pages o Firebase Hosting dentro de sus límites gratuitos.
- Firebase Authentication anónima para el celular y la Realtime Database ya usada por el Monitor. Las reglas permiten crear un QR individual de 5 minutos, reclamarlo una sola vez y reanudarlo en la misma identidad móvil durante la evaluación.
- WebRTC con conexión directa y un servidor STUN público para descubrir la ruta. No hay TURN ni retransmisión de video por Firebase. Si las redes bloquean la conexión directa, el docente recibe una alerta y puede usar la segunda cámara de Meet como alternativa. **No se garantiza video en todas las redes.**
- `MediaRecorder` en el navegador docente. Al detener una grabación, el clip queda en memoria hasta que el docente pulsa **Descargar clip** o **Descartar**. No se sube a Cloud Storage ni a Drive. Cerrar forzosamente Chrome antes de descargarlo puede perderlo.
- Realtime Database almacena solo estado, señalización y eventos con hora. La señalización WebRTC puede contener direcciones IP de los participantes. Nunca recibe fotogramas ni clips móviles.

No se despliegan Cloud Functions, Cloud Scheduler, Storage ni un servidor TURN. Por lo tanto, este piloto puede ejecutarse en un proyecto Firebase **Spark**, sin asociar una tarjeta. Los límites gratuitos de RTDB corresponden a todo el proyecto, incluido el Monitor actual; una evaluación grande puede alcanzar el máximo de conexiones o descargas. Si el proyecto existente ya está en Blaze, este código por sí solo no impide cargos por superar sus cuotas.

«Sin facturación» no significa que toda la plataforma sea software libre: Firebase, GitHub Pages, Chrome y Meet son servicios/productos externos. El código propio y coturn pueden ser abiertos, pero coturn no se usa en este piloto.

## Flujo y privacidad

1. El docente abre la sesión y, cuando el estudiante aparece conectado, pulsa **Generar QR**. También puede compartir su enlace individual de forma privada.
2. El celular abre `celular.html`, reclama el vínculo con una cuenta anónima y pide permiso para la cámara. El QR vence a los 5 minutos; regenerarlo revoca el anterior.
3. El celular informa visibilidad, foco, estado de cámara y una señal cada 5 segundos. A los 20 segundos sin señal el panel alerta. Esto no identifica qué otra aplicación abrió el estudiante.
4. **Ver cámara** abre una conexión directa. Solo un panel docente puede reservar esa cámara a la vez. Si no conecta en 20 segundos, se informa el fallo.
5. **Grabar** espera una confirmación técnica de que el celular sigue conectado y con la cámara activa; no muestra un aviso nuevo al estudiante. **Detener grabación** genera un clip local. Se cierra al cortar supervisión o terminar evaluación; no se reanuda automáticamente. Límite: 15 minutos o aproximadamente 20 MB por clip.
6. El panel intenta borrar los datos móviles de RTDB cuando una evaluación queda cerrada y el docente mantiene o vuelve a abrir la vista. Sin un servidor programado no se puede garantizar borrado físico automático si nadie vuelve al panel. El acceso móvil vence a las 24 horas; el docente debe revisar y borrar sesiones abandonadas. Los archivos descargados quedan bajo custodia docente según la política institucional.

## Para probar con dos dispositivos

La versión web está publicada en GitHub Pages. Para la prueba, usá una sesión y un estudiante de ensayo. El celular necesita abrir el enlace por HTTPS.

1. Abrir `docente.html` en Chrome, iniciar sesión, abrir una evaluación de ensayo y conectar la aplicación Windows de un estudiante de prueba.
2. En la tarjeta de ese estudiante, pulsar **Generar QR**. Escanearlo con Android Chrome o iPhone Safari antes de que venza, leer y aceptar las condiciones y permitir la cámara.
3. Pulsar **Ver cámara** en el panel. Verificar la imagen en miniatura, el botón **Ampliar imagen**, el cambio de aplicación, la pantalla bloqueada y la pérdida de conexión.
4. Verificar que mirar no crea un clip. Pulsar **Grabar**, comprobar que no aparece un aviso nuevo en el teléfono, esperar 10 segundos, detener y descargar el archivo. Revisar que contenga imagen y no audio. Probar también la red de datos móviles; puede fallar sin TURN.
5. Cerrar la sesión y comprobar que los datos móviles se eliminan de RTDB. Revisar manualmente sesiones abandonadas y los clips locales.

Las pruebas de código se ejecutan con `npm run test:mobile` y `npm run test:rules`; el segundo comando usa el emulador de RTDB y Java 21. La prueba de navegador `tests/browser-media-smoke.cjs` requiere Playwright y todavía se mantiene separada de CI porque debe verificarse junto con dispositivos reales.

## Recuperación

La versión previa a la cámara móvil está en `07d5d76a804e1c7111dd2eaba74ecd198d84ca5d` y en `backup/pre-mobile-camera-2026-09-27`. Para revertir el sitio se puede restaurar el código anterior desde esa rama, sin sobrescribir los datos actuales de RTDB con una copia antigua.

