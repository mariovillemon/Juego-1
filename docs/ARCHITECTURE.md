# Arquitectura — Simulador de taller mecánico

> Documento escrito **antes** de programar y mantenido después. Describe el diseño
> completo; el estado real de cada parte está en `docs/STATUS.md`.

## 1. Objetivos de diseño

1. **La simulación es la fuente de verdad.** Ningún síntoma ni código de avería se
   escribe a mano: aparecen porque un modo de fallo altera la física o la
   electricidad del coche y la ECU simulada reacciona con su propia lógica.
2. **Datos, no código.** Un coche es una combinación de definiciones JSON
   (motor, componentes, arnés, mapas ECU, apariencia). Añadir un coche no
   requiere compilar.
3. **Determinismo.** Paso de tiempo fijo y un único generador aleatorio con
   semilla inyectable (`DeterministicRandom`, xorshift128+). Misma semilla +
   mismas entradas = mismos resultados bit a bit. Imprescindible para tests,
   repeticiones y depuración.
4. **Núcleo portable.** `Garage.Sim` y `Garage.Data` compilan para
   `netstandard2.1` con C# 9, sin `UnityEngine`, sin dependencias NuGet, sin
   `System.Text.Json` (Unity no lo incluye). Se copian como código fuente dentro
   de Unity.
5. **Interfaz desacoplada.** Las herramientas de diagnóstico son APIs; la CLI
   y Unity sólo las presentan.

## 2. Mapa de proyectos

```
Garage.sln
├─ src/Garage.Sim        (netstandard2.1, C# 9)  núcleo de simulación
├─ src/Garage.Data       (netstandard2.1, C# 9)  JSON propio, schemas, carga en capas
├─ src/Garage.Game       (netstandard2.1, C# 9)  capa de aplicación: sesión, comandos, opciones, idiomas
├─ src/Garage.Cli        (net8.0)                prototipo jugable en terminal
├─ tests/Garage.Sim.Tests(net8.0, xUnit)         unitarios + comportamiento emergente
├─ data/base, data/schemas, data/locale, mods/  contenido, JSON Schemas y textos ES/EN
└─ Unity/                                        proyecto Unity 6 LTS + HDRP
```

Dependencias: `Cli → Game → Data → Sim`, `Tests → Game, Data, Sim`, `Unity Runtime → (fuentes de) Game + Data + Sim`.

## 3. Garage.Sim — módulos

### 3.1 Core (`Garage.Sim.Core`)
- `SimClock`: tiempo de simulación (s) y tiempo de juego (min) separados.
- `DeterministicRandom`: xorshift128+ con `NextDouble`, `Range`, `Gaussian`, `Chance`, `Fork(salt)`.
- `MathUtil`: interpolación, `Clamp`, filtros de primer orden.
- `Units` y constantes físicas (R aire 287 J/kgK, LHV gasolina 43.4 MJ/kg, diésel 42.8, estequiométrico 14.7 / 14.5).

### 3.2 Mapas (`Garage.Sim.Maps`)
- `Axis` (valores crecientes) + `Map2D` (curva) + `Map3D` (tabla RPM × carga) con
  interpolación bilineal y saturación en bordes. Editables por celda, clonables.
  Usados tanto por la ECU (mapas de calibración) como por el motor (VE físico, MBT).

### 3.3 Motor (`Garage.Sim.Engine`)
Modelo cuasi-estacionario de valor medio (*mean value engine model*) resuelto por tick:

1. **Admisión.** Presión antes de mariposa = atmosférica (+ sobrealimentación − pérdidas de filtro/intercooler).
   La presión de colector (MAP) se obtiene resolviendo por bisección el equilibrio
   *caudal por mariposa (orificio compresible) + fugas = caudal aspirado por el motor
   (VE · ρ · Vd · rpm/120)*. El MAF mide **sólo** el aire que pasa por él; una fuga
   después del MAF mete aire no medido → mezcla pobre.
2. **Turbo.** Presión de soplado con dinámica de primer orden hacia
   `min(objetivo controlado por wastegate, capacidad del turbo según caudal de gases)`.
   Wastegate con electroválvula (duty) y actuador neumático; fallos: atascada abierta/cerrada,
   fuga de presión, turbo dañado. Intercooler por eficiencia.
3. **Combustible.** Bomba → presión de rail (regulador). Inyector: caudal ∝ √(ΔP), tiempo muerto
   según tensión de batería. La ECU calcula el tiempo de inyección con el **MAF medido** y sus
   creencias (presión nominal, caudal nominal); el motor entrega combustible con los valores **reales**.
   Lambda real por cilindro = aire real / (combustible real · estequiométrico).
4. **Encendido.** Avance de mapa − retardo por detonación − correcciones. Límite de detonación
   (KLA) función de octanaje, presión de cilindro (carga), temperatura de carga, ECT y lambda.
   Avance > KLA ⇒ intensidad de detonación; el sensor de picado la ve; la ECU retrasa.
5. **Combustión y par.** Energía del combustible quemado (limitada por oxígeno) × rendimiento
   indicado (0.38 base × eficiencia por avance respecto a MBT × factor lambda). Cilindro con
   fallo de encendido aporta 0 (y el combustible sin quemar va al catalizador → temperatura).
   Pérdidas por fricción (FMEP según rpm) y bombeo. Potencia = par × ω.
6. **Térmico.** Refrigerante con capacidad térmica, calor ≈ 30 % de la energía del combustible,
   termostato de cera (apertura 88–102 °C), radiador + ventilador, aceite siguiendo al refrigerante,
   EGT según lambda/avance/carga, catalizador.
7. **Daño.** Acumuladores 0..1 por pieza: pistón (detonación), junta de culata (sobretemperatura,
   presión), biela (sobrerrégimen, detonación severa), turbo (sobrevelocidad/EGT), válvulas,
   catalizador (fallos de encendido). Al llegar a 1 la pieza se rompe y cambia la física
   (compresión baja, consumo de refrigerante, ruido, humo).

Diseño extensible: `EngineDefinition.Kind` = `GasolineNA`, `GasolineTurbo`, `DieselTurbo`
(experimental: sin mariposa, par por cantidad inyectada, lambda pobre, sin bujías),
`Hybrid`/`Electric` reservados (roadmap).

Órdenes de magnitud y fuentes en `docs/DECISIONS.md` (Heywood, *Internal Combustion Engine
Fundamentals*; Bosch *Automotive Handbook*; hojas de datos públicas de sensores).

### 3.4 Componentes (`Garage.Sim.Components`)
Todo componente implementa `IComponent`: `Id`, `Kind` (enum extensible de tipos),
`Parameters` (diccionario numérico definido por datos), `Health` (0..1),
`Faults` (instancias activas), `VisualSlot`. Familias:
- **Sensores** (`SensorBase`): magnitud física → señal eléctrica mediante una curva
  (`TransferCurve`): NTC (ECT/IAT, Beta), lineal 0.5–4.5 V (MAP, presión de rail, TPS, APP),
  MAF (frecuencia o tensión no lineal), narrowband (Nernst), wideband (λ → corriente de bomba/ V eq.),
  CKP inductivo (AC ∝ rpm, rueda 60-2), CMP Hall, piezo de detonación.
- **Actuadores**: inyectores, bobinas, mariposa motorizada, electroválvula wastegate, termostato,
  ventilador, bomba de combustible, EGR, purga cánister.
- **Mecánicos**: cilindros (compresión, segmentos, válvulas), junta de admisión, manguitos,
  escape, catalizador, filtros, bujías.
- **Eléctricos**: batería, alternador, fusibles, relés, masas.

### 3.5 Eléctrico (`Garage.Sim.Electrical`)
Cada circuito de sensor/actuador es una **red resistiva real** resuelta por análisis nodal
(Gauss con pivoteo): pines de la ECU, cables, conector (lado arnés / lado componente), el
componente como fuente Thévenin o resistencia, pull-ups/pull-downs internos de la ECU.
- Fallos de cable (abierto, corto a masa, corto a positivo, alta resistencia) y de conector
  (pin oxidado, pin suelto) son simplemente cambios de resistencia en la red.
- La ECU lee la tensión del nodo de su pin ⇒ los códigos de circuito (P0117/P0118, P0107/P0108…)
  emergen solos; el multímetro mide en cualquier par de nodos, con escala real y "OL".
- Medida de ohmios: se quitan las fuentes y se inyecta corriente de prueba (si el circuito está
  alimentado, la lectura es errónea, como en la vida real).

### 3.6 Modos de fallo (`Garage.Sim.Faults`)
- `FailureModeDefinition` (datos): id, familia, tipos de componente aplicables, **efecto genérico**
  (`EffectKind`: `SignalOffset`, `SignalGain`, `SignalStuck`, `SignalNoise`, `SignalLag`,
  `SignalDropout`, `WireOpen`, `WireShortGround`, `WireShortPower`, `WireHighResistance`,
  `ConnectorCorrosion`, `Leak`, `Restriction`, `Wear`, `StuckOpen`, `StuckClosed`, `Clog`,
  `Weak`, `Dead`, `Slack`, `InternalShort`), parámetros por defecto y rango de severidad.
- `FaultInstance`: modo + componente objetivo + pin opcional + severidad + `FaultCondition`
  (siempre, en frío, en caliente, por encima de carga/rpm, con vibración, intermitente aleatorio).
- Los componentes consultan `FaultSet.Effect(kind)` al calcular; nada más.
- `FaultGenerator`: elige componentes y modos compatibles según dificultad y semilla
  (1 fallo simple → varios + intermitentes + en cadena).
- **Cadenas**: el daño acumulado convierte un estado en un fallo nuevo (p. ej. fallo de encendido
  prolongado → catalizador fundido → restricción de escape).
- `SensoryCueAnalyzer`: deriva pistas (sonido, humo, olor, vibración, testigos) del estado
  simulado con intensidad 0..1, para la CLI (texto) y Unity (audio/partículas).

### 3.7 ECU (`Garage.Sim.Ecu`)
- `EcuCalibration`: mapas (lambda objetivo, avance, presión de turbo, duty base wastegate,
  correcciones por ECT/IAT, escalado de inyector, limitadores, ralentí).
- `EngineControlUnit`: lectura de sensores **a través de los circuitos**, conversión con curvas
  nominales, sustitución por valores por defecto cuando un sensor falla (estrategia real),
  lazo cerrado de mezcla (STFT PI ±25 %, LTFT aprendizaje lento ±25 %, celdas por rango),
  control de ralentí, de turbo (PID + feedforward), de detonación (retardo/recuperación),
  ventilador, *limp mode* (limita mariposa y turbo).
- `DiagnosticManager` + `Monitors`: cada monitor tiene condiciones de habilitación,
  umbral y tiempo de confirmación. Estados DTC reales: *pending* (1 ciclo), *confirmed* (2 ciclos
  consecutivos), MIL, *permanent*; borrado de MIL tras 3 ciclos sin fallo, olvido tras 40 ciclos
  de calentamiento. Freeze frame con el primer fallo. Readiness (misfire, fuel, components,
  catalyst, EVAP, O2, O2 heater, EGR).
- `DtcCatalog`: catálogo cargado de `data/base/dtc.json` (≥150 códigos genéricos reales).
- `Obd2`: PIDs modo 01 con fórmulas SAE J1979 (codificación a bytes y decodificación),
  modos 02/03/04/07/09.
- `CanNetwork`: módulos (ECM, ABS, IPC, BCM) que emiten mensajes periódicos; cada uno vigila
  los de los demás y registra U0100/U0121/U0155/U0140/U0001/U0073 por timeout o bus off.

### 3.8 Vehículo (`Garage.Sim.Vehicle`)
`Car` ensambla `EngineModel`, componentes, arnés, ECU, red CAN, batería/alternador,
apariencia (antigüedad, km, mantenimiento → desgaste visual) y `FaultSet`.
`CarFactory` construye un `Car` a partir de un `CarDefinition` (datos). Entradas de control:
llave (off/on/arranque), pedal, marcha, carga externa (banco), velocidad.

### 3.9 Herramientas (`Garage.Sim.Tools`)
Cada medición devuelve un resultado y **coste en minutos de juego**:
- `ScanTool` (lectura/borrado por módulo, freeze frame, datos en vivo, readiness, pruebas de actuador),
- `Multimeter` (V DC, Ω, continuidad; escalas 200 mV…200 V, 200 Ω…20 MΩ; "OL" y "1." fuera de escala),
- `FuelPressureGauge`, `CompressionTester`, `LeakDownTester`, `SmokeMachine`, `Oscilloscope`
  (formas de onda sintetizadas del estado: CKP 60-2, CMP, inyector con pico inductivo, primario
  de bobina con chispa, lambda conmutando).
- `WiringDiagram`: esquema de pines por componente.

### 3.10 Banco y datalog (`Garage.Sim.Dyno`)
Banco de rodillos inercial: pasada a fondo en una marcha, integración de velocidad de rodillo,
par en rueda → par motor estimado (pérdidas de transmisión), corrección SAE J1349 simplificada.
`Datalog` con canales (rpm, par, potencia, λ, boost, avance, knock, ECT, IAT, EGT) a 20 Hz,
exportable a CSV. La rotura durante la pasada es posible y aborta la prueba.

### 3.11 Meta-juego (`Garage.Sim.Game`)
`Workshop` (dinero, reputación, herramientas, mejoras, día), `Customer` (perfil),
`Job` (coche + fallos + objetivo + plazo + presupuesto), `Quote`, `PartsCatalog`
(OEM / aftermarket / usada: precio, calidad, fiabilidad → probabilidad de fallo prematuro),
`JobEvaluator` (¿se arregló?, ¿se cumplió el objetivo de potencia?, ¿pasa ITV?),
devoluciones por reaparición, `ProgressionTable`, `SaveGame` (JSON versionado).

## 4. Garage.Data

- `Json` propio: `JsonValue` (DOM), `JsonParser` (RFC 8259, con líneas/columnas en errores),
  `JsonWriter` (indentado, invariante de cultura). Evita depender de `System.Text.Json` o
  Newtonsoft en Unity; si un día se quiere otro parser basta con implementar `IJsonParser`.
- `SchemaValidator`: subconjunto de JSON Schema draft 2020-12 (`type`, `required`, `properties`,
  `additionalProperties`, `items`, `enum`, `const`, `minimum`, `maximum`, `minLength`,
  `pattern`, `minItems`, `maxItems`, `$ref` local y entre archivos del directorio de schemas,
  `$defs`, `oneOf`/`anyOf`). Errores con ruta JSON Pointer.
- `ContentLoader`: carga **en capas** `data/base` → `mods/*` (orden alfabético o `mod.json` con
  prioridad). Cada entidad tiene `id`; un mod puede añadir ids nuevos o sobrescribir existentes
  (sustitución completa, o parcial con `"$patch": true`).
- `ContentDatabase`: diccionarios tipados (motores, coches, componentes, modos de fallo, DTC,
  piezas, clientes, escenarios, encargos, calibraciones).
- `Mappers`: JSON → definiciones de `Garage.Sim`.

## 5. Bucle de simulación

```
Car.Step(dt):
  1. Entradas (llave, pedal, carga) y fallos condicionales (evaluar condiciones → activos)
  2. Electricidad: batería/alternador → tensiones de alimentación, fusibles/relés
  3. ECU.ReadSensors(): resolver redes de sensores → tensiones en pines → valores creídos
  4. ECU.Control(): mariposa, inyección, avance, turbo, ralentí, ventilador, limp
  5. Engine.Step(): aire, turbo, combustible real, combustión por cilindro, par, rpm, térmico, daño
  6. Sensores físicos actualizan su magnitud (con retardo/ruido/fallo)
  7. ECU.Diagnose(): monitores, DTC, freeze frame, readiness
  8. CAN.Step()
  9. Cadena de daños → nuevos fallos
```
`dt` por defecto 0.02 s (50 Hz). Herramientas y CLI usan `Car.RunFor(seconds)`.

## 5b. Sistemas ampliados

Partes opcionales que sólo se activan si el coche tiene el componente:
- `EngineModel.Systems.cs`: desfasador VVT (aceite, bloqueo en retraso, lodos), bomba de alta GDI con válvula
  dosificadora, actuador VGT con contrapresión, FAP (hollín, cenizas, regeneración pasiva y activa, presión
  diferencial) y estimación de NOx.
- `EngineControlUnit.Systems.cs`: control y diagnóstico de esos sistemas. Incluye la máquina de estados de la
  prueba EVAP (sellado → vaciado → mantenimiento → apertura), el monitor de EGR y la regeneración activa por
  postinyección.
- `Vehicle/EvapSystem.cs`: presión del depósito (purga, venteo, fugas por diámetro equivalente, vapor, válvula de
  alivio del tapón).
- La correlación de pistas TPS/pedal está en `EngineControlUnit.cs` (pista B invertida o de media pendiente).

## 5c. Garage.Game (capa de aplicación)

`GameSession` envuelve el `Workshop` con comandos que devuelven `CommandResult` (errores legibles) y publica
`GameEvent` en un bus: lo usan igual la CLI y Unity. También incluye `CarWork` (el coche en el elevador y las
herramientas), `Inventory`, `QuoteDraft`, `EcuEditor` (deshacer e interpolar), `SaveSlots`, `TutorialRunner`, el audio
(`SoundBank`, `EngineSoundMixer`) y `Settings/` (`GameSettings`, `KeyBindings` con intercambio de teclas y
`Localizer` con tablas `data/locale/*.json`, que recurre al español).

## 6. Unity

- `Unity/Assets/Garage/Sim/Generated/` recibe las fuentes de `Garage.Sim`, `Garage.Data` y `Garage.Game` vía
  `tools/sync-sim-to-unity.(sh|ps1)`, junto con `data/` en `StreamingAssets/data`.
- `Garage.Unity.Runtime`:
  - `Core`: `SimulationRunner` (dueño de la sesión, paso fijo), `GameBootstrap`, `MainMenuController` y
    `GameOptions` (aplica opciones, crea acciones de entrada reasignables y carga los idiomas).
  - `UI`: uGUI generada en código (`UiKit`, `UiPanel`, paneles, `OptionsView`, `LoadingScreen`).
  - `Player`: controlador en primera persona e `InteractionSystem`.
  - `Car`: montaje del coche con modelos y desgaste. También `Tools`, `Audio` y `World`.
- `Garage.Unity.Editor`: menús `Garage/…` idempotentes para HDRP, escenas, materiales, luces físicas, assets CC0,
  postprocesado de modelos y build de Windows (`BuildWindows`, también por `-executeMethod`).
- `Garage.Unity.Tests`: EditMode.
- `tools/unity-typecheck`: compila todos los scripts contra los ensamblados de referencia de UnityEngine y unos
  *stand-ins* de los paquetes. Detecta errores de tipos sin el editor.

## 7. Calidad
- `TreatWarningsAsErrors`, documentación XML en API pública, `Nullable` activado.
- CI de GitHub Actions: `dotnet build` + `dotnet test` + validación de datos con la CLI.
- Cada bloque se cierra con build y tests verdes.
