# App Windows · Monitor Evaluaciones UTEC

Prototipo de navegador de examen liviano para Windows, construido con .NET 8 + WebView2.

## Qué hace esta primera versión

- recibe un código de sesión;
- lee `sessions/<sesion>/config` desde Firebase Realtime Database;
- abre la `homeUrl` configurada por el docente;
- permite únicamente la página inicial y las direcciones de `allowedSites`;
- soporta tres alcances: `exact`, `path` y `domain`;
- bloquea ventanas nuevas no autorizadas;
- vuelve a consultar la configuración cada 3 segundos, por lo que un sitio agregado o quitado por el docente se aplica durante la evaluación;
- deshabilita DevTools y el menú contextual de WebView2.

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

La app sólo necesita lectura pública de:

`/sessions/<sesion>/config`

No necesita credenciales docentes ni contiene contraseñas.

## Próximos pasos

1. integración del monitor del estudiante dentro de la app;
2. comandos remotos docente → estudiante (Inicio, Recargar, Recuperar, Desbloquear, Finalizar);
3. modo pantalla completa;
4. nivel de bloqueo reforzado opcional;
5. instalador/actualización automática.
