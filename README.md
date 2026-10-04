# Taller — simulador de diagnosis y reprogramación

Videojuego de PC (Unity 6 + HDRP) en el que llevas un taller mecánico. Llegan clientes con coches que tienen
averías, peticiones de potencia o ambas cosas. Diagnosticas con herramientas que funcionan como las reales
(escáner OBD-II, datos en vivo, multímetro, osciloscopio, manómetro de combustible, compresímetro, prueba de fugas,
máquina de humo), reparas, reprogramas la ECU y validas en un banco de potencia.

**La simulación es la fuente de verdad**: ninguna avería tiene síntomas escritos a mano. Las averías son *modos de
fallo* de componentes (fuga, cable abierto, sensor sesgado, bobina débil…) que alteran la física del motor o la red
eléctrica; la ECU simulada reacciona con su lógica real y los síntomas y códigos OBD-II aparecen solos. Ejemplo:
una grieta en un manguito de vacío → aire no medido → mezcla pobre → la sonda lo ve → STFT/LTFT positivos (sólo al
ralentí) → `P0171`, silbido audible y ralentí irregular.

Todas las marcas y modelos son **ficticios** (Aurex, Velmora, Nordak, Kessler).

## Estado

| Bloque | Estado |
|---|---|
| Núcleo de simulación (motor, sensores, eléctrico, ECU, OBD-II, CAN) | ✅ completo, 180 tests |
| Sistemas ampliados: EVAP completo, TPS/pedal de doble pista, VVT, GDI, diésel Euro 6 (VGT, FAP, EGR, NOx) | ✅ |
| Modos de fallo, generador, pistas sensoriales, cadenas de daño | ✅ |
| Herramientas de diagnóstico, banco de potencia, datalog CSV | ✅ |
| Meta-juego (clientes, encargos, economía, devoluciones, progresión, guardado) | ✅ |
| CLI jugable | ✅ |
| Contenido: 8 coches, 237 DTC, 36 modos de fallo, 62 escenarios, 15 encargos | ✅ |
| Capa de juego compartida (`Garage.Game`): sesión, tienda, almacén, tutorial, editor ECU, opciones, idiomas ES/EN | ✅ |
| Proyecto Unity HDRP jugable de principio a fin (UI, herramientas, reparación, banco, opciones, build de Windows) | ⚠️ comprobado con el verificador de tipos, sin probar en el editor; ver `docs/STATUS.md` |

## Estructura

```
src/Garage.Sim     núcleo de simulación (netstandard2.1, C# 9, sin Unity)
src/Garage.Data    JSON propio, validador de JSON Schema, carga en capas (base + mods), guardado
src/Garage.Game    capa de aplicación compartida por la CLI y Unity (sesión, comandos, opciones, idiomas)
src/Garage.Cli     prototipo jugable en terminal (net8.0)
tests/             xUnit (net8.0)
data/base          contenido del juego (JSON)    data/schemas   JSON Schemas    data/locale  textos ES/EN
mods/              mods (un ejemplo desactivado)
Unity/             proyecto Unity 6 LTS + HDRP
tools/             contenido, sincronización con Unity, modelos Blender, audio, build de Windows, verificador de tipos
docs/              arquitectura, decisiones, modding, Unity, arte, estilo visual, créditos, estado
```

## Compilar y probar

Requisitos: .NET SDK 8.

```bash
dotnet build Garage.sln            # sin warnings (TreatWarningsAsErrors)
dotnet test                        # 180 tests (≈2 min)
tools/unity-typecheck/check.sh     # compila los scripts de Unity sin el editor
dotnet run --project src/Garage.Cli -- validate   # valida todos los JSON contra sus schemas
```

## Jugar con la CLI

```bash
dotnet run --project src/Garage.Cli -- --formacion          # partida completa, modo formación
dotnet run --project src/Garage.Cli -- --realista --seed 7  # sin pistas
dotnet run --project src/Garage.Cli -- sandbox --scenario sc_coil4_weak_load --formacion
dotnet run --project src/Garage.Cli -- dyno --car kessler_rapace --csv rapace.csv
dotnet run --project src/Garage.Cli -- list
dotnet run --project src/Garage.Cli -- --script tools/demo_session.txt --formacion   # demo guionizada
```

El ciclo completo: tablón de encargos → presupuesto (el cliente acepta, regatea o rechaza) → trabajar en el coche
(escáner, datos en vivo que se actualizan, multímetro entre pines concretos, osciloscopio, pruebas mecánicas,
esquema de pines) → sustituir piezas (OEM / recambio / usada) o reparar cableado → editar celdas de los mapas de la ECU
→ pasada de banco con gráfica ASCII y exportación a CSV → entregar (prueba de carretera, evaluación objetiva) →
cobrar → guardar/cargar partida (JSON versionado).

### Ejemplos de salida

Lectura de códigos de un coche que llega con una fuga de vacío (modo formación):

```
  == Motor (ECM) ==  MIL: ENCENDIDA
  Modo 03 (confirmados):    [43 01 01 71]
    P0171  Sistema demasiado pobre (banco 1)  (confirmado, MIL, ocurrencias 2)
          Causas típicas: Fuga de vacío/aire no medido; Caudalímetro sucio; Presión de combustible baja; Inyectores obstruidos
  Modo 07 (pendientes): ninguno
  Modo 0A (permanentes): P0171
```

Datos en vivo (la ECU sólo corrige al ralentí: firma clásica de aire no medido):

```
    Régimen motor                              996 rpm
    Corrección corto plazo B1 (STFT)          0.78 %
    Corrección largo plazo B1 (LTFT)          25.0 %
    Corrección LTFT celda ralentí / crucero 25.0 / 3.2 %
```

Formación:

```
  • P0171 = la ECU añade mucho combustible porque la sonda ve mezcla pobre. Compara LTFT al ralentí y en crucero:
    si sólo es alto al ralentí y baja con carga, piensa en aire no medido (fuga de vacío): usa la máquina de humo…
```

Mapa de avance (editor estilo software de calibración, coloreado por valor):

```
══ ignition_advance (deg)  filas: load [rel]  columnas: rpm [rpm]   rango -2…42.7 ═══
   fila |    800   1200   1600   2000   2500   3000   3500   4000 ...
 8  1.7|  ░6.39 ▒11.03 ▒11.67 ▒12.31 ▒13.11 ▒13.91 ▒14.71 ▒15.51 ...
 9    2|  ░2.65  ░7.29  ░7.93  ░8.57  ▒9.37 ▒10.17 ▒10.97 ▒11.77 ...
```

Banco de potencia (`garage dyno --car kessler_rapace`):

```
Potencia máx: 399 CV a 6021 rpm | Par máx: 513 N·m a 4042 rpm | En rueda: 356 CV
  524 │            TTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT
  483 │TTTTTTTTTTTT                                  TTTTTTTTTT
  403 │                                              PPPPPPPPPPPPPPPP######T
  322 │                             PPPPPPPP
  201 │        PPPPPPP
```

## Abrir el proyecto Unity

Resumen (detalle en [`docs/UNITY_SETUP.md`](docs/UNITY_SETUP.md)):

1. `tools/sync-sim-to-unity.sh` (o `.ps1` en Windows) para copiar el núcleo, los datos y los textos al proyecto.
2. Abrir `Unity/` con Unity **6000.0 LTS**.
3. Menú **Garage/Setup/Run All Setup Steps**.
4. **Garage/Assets/Download Free Assets** (opcional: texturas, HDRI y atrezo CC0 de Poly Haven).
5. Abrir `Assets/Garage/Scenes/MainMenu.unity` y pulsar Play.

Ejecutable de Windows: **Garage/Build/Windows x64**, o sin abrir el editor
`powershell -ExecutionPolicy Bypass -File tools/build-windows.ps1` (sale en `Builds/Windows/Taller.exe`).

## Documentación

- [Arquitectura](docs/ARCHITECTURE.md) · [Decisiones](docs/DECISIONS.md) · [Hoja de ruta](docs/ROADMAP.md) · [Estado](docs/STATUS.md)
- [Modding](docs/MODDING.md) · [Unity](docs/UNITY_SETUP.md) · [Pipeline de arte](docs/ART_PIPELINE.md) · [Estilo visual](docs/VISUAL_STYLE.md) · [Audio](docs/AUDIO.md) · [Créditos](docs/ASSET_CREDITS.md)

## Licencia

Código bajo MIT ([LICENSE](LICENSE)). Contenido de `data/` bajo CC BY 4.0. Los assets descargados conservan su
licencia (CC0 en Poly Haven) y se registran en `docs/ASSET_CREDITS.md`.
