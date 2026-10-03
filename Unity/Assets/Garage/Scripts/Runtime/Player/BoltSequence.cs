using System.Linq;
using UnityEngine;

namespace Garage.Unity
{
    /// <summary>
    /// Fasteners of a part: loosened one by one, tightened in the specified order to the specified torque.
    /// Wrong order or torque is reported (a real source of comebacks: warped flanges, leaking gaskets).
    /// </summary>
    public sealed class BoltSequence : MonoBehaviour
    {
        public int boltCount = 4;
        public float torqueNm = 10f;
        public int[] order = { 0, 2, 1, 3 };

        private bool[] _tight;
        private int _next;

        /// <summary>All fasteners loose.</summary>
        public bool AllLoose => _tight != null && _tight.All(t => !t);

        private void Ensure()
        {
            if (_tight == null)
            {
                _tight = Enumerable.Repeat(true, boltCount).ToArray();
            }
        }

        /// <summary>Loosens the next bolt or, if all are loose, tightens following the order.</summary>
        public string Turn()
        {
            Ensure();
            int loose = _tight.Count(t => !t);
            if (_tight.Any(t => t) && _next == 0 && loose < boltCount)
            {
                int i = System.Array.IndexOf(_tight, true);
                _tight[i] = false;
                return $"Tornillo {i + 1} aflojado ({_tight.Count(t => !t)}/{boltCount}).";
            }

            int b = order[_next % order.Length] % boltCount;
            _tight[b] = true;
            _next++;
            if (_next >= boltCount)
            {
                _next = 0;
                return $"Apretado en cruz a {torqueNm:0} N·m. Montaje correcto.";
            }

            return $"Tornillo {b + 1} apretado a {torqueNm:0} N·m (siguiente en orden: {order[_next % order.Length] + 1}).";
        }

        /// <summary>Marks as freshly fitted.</summary>
        public void ResetTight()
        {
            _tight = Enumerable.Repeat(true, boltCount).ToArray();
            _next = 0;
        }
    }
}
