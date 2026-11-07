# POC 01 — impresora virtual local

## Qué quedó construido

Un paquete MSIX de Print Support App v4 que declara una sola cola, `NPB - Prueba Virtual`, una extensión de impresión y una tarea que persiste el flujo recibido. `1.0.0.0` quedó registrada y no creó la cola: el nombre visible era un texto literal y faltaba la extensión de impresión. `1.0.1.0` conserva el nombre, el Publisher y el certificado. Con esa versión la cola quedó registrada.

La captura ya está demostrada: tres trabajos independientes desde Bloc de notas, documento OXPS, metadatos, cierre comunicado a Windows, cola despejada e impresora predeterminada conservada. La desinstalación del paquete y el evento 307 del servicio de impresión siguen abiertos. En Windows 11 el sideloading de un MSIX firmado no exige activar el modo de desarrollador.

## Compilación

Comando usado:

```powershell
dotnet msbuild src\Windows\VirtualPrinter\Neobash.PrintBridge.VirtualPrinter.csproj /restore /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64
```

Resultado: correcta. MSBuild 18.9.15 del SDK de .NET 10.

Advertencias que no impiden el paquete:

- `maxVersionTested` no es reconocido por el compilador del manifiesto de aplicación.
- No se encontró `mspdbcmf.exe`, así que no se generó un paquete de símbolos.

MSBuild de Visual Studio Build Tools no pudo resolver `Microsoft.NET.Sdk`. Por eso el empaquetado usa `dotnet msbuild`.

`1.0.0.0` se instaló con estado Ok y no creó la cola: `DisplayName` era un texto literal y faltaba `windows.printSupportExtension`. `1.0.1.0` corrige esos dos puntos, conserva la identidad y el certificado, y no se instala desde la compilación.

El MSIX generado y firmado es:

`src\Windows\VirtualPrinter\AppPackages\Neobash.PrintBridge.VirtualPrinter_1.0.1.0_x64_Test\Neobash.PrintBridge.VirtualPrinter_1.0.1.0_x64.msix`

El paquete incluye el runtime de Windows App SDK. La firma tiene como firmante `CN=Neobash Print Bridge POC`, thumbprint `70C7483FE4B6ECAC9FAD7CB72002C9AD18FFBA37`, el mismo de `1.0.0.0`. `Get-AuthenticodeSignature` puede informar que la cadena termina en una raíz no confiable; el sideloading usa el certificado público ya confiado en `Cert:\LocalMachine\TrustedPeople`. La compilación no vuelve a importarlo.

El manifiesto empaquetado contiene una sola impresora virtual. Su nombre es el recurso `ms-resource:PrintSupportVirtualPrinterDisplayName`, cuyo valor resuelto es `NPB - Prueba Virtual`. El formato es `application/oxps`, sin `OutputFileTypes`, con `runFullTrust`, `windows.printSupportExtension`, `WinRT.Host.dll` y el winmd de las dos tareas. El componente WinRT sigue dependiendo del runtime de .NET instalado en el equipo; el paquete no incluye `coreclr.dll` ni `hostfxr.dll`.

## Pruebas automatizadas

Comando:

```powershell
dotnet test tests\UnitTests\Neobash.PrintBridge.Tests.csproj
```

Resultado: 30 pruebas superadas, 0 errores, 0 omisiones.

Cubren el almacén inyectable, la ausencia de sobrescritura, tres capturas independientes, identificadores inseguros, metadatos, cancelación, el cierre único del trabajo, la validación del PrintTicket contra el PDC y el manifiesto empaquetado: el nombre es un recurso que resuelve a `NPB - Prueba Virtual`, `resources.pri` va dentro del MSIX, `printSupportExtension` está declarada, las clases WinRT existen en el winmd y el PDC referenciado está en el paquete.

La prueba de límite OpenXPS comprueba lo contrario de una validación: un buffer que empieza por `PK` se guarda como bytes opacos y los metadatos no afirman que el documento sea OpenXPS válido. Esta POC no incluye un validador OXPS.

Estas pruebas no hablan con el spooler. La entrega real de Windows quedó en la comprobación manual de abajo.

## Pruebas manuales

| Prueba | Qué comprobar | Estado |
| --- | --- | --- |
| A. Registro | `NPB - Prueba Virtual` aparece en Configuración, Impresoras y escáneres | Demostrada |
| B. Selector | La cola aparece en el diálogo de impresión | Demostrada |
| C. Recepción | Imprimir desde Bloc de notas crea un archivo real en la carpeta del paquete | Demostrada |
| D. Persistencia | El archivo tiene contenido, el formato declarado es `application/oxps` y `metadata.json` coincide | Demostrada |
| E. Tres trabajos | Tres impresiones crean tres directorios distintos | Demostrada |
| F. Limpieza | Desinstalar solo el paquete `Neobash.PrintBridge.VirtualPrinter` y comprobar que las demás impresoras siguen igual | Pendiente |
| G. Evento 307 | Lectura del evento 307 del servicio de impresión | Pendiente |

La impresora predeterminada no debe cambiar en ningún paso.

## Dependencias

- .NET 10.0.403
- Microsoft.WindowsAppSDK 1.8.260921001
- Microsoft.Windows.SDK.BuildTools 10.0.26100.9457
- Microsoft.Windows.CsWinRT 2.2.0
- Windows SDK 10.0.26100.0 para `signtool`

## Limitación principal

La captura local quedó demostrada. Siguen abiertos la desinstalación del paquete y el evento 307. La impresión física pertenece a A1 y no está aprobada. El modo de desarrollador no es un requisito previo. El procedimiento de instalación está en [installation.md](installation.md).
