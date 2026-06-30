namespace proiect_RISC.Models
{
    public class CacheLine
    {
        public bool Valid { get; set; } = false;
        public uint Tag { get; set; } = 0;
        
        public bool Dirty { get; set; } = false;
        public int LastUsedCycle { get; set; } = 0;
 
        public void Reset()
        {
            Valid = false;
            Tag = 0;
            Dirty = false;
            LastUsedCycle = 0;
        }
    }
}