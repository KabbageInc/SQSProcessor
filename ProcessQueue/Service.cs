using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace ProcessQueue
{
    public class Service : ServiceBase
    {
        public Service()
        {
            ServiceName = "ProcessQueue";
        }

        protected override void OnStart(string[] args)
        {
            ProcessQueue.Start(args);
        }

        protected override void OnStop()
        {
            ProcessQueue.Stop();
        }
    }
}