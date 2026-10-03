# Hoja de ruta

## Fase 1 — Núcleo (hecho)
Motor gasolina turbo/atmosférico, sensores con curvas reales, red eléctrica nodal, ECU con lazo cerrado y
diagnóstico OBD-II, modos de fallo genéricos, herramientas, banco, meta-juego, CLI, 4 coches, proyecto Unity base.

## Fase 2 — Más sistemas
- **Diésel completo**: FAP con hollín/regeneraciones (P2463), inyección piloto/principal, VGT real, sensor NOx, AdBlue/SCR, bujías de incandescencia con módulo.
- **Inyección directa (GDI)**: bomba de alta, raíl 50–350 bar, carbonilla en válvulas de admisión.
- **Distribución variable** (VVT, P0010–P0014) con electroválvulas y presión de aceite.
- **Frenos** (ABS con sensores de rueda, C0xxx), **suspensión** (holguras, ruidos), **transmisión** (TCM, deslizamientos, P07xx), **dirección**.
- **CAN completo**: tramas reales con IDs, terminaciones de 120 Ω medibles, gateway, diagnóstico UDS (servicios 0x19/0x14/0x22/0x2E).
- **Sensores dobles** (TPS/APP con dos pistas y correlación P2135/P2138), sensores de oxígeno en el banco 2 (V6/V8).
- Rendimiento: solver eléctrico sin asignaciones, simulación multihilo de coches aparcados.

## Fase 3 — Modding y Steam Workshop
- Editor de coches en juego (plantilla + overrides) con validación en vivo contra los schemas.
- Empaquetado de mods (`mod.json` + carpetas) y subida/descarga con la API de Steam Workshop (Steamworks.NET).
- Modelos 3D por slot visual en mods (AssetBundles/Addressables).
- Firmas/compatibilidad de versión de datos (`formatVersion` en contenido).

## Fase 4 — Híbridos y eléctricos
- Batería de alta tensión (celdas, BMS, desequilibrios, aislamiento P0AA6), inversor, motor eléctrico, convertidor DC/DC.
- Seguridad de alta tensión como mecánica (EPI, desconexión de servicio, medida de ausencia de tensión).
- Híbrido en paralelo/serie con gestión de energía y códigos P0Axx/P1xxx genéricos.
