using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace proiect_RISC.Models
{
    public class CacheBlackBox
    {
        private readonly HashSet<uint> _seen = new HashSet<uint>();

        public List<CacheAccess> AccessLog { get; } = new List<CacheAccess>();

        public int TotalAccesses { get; private set; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }

        public double HitRate => TotalAccesses == 0 ? 0.0 : (double)Hits / TotalAccesses;
        public double MissRate => TotalAccesses == 0 ? 0.0 : (double)Misses / TotalAccesses;

        /// <summary>
        /// Black-box cache model: does NOT model real tags/sets/ways.
        /// It only tracks which addresses have already been seen and reports
        /// hit/miss + statistics. (Real internal logic comes with the
        /// set-associative cache - E2.2.)
        /// </summary>
        public bool Access(uint address)
        {
            TotalAccesses++;
            bool isHit = _seen.Contains(address);

            if (isHit)
            {
                Hits++;
            }
            else
            {
                Misses++;
                _seen.Add(address); 
            }

            AccessLog.Add(new CacheAccess
            {
                Index = TotalAccesses,
                Address = address,
                IsHit = isHit
            });

            return isHit;
        }

        public void Reset()
        {
            _seen.Clear();
            AccessLog.Clear();
            TotalAccesses = 0;
            Hits = 0;
            Misses = 0;
        }
    }
}
