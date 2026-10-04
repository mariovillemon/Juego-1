# Estado del proyecto

Fecha: 2026-10-04. Rama: `claude/optimistic-fermat-k7qxj6`.

## Sesión 3 — progreso (se actualiza tras cada bloque)

- **Hecho — P1 jugable dentro de Unity** (ver `UNITY_SETUP.md` § Cómo se juega):
  - Capa de aplicación `src/Garage.Game` (`GameSession`, `CarWork`, eventos, comandos con errores legibles,
    presupuestos con contraoferta, tienda con carrito y plazos, almacén, caja de piezas viejas, editor ECU con
    deshacer/interpolación, ranuras de guardado, tutorial por datos). La CLI está refactorizada sobre ella y su
    demo guionizada sigue terminando un trabajo. Hay tests del ciclo completo, incluido guardar → cargar → seguir.
  - Unity:
    - UI uGUI generada en código: HUD, tablón, hoja de trabajo y presupuesto, entrega, tienda, almacén, mejoras,
      pausa con 5 ranuras, escáner con gráfica, multímetro con vista de pines, esquemas, pruebas mecánicas y
      osciloscopio, acciones del coche, pieza con secuencia de desmontaje, banco y portátil ECU.
    - Herramientas que se cogen y se sueltan, puerto OBD en el coche, carro de herramientas, puestos usables,
      traslado automático del coche al banco y tarjeta de tutorial con marcador 3D.
    - Menú principal uGUI.
  - Cambio de simulación: la ECU corta inyección ante fallos que dañan el catalizador, y el daño térmico del
    catalizador está recalibrado (D-59).
  - `tools/unity-typecheck`: compila los scripts de Unity sin el editor (también en CI).
- **Hecho — P2 gráficos**:
  - `tools/blender/` (ejecutado aquí con `bpy` 4.2) genera los FBX a escala real con LOD0/1/2 y colisiones
    `UCX_`: motores I4 e I6, 30 piezas, elevador, banco de trabajo, carro y carrocería de compacto. Ya están
    versionados en `Resources/`.
  - Postprocesador de importación y sustitución automática en `CarAssembler` y en la escena.
  - Poly Haven: texturas, HDRI y ahora también modelos CC0 de atrezo.
  - Desgaste: más aspectos de avería, polvo y charco de aceite.
- **Hecho — P3 audio**: banco de sonidos por datos, mezclador de capas de motor con tests, sonidos de eventos y
  ambiente con síntesis de respaldo, y `tools/fetch-audio` (Freesound, sólo CC0). Ver `docs/AUDIO.md`.
- **Hecho — P4 simulación ampliada** (tests en `NewSystemsTests`, 174 tests en verde):
  - TPS y pedal de doble pista con correlación (P2135/P2138, P0222/P0223), modo emergencia y P2106.
  - EVAP completo: canister, válvula de venteo, sensor de presión del depósito, tapón, prueba de estanqueidad por
    fases (P0440–P0457, incluidas P0442/P0455/P0456/P0457, P0441, P0446, P0451–P0453 y P0496).
  - VVT de admisión (P0010/P0011/P0012) y GDI con bomba de alta y rampa (P0087/P0088/P0089/P0090).
  - Diésel Euro 6: geometría variable (P0234/P0299), FAP con hollín, cenizas, regeneración pasiva y activa,
    presión diferencial (P2002, P2452–P2455, P2463, P244A/B), monitor de EGR (P0401/P0402/P0403) y NOx estimado.
  - Cuatro coches nuevos: Velmora Lumen (GDI turbo), Aurex Civa (VVT atmosférico), Nordak Fjord CRD (diésel
    completo) y Kessler Vento (GDI 2.0). Hay 12 escenarios y 5 encargos nuevos.
- **Hecho — P5 pulido**:
  - Opciones (gráficos, controles, audio, teclas) en el menú y en la pausa, guardadas en `settings.json`.
  - Reasignación de teclas con intercambio automático.
  - Textos ES/EN en `data/locale` para el menú, la pausa, las opciones, el HUD, la ayuda y la carga.
  - Pantalla de carga con fundidos y consejos.
  - Menú **Garage/Build/Windows x64** y `tools/build-windows.ps1/.sh`; `Builds/` está ignorado.
- **Simplificado en P1**:
  - Las puntas del multímetro se colocan desde la vista de pines, no arrastrando cables en 3D.
  - La pasada de banco se calcula al instante y se reproduce en tiempo real en la gráfica.
  - El coche pasa del elevador al banco por traslado automático.
  - El resto de herramientas mecánicas se usan desde un panel.


## Hecho (compilado y verificado con tests)

- **Núcleo `Garage.Sim`** (netstandard2.1, C# 9, sin warnings, documentación XML):
  - Motor de valor medio con MAP por equilibrio de caudales, turbo (wastegate + N75 + límite de caudal del compresor),
    intercooler, inyección real vs. creída por la ECU, combustión discreta por cilindro, detonación con límite KLA,
    par/potencia, fricción, térmico (refrigerante, aceite, EGT, catalizador), daño acumulado y roturas
    (pistón, biela, junta de culata, turbo, válvulas, catalizador) y cadenas de daño.
  - 44 tipos de componente; curvas de señal reales (NTC, lineal 0,5–4,5 V, MAF de película caliente, banda estrecha,
    banda ancha LSU, CKP inductivo 60-2, CMP Hall, picado).
  - Red eléctrica nodal por circuito (7 topologías): cables, conectores, fusibles, relés, cortos y altas resistencias.
  - ECU: lectura por circuito con umbrales y sustitución, lazo cerrado STFT/LTFT por celdas, ralentí, avance con
    control de picado, control de turbo, limitadores, modo emergencia, ventilador, purga, EGR; monitores con
    debounce, DTC pendiente/confirmado/permanente/MIL, freeze frame, readiness, misfire por ventanas de 200/1000 vueltas.
  - OBD-II: 30 PIDs del modo 01 con fórmulas SAE J1979, modos 02/03/04/07/09/0A, codificación de DTC a 2 bytes.
  - CAN simplificado (ECM/ABS/IPC/BCM) con códigos U por pérdida de comunicación y bus off.
  - 33 modos de fallo genéricos, condiciones (frío, caliente, carga, rpm, vibración, intermitente), generador por
    dificultad con semilla, pistas sensoriales (sonidos, humos, olores, vibraciones, testigos).
  - Herramientas: escáner, multímetro (escalas, OL, medidas en vivo erróneas), osciloscopio (modo pico + captura en
    tiempo real), manómetro, compresímetro seco/húmedo, fugas de cilindro, máquina de humo, inspección visual,
    esquemas de pines; cada acción cuesta tiempo. Asesor de formación.
  - Banco de rodillos inercial con corrección SAE, datalog de 20 canales a 20 Hz, CSV, gráfica ASCII, roturas.
  - Meta-juego: clientes con personalidad, tablón de encargos (datos + procedurales), presupuestos, piezas
    OEM/recambio/usada con fiabilidad, mano de obra, diagnosis, evaluación objetiva (carretera, ITV, banco),
    devoluciones, reputación, días y alquiler, compra de herramientas/mejoras.
- **`Garage.Data`**: parser/escritor JSON propio, validador de JSON Schema, carga en capas con mods
  (`$patch`, `$remove`, prioridad), mapeo a la simulación, guardado/carga versionado.
- **Contenido**:
  - 8 coches ficticios. Los 4 originales son un compacto 2.0T, un utilitario 1.4 atmosférico, una berlina 2.0 TD y
    un deportivo R6 biturbo. Los nuevos son un GDI turbo 1.5, un VVT atmosférico 1.6, un diésel Euro 6 1.6 CRD
    completo y un GDI 2.0.
  - 237 DTC genéricos reales (ES/EN), 36 modos de fallo, 62 escenarios, 15 encargos, 12 clientes, catálogo de
    piezas y 12 mejoras. 18 JSON Schemas.
- **CLI** jugable de principio a fin (modo formación y realista, guion de demo usado en CI).
- **Tests**: 180 (unitarios de sensores, PIDs, mapas, red, JSON; comportamiento emergente por familia de fallo;
  daño; determinismo; datos y mods; herramientas; meta-juego; guardado; sesión de juego; audio; sistemas
  nuevos de P4; opciones, teclas e idiomas).
  Cobertura de líneas de `Garage.Sim`: ver "Cobertura" abajo.
- **CI**: GitHub Actions (build Release sin warnings, validación de datos, tests con cobertura, sesión de CLI).

## A medias / simplificado

- **Localización**: sólo están traducidos el menú principal, la pausa, las opciones, el HUD, la ayuda de teclas y
  la pantalla de carga. El resto de paneles (tablón, tienda, escáner…) y los textos de la simulación (nombres de
  piezas, DTC en la UI, mensajes de clientes) siguen en español. Las descripciones de DTC ya existen en inglés en
  `dtc.json`.
- **Teclas**: se reasignan las de teclado. El mando tiene asignaciones fijas solo para moverse, mirar, agacharse y la
  linterna; la UI no se maneja con mando.
- **Diésel**: no tiene inyección piloto, AdBlue/SCR ni calentadores con módulo, y el NOx es una estimación sin
  sensor.
- **Interfaz 3D**: las puntas del multímetro se colocan desde la vista de pines y el resto de herramientas
  mecánicas se usan desde un panel. El banco calcula la pasada al instante y la reproduce en la gráfica.
- **Desgaste**: tintado, decals y aspectos por avería, sin Shader Graph de capas.
- **Audio**: síntesis de respaldo. Sólo habrá muestras reales si ejecutas `tools/fetch-audio` con tu clave de
  Freesound.

## Sin verificar (no hay editor de Unity en este entorno)

Todo lo de `Unity/` está escrito para Unity 6000.0 y pasa el verificador de tipos (`tools/unity-typecheck`, también
en CI), pero **nunca se ha ejecutado en el editor**. Eso incluye la UI y el ciclo de juego, las opciones (resolución,
calidad, FOV de Cinemachine), la reasignación de teclas (`ApplyBindingOverride` en acciones ya creadas), la
pantalla de carga, los menús de build y los scripts de build, el aspecto de los FBX importados y el audio. El
verificador sólo detecta errores de nombres y tipos, no de comportamiento.

## Qué comprobar en el editor (en este orden)

1. `tools/sync-sim-to-unity.ps1` y abrir `Unity/`: la consola no debe tener errores de compilación.
2. *Garage/Setup/Run All Setup Steps* y los tests EditMode (*Test Runner*).
3. Play en `MainMenu.unity`:
   - Cambiar el idioma a inglés en *Opciones* debe cambiar el menú al momento.
   - Cambiar la calidad y el FOV debe notarse.
   - *Volver* debe cerrar el panel.
4. *Nueva partida*:
   - La pantalla de carga debe hacer el fundido.
   - El tutorial debe guiar el primer coche hasta entregarlo.
5. Reasignar *Usar* a F en *Opciones › Teclas* (desde la pausa):
   - E debe dejar de funcionar y F debe usar; la linterna debe pasar a E.
   - La ayuda (F1) debe mostrar las teclas nuevas.
6. Aceptar un encargo de los coches nuevos (Civa por el tapón, Fjord CRD por el FAP) y ver los códigos en el
   escáner. La prueba EVAP necesita unos 2 minutos de motor en marcha y caliente.
7. *Garage/Build/Windows x64*: debe aparecer `Builds/Windows/Taller.exe`. Ejecutarlo y comprobar que el
   `settings.json` se crea en `LocalLow`.
8. Si algo falla con HDRP o Cinemachine, mira primero `UnityCompat.cs` y `GameOptions.Apply`.

## Siguientes pasos recomendados

1. Corregir lo que salga al abrir en el editor (punto anterior) y ajustar la iluminación y el rendimiento en
   escenas reales.
2. Traducir el resto de paneles moviendo sus textos a `data/locale` (el `Localizer` ya cubre el mecanismo y el test
   de claves avisará de las que falten).
3. Shader Graph de desgaste por capas y modelos con texturas pintadas para el vano motor.
4. Muestras de audio CC0 reales con `tools/fetch-audio`.
5. Sistemas pendientes del roadmap: frenos/ABS, transmisión, UDS, banco 2 de sondas, AdBlue.
6. Rendimiento del solver eléctrico para varios coches a la vez.

## Limitaciones conocidas del realismo

- **Simulación**: modelo de valor medio (sin presión por grado de cigüeñal); temperaturas de pared, aceite y EGT
  simplificadas; transmisión rígida (sin convertidor ni embrague real); combustibles sólo por RON; sin humedad del
  aire; turbo sin mapa de compresor real; los modos de fallo de señal usan una fracción del rango (no curvas de
  envejecimiento físicas); el picado es un índice continuo, no eventos por ciclo.
- **Gráficos**:
  - Modelos procedurales sencillos (sin texturas pintadas a mano).
  - Sin Shader Graph de suciedad por capas.

## Cobertura

La última medición con coverlet se hizo con 140 tests: **Garage.Sim 85,6 % de líneas (71,6 % ramas)** y
Garage.Data 89,6 % de líneas (79,2 % ramas). No se ha vuelto a medir con los 180 actuales.
