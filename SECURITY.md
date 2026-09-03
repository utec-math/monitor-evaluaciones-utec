# Seguridad y despliegue

## Principios

- El ejecutable y el código cliente nunca se consideran secretos.
- Ninguna contraseña, credencial de Google o permiso docente se incluye en la aplicación.
- Las autorizaciones se aplican en Firebase y en el receptor de Drive.
- La carpeta de grabaciones debe permanecer restringida a las cuentas docentes autorizadas.

## Activación inicial obligatoria

1. En Firebase Console, abrir **Authentication → Users** y copiar el UID del usuario docente.
2. En Realtime Database, crear `admins/<UID>` con el valor booleano `true` usando la consola administrativa.
3. Publicar `database.rules.json` únicamente después de confirmar que el UID quedó registrado.
4. En Apps Script, abrir **Project Settings → Script properties** y crear `FOLDER_ID` con el identificador de la carpeta privada de clips.
5. Volver a desplegar el Web App de Apps Script con la versión actualizada de `drive-receiver/Code.gs`.
6. Distribuir la aplicación 0.9. Las versiones anteriores no son compatibles con las reglas endurecidas.

## Comprobaciones antes de una evaluación

- La cuenta docente autorizada puede abrir el panel.
- Otra cuenta de Firebase no puede abrir sesiones ni leer datos.
- Una sesión nueva aparece como abierta antes de compartir su código.
- Un estudiante con la aplicación 0.9 puede conectarse, pero no leer otros clientes ni eventos.
- Al cerrar la sesión desde el panel, se rechazan nuevas conexiones y nuevas subidas.
- La carpeta de Drive continúa marcada como **Restringida**.

## Límites deliberados

La ofuscación puede dificultar el análisis del ejecutable, pero no constituye una barrera de seguridad. La protección se conserva aunque una persona conozca el código fuente, porque los permisos críticos se validan en el servidor.
