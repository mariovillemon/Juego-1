using System.Collections.Generic;
using Garage.Sim.Ecu;
using Garage.Sim.Faults;

namespace Garage.Sim.Vehicle
{
    /// <summary>Builds cars from definitions.</summary>
    public static class CarFactory
    {
        /// <summary>Creates a car with optional faults, soaked cold or warm.</summary>
        public static Car Create(CarDefinition definition, EcuCalibration calibration, DtcCatalog catalog, ulong seed, IEnumerable<FaultInstance>? faults = null, bool warm = false, double ambientC = 20)
        {
            var car = new Car(definition, calibration, catalog, seed);
            car.Environment.AmbientC = ambientC;
            if (faults != null)
            {
                foreach (FaultInstance f in faults)
                {
                    car.AddFault(f);
                }
            }

            car.Soak(warm);
            return car;
        }
    }
}
