# App Windows · Monitor Evaluaciones UTEC v0.9

Navegador de evaluación para Windows, construido con .NET 8 + WebView2.

La cámara queda exclusivamente a cargo de Google Meet. La aplicación no solicita acceso a la webcam: registra presencia, navegación y clips de pantalla vinculados a eventos.

La versión 0.9 usa una identidad anónima propia, inmutable durante la sesión. Firebase vincula presencia, comandos y eventos a ese UID y rechaza escrituras que intenten atribuirse a otro cliente.

## Qué hace esta versión

- recibe un código de sesión;
- lee `sessions/<sesion>/config` desde Firebase Realtime Database;
- abre la `homeUrl` configurada por el docente;
- permite únicamente la página inicial y las direcciones de `allowedSites`;
- soporta tres alcances: `exact`, `path` y `domain`;
- bloquea ventanas nuevas no autorizadas;
- vuelve a consultar la configuración periódicamente, por lo que un sitio agregado o quitado por el docente se aplica durante la evaluación;
- deshabilita DevTools y el menú contextual de WebView2.
- muestra al estudiante una vista mínima con identidad y estado de conexión;
- mantiene los estados técnicos y los enlaces a clips en el panel docente.

Esta versión **todavía no bloquea Windows** (Alt+Tab, menú Inicio, otras aplicaciones, etc.). Ese será un nivel posterior.

## Ejecutar desde código

Requiere Windows y .NET 8 SDK.

```powershell
dotnet run --project .\windows-app\MonitorEvaluaciones.App -- --session=EVAL-ABC123
```

También puede iniciarse sin parámetro y escribir el código de sesión en la barra superior.

## Compilar

```powershell
dotnet publish .\windows-app\MonitorEvaluaciones.App\MonitorEvaluaciones.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

El ejecutable queda dentro de `bin/Release/net8.0-windows/win-x64/publish/`.

## Configuración Firebase utilizada

La aplicación lee la configuración pública de:

`/sessions/<sesion>/config`

También usa autenticación anónima de Firebase para registrar presencia, comandos y eventos. No contiene credenciales docentes ni contraseñas. La sesión debe existir y estar abierta por un administrador antes de que un estudiante pueda conectarse.

## Posibles próximos pasos

1. modo pantalla completa;
2. nivel de bloqueo reforzado opcional;
3. instalador y actualización automática.
