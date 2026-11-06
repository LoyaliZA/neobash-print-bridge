# Hoja de ruta

## POC 01 — en curso

Impresora virtual local, captura OXPS y metadatos. El paquete está compilado y firmado. Falta la instalación autorizada del MSIX, sin modo de desarrollador por defecto, y las pruebas manuales con el spooler.

Fuera de esta fase:

- Comunicación entre equipos
- API central
- Autenticación
- Envío de documentos por la red
- Integración con GELIA NV
- Cola distribuida
- Interfaz completa
- Impresión física automática
- Conversión universal de formatos
- Windows anterior a 11 24H2

## Siguiente fase

Cuando la captura local quede demostrada con un documento real:

1. Hacer que el Agent lea el almacén mediante `IJobFileStore`, sin duplicar la lógica de guardado.
2. Definir cómo se publica una impresora física y cómo se representa una impresora remota como otra cola virtual.
3. Mantener la identidad de cada cola estable y separada de la cola de prueba `NPB - Prueba Virtual`.

La detección de impresoras físicas, el reenvío y la red no se implementan hasta cerrar esa demostración.
