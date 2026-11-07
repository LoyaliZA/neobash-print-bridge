# Prueba física de A1

A1 no está aprobado. Esta prueba la inicia una persona desde la aplicación. La compilación no imprime ni instala componentes.

## Ejecutable

`src\Windows\Desktop\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\publish\Neobash.PrintBridge.Desktop.exe`

Hay que conservar la carpeta `publish` completa. El ejecutable no funciona solo, fuera de esas DLL.

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
5. Pulsa **Imprimir** una sola vez. NPB no muestra un diálogo para elegir impresora.
6. Mira el historial:
   - **Enviado al spooler** significa que el spooler aceptó el trabajo. No significa que haya salido papel.
   - **Error** significa que el envío no empezó o se rechazó antes.
   - **Incierto** significa que el envío llegó a empezar y el resultado no se conoce. No pulses Imprimir otra vez sobre ese mismo identificador: la aplicación crea un identificador nuevo en cada pulsación, y un resultado incierto no se reimprime solo.
7. Revisa la impresora. Si hay papel, selecciona esa fila, escribe una nota de quien lo vio y pulsa **Anotar confirmación física**. Esa nota no imprime de nuevo.
8. Cierra la ventana. El proceso termina. Vuelve a mirar la impresora predeterminada y el resto de las colas: deben seguir como al principio.

Los destinos y el historial quedan en `%LOCALAPPDATA%\Neobash\PrintBridge\Desktop\`.

## Qué no cierra esta prueba

Ver papel en la Star BSC10 no aprueba otra impresora, otro formato, la captura OXPS, la red local, Android ni el acceso entre redes. Una impresora de hojas se prueba aparte, cuando haya un equipo disponible.
