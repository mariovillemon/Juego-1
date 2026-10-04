# Audio

## Cómo suena el juego

El banco de sonidos está en `data/base/audio.json` y se valida con `audio.schema.json`. Contiene tres tipos de entradas:

- **Capas de motor** (`engine_layer`): bucles grabados a unas rpm y una carga conocidas, en versión exterior e
  interior. Hay ralentí más tres regímenes (2000, 3800 y 6000 rpm), cada uno con carga y sin carga.
  `EngineSoundMixer` (C# puro, con tests) elige las dos capas que rodean las rpm actuales y las mezcla
  conservando la potencia sonora. Hace lo mismo entre las capas con y sin carga, y cambia la velocidad de
  reproducción según rpm/rpm de grabación. Las capas interiores suenan cuando la cámara está junto al asiento
  del conductor.
- **Eventos** (`event`): sonidos cortos que se disparan con eventos del juego (`GameEventKind`). Ejemplos: el clic
  del conector, el pitido del escáner y del multímetro, la carraca al desmontar, la pistola neumática al montar,
  el motor de arranque, la bomba al dar contacto, la caja registradora, el error de la UI y el golpe de una avería
  grave.
- **Bucles** (`loop`): el ambiente de la nave y las pistas de la simulación (`sound.vacuum_hiss`,
  `sound.rod_knock`, `sound.knock`, `sound.misfire_exhaust`, `sound.turbo_spool`, `sound.fan`…). Suenan cuando
  la simulación las emite, con la intensidad que indica.

Cada sonido se busca en `Resources/Audio/<id>` (`.ogg` o `.wav`). Si no existe:

- El motor se sintetiza: pulsos a la frecuencia real de encendido, huecos en los fallos de encendido, silbido del
  turbo y picado.
- Los eventos y el ambiente usan sonidos sintéticos sencillos (`GameAudio`).

El juego nunca se queda mudo. La reverberación de nave la pone `AudioReverbZone` (preset *Hangar*) y los volúmenes
por grupo, `AudioBuses`.

## Descargar sonidos reales (Freesound, CC0)

1. Crea una cuenta gratuita en https://freesound.org.
2. Solicita una clave de API en https://freesound.org/apiv2/apply/ (*Create new API credentials*). Te dan un
   *Client secret/Api key*.
3. Ejecuta el script con la clave en la variable de entorno `FREESOUND_API_KEY`:
   - Linux/macOS: `FREESOUND_API_KEY=tu_clave tools/fetch-audio/fetch-audio.sh`
   - Windows (PowerShell): `$env:FREESOUND_API_KEY="tu_clave"; powershell -ExecutionPolicy Bypass -File tools/fetch-audio/fetch-audio.ps1`

   Opciones:
   - `--only engine.` descarga sólo los sonidos cuyo id empieza así.
   - `--force` vuelve a descargar los que ya existen.
4. Vuelve a Unity: los `.ogg` aparecen en `Assets/Garage/Resources/Audio/` y se usan automáticamente. El motor pasa
   a las capas grabadas cuando están todas las exteriores.

Cómo funciona el script:

- Para cada entrada busca con su `query`, sólo licencia **Creative Commons 0** y dentro del rango de duración de la
  entrada.
- Toma el resultado mejor valorado y guarda su previsualización OGG de alta calidad.
- Anota título, autor y URL en `docs/ASSET_CREDITS.md`, en la sección FREESOUND.
- Sin clave no cambia nada y termina sin error.

Para afinar un sonido, cambia su `query` en `audio.json` y ejecuta el script con `--only <id> --force`.
Comprueba siempre que el resultado suena bien: la búsqueda es automática.
Las capas de motor deberían salir del mismo coche, grabado a rpm estables; si no las hay en CC0, deja las del
motor sintético.
