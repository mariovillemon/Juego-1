using Garage.Sim.Components;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>Links a scene object to a simulated component (for interaction and visual state).</summary>
    public sealed class ComponentSlot : MonoBehaviour
    {
        public string componentId;
        public string componentName;
        public ComponentKind kind;
        public bool hasConnector;
    }

}
