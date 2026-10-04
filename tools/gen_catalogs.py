"""DTC catalog, parts, customers, scenarios, jobs and upgrades for data/base (see gen_content.py)."""

# ----------------------------------------------------------------------------------------------
# Generic SAE J2012 codes (descriptions in English as standardised, Spanish translation, system,
# trips to confirm, MIL, typical causes). Only generic P0xxx/P2xxx/U0xxx codes are used.
# ----------------------------------------------------------------------------------------------

def circuit_family(base, name_en, name_es, system, causes, low="Low", high="High"):
    """Circuit / Range / Low / High / Intermittent family for a sensor."""
    b = int(base[1:], 16) if base[1:].isalnum() and not base[1:].isdigit() else int(base[1:])
    p = base[0]
    def code(n):
        return f"{p}{b + n:04d}"
    return [
        (code(0), f"{name_en} Circuit", f"{name_es}: circuito", system, 1, causes),
        (code(1), f"{name_en} Circuit Range/Performance", f"{name_es}: rango/funcionamiento del circuito", system, 2, causes),
        (code(2), f"{name_en} Circuit {low}", f"{name_es}: señal baja en el circuito", system, 1, ["Cortocircuito a masa", "Circuito abierto de señal/referencia", "Sensor averiado"]),
        (code(3), f"{name_en} Circuit {high}", f"{name_es}: señal alta en el circuito", system, 1, ["Circuito abierto de masa", "Corto a positivo", "Sensor averiado"]),
        (code(4), f"{name_en} Circuit Intermittent", f"{name_es}: circuito intermitente", system, 2, ["Conector flojo u oxidado", "Cableado rozado"]),
    ]


DTC = []
DTC += [
    ("P0010", '"A" Camshaft Position Actuator Circuit (Bank 1)', 'Actuador de posición del árbol de levas "A": circuito (banco 1)', "vvt", 1, ["Electroválvula VVT", "Cableado"]),
    ("P0011", '"A" Camshaft Position - Timing Over-Advanced or System Performance (Bank 1)', 'Posición del árbol de levas "A": avance excesivo o funcionamiento (banco 1)', "vvt", 2, ["Aceite incorrecto o bajo nivel", "Electroválvula VVT atascada"]),
    ("P0012", '"A" Camshaft Position - Timing Over-Retarded (Bank 1)', 'Posición del árbol de levas "A": retraso excesivo (banco 1)', "vvt", 2, ["Electroválvula VVT", "Cadena estirada"]),
    ("P0016", "Crankshaft Position - Camshaft Position Correlation (Bank 1 Sensor A)", "Correlación posición cigüeñal - árbol de levas (banco 1, sensor A)", "ignition", 2, ["Cadena/correa de distribución estirada o saltada", "Sensor CKP/CMP", "Rueda fónica dañada"]),
    ("P0030", "HO2S Heater Control Circuit (Bank 1 Sensor 1)", "Control del calefactor de sonda lambda: circuito (B1S1)", "o2", 2, ["Calefactor de sonda abierto", "Fusible", "Cableado"]),
    ("P0031", "HO2S Heater Control Circuit Low (Bank 1 Sensor 1)", "Control del calefactor de sonda lambda: señal baja (B1S1)", "o2", 2, ["Corto a masa", "Calefactor abierto"]),
    ("P0032", "HO2S Heater Control Circuit High (Bank 1 Sensor 1)", "Control del calefactor de sonda lambda: señal alta (B1S1)", "o2", 2, ["Corto a positivo"]),
    ("P0036", "HO2S Heater Control Circuit (Bank 1 Sensor 2)", "Control del calefactor de sonda lambda: circuito (B1S2)", "o2", 2, ["Calefactor abierto", "Fusible"]),
    ("P0037", "HO2S Heater Control Circuit Low (Bank 1 Sensor 2)", "Control del calefactor de sonda lambda: señal baja (B1S2)", "o2", 2, ["Corto a masa"]),
    ("P0038", "HO2S Heater Control Circuit High (Bank 1 Sensor 2)", "Control del calefactor de sonda lambda: señal alta (B1S2)", "o2", 2, ["Corto a positivo"]),
    ("P0045", 'Turbocharger/Supercharger Boost Control "A" Circuit/Open', 'Control de presión del turbo "A": circuito abierto', "turbo", 1, ["Electroválvula de wastegate", "Cableado"]),
    ("P0046", 'Turbocharger/Supercharger Boost Control "A" Circuit Range/Performance', 'Control de presión del turbo "A": rango/funcionamiento', "turbo", 2, ["Actuador del turbo", "Fuga de vacío del actuador"]),
    ("P0047", 'Turbocharger/Supercharger Boost Control "A" Circuit Low', 'Control de presión del turbo "A": señal baja', "turbo", 1, ["Corto a masa"]),
    ("P0048", 'Turbocharger/Supercharger Boost Control "A" Circuit High', 'Control de presión del turbo "A": señal alta', "turbo", 1, ["Corto a positivo"]),
    ("P0068", "MAP/MAF - Throttle Position Correlation", "Correlación MAP/MAF - posición de mariposa", "fuel_air", 2, ["Fuga de vacío grande", "Caudalímetro", "Mariposa sucia"]),
    ("P0069", "Manifold Absolute Pressure - Barometric Pressure Correlation", "Correlación presión de colector - presión barométrica", "fuel_air", 2, ["Sensor MAP sesgado"]),
    ("P0087", "Fuel Rail/System Pressure - Too Low", "Presión de rampa/sistema de combustible demasiado baja", "fuel_air", 2, ["Bomba débil", "Filtro de combustible obstruido", "Regulador defectuoso", "Fusible/relé de bomba"]),
    ("P0088", "Fuel Rail/System Pressure - Too High", "Presión de rampa/sistema de combustible demasiado alta", "fuel_air", 2, ["Regulador atascado", "Retorno obstruido"]),
    ("P0089", "Fuel Pressure Regulator 1 Performance", "Regulador de presión de combustible 1: funcionamiento", "fuel_air", 2, ["Regulador"]),
    ("P0090", "Fuel Pressure Regulator 1 Control Circuit", "Regulador de presión de combustible 1: circuito de control", "fuel_air", 1, ["Cableado", "Regulador"]),
]
DTC += circuit_family("P0100", 'Mass or Volume Air Flow "A"', 'Caudalímetro de aire "A"', "fuel_air", ["Caudalímetro sucio o averiado", "Fuga de aire tras el caudalímetro", "Cableado/conector"])
DTC += circuit_family("P0105", "Manifold Absolute Pressure/Barometric Pressure", "Sensor de presión absoluta de colector/barométrica", "fuel_air", ["Sensor MAP", "Toma de vacío obstruida", "Cableado"])
DTC += circuit_family("P0110", "Intake Air Temperature Sensor 1", "Sensor de temperatura del aire de admisión 1", "fuel_air", ["Sensor IAT", "Cableado/conector"])
DTC += circuit_family("P0115", "Engine Coolant Temperature Sensor 1", "Sensor de temperatura del refrigerante 1", "cooling", ["Sensor ECT", "Cableado/conector", "Termostato"])
DTC += circuit_family("P0120", 'Throttle/Pedal Position Sensor/Switch "A"', 'Sensor de posición de mariposa/pedal "A"', "fuel_air", ["Sensor TPS", "Cableado"])
DTC += [
    ("P0125", "Insufficient Coolant Temperature for Closed Loop Fuel Control", "Temperatura de refrigerante insuficiente para lazo cerrado", "cooling", 2, ["Termostato abierto", "Sensor ECT"]),
    ("P0128", "Coolant Thermostat (Coolant Temperature Below Thermostat Regulating Temperature)", "Termostato: temperatura del refrigerante por debajo de la de regulación", "cooling", 2, ["Termostato atascado abierto", "Sensor ECT sesgado", "Ventilador siempre funcionando"]),
    ("P0130", "O2 Sensor Circuit (Bank 1 Sensor 1)", "Sonda lambda: circuito (B1S1)", "o2", 2, ["Sonda", "Cableado"]),
    ("P0131", "O2 Sensor Circuit Low Voltage (Bank 1 Sensor 1)", "Sonda lambda: tensión baja (B1S1)", "o2", 2, ["Corto a masa", "Fuga de escape", "Mezcla pobre real"]),
    ("P0132", "O2 Sensor Circuit High Voltage (Bank 1 Sensor 1)", "Sonda lambda: tensión alta (B1S1)", "o2", 2, ["Corto a positivo", "Sonda contaminada"]),
    ("P0133", "O2 Sensor Circuit Slow Response (Bank 1 Sensor 1)", "Sonda lambda: respuesta lenta (B1S1)", "o2", 2, ["Sonda envejecida/contaminada", "Fuga de escape"]),
    ("P0134", "O2 Sensor Circuit No Activity Detected (Bank 1 Sensor 1)", "Sonda lambda: sin actividad (B1S1)", "o2", 2, ["Señal abierta", "Calefactor averiado", "Sonda muerta"]),
    ("P0135", "O2 Sensor Heater Circuit (Bank 1 Sensor 1)", "Calefactor de sonda lambda: circuito (B1S1)", "o2", 2, ["Calefactor abierto", "Fusible", "Cableado"]),
    ("P0136", "O2 Sensor Circuit (Bank 1 Sensor 2)", "Sonda lambda: circuito (B1S2)", "o2", 2, ["Sonda", "Cableado"]),
    ("P0137", "O2 Sensor Circuit Low Voltage (Bank 1 Sensor 2)", "Sonda lambda: tensión baja (B1S2)", "o2", 2, ["Corto a masa", "Fuga de escape"]),
    ("P0138", "O2 Sensor Circuit High Voltage (Bank 1 Sensor 2)", "Sonda lambda: tensión alta (B1S2)", "o2", 2, ["Corto a positivo"]),
    ("P0139", "O2 Sensor Circuit Slow Response (Bank 1 Sensor 2)", "Sonda lambda: respuesta lenta (B1S2)", "o2", 2, ["Sonda envejecida"]),
    ("P0140", "O2 Sensor Circuit No Activity Detected (Bank 1 Sensor 2)", "Sonda lambda: sin actividad (B1S2)", "o2", 2, ["Circuito abierto"]),
    ("P0141", "O2 Sensor Heater Circuit (Bank 1 Sensor 2)", "Calefactor de sonda lambda: circuito (B1S2)", "o2", 2, ["Calefactor abierto", "Fusible"]),
    ("P0170", "Fuel Trim (Bank 1)", "Corrección de combustible (banco 1)", "fuel_air", 2, ["Fuga de vacío", "Presión de combustible", "Inyectores"]),
    ("P0171", "System Too Lean (Bank 1)", "Sistema demasiado pobre (banco 1)", "fuel_air", 2, ["Fuga de vacío/aire no medido", "Caudalímetro sucio", "Presión de combustible baja", "Inyectores obstruidos"]),
    ("P0172", "System Too Rich (Bank 1)", "Sistema demasiado rico (banco 1)", "fuel_air", 2, ["Inyector que gotea", "Regulador de presión", "Fuga de presión de turbo (aire medido que se pierde)", "Sensor sesgado"]),
    ("P0174", "System Too Lean (Bank 2)", "Sistema demasiado pobre (banco 2)", "fuel_air", 2, ["Fuga de vacío", "Presión de combustible baja"]),
    ("P0175", "System Too Rich (Bank 2)", "Sistema demasiado rico (banco 2)", "fuel_air", 2, ["Inyector que gotea"]),
    ("P0180", "Fuel Temperature Sensor A Circuit", "Sensor de temperatura de combustible A: circuito", "fuel_air", 2, ["Sensor", "Cableado"]),
]
DTC += circuit_family("P0190", 'Fuel Rail Pressure Sensor "A"', 'Sensor de presión de rampa de combustible "A"', "fuel_air", ["Sensor de presión de rampa", "Cableado"])
DTC += [("P0200", "Injector Circuit/Open", "Circuito de inyectores abierto", "fuel_air", 1, ["Fusible de inyectores", "Cableado"])]
for i in range(1, 7):
    DTC.append((f"P020{i}", f"Injector Circuit/Open - Cylinder {i}", f"Inyector cilindro {i}: circuito abierto", "fuel_air", 1, [f"Inyector {i} con bobinado abierto", "Cable de control abierto", "Conector"]))
DTC += [
    ("P0217", "Engine Coolant Over Temperature Condition", "Sobretemperatura del refrigerante", "cooling", 1, ["Ventilador averiado", "Termostato atascado cerrado", "Bajo nivel de refrigerante", "Junta de culata"]),
    ("P0218", "Transmission Fluid Over Temperature Condition", "Sobretemperatura del aceite de la transmisión", "transmission", 2, ["Uso intensivo", "Enfriador"]),
    ("P0219", "Engine Overspeed Condition", "Condición de sobrerrégimen del motor", "engine", 1, ["Reducción de marcha incorrecta", "Limitador modificado"]),
    ("P0220", 'Throttle/Pedal Position Sensor/Switch "B" Circuit', 'Sensor de posición de mariposa/pedal "B": circuito', "fuel_air", 1, ["Sensor", "Cableado"]),
    ("P0221", 'Throttle/Pedal Position Sensor/Switch "B" Circuit Range/Performance', 'Sensor de posición "B": rango/funcionamiento', "fuel_air", 2, ["Sensor"]),
    ("P0222", 'Throttle/Pedal Position Sensor/Switch "B" Circuit Low', 'Sensor de posición "B": señal baja', "fuel_air", 1, ["Corto a masa"]),
    ("P0223", 'Throttle/Pedal Position Sensor/Switch "B" Circuit High', 'Sensor de posición "B": señal alta', "fuel_air", 1, ["Circuito abierto de masa"]),
    ("P0230", "Fuel Pump Primary Circuit", "Bomba de combustible: circuito primario", "fuel_air", 1, ["Relé de bomba", "Cable de control del relé", "Fusible"]),
    ("P0231", "Fuel Pump Secondary Circuit Low", "Bomba de combustible: circuito secundario bajo", "fuel_air", 1, ["Corto a masa"]),
    ("P0232", "Fuel Pump Secondary Circuit High", "Bomba de combustible: circuito secundario alto", "fuel_air", 1, ["Corto a positivo"]),
    ("P0234", 'Turbocharger/Supercharger "A" Overboost Condition', 'Turbo "A": sobrepresión', "turbo", 1, ["Wastegate atascada cerrada", "Electroválvula N75", "Mapa modificado"]),
    ("P0235", 'Turbocharger/Supercharger Boost Sensor "A" Circuit', 'Sensor de presión del turbo "A": circuito', "turbo", 1, ["Sensor", "Cableado"]),
    ("P0236", 'Turbocharger/Supercharger Boost Sensor "A" Circuit Range/Performance', 'Sensor de presión del turbo "A": rango/funcionamiento', "turbo", 2, ["Sensor", "Fuga de presión"]),
    ("P0237", 'Turbocharger/Supercharger Boost Sensor "A" Circuit Low', 'Sensor de presión del turbo "A": señal baja', "turbo", 1, ["Corto a masa", "Circuito abierto"]),
    ("P0238", 'Turbocharger/Supercharger Boost Sensor "A" Circuit High', 'Sensor de presión del turbo "A": señal alta', "turbo", 1, ["Masa abierta", "Corto a positivo"]),
    ("P0243", 'Turbocharger/Supercharger Wastegate Solenoid "A"', 'Electroválvula de wastegate "A"', "turbo", 1, ["Electroválvula abierta", "Cableado", "Fusible"]),
    ("P0244", 'Turbocharger/Supercharger Wastegate Solenoid "A" Range/Performance', 'Electroválvula de wastegate "A": rango/funcionamiento', "turbo", 2, ["Electroválvula"]),
    ("P0245", 'Turbocharger/Supercharger Wastegate Solenoid "A" Low', 'Electroválvula de wastegate "A": señal baja', "turbo", 1, ["Corto a masa"]),
    ("P0246", 'Turbocharger/Supercharger Wastegate Solenoid "A" High', 'Electroválvula de wastegate "A": señal alta', "turbo", 1, ["Corto a positivo", "Bobinado en corto"]),
]
for i, (lo, hi) in enumerate([("P0261", "P0262"), ("P0264", "P0265"), ("P0267", "P0268"), ("P0270", "P0271"), ("P0273", "P0274"), ("P0276", "P0277")], 1):
    DTC.append((lo, f'Cylinder {i} Injector "A" Circuit Low', f"Inyector cilindro {i}: circuito bajo", "fuel_air", 1, ["Corto a masa del cable de control"]))
    DTC.append((hi, f'Cylinder {i} Injector "A" Circuit High', f"Inyector cilindro {i}: circuito alto", "fuel_air", 1, ["Bobinado en corto", "Corto a positivo"]))
DTC += [
    ("P0299", 'Turbocharger/Supercharger "A" Underboost Condition', 'Turbo "A": presión insuficiente', "turbo", 2, ["Fuga en manguitos/intercooler", "Wastegate atascada abierta", "Electroválvula N75", "Turbo desgastado"]),
    ("P0300", "Random/Multiple Cylinder Misfire Detected", "Fallos de encendido aleatorios/múltiples", "ignition", 2, ["Mezcla pobre general", "Presión de combustible", "Bujías desgastadas", "Fuga de vacío"]),
]
for i in range(1, 7):
    DTC.append((f"P030{i}", f"Cylinder {i} Misfire Detected", f"Fallo de encendido en el cilindro {i}", "ignition", 2, [f"Bujía o bobina del cilindro {i}", f"Inyector {i}", f"Compresión baja en cilindro {i}", "Junta de admisión"]))
DTC += [
    ("P0313", "Misfire Detected with Low Fuel", "Fallo de encendido con bajo nivel de combustible", "ignition", 2, ["Depósito casi vacío"]),
    ("P0316", "Engine Misfire Detected on Startup (First 1000 Revolutions)", "Fallo de encendido en el arranque (primeras 1000 vueltas)", "ignition", 2, ["Bujías", "Inyector que gotea"]),
    ("P0325", "Knock Sensor 1 Circuit (Bank 1 or Single Sensor)", "Sensor de detonación 1: circuito", "ignition", 2, ["Sensor de picado", "Par de apriete incorrecto", "Cableado"]),
    ("P0326", "Knock Sensor 1 Circuit Range/Performance (Bank 1 or Single Sensor)", "Sensor de detonación 1: rango/funcionamiento", "ignition", 2, ["Sensor flojo"]),
    ("P0327", "Knock Sensor 1 Circuit Low (Bank 1 or Single Sensor)", "Sensor de detonación 1: señal baja", "ignition", 2, ["Circuito abierto", "Sensor flojo o averiado"]),
    ("P0328", "Knock Sensor 1 Circuit High (Bank 1 or Single Sensor)", "Sensor de detonación 1: señal alta", "ignition", 2, ["Corto a positivo", "Ruido mecánico"]),
    ("P0335", 'Crankshaft Position Sensor "A" Circuit', 'Sensor de posición del cigüeñal "A": circuito', "ignition", 1, ["Sensor CKP", "Cableado/conector", "Entrehierro excesivo"]),
    ("P0336", 'Crankshaft Position Sensor "A" Circuit Range/Performance', 'Sensor de posición del cigüeñal "A": rango/funcionamiento', "ignition", 1, ["Rueda fónica dañada", "Señal intermitente"]),
    ("P0337", 'Crankshaft Position Sensor "A" Circuit Low', 'Sensor de posición del cigüeñal "A": señal baja', "ignition", 1, ["Corto a masa"]),
    ("P0338", 'Crankshaft Position Sensor "A" Circuit High', 'Sensor de posición del cigüeñal "A": señal alta', "ignition", 1, ["Corto a positivo"]),
    ("P0339", 'Crankshaft Position Sensor "A" Circuit Intermittent', 'Sensor de posición del cigüeñal "A": intermitente', "ignition", 1, ["Conector", "Cableado"]),
    ("P0340", 'Camshaft Position Sensor "A" Circuit (Bank 1 or Single Sensor)', 'Sensor de posición del árbol de levas "A": circuito', "ignition", 1, ["Sensor CMP", "Alimentación 5 V", "Cableado"]),
    ("P0341", 'Camshaft Position Sensor "A" Circuit Range/Performance (Bank 1 or Single Sensor)', 'Sensor de árbol de levas "A": rango/funcionamiento', "ignition", 2, ["Distribución", "Sensor"]),
    ("P0342", 'Camshaft Position Sensor "A" Circuit Low (Bank 1 or Single Sensor)', 'Sensor de árbol de levas "A": señal baja', "ignition", 1, ["Corto a masa"]),
    ("P0343", 'Camshaft Position Sensor "A" Circuit High (Bank 1 or Single Sensor)', 'Sensor de árbol de levas "A": señal alta', "ignition", 1, ["Masa abierta"]),
]
for i, letter in enumerate("ABCDEF", 1):
    DTC.append((f"P035{i}", f'Ignition Coil "{letter}" Primary/Secondary Circuit', f'Bobina de encendido "{letter}": circuito primario/secundario', "ignition", 1, [f"Bobina del cilindro {i}", "Cable de disparo", "Alimentación"]))
DTC += [
    ("P0380", 'Glow Plug/Heater Circuit "A"', 'Circuito de calentadores "A"', "diesel", 1, ["Calentador", "Relé de calentadores"]),
    ("P0400", 'Exhaust Gas Recirculation "A" Flow', 'Recirculación de gases de escape "A": caudal', "emissions", 2, ["Válvula EGR", "Conductos obstruidos"]),
    ("P0401", 'Exhaust Gas Recirculation "A" Flow Insufficient Detected', 'EGR "A": caudal insuficiente', "emissions", 2, ["Carbonilla en conductos", "Válvula EGR atascada cerrada"]),
    ("P0402", 'Exhaust Gas Recirculation "A" Flow Excessive Detected', 'EGR "A": caudal excesivo', "emissions", 2, ["Válvula EGR atascada abierta"]),
    ("P0403", 'Exhaust Gas Recirculation "A" Control Circuit', 'EGR "A": circuito de control', "emissions", 1, ["Cableado", "Válvula"]),
    ("P0404", 'Exhaust Gas Recirculation "A" Control Circuit Range/Performance', 'EGR "A": rango/funcionamiento del control', "emissions", 2, ["Válvula EGR"]),
    ("P0420", "Catalyst System Efficiency Below Threshold (Bank 1)", "Eficiencia del catalizador por debajo del umbral (banco 1)", "emissions", 2, ["Catalizador agotado", "Fuga de escape", "Sonda postcatalizador"]),
    ("P0421", "Warm Up Catalyst Efficiency Below Threshold (Bank 1)", "Eficiencia del precatalizador por debajo del umbral (banco 1)", "emissions", 2, ["Precatalizador"]),
    ("P0440", "Evaporative Emission System", "Sistema de control de emisiones evaporativas", "evap", 2, ["Tapón de combustible", "Cánister"]),
    ("P0441", "Evaporative Emission System Incorrect Purge Flow", "Sistema EVAP: caudal de purga incorrecto", "evap", 2, ["Válvula de purga atascada abierta", "Tubos"]),
    ("P0442", "Evaporative Emission System Leak Detected (small leak)", "Sistema EVAP: fuga pequeña", "evap", 2, ["Tapón", "Tubos"]),
    ("P0443", 'Evaporative Emission System Purge Control Valve "A" Circuit', 'Sistema EVAP: circuito de la válvula de purga "A"', "evap", 1, ["Válvula de purga", "Cableado"]),
    ("P0446", "Evaporative Emission System Vent Control Circuit", "Sistema EVAP: circuito de la válvula de ventilación", "evap", 2, ["Válvula de ventilación"]),
    ("P0455", "Evaporative Emission System Leak Detected (large leak)", "Sistema EVAP: fuga grande", "evap", 2, ["Tapón de combustible suelto"]),
    ("P0456", "Evaporative Emission System Leak Detected (very small leak)", "Sistema EVAP: fuga muy pequeña", "evap", 2, ["Juntas"]),
    ("P0447", "Evaporative Emission System Vent Control Circuit Open", "Sistema EVAP: circuito de ventilación abierto", "evap", 1, ["Válvula de ventilación", "Cableado"]),
    ("P0449", "Evaporative Emission System Vent Valve/Solenoid Circuit", "Sistema EVAP: circuito de la electroválvula de ventilación", "evap", 1, ["Electroválvula de ventilación"]),
    ("P0451", "Evaporative Emission System Pressure Sensor/Switch Range/Performance", "Sistema EVAP: sensor de presión del depósito, rango/funcionamiento", "evap", 2, ["Sensor de presión del depósito"]),
    ("P0452", "Evaporative Emission System Pressure Sensor/Switch Low", "Sistema EVAP: sensor de presión del depósito, señal baja", "evap", 1, ["Corto a masa", "Sensor"]),
    ("P0453", "Evaporative Emission System Pressure Sensor/Switch High", "Sistema EVAP: sensor de presión del depósito, señal alta", "evap", 1, ["Masa abierta", "Sensor"]),
    ("P0457", "Evaporative Emission System Leak Detected (fuel cap loose/off)", "Sistema EVAP: fuga detectada (tapón suelto o ausente)", "evap", 1, ["Tapón de combustible suelto o sin junta"]),
    ("P0496", "Evaporative Emission System High Purge Flow", "Sistema EVAP: caudal de purga excesivo", "evap", 2, ["Válvula de purga atascada abierta"]),
    ("P0458", 'Evaporative Emission System Purge Control Valve "A" Circuit Low', 'Válvula de purga "A": circuito bajo', "evap", 1, ["Corto a masa"]),
    ("P0459", 'Evaporative Emission System Purge Control Valve "A" Circuit High', 'Válvula de purga "A": circuito alto', "evap", 1, ["Corto a positivo"]),
    ("P0480", "Fan 1 Control Circuit", "Ventilador 1: circuito de control", "cooling", 1, ["Relé del ventilador", "Cableado"]),
    ("P0481", "Fan 2 Control Circuit", "Ventilador 2: circuito de control", "cooling", 1, ["Relé", "Cableado"]),
    ("P0483", "Fan Rationality Check", "Ventilador: comprobación de plausibilidad", "cooling", 2, ["Motor del ventilador"]),
    ("P0500", 'Vehicle Speed Sensor "A"', 'Sensor de velocidad del vehículo "A"', "speed_idle", 2, ["Sensor de rueda", "Comunicación con ABS"]),
    ("P0501", 'Vehicle Speed Sensor "A" Range/Performance', 'Sensor de velocidad "A": rango/funcionamiento', "speed_idle", 2, ["Sensor"]),
    ("P0505", "Idle Air Control System", "Sistema de control de ralentí", "speed_idle", 2, ["Mariposa sucia", "Fuga de vacío"]),
    ("P0506", "Idle Air Control System - RPM Lower Than Expected", "Control de ralentí: rpm inferiores a lo esperado", "speed_idle", 2, ["Mariposa sucia/obstruida", "Carga excesiva"]),
    ("P0507", "Idle Air Control System - RPM Higher Than Expected", "Control de ralentí: rpm superiores a lo esperado", "speed_idle", 2, ["Fuga de vacío", "Mariposa atascada"]),
    ("P0520", 'Engine Oil Pressure Sensor/Switch "A" Circuit', 'Sensor/interruptor de presión de aceite "A": circuito', "engine", 2, ["Sensor", "Cableado"]),
    ("P0521", 'Engine Oil Pressure Sensor/Switch "A" Range/Performance', 'Presión de aceite "A": rango/funcionamiento', "engine", 2, ["Bajo nivel de aceite", "Bomba de aceite", "Cojinetes desgastados"]),
    ("P0560", "System Voltage", "Tensión del sistema", "electrical", 2, ["Batería", "Alternador"]),
    ("P0562", "System Voltage Low", "Tensión del sistema baja", "electrical", 2, ["Alternador averiado", "Correa", "Batería", "Masas"]),
    ("P0563", "System Voltage High", "Tensión del sistema alta", "electrical", 2, ["Regulador del alternador"]),
    ("P0571", 'Brake Switch "A" Circuit', 'Interruptor de freno "A": circuito', "electrical", 2, ["Interruptor de freno"]),
    ("P0600", "Serial Communication Link", "Enlace de comunicación serie", "ecu", 1, ["Comunicación interna"]),
    ("P0601", "Internal Control Module Memory Check Sum Error", "Módulo de control: error de suma de comprobación de memoria", "ecu", 1, ["ECU averiada", "Reprogramación fallida"]),
    ("P0602", "Control Module Programming Error", "Módulo de control: error de programación", "ecu", 1, ["Software corrupto"]),
    ("P0603", "Internal Control Module Keep Alive Memory (KAM) Error", "Módulo de control: error de memoria KAM", "ecu", 2, ["Desconexión de batería", "ECU"]),
    ("P0604", "Internal Control Module Random Access Memory (RAM) Error", "Módulo de control: error de RAM", "ecu", 1, ["ECU"]),
    ("P0605", "Internal Control Module Read Only Memory (ROM) Error", "Módulo de control: error de ROM", "ecu", 1, ["ECU"]),
    ("P0606", "Control Module Processor", "Procesador del módulo de control", "ecu", 1, ["ECU"]),
    ("P0607", "Control Module Performance", "Rendimiento del módulo de control", "ecu", 1, ["ECU"]),
    ("P0627", 'Fuel Pump "A" Control Circuit/Open', 'Bomba de combustible "A": circuito de control abierto', "fuel_air", 1, ["Relé", "Cableado"]),
    ("P0628", 'Fuel Pump "A" Control Circuit Low', 'Bomba de combustible "A": circuito de control bajo', "fuel_air", 1, ["Corto a masa"]),
    ("P0629", 'Fuel Pump "A" Control Circuit High', 'Bomba de combustible "A": circuito de control alto', "fuel_air", 1, ["Corto a positivo"]),
    ("P0638", 'Throttle Actuator "A" Control Range/Performance (Bank 1)', 'Actuador de mariposa "A": rango/funcionamiento (banco 1)', "fuel_air", 1, ["Mariposa sucia o atascada", "Motor de mariposa"]),
    ("P0641", 'Sensor Reference Voltage "A" Circuit/Open', 'Tensión de referencia de sensores "A": circuito abierto', "ecu", 1, ["Sensor en corto que tira de la referencia", "Cableado"]),
    ("P0651", 'Sensor Reference Voltage "B" Circuit/Open', 'Tensión de referencia de sensores "B": circuito abierto', "ecu", 1, ["Cableado"]),
    ("P0670", "Glow Plug Control Module Circuit", "Módulo de control de calentadores: circuito", "diesel", 1, ["Módulo de calentadores"]),
]
for i in range(1, 5):
    DTC.append((f"P067{i}", f"Cylinder {i} Glow Plug Circuit", f"Calentador del cilindro {i}: circuito", "diesel", 1, [f"Calentador {i} abierto"]))
DTC += [
    ("P0700", "Transmission Control System (MIL Request)", "Sistema de control de la transmisión (solicitud de MIL)", "transmission", 1, ["Leer códigos del módulo de cambio"]),
    ("P2096", "Post Catalyst Fuel Trim System Too Lean (Bank 1)", "Corrección postcatalizador: demasiado pobre (banco 1)", "fuel_air", 2, ["Fuga de escape", "Sonda"]),
    ("P2097", "Post Catalyst Fuel Trim System Too Rich (Bank 1)", "Corrección postcatalizador: demasiado rica (banco 1)", "fuel_air", 2, ["Sonda", "Inyector"]),
    ("P2100", 'Throttle Actuator "A" Control Motor Circuit/Open', 'Motor del actuador de mariposa "A": circuito abierto', "fuel_air", 1, ["Motor de mariposa", "Cableado"]),
    ("P2101", 'Throttle Actuator "A" Control Motor Circuit Range/Performance', 'Motor del actuador de mariposa "A": rango/funcionamiento', "fuel_air", 1, ["Mariposa atascada/sucia", "Motor"]),
    ("P2106", 'Throttle Actuator "A" Control System - Forced Limited Power', 'Control de mariposa "A": potencia limitada forzada', "fuel_air", 1, ["Fallo en sensores de mariposa/pedal"]),
    ("P2119", 'Throttle Actuator "A" Control Throttle Body Range/Performance', 'Cuerpo de mariposa "A": rango/funcionamiento', "fuel_air", 1, ["Muelle de retorno", "Mariposa"]),
    ("P2122", 'Throttle/Pedal Position Sensor/Switch "D" Circuit Low', 'Sensor de posición de pedal "D": señal baja', "fuel_air", 1, ["Circuito abierto", "Corto a masa"]),
    ("P2123", 'Throttle/Pedal Position Sensor/Switch "D" Circuit High', 'Sensor de posición de pedal "D": señal alta', "fuel_air", 1, ["Masa abierta", "Corto a positivo"]),
    ("P2127", 'Throttle/Pedal Position Sensor/Switch "E" Circuit Low', 'Sensor de posición de pedal "E": señal baja', "fuel_air", 1, ["Corto a masa"]),
    ("P2128", 'Throttle/Pedal Position Sensor/Switch "E" Circuit High', 'Sensor de posición de pedal "E": señal alta', "fuel_air", 1, ["Masa abierta"]),
    ("P2135", 'Throttle/Pedal Position Sensor/Switch "A"/"B" Voltage Correlation', 'Correlación de tensión sensores de mariposa "A"/"B"', "fuel_air", 1, ["Sensor de mariposa", "Conector"]),
    ("P2138", 'Throttle/Pedal Position Sensor/Switch "D"/"E" Voltage Correlation', 'Correlación de tensión sensores de pedal "D"/"E"', "fuel_air", 1, ["Sensor de pedal"]),
    ("P2187", "System Too Lean at Idle (Bank 1)", "Sistema demasiado pobre al ralentí (banco 1)", "fuel_air", 2, ["Fuga de vacío", "Junta de admisión"]),
    ("P2188", "System Too Rich at Idle (Bank 1)", "Sistema demasiado rico al ralentí (banco 1)", "fuel_air", 2, ["Inyector que gotea", "Purga del cánister"]),
    ("P2195", "O2 Sensor Signal Biased/Stuck Lean (Bank 1 Sensor 1)", "Señal de sonda sesgada/bloqueada en pobre (B1S1)", "o2", 2, ["Sonda", "Fuga de escape"]),
    ("P2196", "O2 Sensor Signal Biased/Stuck Rich (Bank 1 Sensor 1)", "Señal de sonda sesgada/bloqueada en rico (B1S1)", "o2", 2, ["Sonda", "Inyector que gotea"]),
    ("P2237", "O2 Sensor Positive Current Control Circuit/Open (Bank 1 Sensor 1)", "Sonda: circuito de control de corriente positiva abierto (B1S1)", "o2", 2, ["Sonda de banda ancha", "Cableado"]),
    ("P2243", "O2 Sensor Reference Voltage Circuit/Open (Bank 1 Sensor 1)", "Sonda: circuito de tensión de referencia abierto (B1S1)", "o2", 2, ["Sonda de banda ancha", "Cableado"]),
    ("P2263", "Turbocharger/Supercharger Boost System Performance", "Sistema de sobrealimentación: funcionamiento", "turbo", 2, ["Fugas", "Turbo", "Actuador"]),
    ("P2279", "Intake Air System Leak", "Fuga en el sistema de admisión", "fuel_air", 2, ["Manguito de vacío", "Junta de admisión"]),
    ("P242F", "Diesel Particulate Filter Restriction - Ash Accumulation", "Filtro de partículas: obstrucción por cenizas", "diesel", 2, ["Filtro de partículas"]),
    ("P2002", "Diesel Particulate Filter Efficiency Below Threshold (Bank 1)", "Filtro de partículas: eficiencia por debajo del umbral (banco 1)", "diesel", 2, ["Filtro agrietado o vaciado", "Tubos del sensor de presión diferencial"]),
    ("P244A", "Diesel Particulate Filter Differential Pressure Too Low (Bank 1)", "Filtro de partículas: presión diferencial demasiado baja (banco 1)", "diesel", 2, ["Filtro agrietado", "Tubo del sensor suelto"]),
    ("P244B", "Diesel Particulate Filter Differential Pressure Too High (Bank 1)", "Filtro de partículas: presión diferencial demasiado alta (banco 1)", "diesel", 2, ["Filtro saturado", "Cenizas"]),
    ("P2452", 'Diesel Particulate Filter Differential Pressure Sensor "A" Circuit', 'Sensor de presión diferencial del FAP "A": circuito', "diesel", 1, ["Sensor", "Cableado"]),
    ("P2453", 'Diesel Particulate Filter Differential Pressure Sensor "A" Circuit Range/Performance', 'Sensor de presión diferencial del FAP "A": rango/funcionamiento', "diesel", 2, ["Tubos del sensor obstruidos", "Sensor"]),
    ("P2454", 'Diesel Particulate Filter Differential Pressure Sensor "A" Circuit Low', 'Sensor de presión diferencial del FAP "A": señal baja', "diesel", 1, ["Corto a masa"]),
    ("P2455", 'Diesel Particulate Filter Differential Pressure Sensor "A" Circuit High', 'Sensor de presión diferencial del FAP "A": señal alta', "diesel", 1, ["Masa abierta"]),
    ("P0544", "Exhaust Gas Temperature Sensor Circuit (Bank 1 Sensor 1)", "Sensor de temperatura de escape: circuito (banco 1, sensor 1)", "diesel", 1, ["Sensor", "Cableado"]),
    ("P0545", "Exhaust Gas Temperature Sensor Circuit Low (Bank 1 Sensor 1)", "Sensor de temperatura de escape: señal baja (B1S1)", "diesel", 1, ["Corto a masa"]),
    ("P0546", "Exhaust Gas Temperature Sensor Circuit High (Bank 1 Sensor 1)", "Sensor de temperatura de escape: señal alta (B1S1)", "diesel", 1, ["Sensor abierto"]),
    ("P0089", "Fuel Pressure Regulator 1 Performance", "Regulador de presión de combustible 1: funcionamiento", "fuel_air", 2, ["Válvula dosificadora de la bomba de alta presión", "Bomba de alta presión"]),
    ("P0090", "Fuel Pressure Regulator 1 Control Circuit", "Regulador de presión de combustible 1: circuito de control", "fuel_air", 1, ["Válvula dosificadora", "Cableado"]),
    ("P2463", "Diesel Particulate Filter Restriction - Soot Accumulation", "Filtro de partículas: obstrucción por hollín", "diesel", 2, ["Regeneraciones incompletas"]),
    ("U0001", "High Speed CAN Communication Bus", "Bus de comunicación CAN de alta velocidad", "network", 1, ["Cableado CAN", "Resistencia terminal", "Módulo que bloquea el bus"]),
    ("U0073", 'Control Module Communication Bus "A" Off', 'Bus de comunicación "A" desactivado (bus off)', "network", 1, ["Corto en CAN-H/CAN-L"]),
    ("U0100", 'Lost Communication With ECM/PCM "A"', 'Pérdida de comunicación con la ECU de motor "A"', "network", 1, ["Alimentación/masa de la ECU", "Cableado CAN"]),
    ("U0101", "Lost Communication With TCM", "Pérdida de comunicación con el módulo del cambio", "network", 1, ["Alimentación del TCM", "CAN"]),
    ("U0121", "Lost Communication With Anti-Lock Brake System (ABS) Control Module", "Pérdida de comunicación con el módulo ABS", "network", 1, ["Fusible del ABS", "Ramal CAN del ABS"]),
    ("U0140", "Lost Communication With Body Control Module", "Pérdida de comunicación con el módulo de carrocería", "network", 1, ["Alimentación del BCM", "CAN"]),
    ("U0151", "Lost Communication With Restraints Control Module", "Pérdida de comunicación con el módulo de airbag", "network", 1, ["Módulo de airbag"]),
    ("U0155", "Lost Communication With Instrument Panel Cluster (IPC) Control Module", "Pérdida de comunicación con el cuadro de instrumentos", "network", 1, ["Ramal CAN del cuadro", "Alimentación del cuadro"]),
    ("U0401", "Invalid Data Received From ECM/PCM A", "Datos no válidos recibidos de la ECU de motor A", "network", 1, ["ECU", "Software"]),
    ("U0415", "Invalid Data Received From Anti-Lock Brake System (ABS) Control Module", "Datos no válidos recibidos del módulo ABS", "network", 1, ["Sensor de rueda", "ABS"]),
]


def dtc_entries():
    seen = set()
    out = []
    for code, en, es, system, trips, causes in DTC:
        if code in seen:
            continue
        seen.add(code)
        mil = not code.startswith("U") or code in ("U0073", "U0001", "U0100")
        out.append({"code": code, "description": en, "descriptionEs": es, "system": system, "trips": trips, "mil": mil, "causes": causes})
    out.sort(key=lambda d: d["code"])
    return out


# ---------------------------------------------------------------- parts
PART_KINDS = {
    # kind: (name, base OEM price €)
    "MafSensor": ("Caudalímetro", 185), "MapSensor": ("Sensor MAP", 75), "IatSensor": ("Sensor IAT", 25),
    "EctSensor": ("Sensor ECT", 28), "TpsSensor": ("Sensor TPS", 60), "AppSensor": ("Pedal acelerador con sensor", 140),
    "CkpSensor": ("Sensor de cigüeñal", 55), "CmpSensor": ("Sensor de árbol de levas", 48), "O2Narrowband": ("Sonda lambda banda estrecha", 85),
    "O2Wideband": ("Sonda lambda banda ancha", 165), "O2Downstream": ("Sonda lambda postcatalizador", 90), "KnockSensor": ("Sensor de detonación", 45),
    "FuelPressureSensor": ("Sensor de presión de rampa", 95), "BoostSensor": ("Sensor de presión de turbo", 80), "Injector": ("Inyector", 120),
    "IgnitionCoil": ("Bobina de encendido", 65), "SparkPlug": ("Bujía", 14), "ElectronicThrottle": ("Cuerpo de mariposa motorizado", 290),
    "WastegateSolenoid": ("Electroválvula de wastegate", 70), "Turbocharger": ("Turbocompresor", 1100), "Intercooler": ("Intercooler", 320),
    "Thermostat": ("Termostato", 38), "CoolingFan": ("Electroventilador", 210), "FuelPump": ("Bomba de combustible", 230),
    "FuelPressureRegulator": ("Regulador de presión", 85), "EgrValve": ("Válvula EGR", 260), "PurgeValve": ("Electroválvula de purga", 55),
    "Cylinder": ("Kit de pistón, segmentos y válvulas", 620), "IntakeGasket": ("Junta de colector de admisión", 18), "VacuumHose": ("Manguito de vacío", 22),
    "BoostHose": ("Manguito de presión", 65), "Exhaust": ("Colector/tramo de escape con juntas", 240), "Catalyst": ("Catalizador", 680),
    "AirFilter": ("Filtro de aire", 16), "FuelFilter": ("Filtro de combustible", 24), "TimingDrive": ("Kit de distribución", 340),
    "HeadGasket": ("Junta de culata y tornillería", 140), "Battery": ("Batería 70 Ah", 120), "Alternator": ("Alternador", 310),
    "Fuse": ("Fusible", 1), "Relay": ("Relé", 14), "GroundStrap": ("Trenza de masa", 19), "ControlModule": ("Módulo de control (reacondicionado)", 450),
    "CanBus": ("Reparación de ramal CAN", 30), "GlowPlug": ("Calentador", 28),
    "EvapCanister": ("Cánister de carbón activo", 160), "EvapVentValve": ("Electroválvula de ventilación EVAP", 65),
    "EvapPressureSensor": ("Sensor de presión del depósito", 70), "FuelCap": ("Tapón de combustible", 18),
    "VvtSolenoid": ("Electroválvula VVT", 95), "HighPressurePump": ("Bomba de alta presión", 520),
    "VgtActuator": ("Actuador de turbo de geometría variable", 380), "ParticulateFilter": ("Filtro de partículas (FAP)", 1250),
    "DpfPressureSensor": ("Sensor de presión diferencial del FAP", 110), "ExhaustTempSensor": ("Sensor de temperatura de escape", 85),
}
QUALITIES = [("oem", "OEM", 1.0, 0.98), ("aftermarket", "Recambio", 0.6, 0.88), ("used", "Usada (desguace)", 0.3, 0.7)]


def parts_entries():
    out = []
    for kind, (name, price) in PART_KINDS.items():
        for q, qname, pf, rel in QUALITIES:
            if kind in ("Fuse", "CanBus") and q != "oem":
                continue
            out.append({"id": f"part_{kind.lower()}_{q}", "kind": kind, "name": f"{name} ({qname})", "quality": q,
                        "price": round(price * pf, 2), "reliability": rel, "fits": ["*"]})
    # performance parts for tuning jobs
    out += [
        {"id": "perf_injector_550", "kind": "Injector", "name": "Inyector alto caudal 550 cc/min", "quality": "performance", "price": 165, "reliability": 0.95, "fits": ["*"], "params": {"flow_ccmin": 550}},
        {"id": "perf_intercooler", "kind": "Intercooler", "name": "Intercooler de alto rendimiento", "quality": "performance", "price": 540, "reliability": 0.96, "fits": ["*"], "params": {"effectiveness": 0.82}},
        {"id": "perf_fuel_pump", "kind": "FuelPump", "name": "Bomba de combustible reforzada", "quality": "performance", "price": 290, "reliability": 0.96, "fits": ["*"], "params": {"max_pressure_kpa": 800}},
        {"id": "perf_spark_plug_cold", "kind": "SparkPlug", "name": "Bujía de grado térmico frío", "quality": "performance", "price": 22, "reliability": 0.97, "fits": ["*"], "params": {"gap_mm": 0.65}},
    ]
    return out


# ---------------------------------------------------------------- customers
CUSTOMERS = [
    ("cust_marta", "Marta Ibáñez", "Enfermera, usa el coche a diario para ir al hospital", 600, 3, "commute", "honest"),
    ("cust_javi", "Javi \"el Turbo\" Roldán", "Aficionado a las tandas en circuito", 2500, 10, "track", "enthusiast"),
    ("cust_lucia", "Lucía Benítez", "Repartidora autónoma; cada día parada le cuesta dinero", 800, 1, "delivery", "impatient"),
    ("cust_anton", "Antón Ferreiro", "Jubilado, coche viejo bien cuidado", 400, 7, "occasional", "haggler"),
    ("cust_sara", "Sara Montes", "Comercial, 40.000 km al año por autopista", 900, 2, "highway", "honest"),
    ("cust_dani", "Dani Cortés", "Estudiante con poco presupuesto", 250, 5, "city", "haggler"),
    ("cust_flota", "Flotas Mediterráneo S.L.", "Empresa de alquiler, exige factura detallada", 1500, 3, "fleet", "demanding"),
    ("cust_elena", "Elena Vidal", "Preparadora de rallyes amateur", 4000, 14, "track", "enthusiast"),
    ("cust_pablo", "Pablo Sanz", "Taxista; el coche es su herramienta de trabajo", 1000, 1, "taxi", "impatient"),
    ("cust_irene", "Irene Gallardo", "Necesita pasar la inspección técnica la semana que viene", 500, 6, "commute", "honest"),
    ("cust_tomas", "Tomás Herrero", "Comprador de coches de segunda mano para revender", 700, 4, "resale", "haggler"),
    ("cust_nuria", "Nuria Esteve", "Ingeniera, pregunta por cada lectura del diagnóstico", 1200, 5, "commute", "demanding"),
]


def customers_entries():
    return [{"id": i, "name": n, "description": d, "budget": b, "patienceDays": p, "usage": u, "personality": per}
            for i, n, d, b, p, u, per in CUSTOMERS]


# ---------------------------------------------------------------- scenarios (fault combinations)
def f(mode, component, severity=0.6, pin=None, cond=None, th=None, th2=None):
    d = {"mode": mode, "component": component, "severity": severity}
    if pin:
        d["pin"] = pin
    if cond:
        d["condition"] = {"kind": cond}
        if th is not None:
            d["condition"]["threshold"] = th
        if th2 is not None:
            d["condition"]["threshold2"] = th2
    return d


SCENARIOS = [
    ("sc_vacuum_leak_pcv", "Fuga en el manguito PCV", "aurex_strada_gt", 1, [f("leak_vacuum", "pcv_hose", 0.6)], "Ralentí irregular y testigo de motor encendido."),
    ("sc_vacuum_leak_gasket3", "Junta de admisión del cilindro 3", "aurex_strada_gt", 3, [f("leak_vacuum", "intake_gasket3", 0.7)], "Tiembla al ralentí, sobre todo en frío."),
    ("sc_thermostat_open", "Termostato abierto", "velmora_pico", 1, [f("stuck_open", "thermostat", 0.8)], "La calefacción no calienta y gasta más."),
    ("sc_thermostat_closed", "Termostato atascado cerrado", "velmora_pico", 2, [f("stuck_closed", "thermostat", 1.0)], "Se le sube la temperatura en atascos."),
    ("sc_ect_open", "Cable de señal ECT abierto", "aurex_strada_gt", 1, [f("wire_open", "ect", 1, pin="signal")], "Ventilador siempre a tope y consume mucho."),
    ("sc_ect_short", "Señal ECT en corto a masa", "velmora_pico", 2, [f("wire_short_ground", "ect", 1, pin="signal")], "Arranca mal en frío y ventilador a tope."),
    ("sc_ect_corroded", "Conector ECT oxidado", "velmora_pico", 4, [f("connector_corrosion", "ect", 0.6, pin="ground")], "Arranca mal en caliente, gasta mucho."),
    ("sc_coil2_dead", "Bobina 2 averiada", "aurex_strada_gt", 1, [f("dead", "coil2", 1)], "Va a tirones y vibra mucho."),
    ("sc_coil4_weak_load", "Bobina 4 débil bajo carga", "aurex_strada_gt", 3, [f("weak", "coil4", 0.6, cond="AboveLoad", th=0.8)], "Petardea al acelerar fuerte en autopista."),
    ("sc_plug1_worn", "Bujía 1 desgastada", "velmora_pico", 2, [f("wear", "plug1", 0.9)], "Tirones suaves al acelerar."),
    ("sc_all_plugs_worn", "Bujías gastadas + bobina débil", "kessler_rapace", 3, [f("wear", "plug1", 0.8), f("wear", "plug5", 0.9), f("weak", "coil5", 0.4)], "Pierde fuerza arriba y tiembla."),
    ("sc_injector3_clogged", "Inyector 3 obstruido", "aurex_strada_gt", 3, [f("clog", "inj3", 0.6)], "Ralentí con vibración y huele raro."),
    ("sc_injector1_open", "Inyector 1 bobinado abierto", "velmora_pico", 2, [f("dead", "inj1", 1)], "Va con tres cilindros."),
    ("sc_injector2_wire", "Cable de control del inyector 2 abierto", "aurex_strada_gt", 2, [f("wire_open", "inj2", 1, pin="control")], "Va con tres cilindros."),
    ("sc_injector4_dribble", "Inyector 4 gotea", "kessler_rapace", 4, [f("dribble", "inj4", 0.6)], "Huele a gasolina y arranca mal en caliente."),
    ("sc_maf_dirty", "Caudalímetro sucio", "aurex_strada_gt", 2, [f("signal_gain_low", "maf", 0.6)], "Le falta fuerza y a veces se cala."),
    ("sc_maf_open", "Señal MAF abierta", "kessler_rapace", 2, [f("wire_open", "maf", 1, pin="signal")], "Funciona raro y el testigo está encendido."),
    ("sc_map_ref_open", "Referencia 5 V del MAP abierta", "velmora_pico", 3, [f("wire_open", "map", 1, pin="ref")], "Va fatal, gasta muchísimo."),
    ("sc_tps_ground_open", "Masa del TPS abierta", "aurex_strada_gt", 3, [f("wire_open", "tps", 1, pin="ground")], "Se queda sin fuerza (modo emergencia)."),
    ("sc_app_short", "Pedal en corto a masa", "aurex_strada_gt", 2, [f("wire_short_ground", "app", 1, pin="signal")], "El acelerador no responde."),
    ("sc_ckp_intermittent", "Sensor de cigüeñal intermitente en caliente", "velmora_pico", 4, [f("signal_dropout", "ckp", 1, cond="Hot", th=85)], "Se para en caliente y luego no arranca hasta que enfría."),
    ("sc_ckp_open", "Sensor de cigüeñal abierto", "aurex_strada_gt", 2, [f("wire_open", "ckp", 1, pin="signal")], "Gira pero no arranca."),
    ("sc_cmp_dead", "Sensor de árbol de levas averiado", "aurex_strada_gt", 2, [f("dead", "cmp", 1)], "Tarda en arrancar."),
    ("sc_o2_heater_open", "Calefactor de sonda abierto", "velmora_pico", 2, [f("heater_open", "o2_up", 1)], "Testigo encendido, consume algo más."),
    ("sc_o2_slow", "Sonda lambda lenta", "velmora_pico", 4, [f("signal_slow", "o2_up", 0.7)], "Testigo de motor, sin síntomas claros."),
    ("sc_cat_worn", "Catalizador agotado", "velmora_pico", 3, [f("wear", "catalyst", 0.85)], "No pasa la ITV por emisiones."),
    ("sc_exhaust_leak", "Fuga de escape antes de la sonda", "aurex_strada_gt", 3, [f("leak_exhaust", "exhaust", 0.7)], "Hace tic-tic en frío."),
    ("sc_boost_leak", "Manguito de presión agrietado", "aurex_strada_gt", 2, [f("leak_boost", "charge_pipe", 0.6)], "Sopla al acelerar y no tira."),
    ("sc_wastegate_stuck_open", "Wastegate atascada abierta", "kessler_rapace", 3, [f("stuck_open", "turbo", 0.8)], "No tiene fuerza, como si no tuviera turbo."),
    ("sc_n75_open", "Electroválvula de wastegate sin señal", "aurex_strada_gt", 2, [f("wire_open", "wastegate_valve", 1, pin="control")], "Va flojo y testigo encendido."),
    ("sc_overboost", "Wastegate agarrotada cerrada", "aurex_strada_gt", 4, [f("stuck_closed", "turbo", 1)], "Da un tirón brutal y luego se corta."),
    ("sc_fuel_pump_weak", "Bomba de combustible débil", "aurex_strada_gt", 3, [f("weak", "fuel_pump", 0.5)], "Se queda sin fuerza a altas vueltas."),
    ("sc_fuel_filter", "Filtro de combustible obstruido", "nordak_atlas_td", 2, [f("clog", "fuel_filter", 0.7)], "Pierde potencia subiendo puertos."),
    ("sc_pump_fuse", "Fusible de bomba fundido", "velmora_pico", 1, [f("dead", "fuse_pump", 1)], "No arranca, se oye el motor de arranque."),
    ("sc_pump_relay", "Relé de bomba con contactos quemados", "aurex_strada_gt", 2, [f("stuck_open", "fuel_pump_relay", 1)], "No arranca."),
    ("sc_fan_dead", "Ventilador averiado", "velmora_pico", 2, [f("dead", "fan", 1)], "Se calienta en ciudad."),
    ("sc_alternator_weak", "Alternador débil", "aurex_strada_gt", 2, [f("weak", "alternator", 0.7)], "Luces débiles y testigo de batería."),
    ("sc_battery_weak", "Batería sulfatada", "velmora_pico", 1, [f("weak", "battery", 0.8)], "Arranca con dificultad por la mañana."),
    ("sc_ground_strap", "Masa motor corroída", "aurex_strada_gt", 4, [f("connector_corrosion", "ground_strap", 0.02)], "Arranque lento y fallos raros."),
    ("sc_knock_open", "Sensor de picado sin señal", "kessler_rapace", 3, [f("wire_open", "knock", 1, pin="signal")], "Va algo más flojo y testigo encendido."),
    ("sc_timing_stretch", "Cadena de distribución estirada", "aurex_strada_gt", 5, [f("slack", "timing_chain", 0.6)], "Ruido metálico al arrancar en frío."),
    ("sc_compression_cyl2", "Válvula de escape quemada en cilindro 2", "velmora_pico", 4, [f("leak_compression", "cyl2", 0.7)], "Vibra mucho y le falta fuerza."),
    ("sc_head_gasket", "Junta de culata en las últimas", "aurex_strada_gt", 5, [f("leak_fluid", "head_gasket", 0.5)], "Pierde agua y echa humo blanco."),
    ("sc_can_ipc", "Ramal CAN del cuadro abierto", "aurex_strada_gt", 4, [f("can_stub_open", "can_bus", 1, pin="ipc")], "El cuadro se apaga a veces."),
    ("sc_abs_dead", "Módulo ABS sin alimentación", "kessler_rapace", 3, [f("dead", "module_abs", 1)], "Testigos de ABS y velocímetro a cero."),
    ("sc_throttle_dirty", "Mariposa muy sucia", "velmora_pico", 2, [f("restriction", "throttle", 0.7)], "Se le baja el ralentí y a veces se cala."),
    ("sc_combo_leak_maf", "Fuga de vacío + caudalímetro sucio", "aurex_strada_gt", 4, [f("leak_vacuum", "brake_booster_hose", 0.4), f("signal_gain_low", "maf", 0.4)], "Ralentí inestable y le falta fuerza."),
    ("sc_combo_coil_plug", "Bobina en corto interno + bujía gastada", "kessler_rapace", 4, [f("internal_short", "coil3", 0.6), f("wear", "plug3", 0.6)], "Petardea al acelerar."),
    ("sc_diesel_egr_open", "EGR atascada abierta (diésel)", "nordak_atlas_td", 3, [f("stuck_open", "egr_valve", 0.8)], "Humo negro y falta de fuerza."),
    ("sc_diesel_boost_leak", "Fuga de presión en diésel", "nordak_atlas_td", 2, [f("leak_boost", "intercooler", 0.6)], "Pierde fuerza y echa humo negro al acelerar."),
]


def scenarios_entries():
    return [{"id": i, "name": n, "car": c, "difficulty": d, "faults": fs, "complaint": comp} for i, n, c, d, fs, comp in SCENARIOS]


JOBS = [
    ("job_01", "cust_marta", "aurex_strada_gt", "repair", "sc_vacuum_leak_pcv", None, "Me sale la luz del motor y al ralentí va como a saltitos.", 450, 3),
    ("job_02", "cust_irene", "velmora_pico", "inspection", "sc_cat_worn", None, "Tengo la ITV el lunes y me dicen que las emisiones no van a pasar.", 900, 5),
    ("job_03", "cust_javi", "aurex_strada_gt", "power", None, 275, "Quiero unos 275 CV para tandas, sin romper nada.", 1800, 10),
    ("job_04", "cust_lucia", "velmora_pico", "repair", "sc_pump_fuse", None, "Esta mañana no ha querido arrancar. ¡Lo necesito ya!", 300, 1),
    ("job_05", "cust_pablo", "aurex_strada_gt", "repair", "sc_coil2_dead", None, "Va a tirones y la luz del motor parpadea.", 400, 1),
    ("job_06", "cust_sara", "nordak_atlas_td", "repair", "sc_fuel_filter", None, "En las cuestas de la autopista se queda sin fuerza.", 500, 2),
    ("job_07", "cust_elena", "kessler_rapace", "track", None, 420, "Lo preparo para una subida de montaña: más potencia y que aguante.", 3500, 14),
    ("job_08", "cust_anton", "velmora_pico", "repair", "sc_thermostat_open", None, "La calefacción no calienta nada y gasta más que antes.", 250, 7),
    ("job_09", "cust_nuria", "aurex_strada_gt", "repair", "sc_timing_stretch", None, "Hace un ruido metálico al arrancar en frío y ahora tengo un código.", 1200, 5),
    ("job_10", "cust_flota", "kessler_rapace", "repair", None, None, "Revisión completa: testigo encendido tras el último cliente de alquiler.", 1500, 3),
]


def jobs_entries():
    out = []
    for i, cust, car, goal, sc, power, complaint, budget, days in JOBS:
        j = {"id": i, "customer": cust, "car": car, "goal": goal, "complaint": complaint, "budget": budget, "deadlineDays": days}
        if sc:
            j["scenario"] = sc
        elif goal == "repair":
            j["generate"] = {"difficulty": 3}
        if power:
            j["powerTargetPs"] = power
        out.append(j)
    return out


UPGRADES = [
    ("tool_multimeter", "Multímetro digital", "tool", 0, 0, "Medición de tensión, resistencia y continuidad"),
    ("tool_scanner", "Escáner OBD-II básico", "tool", 0, 0, "Lectura/borrado de códigos y datos en vivo"),
    ("tool_fuel_gauge", "Manómetro de combustible", "tool", 180, 0, "Presión de rampa, residual y de corte"),
    ("tool_compression", "Compresímetro", "tool", 120, 0, "Compresión por cilindro"),
    ("tool_leakdown", "Comprobador de fugas de cilindro", "tool", 260, 10, "Porcentaje de fuga y dónde se escapa"),
    ("tool_smoke", "Máquina de humo", "tool", 650, 15, "Localiza fugas de vacío y de escape"),
    ("tool_scope", "Osciloscopio de 4 canales", "tool", 1200, 25, "Formas de onda de sensores y actuadores"),
    ("tool_ecu_flash", "Interfaz de reprogramación ECU", "tool", 900, 20, "Lectura y escritura de mapas"),
    ("tool_dyno", "Banco de potencia de rodillos", "tool", 18000, 40, "Medición de potencia y datalog"),
    ("upg_lift2", "Segundo elevador", "workshop", 4500, 30, "Permite dos coches a la vez"),
    ("upg_parts_account", "Cuenta con distribuidor OEM", "workshop", 1500, 20, "10 % de descuento en recambio OEM"),
    ("upg_training", "Curso de diagnosis avanzada", "workshop", 800, 10, "Reduce el tiempo de cada medición un 15 %"),
]


def upgrades_entries():
    return [{"id": i, "name": n, "type": t, "price": p, "reputationRequired": rep, "description": d} for i, n, t, p, rep, d in UPGRADES]


def main(base, dump):
    dump("dtc.json", {"$schema": "../schemas/dtc.schema.json", "codes": dtc_entries()})
    dump("parts.json", {"$schema": "../schemas/part.schema.json", "parts": parts_entries()})
    dump("customers.json", {"$schema": "../schemas/customer.schema.json", "customers": customers_entries()})
    dump("scenarios.json", {"$schema": "../schemas/scenario.schema.json", "scenarios": scenarios_entries()})
    dump("jobs.json", {"$schema": "../schemas/job.schema.json", "jobs": jobs_entries()})
    dump("upgrades.json", {"$schema": "../schemas/upgrade.schema.json", "upgrades": upgrades_entries()})
