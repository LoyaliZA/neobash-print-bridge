# PR #6: auditoría local C1 en Windows

Revisión del 11 de noviembre de 2026, sobre `feature/c1-local-desktop`, commit remoto `052550b03d9d9a2994d9dbd6bf7c6f0cee94ac3e`, base `b04bd496ebd604015d1dab2cb14e5c826df7df3b`. Entorno real: Windows 11 Pro, compilación 26300, .NET SDK 10.0.403, x64. No se usó Linux.

Se actualizaron las referencias con `git fetch origin --prune` y se creó la rama local que sigue a `origin/feature/c1-local-desktop`. El árbol inicial estaba limpio; no hubo que guardar ni descartar cambios del usuario. Las correcciones se consolidan en esta rama para actualizar el PR #6; no se hace merge.

## Validación antes de modificar

- UnitTests: 161/161 correctas al ejecutarse con acceso al perfil Windows local. La primera ejecución dentro del aislamiento dio 145 correctas y 16 errores criptográficos de B1 al importar certificados. La misma suite pasó fuera del aislamiento, sin cambiar B1.
- WindowsPrintTests: 8/8 correctas en Windows; PDFium, EMF/GDI, preflight, vista previa WinRT y cola STA. No abren una impresora para enviar trabajos.
- Desktop: `dotnet build src/Windows/Desktop/Neobash.PrintBridge.Desktop.csproj -c Release -p:Platform=x64 -r win-x64`, correcta, cero errores. Advertencias NU1900 por acceso al índice de vulnerabilidades de NuGet desde el aislamiento.

## Defectos corregidos

1. **Papel temporal revertido por refrescos.** Inicio cambiaba el ComboBox, pero no `DesktopSession.SelectedPaper`. Al llegar una observación periódica, `BindSetup` volvía al papel guardado. Ahora el evento actualiza la sesión, el enlace usa una sola selección temporal y Cancelar restaura la selección persistida. Cambiar de cola limpia el papel anterior, incluso si ambas anuncian el mismo nombre. Guardar se deshabilita durante operaciones o mientras no estén disponibles el catálogo y el papel.
2. **Guardado parcial y excepción sin manejar.** Si fallaba `preferences.json` después de escribir `destinations.json`, la excepción escapaba al controlador de Inicio y quedaba una modificación parcial. Ahora se captura el error de persistencia y se restaura el destino anterior, o se retira el recién creado. Si también falla la restauración, se informa explícitamente que el guardado quedó incompleto. Se conserva el almacén existente de dos archivos; no se añade otra base de datos. Esto no constituye una transacción duradera frente a una caída del proceso entre las dos escrituras.
3. **Controlador validado solo para el destino activo.** Imprimir podía seleccionar otro destino guardado cuyo controlador hubiera cambiado y el dispatcher no comparaba `DriverName`. Ahora Desktop evalúa el destino seleccionado, exige un estado Ready y el dispatcher rechaza un controlador distinto o no identificado cuando el destino guardó su identidad. Se rechaza antes de preparar, registrar o enviar el trabajo. Los destinos anteriores que no guardaron DriverName siguen siendo compatibles; su identidad se registra al confirmar la selección local.

Las regresiones previas a la corrección demostraron tres fallos (selección cruzada y dos variantes de error de persistencia) y un envío al puerto simulado con controlador cambiado. Nunca se imprimió para demostrarlos. Se añadieron siete casos en total, incluidos controlador vacío, cancelar borrador y evaluación independiente del destino seleccionado.

## POS80 real y papeles anunciados

Cola instalada: `POS80 Printer`. Controlador: `POS80`. Puerto: `USB008`. La consulta de capacidades de producción y la lista de Inicio coincidieron:

- `User Defined`: RawKind 256; 283 × 12898 centésimas de pulgada.
- `72.00x210.00(mm)`: RawKind 257; 283 × 827 centésimas de pulgada.
- `72.00x297.00(mm)`: RawKind 258; 283 × 1169 centésimas de pulgada.
- `72.00x3276.00(mm)`: RawKind 259; 283 × 12898 centésimas de pulgada.

Son nombres y medidas reportados por el controlador, con su redondeo. `User Defined` también viene del controlador; no es un formulario creado por NPB. El papel predeterminado del controlador consultado fue `72.00x3276.00(mm)`. No se dedujo un ancho de 80 mm por el nombre POS80.

La instalación ya tenía un destino de NPB para `72.00x210.00(mm)`, una copia, vertical y 90 % del tamaño físico del PDF. Se abrió con esos valores existentes. Durante la auditoría de interfaz solo se cambiaron borradores y se cancelaron; no se guardó una nueva preferencia del usuario.

En el ejecutable publicado se eligió temporalmente `72.00x297.00(mm)` y se comprobó visualmente que siguiera seleccionado tras varios ciclos de observación de estado de 15 segundos. El destino en disco permaneció en `72.00x210.00(mm)`. Al cancelar volvió a mostrarse la selección persistida. La publicación queda abierta en Inicio para la prueba manual.

No se añadieron formularios ni se ejecutaron SetPrinter, SetPrintConfiguration, cambios de impresora predeterminada o Print. La consulta nativa solo lee capacidades. El adaptador de envío conserva la selección explícita por nombre, `IsValid`, la búsqueda exacta del PaperSize anunciado y su rechazo si desaparece. No hay búsqueda de una impresora sustituta ni un PaperSize construido por la aplicación. Las pruebas de cola ausente y papel no anunciado pasan. No se retiraron ni cambiaron colas reales para simular errores.

## Arranque y estados de error

Se conserva la activación de la ventana antes de `BeginStartup`. El catálogo usa tareas en segundo plano y el acceso al controlador conserva su cola STA; el historial y la recuperación siguen fuera del hilo de interfaz. No se añadió enumeración síncrona en Inicio.

En la publicación final, lanzada a las 15:47:47 de Ciudad de México:

- Inicio de proceso a entrada administrada: 246 ms.
- Solicitud de activación: 362 ms desde entrada administrada; evento Activated: 372 ms.
- Callback de interfaz normal: 408 ms; prioridad baja: 693 ms, desde entrada administrada.
- Enumeración nativa: 20 ms, ocho nombres; aplicación del catálogo: 443 ms desde entrada administrada.
- Capacidades POS80: 57 ms; recuperación: 59 ms, 41 identificadores; lectura de historial: 189 ms.

Son operaciones con bases de tiempo diferentes: no se suman ni representan el primer píxel. Un solo arranque publicado no es un percentil ni una comparación de rendimiento controlada. El primer arranque de la rama sin corregir midió 495 ms para el callback normal y 276 ms para capacidades POS80; influyen las cachés del sistema.

Las pruebas cubren catálogo pendiente/fallido, capacidades pendientes/ilegibles, cola ausente, controlador cambiado, papel retirado, conservación de preferencias, rechazo sin cambiar de cola y fallo al persistir. Los cambios de hardware se simularon en pruebas; no se provocaron en la POS80 real.

## Resultado final y evidencias

- UnitTests: **168/168**, cero omitidas.
- WindowsPrintTests: **8/8**, cero omitidas.
- Publicación Release x64: correcta y abierta en Windows, con DLL de WinUI, PDFium y avisos de terceros presentes.
- `git diff --check`: correcto.

En una ejecución intermedia del subconjunto se observó un fallo de limpieza de `DesktopCoordinationTests` por un archivo aún abierto durante `Dispose`, asociado al trabajo de fondo de la prueba. La ejecución completa posterior pasó. No se ocultó ni se deshabilitó esa prueba.

Comandos finales:

```powershell
dotnet test tests/UnitTests/Neobash.PrintBridge.Tests.csproj --no-restore --logger 'trx;LogFileName=c1-unit-final.trx'
dotnet test tests/WindowsPrintTests/Neobash.PrintBridge.WindowsPrintTests.csproj --no-restore --logger 'trx;LogFileName=c1-windows-final.trx'
dotnet publish src/Windows/Desktop/Neobash.PrintBridge.Desktop.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained false -o artifacts/C1-Desktop-win-x64
```

Evidencias locales ignoradas por Git: `artifacts/c1-audit/` contiene logs de regresiones, pruebas y publicación, y `pos80-driver.json` con la consulta nativa. Los TRX están en `tests/UnitTests/TestResults/` y `tests/WindowsPrintTests/TestResults/`. Los tiempos proceden de `%LOCALAPPDATA%\Neobash\PrintBridge\Desktop\diagnostic.log`.

## Impresión física confirmada y consolidación del 12 de noviembre

El usuario confirmó una impresión física exitosa con `POS80 Printer` desde Desktop.
Es una confirmación del usuario; no se midieron nitidez, escala, avance, corte, tiempos,
ni una comparación controlada con el navegador. No se enviaron trabajos físicos adicionales.
La confirmación corresponde al Desktop anterior, no certifica el nuevo ZIP autocontenido.

Se contrastó el árbol local con el PR #6 y `origin/feature/c1-local-desktop`, ambos en
`052550b03d9d9a2994d9dbd6bf7c6f0cee94ac3e`: las siete modificaciones locales no estaban
incluidas. Se conservan selección temporal, cancelación, compensación del guardado parcial
y validación del controlador para el destino seleccionado y en el dispatcher.

Revalidación local Windows 11 x64:

- UnitTests: 168/168, cero omitidas, con acceso al perfil Windows. TRX `c1-consolidated-profile.trx`.
  Dentro del aislamiento: 152 correctas y 16 errores criptográficos de B1; no se cambió B1.
- WindowsPrintTests: 8/8, cero omitidas; TRX `c1-consolidated.trx`. No envían trabajos físicos.
- Desktop WinUI Release x64: correcto, cero errores y cero advertencias en la compilación.
- Publicación con .NET 10 self-contained y Windows App SDK self-contained: correcta.
  La publicación anterior `C1-Desktop-win-x64` era dependiente de .NET instalado;
  el nuevo ZIP se genera mediante `scripts/c1/Publish-Portable.ps1`.

Se conserva PrintEngine, PageLayoutCalculator, JobRecovery, la geometría PDFium/GDI,
el límite de memoria y Uncertain sin reimpresión. El guard de controlador en PrintDispatcher
es la corrección auditada; no se modifica el motor de impresión. No se incorpora Agent ni C2/C3.

Ver `C1-portable.md` para requisitos, reproducción, CI, descarga y límites de confianza.
La prueba en Windows 11 limpio, sin SDK/runtime externo y sin Internet sigue pendiente
hasta disponer de ese entorno. Este equipo de desarrollo sí tiene SDK y runtimes instalados.
El ZIP no tiene firma pública; no se certifica compatibilidad con Smart App Control.
