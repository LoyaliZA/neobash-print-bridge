# Desktop C1 portátil para pruebas

Extraer todo `NeobashPrintBridge-C1-win-x64-portable.zip` en una carpeta local y abrir
`Neobash.PrintBridge.Desktop.exe`. Conservar todas las subcarpetas y DLL.
No ejecutar dentro del ZIP. No se instala MSIX ni Agent.

Requisitos del sistema: Windows 11 x64 24H2 (build 26100) o posterior, sesión gráfica,
servicio de impresión y controlador instalado para la impresora que se quiera usar.
El ZIP incluye .NET 10, Windows App SDK 1.8, PDFium, CRT de Visual C++ x64 de
la carpeta redistribuible de Visual Studio y recursos/avisos de terceros.
El equipo final no necesita .NET SDK, Visual Studio, runtime .NET externo ni
Internet para el flujo local C1. El sistema aporta WinRT, GDI y Universal CRT.
No incluye controladores de impresoras. No se usa WebView2 en el flujo C1.

Es una distribución de prueba **sin firma pública**. No se garantiza su ejecución
en equipos con Smart App Control, WDAC, AppLocker u otras políticas de confianza.
No desactivar esas protecciones ni instalar certificados autofirmados para abrirla.
La distribución confiable en esos equipos queda pendiente de firma pública y validación.

Los datos permanecen en `%LOCALAPPDATA%\Neobash\PrintBridge`; la aplicación portátil
reutiliza la configuración del usuario. No es un modo de datos aislados en el ZIP.
El catálogo y la configuración solo leen los papeles anunciados por cada controlador.
Para comprobar sin imprimir: abrir Inicio, esperar catálogo, revisar cola y papeles,
cancelar cambios de configuración, abrir Imprimir y elegir un PDF local para vista previa.
No pulsar Imprimir sin autorización para enviar un trabajo físico.

## Reproducir la publicación

En Windows con .NET SDK 10.0.403, herramientas WinUI/Windows SDK y el componente
redistribuible VC++ x64 de Visual Studio, ejecutar desde PowerShell:

```powershell
./scripts/c1/Publish-Portable.ps1
```

La publicación usa explícitamente `--self-contained true`,
`PublishSelfContained=true`, `WindowsAppSDKSelfContained=true`, sin single-file ni
trimming. El script conserva el árbol completo, comprueba dependencias esenciales,
rechaza un runtimeconfig dependiente de .NET externo y crea el ZIP en `artifacts/`.
`build-info.json` registra commit, SDK, runtime y CRT; `SHA256SUMS.txt` permite
comprobar el contenido. Las actualizaciones de estos runtimes requieren republicar.

La modalidad app-local del CRT se basa en la
[documentación de Microsoft](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files).
La publicación de Windows App SDK sigue la
[guía self-contained](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps).

## GitHub Actions y MSIX

`C1 Windows` compila Desktop Release x64, ejecuta 167 casos unitarios y los 8
WindowsPrintTests sin impresora física, publica y adjunta el ZIP. Otro job genera
el MSIX existente sin firmarlo ni instalarlo y ejecuta el caso binario restante:
168 casos unitarios entre ambos jobs. No hay `continue-on-error` ni omisión silenciosa
del contrato MSIX. El MSIX es validación del componente previo; no forma parte del ZIP C1.
Estas pruebas no certifican salida en papel ni interacción de escritorio en CI.

En el PR #6 abrir Checks → C1 Windows → Details, o Actions → C1 Windows → ejecución
del commit deseado. Al terminar correctamente, descargar en Artifacts
`NeobashPrintBridge-C1-win-x64-portable`. El archivo descargado por GitHub contiene
el ZIP de distribución. Los TRX se publican como artefactos separados incluso si fallan pruebas.

La prueba en un Windows 11 limpio, sin SDK/runtime .NET y desconectado debe confirmar
apertura, catálogo, selección/cancelación y guardado de configuración, vista previa,
historial y funcionamiento local. La ejecución en el equipo de desarrollo no sustituye
esa validación. Consultar `C1-auditoria-local-Windows.md` para resultados observados.
