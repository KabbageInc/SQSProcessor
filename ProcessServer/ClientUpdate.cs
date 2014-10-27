using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcessServer
{
    public class ClientUpdate
    {
        public string ClientName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public float FilesPerSecond { get; set; }
        public string LocalIP { get; set; }
    }
}
