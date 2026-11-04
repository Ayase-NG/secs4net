using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSdata
{
    public class S16F11_data
    {
        public string DATAID { get; set; }      // U4:1 (varies)  an identifier to correlate related messages
        public string PRJOBID { get; set; }     // A:n (always)  process job identifier
        public string MF { get; set; }      // B:1 (varies)  material format code, ASCII indicates generic units, E40 restricts to B:1
        public string CARRIERID { get; set; }   // A:n (always)  carrier ID
        public uint SLOTID { get; set; }    // U1:1 (always)  slot position within a carrier
        public uint PRRECIPEMETHOD { get; set; }    // U1:1 (always)  recipe type
        public string RCPSPEC { get; set; }     // A:n (always)  recipe specifier
        public string RCPPARNM { get; set; }    // A:256 (always)  the name of a recipe variable parameter
        public string RCPPARVAL { get; set; }   // A:80 (varies)  the value of a recipe variable parameter, any scalar format type
        public int PRPROCESSSTART { get; set; }     // TF:1 (always)  automatic start flag, false implies manual start
        public string PRPAUSEEVENTID { get; set; }      // U4:1 (varies)  an event identifier for which a process job should be paused
    }
}
