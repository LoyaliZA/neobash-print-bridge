# Prueba física de A1

A1 no está aprobado. Esta prueba la inicia una persona desde la aplicación. La compilación no imprime ni instala componentes.

## Ejecutable

`src\Windows\Desktop\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\publish-a13\Neobash.PrintBridge.Desktop.exe`

Hay que conservar la carpeta `publish-a13` completa. El ejecutable no funciona solo, fuera de esas DLL. Cierra antes cualquier versión anterior de NPB.

## Dependencias

- Runtime compartido `Microsoft.NETCore.App` 10.0.0, x64. Lo declara `Neobash.PrintBridge.Desktop.runtimeconfig.json`. Para verlo sin instalar nada: `dotnet --list-runtimes`.
- Windows App SDK 1.8.260921001, dentro de la misma carpeta (`Microsoft.WindowsAppRuntime.dll`, `Microsoft.ui.xaml.dll`). No hace falta instalar el runtime de Windows App SDK aparte.
- `System.Drawing.Common.dll`, también dentro de la carpeta. Sirve para entregar las páginas al controlador por GDI.
- Windows 11 en el equipo anfitrión, con el controlador de la impresora ya instalado. NPB no lo instala ni lo cambia.

## Antes de abrir la aplicación

1. Anota el nombre de la impresora predeterminada en Configuración, Impresoras y escáneres.
2. Confirma que la Star BSC10 aparece ahí, con su controlador. Si no está, no elijas otra cola para dar la prueba por hecha.
3. Ten a mano un PDF que ya exista. NPB no crea documentos.

La prueba de «impresora desaparecida» ya está automatizada con un catálogo simulado (`Does_not_follow_a_different_queue_when_the_saved_one_is_missing`). No retires ni modifiques una cola real para repetirla.

## Pasos

1. Abre el ejecutable. La ventana no envía ningún trabajo al abrirse.
2. Elige la cola Star BSC10 y un papel que esa cola anuncie. Pulsa **Guardar destino**. El texto debe mostrar un identificador estable, la cola y el papel.
3. Si la cola guardada no está en el catálogo, el texto lo dice y no selecciona otra. No cambies la predeterminada para corregirlo.
4. Pulsa **Elegir PDF** y selecciona el PDF existente.
5. En **Impresión manual**, elige el mismo PDF que vas a imprimir desde el navegador. Para la comparación: una copia, todas las páginas o el mismo rango, orientación vertical, escala **Porcentaje** y escribe `90`. Ese 90 % es del tamaño físico del PDF, no un ajuste al papel seguido de otro recorte. **Tamaño real** y **Ajustar al área imprimible** son otras elecciones; no quedan impuestas.
6. Pulsa **Imprimir** una sola vez. NPB no muestra un diálogo para elegir impresora. Si el tamaño real o el porcentaje exceden el área imprimible, muestra un diálogo con la página, las dimensiones escaladas y disponibles en milímetros, y si se excede el ancho o el alto. Se compara toda la página, incluidos los espacios en blanco: esto no demuestra que se pierda contenido. No hay un segundo escalado silencioso.
   - **Cancelar** no envía nada; puedes cambiar la escala o elegir **Ajustar al área imprimible**.
   - **Imprimir con posible recorte** autoriza solamente ese envío, conserva la escala y recorta lo que quede fuera del área imprimible. La autorización no se guarda en el destino ni se hereda por trabajos futuros. El historial conserva el intento bloqueado sin envío y un nuevo identificador para el envío confirmado, con `AllowClippingConfirmed=true`.
   - Si no aparece la advertencia porque la página cabe, se envía directamente con las opciones elegidas.
7. Mira el historial. Debe mostrar las opciones usadas, incluido el 90 % si lo elegiste:
   - **Enviado al spooler** significa que el spooler aceptó el trabajo. No significa que haya salido papel.
   - **Error** significa que el envío no empezó o se rechazó antes.
   - **Incierto** significa que el envío llegó a empezar y el resultado no se conoce. No pulses Imprimir otra vez sobre ese mismo identificador: la aplicación crea un identificador nuevo en cada pulsación, y un resultado incierto no se reimprime solo.
8. Revisa la impresora. Compara esa hoja con la impresa desde el navegador al 90 %, en el mismo papel y la misma impresora. La calidad no queda corregida hasta hacer esa comparación.
9. Si hay papel, selecciona esa fila, escribe una nota de quien lo vio y pulsa **Anotar confirmación física**. Esa nota no imprime de nuevo.
10. Cierra la ventana. El proceso termina. Vuelve a mirar la impresora predeterminada y el resto de las colas: deben seguir como al principio. Las preferencias permanentes del controlador no deben haber cambiado.

Los destinos y el historial quedan en `%LOCALAPPDATA%\Neobash\PrintBridge\Desktop\`.

## Comprobación del aviso de recorte

Con un documento que exceda el área imprimible, prueba primero **Cancelar**: no debe salir papel. Vuelve a solicitar la impresión y confirma el posible recorte solo si deseas esa salida. Comprueba que conserva la escala y que el siguiente trabajo vuelve a pedir consentimiento. **Ajustar al área imprimible** debe encajar la página sin pedir permiso para recortarla. Estas comprobaciones físicas y el diálogo siguen pendientes hasta ejecutarlos desde la aplicación; las pruebas automatizadas no imprimen.

## Qué no cierra esta prueba

Ver papel en la Star BSC10 no aprueba otra impresora, otro formato, la captura OXPS, la red local, Android ni el acceso entre redes. Una impresora de hojas se prueba aparte, cuando haya un equipo disponible.

## A1.4: arranque

La ventana de A1.4 aparece antes de enumerar las colas y antes de leer el historial. Mientras tanto, cola, papel, guardar e imprimir permanecen deshabilitados. Si la cola guardada no está, no se elige otra. Un aviso de espera prolongada significa que el controlador todavía no ha respondido; no significa que la consulta se haya cancelado. Los pasos de papel de esta página no cambian y siguen pendientes hasta hacerlos desde la aplicación. Los tiempos de arranque se leen en [A1.4-rendimiento.md](A1.4-rendimiento.md). Esta nota no sustituye esa medición ni da la prueba física por hecha.

## A1.3: salida PDF al contexto de impresión

La salida utiliza `FPDF_RenderPage` de PDFium, en modo de impresión Windows/EMF. NPB ya no convierte obligatoriamente cada página completa en PNG. PDFium conserva comandos de dibujo cuando puede; transparencias, imágenes y otros efectos pueden requerir rasterización interna. Esto no garantiza por sí solo igualdad visual con un navegador.

Dependencia añadida: `bblanchon.PDFium.Win32` 157.0.8100; `pdfium.dll` se distribuye dentro de `publish-a13`, con avisos en `third-party/pdfium`. No se instala un servicio ni un controlador.

Se conserva exactamente el papel Receipt anunciado por el controlador, incluido su identificador. La altura virtual de unos tres metros no se usa para centrar el ticket ni para pintar un fondo blanco de esa longitud. El dibujo empieza en el origen imprimible tanto en ajuste como en tamaño real o porcentaje. El avance y corte físico siguen dependiendo del controlador y necesitan comprobarse en papel. No se cambia su configuración permanente ni se imponen dimensiones de ticket a impresoras de hojas.

Compara el mismo PDF a 90 % en navegador y NPB A1.3. Después, si deseas probar el modo de ajuste, realiza solo un envío y revisa dónde comienza el contenido y cuánto papel avanza. No declares corregida la nitidez o la longitud del recibo hasta observar ambos resultados.

Comprobaciones automatizadas sin impresora:

```powershell
dotnet test tests\UnitTests\Neobash.PrintBridge.Tests.csproj
dotnet test tests\WindowsPrintTests\Neobash.PrintBridge.WindowsPrintTests.csproj
```

El segundo proyecto prueba la clase nativa usada en producción contra un archivo EMF en memoria, con un PDF sintético. Comprueba comandos de dibujo, colocación al inicio, ausencia de un bitmap de página completa y rechazo de PDF o página inválidos. No abre una cola ni imprime.
