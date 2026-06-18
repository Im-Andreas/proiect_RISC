using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace proiect_RISC.Models
{
    public class CacheAccess
    {
        public int Index { get; set; }     
        public uint Address { get; set; } 
        public bool IsHit { get; set; }
    }
}
