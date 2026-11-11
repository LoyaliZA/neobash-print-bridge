# Neobash Print Bridge

Neobash Print Bridge comparte impresoras entre equipos. Esta primera prueba solo registra una impresora virtual local en Windows 11 y guarda los trabajos que el sistema le entrega.

La cola se llama `NPB - Prueba Virtual`. Es una cola real del administrador de impresión, registrada por una Print Support App v4 dentro de un paquete MSIX. No es una ventana que simule una impresora.

## Requisitos

- Windows 11 24H2 o posterior, compilación 26100.
- .NET SDK 10.
- Windows SDK 10.0.26100, para empaquetar y firmar.
- El paquete incluye el runtime de Windows App SDK, así que no hace falta instalarlo aparte.

El proyecto de Windows se compila con MSBuild del SDK de .NET (`dotnet msbuild`). MSBuild de Visual Studio Build Tools no resuelve el SDK de .NET 10 en este equipo. No se usan componentes C++.

## Compilar y probar

Desde la raíz del repositorio:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\poc\build.ps1
```

Eso ejecuta las pruebas del almacén, genera el MSIX y lo firma. También exporta el certificado público a `local-certs\npb-poc.cer`. No lo importa en ningún almacén.

El paquete firmado aparece en:

`src\Windows\VirtualPrinter\AppPackages\Neobash.PrintBridge.VirtualPrinter_1.0.1.0_x64_Test\`

## Instalación

La instalación sideload confía el certificado público en el equipo y registra el paquete. No exige el modo de desarrollador. No forma parte de la compilación. Los pasos están en [docs/installation.md](docs/installation.md) y solo deben ejecutarse tras autorizarlos.

## Documentación

- [docs/architecture.md](docs/architecture.md)
- [docs/POC-01.md](docs/POC-01.md)
- [docs/installation.md](docs/installation.md)
- [docs/roadmap.md](docs/roadmap.md)

## B1: puente LAN PDF

La escucha predeterminada es local; para dos PCs hay que seleccionar una IP LAN
privada explicitamente. El envio usa `send --file` (`--pdf` sigue como alias).

- [Auditoria correctiva Windows del PR #5](docs/B1-auditoria-correctiva.md)
- [Prueba LAN entre dos Windows](docs/B1-prueba-LAN-Windows.md)
