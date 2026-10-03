# Guía de estilo visual (realismo)

## Iluminación de un taller real
- Naves con **LED campana** a 4–6 m: 4000–5000 K, 10–20 klm por luminaria. Nivel en suelo **300–500 lx**
  (EN 12464-1 para reparación de vehículos); bancos con luz de tarea 750 lx.
- Luz exterior por el portón: sol hasta 100 klx, cielo 10–25 klx; el interior parece oscuro desde fuera y el
  exterior "quemado" desde dentro: deja que la **exposición automática** (EV100 4–13) lo resuelva, no lo falsees.
- Fosos y vanos motor oscuros: para eso está la lámpara de inspección (≈450 lm, 6000 K).
- Oficina con luz más cálida (3000 K) que contraste.

## Rangos PBR realistas (albedo sRGB / lineal, suavidad)
| Material | Albedo | Metálico | Suavidad |
|---|---|---|---|
| Hormigón pulido | 110–140 sRGB (0,15–0,26) | 0 | 0,3–0,5 (manchas más brillantes) |
| Asfalto | 30–50 sRGB | 0 | 0,15–0,3 |
| Pintura de pared | 160–200 sRGB | 0 | 0,1–0,2 |
| Acero desnudo | 140–170 sRGB, metálico | 1 | 0,4–0,7 |
| Aluminio fundido (motor) | 170–200 | 1 | 0,35–0,55 |
| Pintura de coche | según color, nunca < 30 ni > 240 | 0 (barniz por coat) | 0,8–0,95 nueva, 0,5–0,7 vieja |
| Goma / neumático | 20–35 | 0 | 0,15–0,3 |
| Plástico negro del vano | 25–40 | 0 | 0,25–0,45 |
| Óxido | 70–110 (marrón anaranjado) | 0 | 0,1–0,25 |
| Grasa/aceite | 15–30 | 0 | 0,6–0,9 (película brillante) |

Nada es negro puro (0) ni blanco puro (255).

## Desgaste: evitar coches "demasiado limpios"
- Un coche de 8 años y 120.000 km tiene: polvo en el vano, grasa alrededor de juntas, óxido superficial en
  tornillería y abrazaderas, plásticos decolorados, tierra en pasos de rueda, marcas en la parte baja de la carrocería.
- Parámetros: `ageYears`, `kilometers`, `maintenance` → suciedad, óxido, grasa y decoloración (`AppearanceDefinition`).
  Valores de referencia: coche nuevo bien cuidado = suciedad < 0,2; coche "normal" 0,4–0,6; abandonado > 0,8.
- Las piezas averiadas se ven acordes cuando en la realidad se ve: manguito agrietado, conector con óxido verde,
  bujía con hollín. Muchas averías **no** se ven (sensor sesgado): no las delates visualmente.
- El taller también está usado: manchas de aceite y marcas de neumático (decals), polvo en estanterías,
  pintura del elevador gastada en los brazos.

## Cámara y postproceso
- Exposición física automática, tonemapping ACES, bloom muy sutil (0,05–0,1), grano 0,05–0,1, viñeta ≤ 0,2.
- Profundidad de campo sólo al inspeccionar de cerca (< 1,2 m), enfocando la pieza mirada.
- FOV 65–75°, altura de ojos 1,65 m, cabeceo al andar muy leve (≈1 cm).
