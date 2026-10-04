# Créditos de assets

El **código** es MIT; el **contenido de datos** (`data/`) es CC BY 4.0 y de creación propia.

## Recursos incluidos en el repositorio
| Recurso | Origen | Licencia |
|---|---|---|
| Texturas de decals (mancha de aceite, marcas de neumático, cartel del taller) | Generadas proceduralmente por `DecalFactory` (editor) | MIT (propias) |
| Sonidos de motor y de averías | Síntesis procedural (`EngineAudio`, `CueAudioAndSmoke`) | MIT (propios) |
| Geometría del taller y coches | Modelos procedurales de `tools/blender` y primitivas generadas por script | MIT (propias) |
| Textos de la interfaz (`data/locale`) | Propios | CC BY 4.0 |

## Modelos 3D generados por el proyecto

Todos los FBX de `Unity/Assets/Garage/Resources/` los genera `tools/blender/models.py`: son geometría procedural
propia, sin assets de terceros, y se distribuyen con la misma licencia que el código (MIT).

## Sonidos grabados (Freesound)
`tools/fetch-audio` descarga sólo muestras **CC0** de Freesound (necesita `FREESOUND_API_KEY`) y anota aquí
nombre, autor, URL y licencia de cada una. Ver `docs/AUDIO.md`. El repositorio no incluye ninguna muestra
grabada: sin ellas suena la síntesis de respaldo.
Colocar en `Assets/Garage/Resources/Audio/<id de pista>` (p. ej. `sound.vacuum_hiss`) o asignarlas a `EngineAudio.sampleLayers`.

## Assets descargados por el editor (Poly Haven, CC0)
El menú *Garage/Assets/Download Free Assets* completa la tabla siguiente automáticamente. Los ficheros se guardan en
`Unity/Assets/Garage/Downloaded/` (ignorado por git; se pueden volver a descargar).

<!-- DOWNLOADED-ASSETS -->
(aún no se ha ejecutado la descarga)
