# Estado del proyecto

Fecha: 2026-10-03. Rama: `claude/optimistic-fermat-k7qxj6`.

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
- **Contenido**: 4 coches ficticios (compacto 2.0T, utilitario 1.4 atmosférico speed-density, berlina 2.0 TD
  *experimental*, deportivo R6 3.0 biturbo), mapas de serie coherentes, 220 DTC genéricos reales con descripción en
  inglés y español, 50 escenarios, 10 encargos, 12 clientes, catálogo de piezas, 12 mejoras. 15 JSON Schemas.
- **CLI** jugable de principio a fin (modo formación y realista, guion de demo usado en CI).
- **Tests**: 140 (unitarios de sensores, PIDs, mapas, red, JSON; comportamiento emergente por familia de fallo;
  daño; determinismo; datos y mods; herramientas; meta-juego; guardado).
  Cobertura de líneas de `Garage.Sim`: ver "Cobertura" abajo.
- **CI**: GitHub Actions (build Release sin warnings, validación de datos, tests con cobertura, sesión de CLI).

## Escrito pero NO verificado (sin editor de Unity disponible)

- Proyecto `Unity/` (Unity 6000.0 LTS + HDRP 17): manifest, asmdefs, sincronización del núcleo, menús idempotentes
  (`Configure HDRP`, escenas de taller/menú/banco, luces físicas en lúmenes/lux y Kelvin, sondas, cielo físico,
  decals procedurales, bake), descargador CC0 de Poly Haven, validador de escena, puente con la simulación,
  controlador en primera persona, interacción (inspeccionar, conectores, tornillos en orden, sustituir), pantallas
  de herramientas en RenderTexture (escáner, multímetro, osciloscopio, portátil de mapas con superficie 3D, monitor
  del banco), desgaste visual desde datos, audio procedural por capas y pistas, tests EditMode.
  **Puede necesitar ajustes al importarlo** (nombres de API de HDRP; las partes frágiles usan reflexión).
  La API de Poly Haven no era accesible desde el entorno de desarrollo.

## A medias / simplificado

- Diésel: funcional pero experimental (sin FAP, sin NOx, sin piloto).
- Wideband como tensión equivalente; TPS/APP de una sola pista (sin correlación P2135).
- EVAP sólo detecta purga atascada; sin prueba de estanqueidad.
- Interfaz de Unity: menús de recepción/menú principal con IMGUI provisional; el desgaste usa tintado + decals en
  lugar de un Shader Graph de capas.
- Sin modelos 3D reales (placeholders bien escalados y nombrados); sin muestras de audio grabadas.

## Limitaciones conocidas del realismo

- **Simulación**: modelo de valor medio (sin presión por grado de cigüeñal); temperaturas de pared, aceite y EGT
  simplificadas; transmisión rígida (sin convertidor ni embrague real); combustibles sólo por RON; sin humedad del
  aire; turbo sin mapa de compresor real; los modos de fallo de señal usan una fracción del rango (no curvas de
  envejecimiento físicas); el picado es un índice continuo, no eventos por ciclo.
- **Gráficos**: geometría primitiva; sin Shader Graph de suciedad por capas; el depth of field se activa por
  distancia; sin LODs reales hasta tener modelos.

## Siguientes pasos recomendados

1. Abrir el proyecto en Unity 6, corregir lo que no compile, ejecutar los menús y los tests EditMode.
2. Sustituir placeholders por modelos (empezar por motor T20 y vano, ver `ART_PIPELINE.md`) y grabar/obtener audio CC0.
3. Shader Graph de desgaste con máscaras (suciedad, grasa, óxido, polvo, huellas) leído desde `WearController`.
4. TPS/APP de doble pista, EVAP completo, GDI y VVT (fase 2 del roadmap).
5. UI diegética de la recepción y del portátil; flujo de presupuesto con piezas desglosadas.
6. Optimizar el solver eléctrico (sin asignaciones) para simular varios coches a la vez.

## Cobertura

Medida con coverlet (Release, 140 tests): **Garage.Sim 85,6 % de líneas (71,6 % ramas)**, Garage.Data 89,6 % líneas (79,2 % ramas).
