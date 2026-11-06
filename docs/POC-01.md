# POC 01 — impresora virtual local

## Qué quedó construido

Un paquete MSIX de Print Support App v4 que declara una sola cola, `NPB - Prueba Virtual`, y una tarea que persiste el flujo recibido. El paquete se compiló y se firmó con un certificado de prueba que no está en el almacén de confianza.

Windows todavía no tiene registrada la cola. La POC queda autorizada para iniciar la prueba de instalación, no para dar por válida la captura. En Windows 11 el sideloading de un MSIX firmado no exige activar el modo de desarrollador. La confianza pendiente es el certificado público en `Cert:\LocalMachine\TrustedPeople`, con elevación y aprobación explícita. Hasta esa instalación no hay evidencia de un trabajo real procedente de Bloc de notas u otra aplicación.

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

El MSIX generado y firmado es:

`src\Windows\VirtualPrinter\AppPackages\Neobash.PrintBridge.VirtualPrinter_1.0.0.0_x64_Test\Neobash.PrintBridge.VirtualPrinter_1.0.0.0_x64.msix`

Tamaño aproximado: 54 MB, porque incluye el runtime de Windows App SDK. La firma tiene como firmante `CN=Neobash Print Bridge POC`, thumbprint `70C7483FE4B6ECAC9FAD7CB72002C9AD18FFBA37`. `Get-AuthenticodeSignature` informa que la cadena termina en una raíz no confiable. El paquete no se volvió a compilar ni a firmar al corregir el procedimiento. La confianza usa el certificado público exportado desde esa firma, no el PFX, y el almacén previsto es `Cert:\LocalMachine\TrustedPeople`.

El manifiesto empaquetado contiene una sola impresora virtual, formato `application/oxps`, sin `OutputFileTypes`, con `runFullTrust`, `WinRT.Host.dll` y el winmd de la tarea.

## Pruebas automatizadas

Comando:

```powershell
dotnet test tests\UnitTests\Neobash.PrintBridge.Tests.csproj
```

Resultado: 15 pruebas superadas, 0 errores, 0 omisiones.

Cubren el almacén inyectable, la ausencia de sobrescritura, tres capturas independientes, identificadores inseguros, metadatos, cancelación, el cierre único del trabajo y la identidad estable del manifiesto.

La prueba de límite OpenXPS comprueba lo contrario de una validación: un buffer que empieza por `PK` se guarda como bytes opacos y los metadatos no afirman que el documento sea OpenXPS válido. Esta POC no incluye un validador OXPS.

Estas pruebas no hablan con el spooler y no demuestran que Windows haya entregado un trabajo.

## Pruebas manuales pendientes

No se ejecutó ninguna. No deben marcarse como aprobadas.

| Prueba | Qué comprobar | Estado |
| --- | --- | --- |
| A. Registro | `NPB - Prueba Virtual` aparece en Configuración, Impresoras y escáneres | Pendiente |
| B. Selector | La cola aparece en el diálogo de impresión | Pendiente |
| C. Recepción | Imprimir desde Bloc de notas crea un archivo real en la carpeta del paquete | Pendiente |
| D. Persistencia | El archivo tiene contenido, el formato declarado es `application/oxps` y `metadata.json` coincide | Pendiente |
| E. Tres trabajos | Tres impresiones crean tres directorios distintos | Pendiente |
| F. Limpieza | Desinstalar solo el paquete `Neobash.PrintBridge.VirtualPrinter` y comprobar que las demás impresoras siguen igual | Pendiente |

La impresora predeterminada no debe cambiar en ningún paso.

## Dependencias

- .NET 10.0.403
- Microsoft.WindowsAppSDK 1.8.260921001
- Microsoft.Windows.SDK.BuildTools 10.0.26100.9457
- Microsoft.Windows.CsWinRT 2.2.0
- Windows SDK 10.0.26100.0 para `signtool`

## Limitación principal

El criterio de éxito —que Windows reconozca la cola y que un documento convencional produzca una captura— sigue pendiente de la instalación autorizada y de las pruebas manuales. El modo de desarrollador no es un requisito previo. El procedimiento está en [installation.md](installation.md).
