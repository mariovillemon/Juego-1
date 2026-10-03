# Modding

Todo el contenido del juego está en JSON y se valida contra `data/schemas/`. Los mods se cargan **después** de
`data/base`, en orden de `priority` (menor primero) y luego alfabético.

## Estructura de un mod

```
mods/mi_mod/
  mod.json                 { "name": "...", "author": "...", "version": "1.0.0", "priority": 100, "enabled": true }
  cars/*.json              coches (uno por fichero)
  engines/*.json           motores
  templates/*.json         plantillas de componentes
  calibrations/*.json      mapas de ECU
  dtc.json, failure_modes.json, parts.json, customers.json, scenarios.json, jobs.json, upgrades.json   (colecciones)
```

Reglas de fusión por `id` (o `code` en DTC):
- id nuevo → se **añade**;
- id existente → se **sustituye** entero;
- `"$patch": true` → se **fusiona** en profundidad con el existente (sólo los campos indicados);
- `"$remove": true` → se **elimina**.

Valida siempre: `dotnet run --project src/Garage.Cli -- validate` (lista mods cargados, parches aplicados y errores con
fichero, línea y ruta JSON Pointer).

## Crear un coche nuevo paso a paso

1. **Elige o crea el motor** (`engines/`). Campos clave: cilindros, cilindrada, relación de compresión, `ve`
   (tabla rpm × MAP con la eficiencia volumétrica física), `mbt` (avance óptimo rpm × carga), `knockMarginDeg`,
   inyectores, presión de rampa, `turbo` si lo lleva. Copia uno existente y ajústalo.
2. **Elige plantilla de componentes** (`templates/`): `gasoline_turbo`, `gasoline_na_sd` (speed-density, sin MAF),
   `diesel_turbo`. Una plantilla lista componentes con su circuito (topología, pines, colores, cavidades de la ECU,
   fusible). Los componentes con `"perCylinder": true` se repiten por cilindro sustituyendo `{n}`.
3. **Crea la calibración** (`calibrations/`): tablas `lambda_target`, `ignition_advance`, `boost_target`,
   `wastegate_duty`, `ve_estimate` (y `diesel_quantity` en diésel), curvas de corrección y escalares (limitador,
   ralentí, caudal de inyector que *cree* la ECU, `maf_max_gps`…). Puedes partir de `tools/gen_content.py`, que
   genera mapas de avance coherentes con el límite de detonación del motor.
4. **Crea el coche** (`cars/mi_coche.json`):

```json
{
  "id": "mi_coche", "brand": "Marca Ficticia", "model": "Modelo", "year": 2018,
  "engine": "t20_turbo", "template": "gasoline_turbo", "calibration": "t20_stock",
  "massKg": 1380, "wheelRadiusM": 0.31, "gearRatios": [3.4, 2.1, 1.4, 1.1, 0.87, 0.72], "finalDrive": 3.9,
  "vin": "ABC1234567890XYZ1",
  "appearance": { "ageYears": 6, "kilometers": 90000, "maintenance": 0.6, "paintColor": "#334455", "humidity": 0.3 },
  "componentOverrides": [ { "id": "maf", "params": { "max_gps": 220 } } ],
  "removeComponents": [ "purge_valve" ],
  "addComponents": [ ]
}
```

5. **Prueba**: `garage validate`, `garage dyno --car mi_coche`, `garage sandbox --car mi_coche --difficulty 3`.
6. Opcional: **escenarios** (`scenarios.json`) combinando modos de fallo sobre componentes de tu coche y **encargos**
   (`jobs.json`).

El mod de ejemplo `mods/ejemplo_pico_gti` (desactivado) añade un coche clonado y parchea el color de otro.

## Modos de fallo

Un modo de fallo es genérico: efecto (`SignalOffset`, `WireOpen`, `Leak`, `Weak`…), tipos de componente a los que
se aplica, rango de magnitud, dificultad y condiciones posibles (en frío, en caliente, con carga, intermitente…).
Los efectos están implementados en el núcleo; un mod puede crear modos nuevos combinando efectos y rangos. Las
magnitudes de señal están en fracción del rango del sensor; las de cableado en ohmios; las fugas de aire en mm².

## Apariencia y slots visuales

`appearance` alimenta el desgaste visual en Unity (suciedad, óxido, grasa, decoloración). Cada componente tiene
`visualSlot`; Unity busca `Resources/CarParts/<slot>` o `Resources/CarParts/<Kind>` para sustituir el placeholder
por un modelo real (ver `ART_PIPELINE.md`).
